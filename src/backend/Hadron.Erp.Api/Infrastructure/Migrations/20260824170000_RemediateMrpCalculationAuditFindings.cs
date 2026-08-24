using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hadron.Erp.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RemediateMrpCalculationAuditFindings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "FirmCoverageDays",
                schema: "planning",
                table: "mrp_run_item_summaries",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "StockCoverageDays",
                schema: "planning",
                table: "mrp_run_item_summaries",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "mrp_run_external_source_inputs",
                schema: "planning",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RunId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourcingOptionId = table.Column<Guid>(type: "uuid", nullable: false),
                    ItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    OptionIsActive = table.Column<bool>(type: "boolean", nullable: false),
                    SupplierIsActive = table.Column<bool>(type: "boolean", nullable: false),
                    ItemIsActive = table.Column<bool>(type: "boolean", nullable: false),
                    ItemBaseUnitId = table.Column<Guid>(type: "uuid", nullable: false),
                    MinimumOrderQuantityIsSet = table.Column<bool>(type: "boolean", nullable: false),
                    ShippingModeIsActive = table.Column<bool>(type: "boolean", nullable: false),
                    IncotermIsValid = table.Column<bool>(type: "boolean", nullable: false),
                    AllLeadStagesAreSet = table.Column<bool>(type: "boolean", nullable: false),
                    PriceId = table.Column<Guid>(type: "uuid", nullable: true),
                    CurrencyIsActive = table.Column<bool>(type: "boolean", nullable: true),
                    EffectiveFrom = table.Column<DateOnly>(type: "date", nullable: true),
                    EffectiveTo = table.Column<DateOnly>(type: "date", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_mrp_run_external_source_inputs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_mrp_run_external_source_inputs_mrp_runs_RunId",
                        column: x => x.RunId,
                        principalSchema: "planning",
                        principalTable: "mrp_runs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "mrp_run_forecast_period_inputs",
                schema: "planning",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RunId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    BusinessPeriodId = table.Column<Guid>(type: "uuid", nullable: false),
                    ForecastSeriesId = table.Column<Guid>(type: "uuid", nullable: true),
                    ForecastRevisionId = table.Column<Guid>(type: "uuid", nullable: true),
                    OfficialForecastQty = table.Column<decimal>(type: "numeric(28,12)", precision: 28, scale: 12, nullable: true),
                    ActualMtdQty = table.Column<decimal>(type: "numeric(28,12)", precision: 28, scale: 12, nullable: false),
                    RemainingForecastQty = table.Column<decimal>(type: "numeric(28,12)", precision: 28, scale: 12, nullable: true),
                    ProductBaseUnitId = table.Column<Guid>(type: "uuid", nullable: false),
                    PeriodStartDate = table.Column<DateOnly>(type: "date", nullable: false),
                    PeriodEndDate = table.Column<DateOnly>(type: "date", nullable: false),
                    EligibleAllocationStart = table.Column<DateOnly>(type: "date", nullable: false),
                    AllocationState = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    ExceptionCode = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_mrp_run_forecast_period_inputs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_mrp_run_forecast_period_inputs_mrp_runs_RunId",
                        column: x => x.RunId,
                        principalSchema: "planning",
                        principalTable: "mrp_runs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_mrp_run_external_source_inputs_RunId_ItemId",
                schema: "planning",
                table: "mrp_run_external_source_inputs",
                columns: new[] { "RunId", "ItemId" });

            migrationBuilder.CreateIndex(
                name: "IX_mrp_run_external_source_inputs_RunId_SourcingOptionId_Price~",
                schema: "planning",
                table: "mrp_run_external_source_inputs",
                columns: new[] { "RunId", "SourcingOptionId", "PriceId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_mrp_run_forecast_period_inputs_RunId_PeriodStartDate_Period~",
                schema: "planning",
                table: "mrp_run_forecast_period_inputs",
                columns: new[] { "RunId", "PeriodStartDate", "PeriodEndDate" });

            migrationBuilder.CreateIndex(
                name: "IX_mrp_run_forecast_period_inputs_RunId_ProductId_BusinessPeri~",
                schema: "planning",
                table: "mrp_run_forecast_period_inputs",
                columns: new[] { "RunId", "ProductId", "BusinessPeriodId" },
                unique: true);

            migrationBuilder.Sql("""
                CREATE TRIGGER trg_mrp_run_external_source_inputs_immutable
                BEFORE INSERT OR UPDATE OR DELETE ON planning.mrp_run_external_source_inputs
                FOR EACH ROW EXECUTE FUNCTION planning.guard_mrp_run_child();

                CREATE TRIGGER trg_mrp_run_forecast_period_inputs_immutable
                BEFORE INSERT OR UPDATE OR DELETE ON planning.mrp_run_forecast_period_inputs
                FOR EACH ROW EXECUTE FUNCTION planning.guard_mrp_run_child();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "mrp_run_external_source_inputs",
                schema: "planning");

            migrationBuilder.DropTable(
                name: "mrp_run_forecast_period_inputs",
                schema: "planning");

            migrationBuilder.DropColumn(
                name: "FirmCoverageDays",
                schema: "planning",
                table: "mrp_run_item_summaries");

            migrationBuilder.DropColumn(
                name: "StockCoverageDays",
                schema: "planning",
                table: "mrp_run_item_summaries");
        }
    }
}
