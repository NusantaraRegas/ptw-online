using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ptw.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AlignPermitLifecycleV16 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                UPDATE [ptw].[Permit]
                SET [Status] = CASE [Status]
                    WHEN 'Submitted' THEN 'UnderValidation'
                    WHEN 'UnderReview' THEN 'UnderValidation'
                    WHEN 'AwaitingApproval' THEN 'AwaitingAreaApproval'
                    WHEN 'Approved' THEN 'AwaitingAreaApproval'
                    WHEN 'ReadyForIssue' THEN 'AwaitingAreaApproval'
                    WHEN 'Open' THEN 'Issued'
                    WHEN 'SuspensionRequested' THEN 'Suspended'
                    WHEN 'CompletionConfirmationPending' THEN 'Suspended'
                    WHEN 'WorkCompleted' THEN 'Suspended'
                    ELSE [Status]
                END,
                [ActiveWorkPeriodId] = NULL,
                [WorkflowEvidenceJson] = REPLACE([WorkflowEvidenceJson], '"hsseValidation"', '"hseValidation"')
                WHERE [Status] IN (
                    'Submitted', 'UnderReview', 'AwaitingApproval', 'Approved', 'ReadyForIssue',
                    'Open', 'SuspensionRequested', 'CompletionConfirmationPending', 'WorkCompleted');

                UPDATE [wf].[PermitTask]
                SET [Type] = 'HSE_VALIDATION',
                    [Label] = 'Validasi PIC HSE',
                    [RequiredRole] = 'HSEValidator'
                WHERE [Type] = 'HSSE_VALIDATION';

                UPDATE [wf].[PermitTask]
                SET [Status] = 'CANCELLED', [CancelledAt] = SYSUTCDATETIME()
                WHERE [Status] = 'PENDING'
                  AND [Type] IN (
                    'GAS_DISTRIBUTION_VALIDATION', 'AREA_OWNER_APPROVAL', 'AREA_OWNER_ISSUE', 'SUSPENSION_APPROVAL',
                    'HSSE_COMPLETION_CONFIRMATION', 'AREA_OWNER_COMPLETION_CONFIRMATION', 'AREA_OWNER_CLOSE');

                INSERT INTO [wf].[PermitTask]
                    ([Id], [PermitId], [PermitVersion], [Type], [Label], [RequiredRole], [AssignedActorId], [Status], [CreatedAt])
                SELECT NEWID(), p.[Id], p.[Version], 'AREA_APPROVE_AND_ISSUE',
                       'Setujui dan terbitkan PTW', 'AreaOwnerManager', NULL, 'PENDING', SYSUTCDATETIME()
                FROM [ptw].[Permit] p
                WHERE p.[Status] = 'AwaitingAreaApproval'
                  AND NOT EXISTS (
                    SELECT 1 FROM [wf].[PermitTask] t
                    WHERE t.[PermitId] = p.[Id]
                      AND t.[PermitVersion] = p.[Version]
                      AND t.[Type] = 'AREA_APPROVE_AND_ISSUE');
                """);

            migrationBuilder.EnsureSchema(
                name: "doc");

            migrationBuilder.CreateTable(
                name: "Decision",
                schema: "wf",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PermitId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PermitVersion = table.Column<int>(type: "int", nullable: false),
                    TaskId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Decision = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    ActorId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ActorPosition = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ApprovalCapacity = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    PrincipalManagerUserId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    PrincipalPosition = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    AuthorizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ActingAssignmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Statement = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    DecidedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    EvidenceHash = table.Column<string>(type: "nchar(64)", fixedLength: true, maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Decision", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Decision_PermitTask_TaskId",
                        column: x => x.TaskId,
                        principalSchema: "wf",
                        principalTable: "PermitTask",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Decision_Permit_PermitId",
                        column: x => x.PermitId,
                        principalSchema: "ptw",
                        principalTable: "Permit",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PrintPackageSnapshot",
                schema: "doc",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PermitId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PermitVersion = table.Column<int>(type: "int", nullable: false),
                    DecisionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RuleVersion = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    PrintTemplateVersion = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    CampaignAssetVersion = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    SnapshotJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SnapshotHash = table.Column<string>(type: "nchar(64)", fixedLength: true, maxLength: 64, nullable: false),
                    RenderStatus = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PrintPackageSnapshot", x => x.Id);
                    table.CheckConstraint("CK_PrintPackageSnapshot_RenderStatus", "[RenderStatus] IN ('PENDING', 'RETRYING', 'READY', 'FAILED')");
                    table.ForeignKey(
                        name: "FK_PrintPackageSnapshot_Decision_DecisionId",
                        column: x => x.DecisionId,
                        principalSchema: "wf",
                        principalTable: "Decision",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PrintPackageSnapshot_Permit_PermitId",
                        column: x => x.PermitId,
                        principalSchema: "ptw",
                        principalTable: "Permit",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "GeneratedDocument",
                schema: "doc",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PrintPackageSnapshotId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StorageKey = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    MediaType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: true),
                    Sha256 = table.Column<string>(type: "nchar(64)", fixedLength: true, maxLength: 64, nullable: true),
                    RenderStatus = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Attempts = table.Column<int>(type: "int", nullable: false),
                    NextAttemptAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    GeneratedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    LastError = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GeneratedDocument", x => x.Id);
                    table.CheckConstraint("CK_GeneratedDocument_RenderStatus", "[RenderStatus] IN ('PENDING', 'RETRYING', 'READY', 'FAILED')");
                    table.ForeignKey(
                        name: "FK_GeneratedDocument_PrintPackageSnapshot_PrintPackageSnapshotId",
                        column: x => x.PrintPackageSnapshotId,
                        principalSchema: "doc",
                        principalTable: "PrintPackageSnapshot",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Decision_PermitId_PermitVersion_Decision",
                schema: "wf",
                table: "Decision",
                columns: new[] { "PermitId", "PermitVersion", "Decision" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Decision_TaskId",
                schema: "wf",
                table: "Decision",
                column: "TaskId");

            migrationBuilder.CreateIndex(
                name: "IX_GeneratedDocument_PrintPackageSnapshotId",
                schema: "doc",
                table: "GeneratedDocument",
                column: "PrintPackageSnapshotId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GeneratedDocument_RenderStatus_NextAttemptAt",
                schema: "doc",
                table: "GeneratedDocument",
                columns: new[] { "RenderStatus", "NextAttemptAt" });

            migrationBuilder.CreateIndex(
                name: "IX_PrintPackageSnapshot_DecisionId",
                schema: "doc",
                table: "PrintPackageSnapshot",
                column: "DecisionId");

            migrationBuilder.CreateIndex(
                name: "IX_PrintPackageSnapshot_PermitId_PermitVersion",
                schema: "doc",
                table: "PrintPackageSnapshot",
                columns: new[] { "PermitId", "PermitVersion" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GeneratedDocument",
                schema: "doc");

            migrationBuilder.DropTable(
                name: "PrintPackageSnapshot",
                schema: "doc");

            migrationBuilder.DropTable(
                name: "Decision",
                schema: "wf");
        }
    }
}
