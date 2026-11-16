using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hadron.Erp.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddForecastPlanEntryCorrections : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "forecast_plan_entry_corrections",
                schema: "planning",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ForecastPlanEntryId = table.Column<Guid>(type: "uuid", nullable: false),
                    ForecastRevisionId = table.Column<Guid>(type: "uuid", nullable: false),
                    RevisionNumber = table.Column<int>(type: "integer", nullable: false),
                    ForecastQty = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    Reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    ActorIdentifier = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_forecast_plan_entry_corrections", x => x.Id);
                    table.CheckConstraint("ck_forecast_plan_entry_corrections_number", "\"RevisionNumber\" >= 2");
                    table.CheckConstraint("ck_forecast_plan_entry_corrections_qty", "\"ForecastQty\" >= 0");
                    table.CheckConstraint("ck_forecast_plan_entry_corrections_reason", "btrim(\"Reason\") <> ''");
                    table.ForeignKey(
                        name: "FK_forecast_plan_entry_corrections_forecast_plan_entries_Forec~",
                        column: x => x.ForecastPlanEntryId,
                        principalSchema: "planning",
                        principalTable: "forecast_plan_entries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_forecast_plan_entry_corrections_forecast_revisions_Forecast~",
                        column: x => x.ForecastRevisionId,
                        principalSchema: "planning",
                        principalTable: "forecast_revisions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_forecast_plan_entry_corrections_ForecastRevisionId",
                schema: "planning",
                table: "forecast_plan_entry_corrections",
                column: "ForecastRevisionId");

            migrationBuilder.CreateIndex(
                name: "ux_forecast_plan_entry_correction_revision",
                schema: "planning",
                table: "forecast_plan_entry_corrections",
                columns: new[] { "ForecastPlanEntryId", "RevisionNumber" },
                unique: true);

            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION planning.validate_forecast_plan_entry_correction()
                RETURNS trigger
                LANGUAGE plpgsql
                AS $function$
                DECLARE
                    expected_revision integer;
                BEGIN
                    SELECT COALESCE(MAX(correction."RevisionNumber"), 1) + 1
                    INTO expected_revision
                    FROM planning.forecast_plan_entry_corrections correction
                    WHERE correction."ForecastPlanEntryId" = NEW."ForecastPlanEntryId";

                    IF NEW."RevisionNumber" <> expected_revision THEN
                        RAISE EXCEPTION 'FORECAST_PLAN_CORRECTION_REVISION_INVALID';
                    END IF;

                    IF NOT EXISTS (
                        SELECT 1
                        FROM planning.forecast_plan_entries entry
                        JOIN planning.forecast_revisions original
                          ON original."Id" = entry."ForecastRevisionId"
                        JOIN planning.forecast_revisions revision
                          ON revision."Id" = NEW."ForecastRevisionId"
                        JOIN planning.forecast_series series
                          ON series."Id" = original."ForecastSeriesId"
                         AND series."Id" = revision."ForecastSeriesId"
                        WHERE entry."Id" = NEW."ForecastPlanEntryId"
                          AND series."ProductId" = entry."ProductId"
                          AND series."BusinessPeriodId" = entry."BusinessPeriodId"
                          AND series."CurrentRevisionId" = revision."Id"
                          AND revision."ForecastQty" = NEW."ForecastQty"
                    ) THEN
                        RAISE EXCEPTION 'FORECAST_PLAN_CORRECTION_LINEAGE_MISMATCH';
                    END IF;
                    RETURN NEW;
                END;
                $function$;

                CREATE TRIGGER trg_forecast_plan_entry_corrections_immutable
                BEFORE UPDATE OR DELETE ON planning.forecast_plan_entry_corrections
                FOR EACH ROW EXECUTE FUNCTION planning.reject_forecast_plan_history_mutation();

                CREATE TRIGGER trg_forecast_plan_entry_corrections_lineage
                BEFORE INSERT ON planning.forecast_plan_entry_corrections
                FOR EACH ROW EXECUTE FUNCTION planning.validate_forecast_plan_entry_correction();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "forecast_plan_entry_corrections",
                schema: "planning");

            migrationBuilder.Sql("DROP FUNCTION IF EXISTS planning.validate_forecast_plan_entry_correction();");
        }
    }
}
