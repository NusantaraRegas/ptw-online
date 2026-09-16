using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ptw.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSupportingDocumentAttachmentCode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SupportingDocumentCode",
                schema: "ptw",
                table: "PermitAttachment",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_PermitAttachment_PermitId_SupportingDocumentCode_RemovedInVersion",
                schema: "ptw",
                table: "PermitAttachment",
                columns: new[] { "PermitId", "SupportingDocumentCode", "RemovedInVersion" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PermitAttachment_PermitId_SupportingDocumentCode_RemovedInVersion",
                schema: "ptw",
                table: "PermitAttachment");

            migrationBuilder.DropColumn(
                name: "SupportingDocumentCode",
                schema: "ptw",
                table: "PermitAttachment");
        }
    }
}
