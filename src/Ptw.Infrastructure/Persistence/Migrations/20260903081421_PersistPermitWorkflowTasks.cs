using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ptw.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PersistPermitWorkflowTasks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "wf");

            migrationBuilder.CreateTable(
                name: "PermitTask",
                schema: "wf",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PermitId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PermitVersion = table.Column<int>(type: "int", nullable: false),
                    Type = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    Label = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    RequiredRole = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    AssignedActorId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CompletedBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    CancelledAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PermitTask", x => x.Id);
                    table.CheckConstraint("CK_PermitTask_Status", "[Status] IN ('PENDING', 'COMPLETED', 'CANCELLED')");
                    table.ForeignKey(
                        name: "FK_PermitTask_Permit_PermitId",
                        column: x => x.PermitId,
                        principalSchema: "ptw",
                        principalTable: "Permit",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PermitTask_PermitId_PermitVersion_Type",
                schema: "wf",
                table: "PermitTask",
                columns: new[] { "PermitId", "PermitVersion", "Type" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PermitTask_Status_RequiredRole_AssignedActorId_CreatedAt",
                schema: "wf",
                table: "PermitTask",
                columns: new[] { "Status", "RequiredRole", "AssignedActorId", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PermitTask",
                schema: "wf");
        }
    }
}
