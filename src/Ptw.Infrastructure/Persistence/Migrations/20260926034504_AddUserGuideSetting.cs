using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ptw.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddUserGuideSetting : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "UserGuideVersion",
                schema: "cfg",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    Sha256 = table.Column<string>(type: "nchar(64)", fixedLength: true, maxLength: 64, nullable: false),
                    StorageKey = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    ScanEvidenceReference = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    ScannedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UploadedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UploadedBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserGuideVersion", x => x.Id);
                    table.CheckConstraint("CK_UserGuideVersion_Size", "[SizeBytes] > 0");
                });

            migrationBuilder.CreateTable(
                name: "UserGuideCommandReceipt",
                schema: "intg",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ActorId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Operation = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Key = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    RequestHash = table.Column<string>(type: "nchar(64)", fixedLength: true, maxLength: 64, nullable: false),
                    ResultVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ResultSettingVersion = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserGuideCommandReceipt", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserGuideCommandReceipt_UserGuideVersion_ResultVersionId",
                        column: x => x.ResultVersionId,
                        principalSchema: "cfg",
                        principalTable: "UserGuideVersion",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "UserGuideSetting",
                schema: "cfg",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CurrentVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserGuideSetting", x => x.Id);
                    table.CheckConstraint("CK_UserGuideSetting_Version", "[Version] > 0");
                    table.ForeignKey(
                        name: "FK_UserGuideSetting_UserGuideVersion_CurrentVersionId",
                        column: x => x.CurrentVersionId,
                        principalSchema: "cfg",
                        principalTable: "UserGuideVersion",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_UserGuideCommandReceipt_ActorId_Operation_Key",
                schema: "intg",
                table: "UserGuideCommandReceipt",
                columns: new[] { "ActorId", "Operation", "Key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserGuideCommandReceipt_ResultVersionId",
                schema: "intg",
                table: "UserGuideCommandReceipt",
                column: "ResultVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_UserGuideSetting_CurrentVersionId",
                schema: "cfg",
                table: "UserGuideSetting",
                column: "CurrentVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_UserGuideVersion_StorageKey",
                schema: "cfg",
                table: "UserGuideVersion",
                column: "StorageKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserGuideVersion_Version",
                schema: "cfg",
                table: "UserGuideVersion",
                column: "Version",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "UserGuideCommandReceipt",
                schema: "intg");

            migrationBuilder.DropTable(
                name: "UserGuideSetting",
                schema: "cfg");

            migrationBuilder.DropTable(
                name: "UserGuideVersion",
                schema: "cfg");
        }
    }
}
