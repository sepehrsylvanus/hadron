using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hadron.Erp.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMrpCalculationEngine : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateSequence(
                name: "mrp_run_number_seq",
                schema: "planning");

            migrationBuilder.CreateTable(
                name: "mrp_runs",
                schema: "planning",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RunNumber = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    AsOfDate = table.Column<DateOnly>(type: "date", nullable: false),
                    HorizonEndDate = table.Column<DateOnly>(type: "date", nullable: false),
                    EngineVersion = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Completeness = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Assumptions = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    ActorIdentifier = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    StartedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    SnapshotCapturedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CompletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    FailureCode = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    FailureMessage = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_mrp_runs", x => x.Id);
                    table.CheckConstraint("ck_mrp_runs_completeness", "\"Completeness\" IN ('COMPLETE','COMPLETE_WITH_WARNINGS','INCOMPLETE')");
                    table.CheckConstraint("ck_mrp_runs_horizon", "\"HorizonEndDate\" >= \"AsOfDate\"");
                    table.CheckConstraint("ck_mrp_runs_number", "\"RunNumber\" ~ '^MRP-RUN-[0-9]{6,}$'");
                    table.CheckConstraint("ck_mrp_runs_status", "\"Status\" IN ('RUNNING','COMPLETED','COMPLETED_WITH_EXCEPTIONS','FAILED')");
                });

            migrationBuilder.CreateTable(
                name: "mrp_run_bom_inputs",
                schema: "planning",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RunId = table.Column<Guid>(type: "uuid", nullable: false),
                    RevisionId = table.Column<Guid>(type: "uuid", nullable: false),
                    RevisionCode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    LineId = table.Column<Guid>(type: "uuid", nullable: false),
                    ParentItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    ChildItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    ParentBaseUnitId = table.Column<Guid>(type: "uuid", nullable: false),
                    ChildBaseUnitId = table.Column<Guid>(type: "uuid", nullable: false),
                    BaseOutputQty = table.Column<decimal>(type: "numeric(28,12)", precision: 28, scale: 12, nullable: false),
                    UsageQty = table.Column<decimal>(type: "numeric(28,12)", precision: 28, scale: 12, nullable: false),
                    ScrapOverridePercent = table.Column<decimal>(type: "numeric(28,12)", precision: 28, scale: 12, nullable: true),
                    EffectiveFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    EffectiveTo = table.Column<DateOnly>(type: "date", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_mrp_run_bom_inputs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_mrp_run_bom_inputs_mrp_runs_RunId",
                        column: x => x.RunId,
                        principalSchema: "planning",
                        principalTable: "mrp_runs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "mrp_run_daily_results",
                schema: "planning",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RunId = table.Column<Guid>(type: "uuid", nullable: false),
                    ItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    GrossDemand = table.Column<decimal>(type: "numeric(28,12)", precision: 28, scale: 12, nullable: false),
                    FirmIncoming = table.Column<decimal>(type: "numeric(28,12)", precision: 28, scale: 12, nullable: false),
                    FirmProjected = table.Column<decimal>(type: "numeric(28,12)", precision: 28, scale: 12, nullable: false),
                    PlanningBeforeNeed = table.Column<decimal>(type: "numeric(28,12)", precision: 28, scale: 12, nullable: false),
                    NetRequirement = table.Column<decimal>(type: "numeric(28,12)", precision: 28, scale: 12, nullable: false),
                    PlanningBalance = table.Column<decimal>(type: "numeric(28,12)", precision: 28, scale: 12, nullable: false),
                    IsRootProduct = table.Column<bool>(type: "boolean", nullable: false),
                    Completeness = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_mrp_run_daily_results", x => x.Id);
                    table.ForeignKey(
                        name: "FK_mrp_run_daily_results_mrp_runs_RunId",
                        column: x => x.RunId,
                        principalSchema: "planning",
                        principalTable: "mrp_runs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "mrp_run_demand_traces",
                schema: "planning",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RunId = table.Column<Guid>(type: "uuid", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    ParentItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    ChildItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    BomRevisionId = table.Column<Guid>(type: "uuid", nullable: true),
                    BomLineId = table.Column<Guid>(type: "uuid", nullable: true),
                    ParentNetRequirement = table.Column<decimal>(type: "numeric(28,12)", precision: 28, scale: 12, nullable: false),
                    BaseRequirement = table.Column<decimal>(type: "numeric(28,12)", precision: 28, scale: 12, nullable: false),
                    ScrapPercent = table.Column<decimal>(type: "numeric(28,12)", precision: 28, scale: 12, nullable: false),
                    ScrapSource = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    ChildGrossRequirement = table.Column<decimal>(type: "numeric(28,12)", precision: 28, scale: 12, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_mrp_run_demand_traces", x => x.Id);
                    table.ForeignKey(
                        name: "FK_mrp_run_demand_traces_mrp_runs_RunId",
                        column: x => x.RunId,
                        principalSchema: "planning",
                        principalTable: "mrp_runs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "mrp_run_exceptions",
                schema: "planning",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RunId = table.Column<Guid>(type: "uuid", nullable: false),
                    ItemId = table.Column<Guid>(type: "uuid", nullable: true),
                    Date = table.Column<DateOnly>(type: "date", nullable: true),
                    Code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Severity = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    AffectsDemand = table.Column<bool>(type: "boolean", nullable: false),
                    Message = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    SourceReference = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_mrp_run_exceptions", x => x.Id);
                    table.CheckConstraint("ck_mrp_run_exception_severity", "\"Severity\" IN ('ERROR','WARNING','INFO')");
                    table.ForeignKey(
                        name: "FK_mrp_run_exceptions_mrp_runs_RunId",
                        column: x => x.RunId,
                        principalSchema: "planning",
                        principalTable: "mrp_runs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "mrp_run_firm_supply_inputs",
                schema: "planning",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RunId = table.Column<Guid>(type: "uuid", nullable: false),
                    ItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    PurchaseOrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    PurchaseOrderNumber = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    PurchaseOrderLineId = table.Column<Guid>(type: "uuid", nullable: false),
                    DeliveryScheduleId = table.Column<Guid>(type: "uuid", nullable: false),
                    OpenQty = table.Column<decimal>(type: "numeric(28,12)", precision: 28, scale: 12, nullable: false),
                    Eta = table.Column<DateOnly>(type: "date", nullable: true),
                    EtaSource = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    EtaStatus = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    PurchaseOrderStatus = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_mrp_run_firm_supply_inputs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_mrp_run_firm_supply_inputs_mrp_runs_RunId",
                        column: x => x.RunId,
                        principalSchema: "planning",
                        principalTable: "mrp_runs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "mrp_run_forecast_inputs",
                schema: "planning",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RunId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    BusinessPeriodId = table.Column<Guid>(type: "uuid", nullable: false),
                    ForecastSeriesId = table.Column<Guid>(type: "uuid", nullable: false),
                    ForecastRevisionId = table.Column<Guid>(type: "uuid", nullable: false),
                    DemandDate = table.Column<DateOnly>(type: "date", nullable: false),
                    DemandQty = table.Column<decimal>(type: "numeric(28,12)", precision: 28, scale: 12, nullable: false),
                    PeriodForecastQty = table.Column<decimal>(type: "numeric(28,12)", precision: 28, scale: 12, nullable: false),
                    ActualMtd = table.Column<decimal>(type: "numeric(28,12)", precision: 28, scale: 12, nullable: false),
                    UnitId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_mrp_run_forecast_inputs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_mrp_run_forecast_inputs_mrp_runs_RunId",
                        column: x => x.RunId,
                        principalSchema: "planning",
                        principalTable: "mrp_runs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "mrp_run_inventory_inputs",
                schema: "planning",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RunId = table.Column<Guid>(type: "uuid", nullable: false),
                    ItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    WarehouseId = table.Column<Guid>(type: "uuid", nullable: false),
                    WarehouseCode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    WarehouseIsActive = table.Column<bool>(type: "boolean", nullable: false),
                    UnitId = table.Column<Guid>(type: "uuid", nullable: false),
                    OnHandQty = table.Column<decimal>(type: "numeric(28,12)", precision: 28, scale: 12, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_mrp_run_inventory_inputs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_mrp_run_inventory_inputs_mrp_runs_RunId",
                        column: x => x.RunId,
                        principalSchema: "planning",
                        principalTable: "mrp_runs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "mrp_run_item_inputs",
                schema: "planning",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RunId = table.Column<Guid>(type: "uuid", nullable: false),
                    ItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    ItemCode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ItemType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    NameFa = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    NameEn = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    BaseUnitId = table.Column<Guid>(type: "uuid", nullable: false),
                    UnitCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    UnitNameFa = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    UnitNameEn = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    SupplyMethod = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    SafetyStockQty = table.Column<decimal>(type: "numeric(28,12)", precision: 28, scale: 12, nullable: false),
                    DefaultScrapPercent = table.Column<decimal>(type: "numeric(28,12)", precision: 28, scale: 12, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_mrp_run_item_inputs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_mrp_run_item_inputs_mrp_runs_RunId",
                        column: x => x.RunId,
                        principalSchema: "planning",
                        principalTable: "mrp_runs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "mrp_run_item_summaries",
                schema: "planning",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RunId = table.Column<Guid>(type: "uuid", nullable: false),
                    ItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    StartingOnHand = table.Column<decimal>(type: "numeric(28,12)", precision: 28, scale: 12, nullable: false),
                    TotalGrossDemand = table.Column<decimal>(type: "numeric(28,12)", precision: 28, scale: 12, nullable: false),
                    TotalFirmIncoming = table.Column<decimal>(type: "numeric(28,12)", precision: 28, scale: 12, nullable: false),
                    TotalNetRequirement = table.Column<decimal>(type: "numeric(28,12)", precision: 28, scale: 12, nullable: false),
                    OverdueFirmQty = table.Column<decimal>(type: "numeric(28,12)", precision: 28, scale: 12, nullable: false),
                    MissingEtaFirmQty = table.Column<decimal>(type: "numeric(28,12)", precision: 28, scale: 12, nullable: false),
                    StockoutDate = table.Column<DateOnly>(type: "date", nullable: true),
                    SafetyBreachDate = table.Column<DateOnly>(type: "date", nullable: true),
                    StockCoverageState = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    StockCoverageDate = table.Column<DateOnly>(type: "date", nullable: true),
                    FirmCoverageState = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    FirmCoverageDate = table.Column<DateOnly>(type: "date", nullable: true),
                    Completeness = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_mrp_run_item_summaries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_mrp_run_item_summaries_mrp_runs_RunId",
                        column: x => x.RunId,
                        principalSchema: "planning",
                        principalTable: "mrp_runs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "mrp_run_route_availability_inputs",
                schema: "planning",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RunId = table.Column<Guid>(type: "uuid", nullable: false),
                    ItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    DemandDate = table.Column<DateOnly>(type: "date", nullable: false),
                    BomReady = table.Column<bool>(type: "boolean", nullable: false),
                    ExternalReady = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_mrp_run_route_availability_inputs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_mrp_run_route_availability_inputs_mrp_runs_RunId",
                        column: x => x.RunId,
                        principalSchema: "planning",
                        principalTable: "mrp_runs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "mrp_run_route_decisions",
                schema: "planning",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RunId = table.Column<Guid>(type: "uuid", nullable: false),
                    ItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    Route = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Source = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_mrp_run_route_decisions", x => x.Id);
                    table.CheckConstraint("ck_mrp_run_route_decision", "\"Route\" IN ('BOM','EXTERNAL')");
                    table.ForeignKey(
                        name: "FK_mrp_run_route_decisions_mrp_runs_RunId",
                        column: x => x.RunId,
                        principalSchema: "planning",
                        principalTable: "mrp_runs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_mrp_run_bom_inputs_RunId_ParentItemId_EffectiveFrom_Effecti~",
                schema: "planning",
                table: "mrp_run_bom_inputs",
                columns: new[] { "RunId", "ParentItemId", "EffectiveFrom", "EffectiveTo" });

            migrationBuilder.CreateIndex(
                name: "IX_mrp_run_bom_inputs_RunId_RevisionId_LineId",
                schema: "planning",
                table: "mrp_run_bom_inputs",
                columns: new[] { "RunId", "RevisionId", "LineId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_mrp_run_daily_results_RunId_ItemId_Date",
                schema: "planning",
                table: "mrp_run_daily_results",
                columns: new[] { "RunId", "ItemId", "Date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_mrp_run_demand_traces_RunId_ChildItemId_Date",
                schema: "planning",
                table: "mrp_run_demand_traces",
                columns: new[] { "RunId", "ChildItemId", "Date" });

            migrationBuilder.CreateIndex(
                name: "IX_mrp_run_exceptions_RunId_ItemId_Date",
                schema: "planning",
                table: "mrp_run_exceptions",
                columns: new[] { "RunId", "ItemId", "Date" });

            migrationBuilder.CreateIndex(
                name: "IX_mrp_run_firm_supply_inputs_RunId_DeliveryScheduleId",
                schema: "planning",
                table: "mrp_run_firm_supply_inputs",
                columns: new[] { "RunId", "DeliveryScheduleId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_mrp_run_firm_supply_inputs_RunId_ItemId_Eta",
                schema: "planning",
                table: "mrp_run_firm_supply_inputs",
                columns: new[] { "RunId", "ItemId", "Eta" });

            migrationBuilder.CreateIndex(
                name: "IX_mrp_run_forecast_inputs_RunId_BusinessPeriodId",
                schema: "planning",
                table: "mrp_run_forecast_inputs",
                columns: new[] { "RunId", "BusinessPeriodId" });

            migrationBuilder.CreateIndex(
                name: "IX_mrp_run_forecast_inputs_RunId_ProductId_DemandDate",
                schema: "planning",
                table: "mrp_run_forecast_inputs",
                columns: new[] { "RunId", "ProductId", "DemandDate" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_mrp_run_inventory_inputs_RunId_ItemId_WarehouseId",
                schema: "planning",
                table: "mrp_run_inventory_inputs",
                columns: new[] { "RunId", "ItemId", "WarehouseId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_mrp_run_item_inputs_RunId_ItemId",
                schema: "planning",
                table: "mrp_run_item_inputs",
                columns: new[] { "RunId", "ItemId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_mrp_run_item_summaries_RunId_ItemId",
                schema: "planning",
                table: "mrp_run_item_summaries",
                columns: new[] { "RunId", "ItemId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_mrp_run_route_availability_inputs_RunId_ItemId_DemandDate",
                schema: "planning",
                table: "mrp_run_route_availability_inputs",
                columns: new[] { "RunId", "ItemId", "DemandDate" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_mrp_run_route_decisions_RunId_ItemId",
                schema: "planning",
                table: "mrp_run_route_decisions",
                columns: new[] { "RunId", "ItemId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_mrp_runs_AsOfDate_StartedAtUtc",
                schema: "planning",
                table: "mrp_runs",
                columns: new[] { "AsOfDate", "StartedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_mrp_runs_RunNumber",
                schema: "planning",
                table: "mrp_runs",
                column: "RunNumber",
                unique: true);

            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION planning.guard_mrp_run_mutation()
                RETURNS trigger LANGUAGE plpgsql AS $function$
                BEGIN
                    IF TG_OP = 'DELETE' THEN
                        RAISE EXCEPTION 'MRP_RUN_IMMUTABLE';
                    END IF;
                    IF TG_OP = 'INSERT' THEN
                        IF NEW."Status" <> 'RUNNING' THEN RAISE EXCEPTION 'MRP_RUN_MUST_START_RUNNING'; END IF;
                        RETURN NEW;
                    END IF;
                    IF OLD."Status" <> 'RUNNING' THEN RAISE EXCEPTION 'MRP_RUN_IMMUTABLE'; END IF;
                    IF NEW."Id" <> OLD."Id" OR NEW."RunNumber" <> OLD."RunNumber"
                       OR NEW."AsOfDate" <> OLD."AsOfDate" OR NEW."HorizonEndDate" <> OLD."HorizonEndDate"
                       OR NEW."EngineVersion" <> OLD."EngineVersion" OR NEW."StartedAtUtc" <> OLD."StartedAtUtc"
                       OR NEW."ActorIdentifier" IS DISTINCT FROM OLD."ActorIdentifier"
                       OR NEW."Assumptions" <> OLD."Assumptions" THEN
                        RAISE EXCEPTION 'MRP_RUN_IDENTITY_IMMUTABLE';
                    END IF;
                    IF NEW."Status" = 'RUNNING' THEN
                        IF NEW."CompletedAtUtc" IS NOT NULL OR NEW."FailureCode" IS NOT NULL OR NEW."FailureMessage" IS NOT NULL
                           OR NEW."Completeness" <> 'COMPLETE' THEN RAISE EXCEPTION 'MRP_RUN_RUNNING_SHAPE_INVALID'; END IF;
                        IF OLD."SnapshotCapturedAtUtc" IS NOT NULL AND NEW."SnapshotCapturedAtUtc" IS DISTINCT FROM OLD."SnapshotCapturedAtUtc"
                           THEN RAISE EXCEPTION 'MRP_RUN_SNAPSHOT_TIME_IMMUTABLE'; END IF;
                        RETURN NEW;
                    END IF;
                    IF NEW."Status" NOT IN ('COMPLETED','COMPLETED_WITH_EXCEPTIONS','FAILED') OR NEW."CompletedAtUtc" IS NULL
                       THEN RAISE EXCEPTION 'MRP_RUN_TERMINAL_SHAPE_INVALID'; END IF;
                    RETURN NEW;
                END $function$;

                CREATE TRIGGER trg_guard_mrp_run_mutation
                BEFORE INSERT OR UPDATE OR DELETE ON planning.mrp_runs
                FOR EACH ROW EXECUTE FUNCTION planning.guard_mrp_run_mutation();

                CREATE OR REPLACE FUNCTION planning.guard_mrp_run_child()
                RETURNS trigger LANGUAGE plpgsql AS $function$
                DECLARE run_id uuid;
                BEGIN
                    IF TG_OP IN ('UPDATE','DELETE') THEN RAISE EXCEPTION 'MRP_RUN_CHILD_IMMUTABLE'; END IF;
                    run_id := NEW."RunId";
                    IF NOT EXISTS (SELECT 1 FROM planning.mrp_runs r WHERE r."Id" = run_id AND r."Status" = 'RUNNING')
                       THEN RAISE EXCEPTION 'MRP_RUN_NOT_RUNNING'; END IF;
                    RETURN NEW;
                END $function$;

                DO $block$
                DECLARE table_name text;
                BEGIN
                    FOREACH table_name IN ARRAY ARRAY[
                        'mrp_run_item_inputs','mrp_run_forecast_inputs','mrp_run_bom_inputs',
                        'mrp_run_inventory_inputs','mrp_run_firm_supply_inputs','mrp_run_route_availability_inputs',
                        'mrp_run_route_decisions','mrp_run_daily_results','mrp_run_item_summaries',
                        'mrp_run_exceptions','mrp_run_demand_traces'
                    ] LOOP
                        EXECUTE format('CREATE TRIGGER trg_guard_%I BEFORE INSERT OR UPDATE OR DELETE ON planning.%I FOR EACH ROW EXECUTE FUNCTION planning.guard_mrp_run_child()', table_name, table_name);
                    END LOOP;
                END $block$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "mrp_run_bom_inputs",
                schema: "planning");

            migrationBuilder.DropTable(
                name: "mrp_run_daily_results",
                schema: "planning");

            migrationBuilder.DropTable(
                name: "mrp_run_demand_traces",
                schema: "planning");

            migrationBuilder.DropTable(
                name: "mrp_run_exceptions",
                schema: "planning");

            migrationBuilder.DropTable(
                name: "mrp_run_firm_supply_inputs",
                schema: "planning");

            migrationBuilder.DropTable(
                name: "mrp_run_forecast_inputs",
                schema: "planning");

            migrationBuilder.DropTable(
                name: "mrp_run_inventory_inputs",
                schema: "planning");

            migrationBuilder.DropTable(
                name: "mrp_run_item_inputs",
                schema: "planning");

            migrationBuilder.DropTable(
                name: "mrp_run_item_summaries",
                schema: "planning");

            migrationBuilder.DropTable(
                name: "mrp_run_route_availability_inputs",
                schema: "planning");

            migrationBuilder.DropTable(
                name: "mrp_run_route_decisions",
                schema: "planning");

            migrationBuilder.DropTable(
                name: "mrp_runs",
                schema: "planning");

            migrationBuilder.DropSequence(
                name: "mrp_run_number_seq",
                schema: "planning");

            migrationBuilder.Sql("DROP FUNCTION IF EXISTS planning.guard_mrp_run_child(); DROP FUNCTION IF EXISTS planning.guard_mrp_run_mutation();");
        }
    }
}
