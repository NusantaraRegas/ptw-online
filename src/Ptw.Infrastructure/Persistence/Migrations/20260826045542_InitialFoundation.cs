using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ptw.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "audit");

            migrationBuilder.EnsureSchema(
                name: "intg");

            migrationBuilder.EnsureSchema(
                name: "ptw");

            migrationBuilder.CreateTable(
                name: "AuditEvent",
                schema: "audit",
                columns: table => new
                {
                    Sequence = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PermitId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EventType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ActorId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    PayloadJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CorrelationId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditEvent", x => x.Sequence);
                });

            migrationBuilder.CreateTable(
                name: "Idempotency",
                schema: "intg",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ActorId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Operation = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Key = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    RequestHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    PermitId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Idempotency", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OutboxMessage",
                schema: "intg",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AggregateId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EventType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    PayloadJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ProcessedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Attempts = table.Column<int>(type: "int", nullable: false),
                    NextAttemptAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    LastError = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutboxMessage", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Permit",
                schema: "ptw",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PermitNumber = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    Status = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false),
                    LocationId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    SponsorId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ValidFrom = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ValidUntil = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    DraftJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ActiveWorkPeriodId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SuspensionReason = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Permit", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PermitVersion",
                schema: "ptw",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PermitId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false),
                    ContentJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ContentHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PermitVersion", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AuditEvent_PermitId_OccurredAt",
                schema: "audit",
                table: "AuditEvent",
                columns: new[] { "PermitId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Idempotency_ActorId_Operation_Key",
                schema: "intg",
                table: "Idempotency",
                columns: new[] { "ActorId", "Operation", "Key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OutboxMessage_ProcessedAt_NextAttemptAt",
                schema: "intg",
                table: "OutboxMessage",
                columns: new[] { "ProcessedAt", "NextAttemptAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Permit_PermitNumber",
                schema: "ptw",
                table: "Permit",
                column: "PermitNumber",
                unique: true,
                filter: "[PermitNumber] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Permit_SponsorId_UpdatedAt",
                schema: "ptw",
                table: "Permit",
                columns: new[] { "SponsorId", "UpdatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Permit_Status_LocationId",
                schema: "ptw",
                table: "Permit",
                columns: new[] { "Status", "LocationId" });

            migrationBuilder.CreateIndex(
                name: "IX_PermitVersion_PermitId_Version",
                schema: "ptw",
                table: "PermitVersion",
                columns: new[] { "PermitId", "Version" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AuditEvent",
                schema: "audit");

            migrationBuilder.DropTable(
                name: "Idempotency",
                schema: "intg");

            migrationBuilder.DropTable(
                name: "OutboxMessage",
                schema: "intg");

            migrationBuilder.DropTable(
                name: "Permit",
                schema: "ptw");

            migrationBuilder.DropTable(
                name: "PermitVersion",
                schema: "ptw");
        }
    }
}
