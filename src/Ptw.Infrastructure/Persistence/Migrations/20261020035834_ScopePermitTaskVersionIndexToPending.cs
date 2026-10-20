using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ptw.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ScopePermitTaskVersionIndexToPending : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PermitTask_PermitId_BusinessPermitVersion_Type",
                schema: "wf",
                table: "PermitTask");

            migrationBuilder.CreateIndex(
                name: "IX_PermitTask_PermitId_BusinessPermitVersion_Type",
                schema: "wf",
                table: "PermitTask",
                columns: new[] { "PermitId", "BusinessPermitVersion", "Type" },
                unique: true,
                filter: "[Status] = N'PENDING'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PermitTask_PermitId_BusinessPermitVersion_Type",
                schema: "wf",
                table: "PermitTask");

            migrationBuilder.CreateIndex(
                name: "IX_PermitTask_PermitId_BusinessPermitVersion_Type",
                schema: "wf",
                table: "PermitTask",
                columns: new[] { "PermitId", "BusinessPermitVersion", "Type" },
                unique: true);
        }
    }
}
