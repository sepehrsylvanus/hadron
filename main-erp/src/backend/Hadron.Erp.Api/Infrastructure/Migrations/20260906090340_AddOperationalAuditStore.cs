using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hadron.Erp.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddOperationalAuditStore : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "platform");

            migrationBuilder.CreateTable(
                name: "operational_audit_events",
                schema: "platform",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EntityType = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    EntityId = table.Column<Guid>(type: "uuid", nullable: false),
                    Action = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    OccurredAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ActorId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    CorrelationId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    PreviousValuesJson = table.Column<string>(type: "jsonb", nullable: true),
                    CurrentValuesJson = table.Column<string>(type: "jsonb", nullable: true),
                    Reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Source = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_operational_audit_events", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_operational_audit_events_CorrelationId",
                schema: "platform",
                table: "operational_audit_events",
                column: "CorrelationId");

            migrationBuilder.CreateIndex(
                name: "IX_operational_audit_events_EntityType_EntityId_OccurredAtUtc",
                schema: "platform",
                table: "operational_audit_events",
                columns: new[] { "EntityType", "EntityId", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_operational_audit_events_OccurredAtUtc",
                schema: "platform",
                table: "operational_audit_events",
                column: "OccurredAtUtc");

            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION platform.guard_operational_audit_append_only() RETURNS trigger AS $$
                BEGIN
                    RAISE EXCEPTION 'Operational audit events are append-only' USING ERRCODE = '23514';
                END;
                $$ LANGUAGE plpgsql;

                CREATE TRIGGER trg_operational_audit_append_only
                BEFORE UPDATE OR DELETE ON platform.operational_audit_events
                FOR EACH ROW EXECUTE FUNCTION platform.guard_operational_audit_append_only();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER IF EXISTS trg_operational_audit_append_only ON platform.operational_audit_events;
                DROP FUNCTION IF EXISTS platform.guard_operational_audit_append_only();
                """);

            migrationBuilder.DropTable(
                name: "operational_audit_events",
                schema: "platform");
        }
    }
}
