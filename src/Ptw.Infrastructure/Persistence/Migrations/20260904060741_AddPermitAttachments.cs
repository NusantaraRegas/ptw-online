using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ptw.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPermitAttachments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PermitAttachment",
                schema: "ptw",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PermitId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AddedInVersion = table.Column<int>(type: "int", nullable: false),
                    RemovedInVersion = table.Column<int>(type: "int", nullable: true),
                    FileName = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    MediaType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Sha256 = table.Column<string>(type: "nchar(64)", fixedLength: true, maxLength: 64, nullable: false),
                    StorageKey = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    ScanStatus = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    UploadedBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    UploadedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RemovedBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    RemovedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PermitAttachment", x => x.Id);
                    table.CheckConstraint("CK_PermitAttachment_ScanStatus", "[ScanStatus] IN ('NOT_SCANNED', 'CLEAN', 'REJECTED')");
                    table.CheckConstraint("CK_PermitAttachment_Size", "[SizeBytes] > 0");
                    table.CheckConstraint("CK_PermitAttachment_Versions", "[RemovedInVersion] IS NULL OR [RemovedInVersion] > [AddedInVersion]");
                    table.ForeignKey(
                        name: "FK_PermitAttachment_Permit_PermitId",
                        column: x => x.PermitId,
                        principalSchema: "ptw",
                        principalTable: "Permit",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PermitAttachmentCommandReceipt",
                schema: "intg",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ActorId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Operation = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Key = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    RequestHash = table.Column<string>(type: "nchar(64)", fixedLength: true, maxLength: 64, nullable: false),
                    PermitId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AttachmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ResultVersion = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PermitAttachmentCommandReceipt", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PermitAttachmentCommandReceipt_PermitAttachment_AttachmentId",
                        column: x => x.AttachmentId,
                        principalSchema: "ptw",
                        principalTable: "PermitAttachment",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PermitAttachmentCommandReceipt_Permit_PermitId",
                        column: x => x.PermitId,
                        principalSchema: "ptw",
                        principalTable: "Permit",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PermitAttachment_PermitId_RemovedInVersion_UploadedAt",
                schema: "ptw",
                table: "PermitAttachment",
                columns: new[] { "PermitId", "RemovedInVersion", "UploadedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_PermitAttachment_StorageKey",
                schema: "ptw",
                table: "PermitAttachment",
                column: "StorageKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PermitAttachmentCommandReceipt_ActorId_Operation_Key",
                schema: "intg",
                table: "PermitAttachmentCommandReceipt",
                columns: new[] { "ActorId", "Operation", "Key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PermitAttachmentCommandReceipt_AttachmentId",
                schema: "intg",
                table: "PermitAttachmentCommandReceipt",
                column: "AttachmentId");

            migrationBuilder.CreateIndex(
                name: "IX_PermitAttachmentCommandReceipt_PermitId",
                schema: "intg",
                table: "PermitAttachmentCommandReceipt",
                column: "PermitId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PermitAttachmentCommandReceipt",
                schema: "intg");

            migrationBuilder.DropTable(
                name: "PermitAttachment",
                schema: "ptw");
        }
    }
}
