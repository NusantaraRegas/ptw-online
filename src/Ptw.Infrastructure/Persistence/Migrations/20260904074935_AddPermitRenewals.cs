using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ptw.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPermitRenewals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "RenewedFromPermitId",
                schema: "ptw",
                table: "Permit",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Permit_RenewedFromPermitId",
                schema: "ptw",
                table: "Permit",
                column: "RenewedFromPermitId",
                unique: true,
                filter: "[RenewedFromPermitId] IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_Permit_Permit_RenewedFromPermitId",
                schema: "ptw",
                table: "Permit",
                column: "RenewedFromPermitId",
                principalSchema: "ptw",
                principalTable: "Permit",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Permit_Permit_RenewedFromPermitId",
                schema: "ptw",
                table: "Permit");

            migrationBuilder.DropIndex(
                name: "IX_Permit_RenewedFromPermitId",
                schema: "ptw",
                table: "Permit");

            migrationBuilder.DropColumn(
                name: "RenewedFromPermitId",
                schema: "ptw",
                table: "Permit");
        }
    }
}
