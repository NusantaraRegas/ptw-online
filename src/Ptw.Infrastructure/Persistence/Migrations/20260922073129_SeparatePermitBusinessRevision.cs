using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ptw.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SeparatePermitBusinessRevision : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_PermitAttachment_Versions",
                schema: "ptw",
                table: "PermitAttachment");

            migrationBuilder.AddColumn<int>(
                name: "LegacyPermitVersion",
                schema: "doc",
                table: "PrintPackageSnapshot",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "LegacyPermitVersion",
                schema: "wf",
                table: "PermitTask",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "LegacyAddedInVersion",
                schema: "ptw",
                table: "PermitAttachment",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "LegacyRemovedInVersion",
                schema: "ptw",
                table: "PermitAttachment",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "LegacyTargetPermitVersion",
                schema: "ptw",
                table: "PermitAttachment",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "BusinessVersion",
                schema: "ptw",
                table: "Permit",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "LegacyPermitVersion",
                schema: "wf",
                table: "Decision",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PermitRevision",
                schema: "ptw",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PermitId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false),
                    ContentJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ContentHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PermitRevision", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PermitRevision_Permit_PermitId",
                        column: x => x.PermitId,
                        principalSchema: "ptw",
                        principalTable: "Permit",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_PermitAttachment_Versions",
                schema: "ptw",
                table: "PermitAttachment",
                sql: "[RemovedInVersion] IS NULL OR [RemovedInVersion] >= [AddedInVersion]");

            migrationBuilder.CreateIndex(
                name: "IX_PermitRevision_PermitId_Version",
                schema: "ptw",
                table: "PermitRevision",
                columns: new[] { "PermitId", "Version" },
                unique: true);

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
                ) submissions;

                UPDATE p
                SET p.[BusinessVersion] = r.[BusinessVersion]
                FROM [ptw].[Permit] p
                INNER JOIN @Reconciled r ON r.[PermitId] = p.[Id];

                UPDATE [wf].[PermitTask]
                SET [LegacyPermitVersion] = [PermitVersion];

                UPDATE taskRow
                SET taskRow.[PermitVersion] = CASE WHEN submissions.[Count] = 0 THEN 1 ELSE submissions.[Count] END
                FROM [wf].[PermitTask] taskRow
                CROSS APPLY
                (
                    SELECT COUNT(*) AS [Count]
                    FROM [audit].[AuditEvent] e
                    WHERE e.[PermitId] = taskRow.[PermitId]
                      AND e.[EventType] = 'permit_submitted'
                      AND e.[OccurredAt] <= taskRow.[CreatedAt]
                ) submissions;

                UPDATE [wf].[Decision]
                SET [LegacyPermitVersion] = [PermitVersion];

                UPDATE decisionRow
                SET decisionRow.[PermitVersion] = CASE WHEN submissions.[Count] = 0 THEN 1 ELSE submissions.[Count] END
                FROM [wf].[Decision] decisionRow
                CROSS APPLY
                (
                    SELECT COUNT(*) AS [Count]
                    FROM [audit].[AuditEvent] e
                    WHERE e.[PermitId] = decisionRow.[PermitId]
                      AND e.[EventType] = 'permit_submitted'
                      AND e.[OccurredAt] <= decisionRow.[DecidedAt]
                ) submissions;

                UPDATE [doc].[PrintPackageSnapshot]
                SET [LegacyPermitVersion] = [PermitVersion];

                UPDATE packageRow
                SET packageRow.[PermitVersion] = CASE WHEN submissions.[Count] = 0 THEN 1 ELSE submissions.[Count] END
                FROM [doc].[PrintPackageSnapshot] packageRow
                CROSS APPLY
                (
                    SELECT COUNT(*) AS [Count]
                    FROM [audit].[AuditEvent] e
                    WHERE e.[PermitId] = packageRow.[PermitId]
                      AND e.[EventType] = 'permit_submitted'
                      AND e.[OccurredAt] <= packageRow.[CreatedAt]
                ) submissions;

                UPDATE [ptw].[PermitAttachment]
                SET [LegacyAddedInVersion] = [AddedInVersion],
                    [LegacyRemovedInVersion] = [RemovedInVersion],
                    [LegacyTargetPermitVersion] = [TargetPermitVersion];

                UPDATE attachmentRow
                SET attachmentRow.[AddedInVersion] =
                    CASE
                        WHEN lastEvent.[EventType] = 'revision_requested' THEN submissions.[Count] + 1
                        WHEN submissions.[Count] = 0 THEN 1
                        ELSE submissions.[Count]
                    END
                FROM [ptw].[PermitAttachment] attachmentRow
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
                ) snapshotRow;

                INSERT INTO [ptw].[PermitRevision]
                    ([Id], [PermitId], [Version], [ContentJson], [ContentHash], [CreatedAt], [CreatedBy])
                SELECT NEWID(), p.[Id], 1,
                    snapshotRow.[ContentJson], snapshotRow.[ContentHash], snapshotRow.[CreatedAt], snapshotRow.[CreatedBy]
                FROM [ptw].[Permit] p
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
                );

                INSERT INTO [audit].[AuditEvent]
                    ([Id], [PermitId], [EventType], [ActorId], [OccurredAt], [PayloadJson], [CorrelationId])
                SELECT r.[Id], r.[PermitId], 'permit_business_version_reconciled', 'system.migration', @OccurredAt,
                    CONCAT('{"legacyChangeSequence":', r.[LegacyChangeSequence],
                        ',"permitVersion":', r.[BusinessVersion], '}'),
                    'migration:SeparatePermitBusinessRevision'
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
                "Rekonsiliasi versi PTW mempertahankan riwayat legacy dan tidak dapat di-downgrade secara aman.");
        }
    }
}
