using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ptw.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class UserAuthorizationFramework : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "sec");

            migrationBuilder.CreateTable(
                name: "UserAuthorization",
                schema: "sec",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SubjectId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    RoleCode = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ActionCodesJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    LocationId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    IncludeDescendants = table.Column<bool>(type: "bit", nullable: false),
                    RequiredCompetencyCodesJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    SourceAuthorizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    EffectiveFrom = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    EffectiveUntil = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false),
                    MakerId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    CheckerId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ApprovedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserAuthorization", x => x.Id);
                    table.CheckConstraint("CK_UserAuthorization_EffectivePeriod", "[EffectiveUntil] IS NULL OR [EffectiveUntil] > [EffectiveFrom]");
                    table.ForeignKey(
                        name: "FK_UserAuthorization_LocationMaster_LocationId",
                        column: x => x.LocationId,
                        principalSchema: "cfg",
                        principalTable: "LocationMaster",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_UserAuthorization_UserAuthorization_SourceAuthorizationId",
                        column: x => x.SourceAuthorizationId,
                        principalSchema: "sec",
                        principalTable: "UserAuthorization",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AuthorizationCommandReceipt",
                schema: "intg",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ActorId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Operation = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Key = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    RequestHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    UserAuthorizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ResultVersion = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuthorizationCommandReceipt", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AuthorizationCommandReceipt_UserAuthorization_UserAuthorizationId",
                        column: x => x.UserAuthorizationId,
                        principalSchema: "sec",
                        principalTable: "UserAuthorization",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "UserAuthorizationVersion",
                schema: "sec",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserAuthorizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false),
                    ContentJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ContentHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserAuthorizationVersion", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserAuthorizationVersion_UserAuthorization_UserAuthorizationId",
                        column: x => x.UserAuthorizationId,
                        principalSchema: "sec",
                        principalTable: "UserAuthorization",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AuthorizationCommandReceipt_ActorId_Operation_Key",
                schema: "intg",
                table: "AuthorizationCommandReceipt",
                columns: new[] { "ActorId", "Operation", "Key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AuthorizationCommandReceipt_UserAuthorizationId",
                schema: "intg",
                table: "AuthorizationCommandReceipt",
                column: "UserAuthorizationId");

            migrationBuilder.CreateIndex(
                name: "IX_UserAuthorization_LocationId_RoleCode_Status",
                schema: "sec",
                table: "UserAuthorization",
                columns: new[] { "LocationId", "RoleCode", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_UserAuthorization_SourceAuthorizationId",
                schema: "sec",
                table: "UserAuthorization",
                column: "SourceAuthorizationId");

            migrationBuilder.CreateIndex(
                name: "IX_UserAuthorization_SubjectId_Status_EffectiveFrom_EffectiveUntil",
                schema: "sec",
                table: "UserAuthorization",
                columns: new[] { "SubjectId", "Status", "EffectiveFrom", "EffectiveUntil" });

            migrationBuilder.CreateIndex(
                name: "IX_UserAuthorizationVersion_UserAuthorizationId_Version",
                schema: "sec",
                table: "UserAuthorizationVersion",
                columns: new[] { "UserAuthorizationId", "Version" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AuthorizationCommandReceipt",
                schema: "intg");

            migrationBuilder.DropTable(
                name: "UserAuthorizationVersion",
                schema: "sec");

            migrationBuilder.DropTable(
                name: "UserAuthorization",
                schema: "sec");
        }
    }
}
