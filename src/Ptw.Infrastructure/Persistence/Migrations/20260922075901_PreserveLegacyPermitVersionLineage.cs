using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ptw.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PreserveLegacyPermitVersionLineage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "BusinessPermitVersion",
                schema: "doc",
                table: "PrintPackageSnapshot",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "BusinessPermitVersion",
                schema: "wf",
                table: "PermitTask",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "AddedInBusinessVersion",
                schema: "ptw",
                table: "PermitAttachment",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "RemovedInBusinessVersion",
                schema: "ptw",
                table: "PermitAttachment",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TargetBusinessPermitVersion",
                schema: "ptw",
                table: "PermitAttachment",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "BusinessPermitVersion",
                schema: "wf",
                table: "Decision",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.Sql(
                """
                SET NOCOUNT ON;

                DECLARE @OccurredAt datetimeoffset = CAST(SYSUTCDATETIME() AS datetimeoffset);
                DECLARE @Affected TABLE
                (
                    [Id] uniqueidentifier NOT NULL,
                    [PermitId] uniqueidentifier NOT NULL
                );

                INSERT INTO @Affected ([Id], [PermitId])
                SELECT NEWID(), affected.[PermitId]
                FROM
                (
                    SELECT [PermitId] FROM [wf].[PermitTask] WHERE [LegacyPermitVersion] IS NOT NULL
                    UNION
                    SELECT [PermitId] FROM [wf].[Decision] WHERE [LegacyPermitVersion] IS NOT NULL
                    UNION
                    SELECT [PermitId] FROM [doc].[PrintPackageSnapshot] WHERE [LegacyPermitVersion] IS NOT NULL
                    UNION
                    SELECT [PermitId] FROM [ptw].[PermitAttachment]
                    WHERE [LegacyAddedInVersion] IS NOT NULL
                       OR [LegacyRemovedInVersion] IS NOT NULL
                       OR [LegacyTargetPermitVersion] IS NOT NULL
                ) affected;

                UPDATE [wf].[PermitTask]
                SET [BusinessPermitVersion] = [PermitVersion],
                    [PermitVersion] = COALESCE([LegacyPermitVersion], [PermitVersion]);

                UPDATE [wf].[Decision]
                SET [BusinessPermitVersion] = [PermitVersion],
                    [PermitVersion] = COALESCE([LegacyPermitVersion], [PermitVersion]);

                UPDATE [doc].[PrintPackageSnapshot]
                SET [BusinessPermitVersion] = [PermitVersion],
                    [PermitVersion] = COALESCE([LegacyPermitVersion], [PermitVersion]);

                UPDATE [ptw].[PermitAttachment]
                SET [AddedInBusinessVersion] = [AddedInVersion],
                    [RemovedInBusinessVersion] = [RemovedInVersion],
                    [TargetBusinessPermitVersion] = [TargetPermitVersion],
                    [AddedInVersion] = COALESCE([LegacyAddedInVersion], [AddedInVersion]),
                    [RemovedInVersion] = COALESCE([LegacyRemovedInVersion], [RemovedInVersion]),
                    [TargetPermitVersion] = COALESCE([LegacyTargetPermitVersion], [TargetPermitVersion]);

                INSERT INTO [audit].[AuditEvent]
                    ([Id], [PermitId], [EventType], [ActorId], [OccurredAt], [PayloadJson], [CorrelationId])
                SELECT affected.[Id], affected.[PermitId], 'permit_version_lineage_preserved',
                    'system.migration', @OccurredAt,
                    '{"businessVersionColumnsAdded":true,"legacyLineagePreserved":true}',
                    'migration:PreserveLegacyPermitVersionLineage'
                FROM @Affected affected;

                INSERT INTO [intg].[OutboxMessage]
                    ([Id], [AggregateId], [EventType], [PayloadJson], [OccurredAt], [Attempts])
                SELECT affected.[Id], affected.[PermitId], 'permit_version_lineage_preserved',
                    '{"businessVersionColumnsAdded":true,"legacyLineagePreserved":true}',
                    @OccurredAt, 0
                FROM @Affected affected;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_PrintPackageSnapshot_PermitId_BusinessPermitVersion",
                schema: "doc",
                table: "PrintPackageSnapshot",
                columns: new[] { "PermitId", "BusinessPermitVersion" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PermitTask_PermitId_BusinessPermitVersion_Type",
                schema: "wf",
                table: "PermitTask",
                columns: new[] { "PermitId", "BusinessPermitVersion", "Type" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PermitAttachment_PermitId_Category_TargetBusinessPermitVersion",
                schema: "ptw",
                table: "PermitAttachment",
                columns: new[] { "PermitId", "Category", "TargetBusinessPermitVersion" });

            migrationBuilder.CreateIndex(
                name: "IX_Decision_PermitId_BusinessPermitVersion_Decision",
                schema: "wf",
                table: "Decision",
                columns: new[] { "PermitId", "BusinessPermitVersion", "Decision" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new NotSupportedException(
                "Kolom versi bisnis menjaga snapshot immutable dan tidak dapat di-downgrade secara aman.");
        }
    }
}
