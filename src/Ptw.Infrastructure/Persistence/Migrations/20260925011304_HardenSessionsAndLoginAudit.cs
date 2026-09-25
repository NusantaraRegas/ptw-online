using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ptw.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class HardenSessionsAndLoginAudit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SecurityStamp",
                schema: "sec",
                table: "UserAccount",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: false,
                defaultValueSql: "LOWER(CONVERT(varchar(36), NEWID()))");

            migrationBuilder.CreateTable(
                name: "LoginAuditEvent",
                schema: "sec",
                columns: table => new
                {
                    Sequence = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UserName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    SubjectId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    DirectoryResult = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    IdentitySource = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Outcome = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    SourceAddress = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    CorrelationId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LoginAuditEvent", x => x.Sequence);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LoginAuditEvent_Id",
                schema: "sec",
                table: "LoginAuditEvent",
                column: "Id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LoginAuditEvent_OccurredAt",
                schema: "sec",
                table: "LoginAuditEvent",
                column: "OccurredAt");

            migrationBuilder.CreateIndex(
                name: "IX_LoginAuditEvent_Outcome_OccurredAt",
                schema: "sec",
                table: "LoginAuditEvent",
                columns: new[] { "Outcome", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_LoginAuditEvent_SubjectId_OccurredAt",
                schema: "sec",
                table: "LoginAuditEvent",
                columns: new[] { "SubjectId", "OccurredAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LoginAuditEvent",
                schema: "sec");

            migrationBuilder.DropColumn(
                name: "SecurityStamp",
                schema: "sec",
                table: "UserAccount");
        }
    }
}
