using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ptw.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PolicyUatEvidence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PolicyUatSuite",
                schema: "cfg",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SuiteKey = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    PolicyVersion = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false),
                    ScenariosJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ContentHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PolicyUatSuite", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PolicyUatRun",
                schema: "audit",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PolicyUatSuiteId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PolicyVersion = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    SuiteContentHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Passed = table.Column<bool>(type: "bit", nullable: false),
                    ScenarioCount = table.Column<int>(type: "int", nullable: false),
                    MatchedCount = table.Column<int>(type: "int", nullable: false),
                    CoverageJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ResultsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ReportHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ExecutedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ExecutedBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PolicyUatRun", x => x.Id);
                    table.CheckConstraint("CK_PolicyUatRun_Counts", "[ScenarioCount] > 0 AND [MatchedCount] >= 0 AND [MatchedCount] <= [ScenarioCount]");
                    table.ForeignKey(
                        name: "FK_PolicyUatRun_PolicyUatSuite_PolicyUatSuiteId",
                        column: x => x.PolicyUatSuiteId,
                        principalSchema: "cfg",
                        principalTable: "PolicyUatSuite",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PolicyUatCommandReceipt",
                schema: "intg",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ActorId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Operation = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Key = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    RequestHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    PolicyUatSuiteId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PolicyUatRunId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PolicyUatCommandReceipt", x => x.Id);
                    table.CheckConstraint("CK_PolicyUatCommandReceipt_Result", "([PolicyUatSuiteId] IS NOT NULL AND [PolicyUatRunId] IS NULL) OR ([PolicyUatSuiteId] IS NULL AND [PolicyUatRunId] IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_PolicyUatCommandReceipt_PolicyUatRun_PolicyUatRunId",
                        column: x => x.PolicyUatRunId,
                        principalSchema: "audit",
                        principalTable: "PolicyUatRun",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PolicyUatCommandReceipt_PolicyUatSuite_PolicyUatSuiteId",
                        column: x => x.PolicyUatSuiteId,
                        principalSchema: "cfg",
                        principalTable: "PolicyUatSuite",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PolicyUatCommandReceipt_ActorId_Operation_Key",
                schema: "intg",
                table: "PolicyUatCommandReceipt",
                columns: new[] { "ActorId", "Operation", "Key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PolicyUatCommandReceipt_PolicyUatRunId",
                schema: "intg",
                table: "PolicyUatCommandReceipt",
                column: "PolicyUatRunId");

            migrationBuilder.CreateIndex(
                name: "IX_PolicyUatCommandReceipt_PolicyUatSuiteId",
                schema: "intg",
                table: "PolicyUatCommandReceipt",
                column: "PolicyUatSuiteId");

            migrationBuilder.CreateIndex(
                name: "IX_PolicyUatRun_PolicyUatSuiteId_ExecutedAt",
                schema: "audit",
                table: "PolicyUatRun",
                columns: new[] { "PolicyUatSuiteId", "ExecutedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_PolicyUatRun_PolicyVersion_Passed_ExecutedAt",
                schema: "audit",
                table: "PolicyUatRun",
                columns: new[] { "PolicyVersion", "Passed", "ExecutedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_PolicyUatSuite_PolicyVersion_CreatedAt",
                schema: "cfg",
                table: "PolicyUatSuite",
                columns: new[] { "PolicyVersion", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_PolicyUatSuite_SuiteKey_Version",
                schema: "cfg",
                table: "PolicyUatSuite",
                columns: new[] { "SuiteKey", "Version" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PolicyUatCommandReceipt",
                schema: "intg");

            migrationBuilder.DropTable(
                name: "PolicyUatRun",
                schema: "audit");

            migrationBuilder.DropTable(
                name: "PolicyUatSuite",
                schema: "cfg");
        }
    }
}
