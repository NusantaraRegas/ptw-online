using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ptw.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ReconcilePermitBusinessVersions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                SET NOCOUNT ON;

                DECLARE @OccurredAt datetimeoffset = CAST(SYSUTCDATETIME() AS datetimeoffset);
                DECLARE @Reconciled TABLE
                (
                    [Id] uniqueidentifier NOT NULL,
                    [PermitId] uniqueidentifier NOT NULL,
                    [LegacyChangeSequence] int NOT NULL,
                    [BusinessVersion] int NOT NULL
                );

                INSERT INTO @Reconciled ([Id], [PermitId], [LegacyChangeSequence], [BusinessVersion])
                SELECT NEWID(), p.[Id], p.[Version],
                    CASE WHEN submissions.[Count] = 0 THEN 1 ELSE submissions.[Count] END
                FROM [ptw].[Permit] p
                CROSS APPLY
                (
                    SELECT COUNT(*) AS [Count]
                    FROM [audit].[AuditEvent] e
                    WHERE e.[PermitId] = p.[Id]
                      AND e.[EventType] = 'permit_submitted'
                ) submissions
                WHERE p.[BusinessVersion] <= 0
                   OR NOT EXISTS
                   (
                       SELECT 1
                       FROM [ptw].[PermitRevision] revisionRow
                       WHERE revisionRow.[PermitId] = p.[Id]
                   );

                UPDATE p
                SET p.[BusinessVersion] = r.[BusinessVersion]
                FROM [ptw].[Permit] p
                INNER JOIN @Reconciled r ON r.[PermitId] = p.[Id];

                UPDATE taskRow
                SET taskRow.[LegacyPermitVersion] = COALESCE(taskRow.[LegacyPermitVersion], taskRow.[PermitVersion]),
                    taskRow.[PermitVersion] = CASE WHEN submissions.[Count] = 0 THEN 1 ELSE submissions.[Count] END
                FROM [wf].[PermitTask] taskRow
                INNER JOIN @Reconciled target ON target.[PermitId] = taskRow.[PermitId]
                CROSS APPLY
                (
                    SELECT COUNT(*) AS [Count]
                    FROM [audit].[AuditEvent] e
                    WHERE e.[PermitId] = taskRow.[PermitId]
                      AND e.[EventType] = 'permit_submitted'
                      AND e.[OccurredAt] <= taskRow.[CreatedAt]
                ) submissions;

                UPDATE decisionRow
                SET decisionRow.[LegacyPermitVersion] = COALESCE(
                        decisionRow.[LegacyPermitVersion], decisionRow.[PermitVersion]),
                    decisionRow.[PermitVersion] = CASE WHEN submissions.[Count] = 0 THEN 1 ELSE submissions.[Count] END
                FROM [wf].[Decision] decisionRow
                INNER JOIN @Reconciled target ON target.[PermitId] = decisionRow.[PermitId]
                CROSS APPLY
                (
                    SELECT COUNT(*) AS [Count]
                    FROM [audit].[AuditEvent] e
                    WHERE e.[PermitId] = decisionRow.[PermitId]
                      AND e.[EventType] = 'permit_submitted'
                      AND e.[OccurredAt] <= decisionRow.[DecidedAt]
                ) submissions;

                UPDATE packageRow
                SET packageRow.[LegacyPermitVersion] = COALESCE(
                        packageRow.[LegacyPermitVersion], packageRow.[PermitVersion]),
                    packageRow.[PermitVersion] = CASE WHEN submissions.[Count] = 0 THEN 1 ELSE submissions.[Count] END
                FROM [doc].[PrintPackageSnapshot] packageRow
                INNER JOIN @Reconciled target ON target.[PermitId] = packageRow.[PermitId]
                CROSS APPLY
                (
                    SELECT COUNT(*) AS [Count]
                    FROM [audit].[AuditEvent] e
                    WHERE e.[PermitId] = packageRow.[PermitId]
                      AND e.[EventType] = 'permit_submitted'
                      AND e.[OccurredAt] <= packageRow.[CreatedAt]
                ) submissions;

                UPDATE attachmentRow
                SET attachmentRow.[LegacyAddedInVersion] = COALESCE(
                        attachmentRow.[LegacyAddedInVersion], attachmentRow.[AddedInVersion]),
                    attachmentRow.[LegacyRemovedInVersion] = COALESCE(
                        attachmentRow.[LegacyRemovedInVersion], attachmentRow.[RemovedInVersion]),
                    attachmentRow.[LegacyTargetPermitVersion] = COALESCE(
                        attachmentRow.[LegacyTargetPermitVersion], attachmentRow.[TargetPermitVersion]),
                    attachmentRow.[AddedInVersion] =
                        CASE
                            WHEN lastEvent.[EventType] = 'revision_requested' THEN submissions.[Count] + 1
                            WHEN submissions.[Count] = 0 THEN 1
                            ELSE submissions.[Count]
                        END
                FROM [ptw].[PermitAttachment] attachmentRow
                INNER JOIN @Reconciled target ON target.[PermitId] = attachmentRow.[PermitId]
                CROSS APPLY
                (
                    SELECT COUNT(*) AS [Count]
                    FROM [audit].[AuditEvent] e
                    WHERE e.[PermitId] = attachmentRow.[PermitId]
                      AND e.[EventType] = 'permit_submitted'
                      AND e.[OccurredAt] <= attachmentRow.[UploadedAt]
                ) submissions
                OUTER APPLY
                (
                    SELECT TOP (1) e.[EventType]
                    FROM [audit].[AuditEvent] e
                    WHERE e.[PermitId] = attachmentRow.[PermitId]
                      AND e.[EventType] IN ('permit_submitted', 'revision_requested')
                      AND e.[OccurredAt] <= attachmentRow.[UploadedAt]
                    ORDER BY e.[OccurredAt] DESC, e.[Sequence] DESC
                ) lastEvent;

                UPDATE attachmentRow
                SET attachmentRow.[RemovedInVersion] =
                    CASE
                        WHEN lastEvent.[EventType] = 'revision_requested' THEN submissions.[Count] + 1
                        WHEN submissions.[Count] = 0 THEN 1
                        ELSE submissions.[Count]
                    END
                FROM [ptw].[PermitAttachment] attachmentRow
                INNER JOIN @Reconciled target ON target.[PermitId] = attachmentRow.[PermitId]
                CROSS APPLY
                (
                    SELECT COUNT(*) AS [Count]
                    FROM [audit].[AuditEvent] e
                    WHERE e.[PermitId] = attachmentRow.[PermitId]
                      AND e.[EventType] = 'permit_submitted'
                      AND e.[OccurredAt] <= attachmentRow.[RemovedAt]
                ) submissions
                OUTER APPLY
                (
                    SELECT TOP (1) e.[EventType]
                    FROM [audit].[AuditEvent] e
                    WHERE e.[PermitId] = attachmentRow.[PermitId]
                      AND e.[EventType] IN ('permit_submitted', 'revision_requested')
                      AND e.[OccurredAt] <= attachmentRow.[RemovedAt]
                    ORDER BY e.[OccurredAt] DESC, e.[Sequence] DESC
                ) lastEvent
                WHERE attachmentRow.[RemovedAt] IS NOT NULL;

                UPDATE attachmentRow
                SET attachmentRow.[TargetPermitVersion] =
                    CASE
                        WHEN packageRow.[Id] IS NOT NULL THEN packageRow.[PermitVersion]
                        ELSE attachmentRow.[AddedInVersion]
                    END
                FROM [ptw].[PermitAttachment] attachmentRow
                INNER JOIN @Reconciled target ON target.[PermitId] = attachmentRow.[PermitId]
                LEFT JOIN [doc].[PrintPackageSnapshot] packageRow
                    ON packageRow.[Id] = attachmentRow.[PrintPackageId];

                ;WITH submissions AS
                (
                    SELECT e.[PermitId], e.[OccurredAt], e.[ActorId],
                        ROW_NUMBER() OVER
                        (
                            PARTITION BY e.[PermitId]
                            ORDER BY e.[OccurredAt], e.[Sequence]
                        ) AS [BusinessVersion]
                    FROM [audit].[AuditEvent] e
                    INNER JOIN @Reconciled target ON target.[PermitId] = e.[PermitId]
                    WHERE e.[EventType] = 'permit_submitted'
                )
                INSERT INTO [ptw].[PermitRevision]
                    ([Id], [PermitId], [Version], [ContentJson], [ContentHash], [CreatedAt], [CreatedBy])
                SELECT NEWID(), submission.[PermitId], submission.[BusinessVersion],
                    snapshotRow.[ContentJson], snapshotRow.[ContentHash], submission.[OccurredAt], submission.[ActorId]
                FROM submissions submission
                CROSS APPLY
                (
                    SELECT TOP (1) legacyVersion.[ContentJson], legacyVersion.[ContentHash]
                    FROM [ptw].[PermitVersion] legacyVersion
                    WHERE legacyVersion.[PermitId] = submission.[PermitId]
                      AND legacyVersion.[CreatedAt] <= submission.[OccurredAt]
                    ORDER BY legacyVersion.[CreatedAt] DESC, legacyVersion.[Version] DESC
                ) snapshotRow
                WHERE NOT EXISTS
                (
                    SELECT 1
                    FROM [ptw].[PermitRevision] existingRevision
                    WHERE existingRevision.[PermitId] = submission.[PermitId]
                      AND existingRevision.[Version] = submission.[BusinessVersion]
                );

                INSERT INTO [ptw].[PermitRevision]
                    ([Id], [PermitId], [Version], [ContentJson], [ContentHash], [CreatedAt], [CreatedBy])
                SELECT NEWID(), p.[Id], 1,
                    snapshotRow.[ContentJson], snapshotRow.[ContentHash], snapshotRow.[CreatedAt], snapshotRow.[CreatedBy]
                FROM [ptw].[Permit] p
                INNER JOIN @Reconciled target ON target.[PermitId] = p.[Id]
                CROSS APPLY
                (
                    SELECT TOP (1) legacyVersion.[ContentJson], legacyVersion.[ContentHash],
                        legacyVersion.[CreatedAt], legacyVersion.[CreatedBy]
                    FROM [ptw].[PermitVersion] legacyVersion
                    WHERE legacyVersion.[PermitId] = p.[Id]
                    ORDER BY legacyVersion.[CreatedAt] DESC, legacyVersion.[Version] DESC
                ) snapshotRow
                WHERE NOT EXISTS
                (
                    SELECT 1
                    FROM [audit].[AuditEvent] e
                    WHERE e.[PermitId] = p.[Id]
                      AND e.[EventType] = 'permit_submitted'
                )
                  AND NOT EXISTS
                (
                    SELECT 1
                    FROM [ptw].[PermitRevision] existingRevision
                    WHERE existingRevision.[PermitId] = p.[Id]
                      AND existingRevision.[Version] = 1
                );

                INSERT INTO [audit].[AuditEvent]
                    ([Id], [PermitId], [EventType], [ActorId], [OccurredAt], [PayloadJson], [CorrelationId])
                SELECT r.[Id], r.[PermitId], 'permit_business_version_reconciled', 'system.migration', @OccurredAt,
                    CONCAT('{"legacyChangeSequence":', r.[LegacyChangeSequence],
                        ',"permitVersion":', r.[BusinessVersion], '}'),
                    'migration:ReconcilePermitBusinessVersions'
                FROM @Reconciled r;

                INSERT INTO [intg].[OutboxMessage]
                    ([Id], [AggregateId], [EventType], [PayloadJson], [OccurredAt], [Attempts])
                SELECT r.[Id], r.[PermitId], 'permit_business_version_reconciled',
                    CONCAT('{"legacyChangeSequence":', r.[LegacyChangeSequence],
                        ',"permitVersion":', r.[BusinessVersion], '}'),
                    @OccurredAt, 0
                FROM @Reconciled r;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new NotSupportedException(
                "Rekonsiliasi versi PTW bersifat append-only dan tidak dapat di-downgrade secara aman.");
        }
    }
}
