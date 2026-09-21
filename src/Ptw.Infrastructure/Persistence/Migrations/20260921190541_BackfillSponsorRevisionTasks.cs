using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ptw.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class BackfillSponsorRevisionTasks : Migration
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
                    [TaskId] uniqueidentifier NOT NULL,
                    [EventId] uniqueidentifier NOT NULL
                );

                INSERT INTO @reconciled ([PermitId], [TaskId], [EventId])
                SELECT p.[Id], NEWID(), NEWID()
                FROM [ptw].[Permit] p
                WHERE p.[Status] = 'RevisionRequired'
                  AND NOT EXISTS (
                      SELECT 1
                      FROM [wf].[PermitTask] t
                      WHERE t.[PermitId] = p.[Id]
                        AND t.[PermitVersion] = p.[Version]
                        AND t.[Type] = 'SPONSOR_REVISION');

                INSERT INTO [wf].[PermitTask]
                    ([Id], [PermitId], [PermitVersion], [Type], [Label], [RequiredRole],
                     [AssignedActorId], [Status], [CreatedAt], [CompletedAt], [CompletedBy], [CancelledAt])
                SELECT r.[TaskId], p.[Id], p.[Version], 'SPONSOR_REVISION',
                       N'Perbaiki dan ajukan ulang PTW', 'Sponsor', p.[SponsorId],
                       'PENDING', @now, NULL, NULL, NULL
                FROM @reconciled r
                INNER JOIN [ptw].[Permit] p ON p.[Id] = r.[PermitId];

                INSERT INTO [audit].[AuditEvent]
                    ([Id], [PermitId], [EventType], [ActorId], [OccurredAt], [PayloadJson], [CorrelationId])
                SELECT r.[EventId], r.[PermitId], 'sponsor_revision_task_reconciled',
                       'system:migration', @now,
                       CONCAT('{"taskId":"', CONVERT(nvarchar(36), r.[TaskId]),
                              '","reason":"missing_sponsor_revision_task"}'),
                       'migration:20260921190541'
                FROM @reconciled r;

                INSERT INTO [intg].[OutboxMessage]
                    ([Id], [AggregateId], [EventType], [PayloadJson], [OccurredAt], [ProcessedAt],
                     [Attempts], [NextAttemptAt], [LastError])
                SELECT r.[EventId], r.[PermitId], 'sponsor_revision_task_reconciled',
                       CONCAT('{"taskId":"', CONVERT(nvarchar(36), r.[TaskId]),
                              '","reason":"missing_sponsor_revision_task"}'),
                       @now, NULL, 0, NULL, NULL
                FROM @reconciled r;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Reconciliation is intentionally not reversed: workflow task and audit history
            // must not be rewritten during a rollback.
        }
    }
}
