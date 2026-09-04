using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ptw.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RetireGasValidationAndReconcileTasks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DECLARE @now datetimeoffset = SYSUTCDATETIME();
                DECLARE @reconciled TABLE
                (
                    [PermitId] uniqueidentifier NOT NULL PRIMARY KEY,
                    [EventId] uniqueidentifier NOT NULL,
                    [OriginalStatus] nvarchar(40) NOT NULL,
                    [HadPendingGasTask] bit NOT NULL
                );

                INSERT INTO @reconciled ([PermitId], [EventId], [OriginalStatus], [HadPendingGasTask])
                SELECT p.[Id], NEWID(), p.[Status],
                       CONVERT(bit, CASE WHEN EXISTS (
                           SELECT 1 FROM [wf].[PermitTask] gas
                           WHERE gas.[PermitId] = p.[Id]
                             AND gas.[Type] = 'GAS_DISTRIBUTION_VALIDATION'
                             AND gas.[Status] = 'PENDING') THEN 1 ELSE 0 END)
                FROM [ptw].[Permit] p
                WHERE p.[Status] = 'UnderReview'
                   OR (p.[Status] = 'AwaitingApproval' AND NOT EXISTS (
                       SELECT 1 FROM [wf].[PermitTask] approval
                       WHERE approval.[PermitId] = p.[Id]
                         AND approval.[PermitVersion] = p.[Version]
                         AND approval.[Type] = 'AREA_OWNER_APPROVAL'))
                   OR EXISTS (
                       SELECT 1 FROM [wf].[PermitTask] gas
                       WHERE gas.[PermitId] = p.[Id]
                         AND gas.[Type] = 'GAS_DISTRIBUTION_VALIDATION'
                         AND gas.[Status] = 'PENDING');

                UPDATE [wf].[PermitTask]
                SET [Status] = 'CANCELLED', [CancelledAt] = @now
                WHERE [Type] = 'GAS_DISTRIBUTION_VALIDATION' AND [Status] = 'PENDING';

                UPDATE [ptw].[Permit]
                SET [Status] = 'AwaitingApproval', [UpdatedAt] = @now
                WHERE [Status] = 'UnderReview'
                  AND JSON_VALUE([WorkflowEvidenceJson], '$.hsseValidation.actorId') IS NOT NULL;

                INSERT INTO [wf].[PermitTask]
                    ([Id], [PermitId], [PermitVersion], [Type], [Label], [RequiredRole],
                     [AssignedActorId], [Status], [CreatedAt], [CompletedAt], [CompletedBy], [CancelledAt])
                SELECT NEWID(), p.[Id], p.[Version], 'HSSE_VALIDATION', 'Validasi HSSE',
                       'HSSEValidator', NULL, 'PENDING', @now, NULL, NULL, NULL
                FROM [ptw].[Permit] p
                WHERE p.[Status] = 'UnderReview'
                  AND NOT EXISTS (
                      SELECT 1 FROM [wf].[PermitTask] t
                      WHERE t.[PermitId] = p.[Id]
                        AND t.[PermitVersion] = p.[Version]
                        AND t.[Type] = 'HSSE_VALIDATION');

                INSERT INTO [wf].[PermitTask]
                    ([Id], [PermitId], [PermitVersion], [Type], [Label], [RequiredRole],
                     [AssignedActorId], [Status], [CreatedAt], [CompletedAt], [CompletedBy], [CancelledAt])
                SELECT NEWID(), p.[Id], p.[Version], 'AREA_OWNER_APPROVAL',
                       'Persetujuan PIC pemilik area', 'AreaOwnerApprover', NULL,
                       'PENDING', @now, NULL, NULL, NULL
                FROM [ptw].[Permit] p
                WHERE p.[Status] = 'AwaitingApproval'
                  AND NOT EXISTS (
                      SELECT 1 FROM [wf].[PermitTask] t
                      WHERE t.[PermitId] = p.[Id]
                        AND t.[PermitVersion] = p.[Version]
                        AND t.[Type] = 'AREA_OWNER_APPROVAL');

                INSERT INTO [audit].[AuditEvent]
                    ([Id], [PermitId], [EventType], [ActorId], [OccurredAt], [PayloadJson], [CorrelationId])
                SELECT r.[EventId], r.[PermitId], 'workflow_route_reconciled', 'system:migration', @now,
                       CONCAT('{"from":"', r.[OriginalStatus], '","to":"', p.[Status],
                              '","reason":"gas_validation_retired","pendingGasTaskCancelled":',
                              CASE WHEN r.[HadPendingGasTask] = 1 THEN 'true' ELSE 'false' END, '}'),
                       'migration:20260903092940'
                FROM @reconciled r
                INNER JOIN [ptw].[Permit] p ON p.[Id] = r.[PermitId];

                INSERT INTO [intg].[OutboxMessage]
                    ([Id], [AggregateId], [EventType], [PayloadJson], [OccurredAt], [ProcessedAt],
                     [Attempts], [NextAttemptAt], [LastError])
                SELECT r.[EventId], r.[PermitId], 'workflow_route_reconciled',
                       CONCAT('{"from":"', r.[OriginalStatus], '","to":"', p.[Status],
                              '","reason":"gas_validation_retired","pendingGasTaskCancelled":',
                              CASE WHEN r.[HadPendingGasTask] = 1 THEN 'true' ELSE 'false' END, '}'),
                       @now, NULL, 0, NULL, NULL
                FROM @reconciled r
                INNER JOIN [ptw].[Permit] p ON p.[Id] = r.[PermitId];
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Reconciliation is intentionally not reversed: historical decisions and task states
            // must not be rewritten during a rollback.
        }
    }
}
