using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hadron.Erp.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAutomaticMrpRefreshOutbox : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "mrp_refresh_outbox",
                schema: "planning",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Source = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    SourceEntityTypes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    SourceEntityId = table.Column<Guid>(type: "uuid", nullable: true),
                    CorrelationId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    OccurredAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AvailableAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CompletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RunId = table.Column<Guid>(type: "uuid", nullable: true),
                    ErrorCode = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ErrorMessage = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_mrp_refresh_outbox", x => x.Id);
                    table.CheckConstraint("CK_mrp_refresh_outbox_attempt_count", "\"AttemptCount\" >= 0");
                    table.CheckConstraint("CK_mrp_refresh_outbox_status", "\"Status\" IN ('PENDING','PROCESSING','COMPLETED','FAILED')");
                    table.ForeignKey(
                        name: "FK_mrp_refresh_outbox_mrp_runs_RunId",
                        column: x => x.RunId,
                        principalSchema: "planning",
                        principalTable: "mrp_runs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_mrp_refresh_outbox_OccurredAtUtc",
                schema: "planning",
                table: "mrp_refresh_outbox",
                column: "OccurredAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_mrp_refresh_outbox_RunId",
                schema: "planning",
                table: "mrp_refresh_outbox",
                column: "RunId");

            migrationBuilder.CreateIndex(
                name: "IX_mrp_refresh_outbox_Status_AvailableAtUtc",
                schema: "planning",
                table: "mrp_refresh_outbox",
                columns: new[] { "Status", "AvailableAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "mrp_refresh_outbox",
                schema: "planning");
        }
    }
}
