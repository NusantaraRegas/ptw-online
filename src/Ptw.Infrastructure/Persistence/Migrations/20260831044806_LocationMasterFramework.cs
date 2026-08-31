using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ptw.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class LocationMasterFramework : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "cfg");

            migrationBuilder.CreateTable(
                name: "ConfigurationAuditEvent",
                schema: "audit",
                columns: table => new
                {
                    Sequence = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AggregateType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    AggregateId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EventType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ActorId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    PayloadJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CorrelationId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConfigurationAuditEvent", x => x.Sequence);
                });

            migrationBuilder.CreateTable(
                name: "LocationMaster",
                schema: "cfg",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ParentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
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
                    table.PrimaryKey("PK_LocationMaster", x => x.Id);
                    table.CheckConstraint("CK_LocationMaster_EffectivePeriod", "[EffectiveUntil] IS NULL OR [EffectiveUntil] > [EffectiveFrom]");
                    table.ForeignKey(
                        name: "FK_LocationMaster_LocationMaster_ParentId",
                        column: x => x.ParentId,
                        principalSchema: "cfg",
                        principalTable: "LocationMaster",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "LocationCommandReceipt",
                schema: "intg",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ActorId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Operation = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Key = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    RequestHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    LocationMasterId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ResultVersion = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LocationCommandReceipt", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LocationCommandReceipt_LocationMaster_LocationMasterId",
                        column: x => x.LocationMasterId,
                        principalSchema: "cfg",
                        principalTable: "LocationMaster",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "LocationMasterVersion",
                schema: "cfg",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LocationMasterId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false),
                    ContentJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ContentHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LocationMasterVersion", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LocationMasterVersion_LocationMaster_LocationMasterId",
                        column: x => x.LocationMasterId,
                        principalSchema: "cfg",
                        principalTable: "LocationMaster",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ConfigurationAuditEvent_AggregateType_AggregateId_Sequence",
                schema: "audit",
                table: "ConfigurationAuditEvent",
                columns: new[] { "AggregateType", "AggregateId", "Sequence" });

            migrationBuilder.CreateIndex(
                name: "IX_LocationCommandReceipt_ActorId_Operation_Key",
                schema: "intg",
                table: "LocationCommandReceipt",
                columns: new[] { "ActorId", "Operation", "Key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LocationCommandReceipt_LocationMasterId",
                schema: "intg",
                table: "LocationCommandReceipt",
                column: "LocationMasterId");

            migrationBuilder.CreateIndex(
                name: "IX_LocationMaster_Code_EffectiveFrom",
                schema: "cfg",
                table: "LocationMaster",
                columns: new[] { "Code", "EffectiveFrom" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LocationMaster_ParentId",
                schema: "cfg",
                table: "LocationMaster",
                column: "ParentId");

            migrationBuilder.CreateIndex(
                name: "IX_LocationMaster_Status_EffectiveFrom_EffectiveUntil",
                schema: "cfg",
                table: "LocationMaster",
                columns: new[] { "Status", "EffectiveFrom", "EffectiveUntil" });

            migrationBuilder.CreateIndex(
                name: "IX_LocationMasterVersion_LocationMasterId_Version",
                schema: "cfg",
                table: "LocationMasterVersion",
                columns: new[] { "LocationMasterId", "Version" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ConfigurationAuditEvent",
                schema: "audit");

            migrationBuilder.DropTable(
                name: "LocationCommandReceipt",
                schema: "intg");

            migrationBuilder.DropTable(
                name: "LocationMasterVersion",
                schema: "cfg");

            migrationBuilder.DropTable(
                name: "LocationMaster",
                schema: "cfg");
        }
    }
}
