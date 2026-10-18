using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hadron.Erp.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddNamedForecastPlans : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "forecast_plans",
                schema: "planning",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    StartJalaliYear = table.Column<int>(type: "integer", nullable: false),
                    StartJalaliMonth = table.Column<int>(type: "integer", nullable: false),
                    EndJalaliYear = table.Column<int>(type: "integer", nullable: false),
                    EndJalaliMonth = table.Column<int>(type: "integer", nullable: false),
                    StartDate = table.Column<DateOnly>(type: "date", nullable: false),
                    EndDate = table.Column<DateOnly>(type: "date", nullable: false),
                    ActorIdentifier = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_forecast_plans", x => x.Id);
                    table.CheckConstraint("ck_forecast_plans_dates", "\"EndDate\" >= \"StartDate\"");
                    table.CheckConstraint("ck_forecast_plans_end_month", "\"EndJalaliMonth\" BETWEEN 1 AND 12");
                    table.CheckConstraint("ck_forecast_plans_name", "btrim(\"Name\") <> ''");
                    table.CheckConstraint("ck_forecast_plans_start_month", "\"StartJalaliMonth\" BETWEEN 1 AND 12");
                });

            migrationBuilder.CreateTable(
                name: "forecast_plan_entries",
                schema: "planning",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ForecastPlanId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    BusinessPeriodId = table.Column<Guid>(type: "uuid", nullable: false),
                    ForecastRevisionId = table.Column<Guid>(type: "uuid", nullable: false),
                    ForecastQty = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_forecast_plan_entries", x => x.Id);
                    table.CheckConstraint("ck_forecast_plan_entries_qty", "\"ForecastQty\" >= 0");
                    table.ForeignKey(
                        name: "FK_forecast_plan_entries_business_periods_BusinessPeriodId",
                        column: x => x.BusinessPeriodId,
                        principalSchema: "planning",
                        principalTable: "business_periods",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_forecast_plan_entries_forecast_plans_ForecastPlanId",
                        column: x => x.ForecastPlanId,
                        principalSchema: "planning",
                        principalTable: "forecast_plans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_forecast_plan_entries_forecast_revisions_ForecastRevisionId",
                        column: x => x.ForecastRevisionId,
                        principalSchema: "planning",
                        principalTable: "forecast_revisions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_forecast_plan_entries_products_ProductId",
                        column: x => x.ProductId,
                        principalSchema: "catalog",
                        principalTable: "products",
                        principalColumn: "ItemId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_forecast_plan_entries_BusinessPeriodId",
                schema: "planning",
                table: "forecast_plan_entries",
                column: "BusinessPeriodId");

            migrationBuilder.CreateIndex(
                name: "ux_forecast_plan_entry_plan_product_period",
                schema: "planning",
                table: "forecast_plan_entries",
                columns: new[] { "ForecastPlanId", "ProductId", "BusinessPeriodId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_forecast_plan_entries_ForecastRevisionId",
                schema: "planning",
                table: "forecast_plan_entries",
                column: "ForecastRevisionId");

            migrationBuilder.CreateIndex(
                name: "IX_forecast_plan_entries_ProductId",
                schema: "planning",
                table: "forecast_plan_entries",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_forecast_plans_CreatedAtUtc",
                schema: "planning",
                table: "forecast_plans",
                column: "CreatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_forecast_plans_StartDate_EndDate",
                schema: "planning",
                table: "forecast_plans",
                columns: new[] { "StartDate", "EndDate" });

            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION planning.reject_forecast_plan_history_mutation()
                RETURNS trigger
                LANGUAGE plpgsql
                AS $function$
                BEGIN
                    RAISE EXCEPTION 'FORECAST_PLAN_HISTORY_IMMUTABLE';
                END;
                $function$;

                CREATE OR REPLACE FUNCTION planning.validate_forecast_plan_entry_lineage()
                RETURNS trigger
                LANGUAGE plpgsql
                AS $function$
                BEGIN
                    IF NOT EXISTS (
                        SELECT 1
                        FROM planning.forecast_revisions revision
                        JOIN planning.forecast_series series
                          ON series."Id" = revision."ForecastSeriesId"
                        WHERE revision."Id" = NEW."ForecastRevisionId"
                          AND series."ProductId" = NEW."ProductId"
                          AND series."BusinessPeriodId" = NEW."BusinessPeriodId"
                    ) THEN
                        RAISE EXCEPTION 'FORECAST_PLAN_ENTRY_LINEAGE_MISMATCH';
                    END IF;
                    RETURN NEW;
                END;
                $function$;

                CREATE TRIGGER trg_forecast_plans_immutable
                BEFORE UPDATE OR DELETE ON planning.forecast_plans
                FOR EACH ROW EXECUTE FUNCTION planning.reject_forecast_plan_history_mutation();

                CREATE TRIGGER trg_forecast_plan_entries_immutable
                BEFORE UPDATE OR DELETE ON planning.forecast_plan_entries
                FOR EACH ROW EXECUTE FUNCTION planning.reject_forecast_plan_history_mutation();

                CREATE TRIGGER trg_forecast_plan_entries_lineage
                BEFORE INSERT ON planning.forecast_plan_entries
                FOR EACH ROW EXECUTE FUNCTION planning.validate_forecast_plan_entry_lineage();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "forecast_plan_entries",
                schema: "planning");

            migrationBuilder.DropTable(
                name: "forecast_plans",
                schema: "planning");

            migrationBuilder.Sql("DROP FUNCTION IF EXISTS planning.validate_forecast_plan_entry_lineage();");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS planning.reject_forecast_plan_history_mutation();");
        }
    }
}
