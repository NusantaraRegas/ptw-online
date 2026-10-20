using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ptw.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class UatPermitNumberSequenceAndInitiation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "InitiatedBy",
                schema: "ptw",
                table: "Permit",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PermitNumberSequence",
                schema: "ptw",
                columns: table => new
                {
                    Year = table.Column<int>(type: "int", nullable: false),
                    LastValue = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PermitNumberSequence", x => x.Year);
                    table.CheckConstraint("CK_PermitNumberSequence_LastValue", "[LastValue] >= 0");
                });

            migrationBuilder.CreateIndex(
                name: "IX_Permit_InitiatedBy_UpdatedAt",
                schema: "ptw",
                table: "Permit",
                columns: new[] { "InitiatedBy", "UpdatedAt" },
                filter: "[InitiatedBy] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PermitNumberSequence",
                schema: "ptw");

            migrationBuilder.DropIndex(
                name: "IX_Permit_InitiatedBy_UpdatedAt",
                schema: "ptw",
                table: "Permit");

            migrationBuilder.DropColumn(
                name: "InitiatedBy",
                schema: "ptw",
                table: "Permit");
        }
    }
}
