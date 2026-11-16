using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hadron.Erp.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAnalyticsLeadTimeApplicationHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "lead_time_recommendation_applications",
                schema: "procurement",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SourcingOptionId = table.Column<Guid>(type: "uuid", nullable: false),
                    StageCode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    PreviousDays = table.Column<int>(type: "integer", nullable: true),
                    RecommendedDays = table.Column<int>(type: "integer", nullable: false),
                    AppliedDays = table.Column<int>(type: "integer", nullable: false),
                    SampleCount = table.Column<int>(type: "integer", nullable: false),
                    WindowStartExclusive = table.Column<DateOnly>(type: "date", nullable: false),
                    WindowEndInclusive = table.Column<DateOnly>(type: "date", nullable: false),
                    Method = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    ActorIdentifier = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    AppliedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_lead_time_recommendation_applications", x => x.Id);
                    table.CheckConstraint("ck_lead_time_application_stage", "\"StageCode\" IN ('SUPPLIER','FORWARDER_PRE_SHIPMENT','SHIPPING','DESTINATION_CUSTOMS')");
                    table.CheckConstraint("ck_lead_time_application_values", "\"RecommendedDays\" >= 0 AND \"AppliedDays\" >= 0 AND \"SampleCount\" >= 5");
                    table.CheckConstraint("ck_lead_time_application_window", "\"WindowEndInclusive\" > \"WindowStartExclusive\"");
                    table.ForeignKey(
                        name: "FK_lead_time_recommendation_applications_sourcing_options_Sour~",
                        column: x => x.SourcingOptionId,
                        principalSchema: "procurement",
                        principalTable: "sourcing_options",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_lead_time_recommendation_applications_SourcingOptionId_Appl~",
                schema: "procurement",
                table: "lead_time_recommendation_applications",
                columns: new[] { "SourcingOptionId", "AppliedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "lead_time_recommendation_applications",
                schema: "procurement");
        }
    }
}
