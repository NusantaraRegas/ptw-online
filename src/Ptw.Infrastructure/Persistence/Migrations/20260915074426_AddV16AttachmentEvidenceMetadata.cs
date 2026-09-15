using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ptw.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddV16AttachmentEvidenceMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_PermitAttachment_ScanStatus",
                schema: "ptw",
                table: "PermitAttachment");

            migrationBuilder.AddColumn<string>(
                name: "Category",
                schema: "ptw",
                table: "PermitAttachment",
                type: "nvarchar(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "SUPPORTING");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "DocumentDate",
                schema: "ptw",
                table: "PermitAttachment",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DocumentNumber",
                schema: "ptw",
                table: "PermitAttachment",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DocumentRevision",
                schema: "ptw",
                table: "PermitAttachment",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PrintPackageId",
                schema: "ptw",
                table: "PermitAttachment",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SupersedesAttachmentId",
                schema: "ptw",
                table: "PermitAttachment",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TargetPermitVersion",
                schema: "ptw",
                table: "PermitAttachment",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.Sql(
                "UPDATE [ptw].[PermitAttachment] SET [TargetPermitVersion] = [AddedInVersion], [ScanStatus] = 'PENDING' WHERE [TargetPermitVersion] = 0 OR [ScanStatus] = 'NOT_SCANNED';");

            migrationBuilder.CreateIndex(
                name: "IX_PermitAttachment_PermitId_Category_TargetPermitVersion",
                schema: "ptw",
                table: "PermitAttachment",
                columns: new[] { "PermitId", "Category", "TargetPermitVersion" });

            migrationBuilder.CreateIndex(
                name: "IX_PermitAttachment_PrintPackageId",
                schema: "ptw",
                table: "PermitAttachment",
                column: "PrintPackageId");

            migrationBuilder.CreateIndex(
                name: "IX_PermitAttachment_SupersedesAttachmentId",
                schema: "ptw",
                table: "PermitAttachment",
                column: "SupersedesAttachmentId");

            migrationBuilder.AddCheckConstraint(
                name: "CK_PermitAttachment_Category",
                schema: "ptw",
                table: "PermitAttachment",
                sql: "[Category] IN ('SUPPORTING', 'JSA', 'SIGNED_FIELD_COPY')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_PermitAttachment_ScanStatus",
                schema: "ptw",
                table: "PermitAttachment",
                sql: "[ScanStatus] IN ('PENDING', 'CLEAN', 'REJECTED')");

            migrationBuilder.AddForeignKey(
                name: "FK_PermitAttachment_PermitAttachment_SupersedesAttachmentId",
                schema: "ptw",
                table: "PermitAttachment",
                column: "SupersedesAttachmentId",
                principalSchema: "ptw",
                principalTable: "PermitAttachment",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_PermitAttachment_PrintPackageSnapshot_PrintPackageId",
                schema: "ptw",
                table: "PermitAttachment",
                column: "PrintPackageId",
                principalSchema: "doc",
                principalTable: "PrintPackageSnapshot",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PermitAttachment_PermitAttachment_SupersedesAttachmentId",
                schema: "ptw",
                table: "PermitAttachment");

            migrationBuilder.DropForeignKey(
                name: "FK_PermitAttachment_PrintPackageSnapshot_PrintPackageId",
                schema: "ptw",
                table: "PermitAttachment");

            migrationBuilder.DropIndex(
                name: "IX_PermitAttachment_PermitId_Category_TargetPermitVersion",
                schema: "ptw",
                table: "PermitAttachment");

            migrationBuilder.DropIndex(
                name: "IX_PermitAttachment_PrintPackageId",
                schema: "ptw",
                table: "PermitAttachment");

            migrationBuilder.DropIndex(
                name: "IX_PermitAttachment_SupersedesAttachmentId",
                schema: "ptw",
                table: "PermitAttachment");

            migrationBuilder.DropCheckConstraint(
                name: "CK_PermitAttachment_Category",
                schema: "ptw",
                table: "PermitAttachment");

            migrationBuilder.DropCheckConstraint(
                name: "CK_PermitAttachment_ScanStatus",
                schema: "ptw",
                table: "PermitAttachment");

            migrationBuilder.DropColumn(
                name: "Category",
                schema: "ptw",
                table: "PermitAttachment");

            migrationBuilder.DropColumn(
                name: "DocumentDate",
                schema: "ptw",
                table: "PermitAttachment");

            migrationBuilder.DropColumn(
                name: "DocumentNumber",
                schema: "ptw",
                table: "PermitAttachment");

            migrationBuilder.DropColumn(
                name: "DocumentRevision",
                schema: "ptw",
                table: "PermitAttachment");

            migrationBuilder.DropColumn(
                name: "PrintPackageId",
                schema: "ptw",
                table: "PermitAttachment");

            migrationBuilder.DropColumn(
                name: "SupersedesAttachmentId",
                schema: "ptw",
                table: "PermitAttachment");

            migrationBuilder.DropColumn(
                name: "TargetPermitVersion",
                schema: "ptw",
                table: "PermitAttachment");

            migrationBuilder.Sql(
                "UPDATE [ptw].[PermitAttachment] SET [ScanStatus] = 'NOT_SCANNED' WHERE [ScanStatus] = 'PENDING';");

            migrationBuilder.AddCheckConstraint(
                name: "CK_PermitAttachment_ScanStatus",
                schema: "ptw",
                table: "PermitAttachment",
                sql: "[ScanStatus] IN ('NOT_SCANNED', 'CLEAN', 'REJECTED')");
        }
    }
}
