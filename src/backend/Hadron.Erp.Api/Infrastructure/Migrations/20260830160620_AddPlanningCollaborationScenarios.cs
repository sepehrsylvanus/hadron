using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hadron.Erp.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPlanningCollaborationScenarios : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "planner_saved_views",
                schema: "planning",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerIdentifier = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    NormalizedName = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    FiltersJson = table.Column<string>(type: "jsonb", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_planner_saved_views", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "planner_work_items",
                schema: "planning",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RunId = table.Column<Guid>(type: "uuid", nullable: false),
                    ExceptionId = table.Column<Guid>(type: "uuid", nullable: false),
                    AssignedUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    Status = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    AcknowledgedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    AcknowledgedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedByIdentifier = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_planner_work_items", x => x.Id);
                    table.CheckConstraint("ck_planner_work_item_status", "\"Status\" IN ('OPEN','ACKNOWLEDGED')");
                    table.ForeignKey(
                        name: "FK_planner_work_items_mrp_run_exceptions_ExceptionId",
                        column: x => x.ExceptionId,
                        principalSchema: "planning",
                        principalTable: "mrp_run_exceptions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_planner_work_items_mrp_runs_RunId",
                        column: x => x.RunId,
                        principalSchema: "planning",
                        principalTable: "mrp_runs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_planner_work_items_users_AcknowledgedByUserId",
                        column: x => x.AcknowledgedByUserId,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_planner_work_items_users_AssignedUserId",
                        column: x => x.AssignedUserId,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "what_if_scenarios",
                schema: "planning",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BaseRunId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    OwnerIdentifier = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Status = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    Completeness = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CompletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_what_if_scenarios", x => x.Id);
                    table.CheckConstraint("ck_what_if_scenario_status", "\"Status\" = 'CALCULATED'");
                    table.ForeignKey(
                        name: "FK_what_if_scenarios_mrp_runs_BaseRunId",
                        column: x => x.BaseRunId,
                        principalSchema: "planning",
                        principalTable: "mrp_runs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "planner_work_item_events",
                schema: "planning",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    EventType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ActorIdentifier = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    AssignedUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    OccurredAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_planner_work_item_events", x => x.Id);
                    table.ForeignKey(
                        name: "FK_planner_work_item_events_planner_work_items_WorkItemId",
                        column: x => x.WorkItemId,
                        principalSchema: "planning",
                        principalTable: "planner_work_items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_planner_work_item_events_users_AssignedUserId",
                        column: x => x.AssignedUserId,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "what_if_daily_results",
                schema: "planning",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ScenarioId = table.Column<Guid>(type: "uuid", nullable: false),
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
                    table.PrimaryKey("PK_what_if_daily_results", x => x.Id);
                    table.ForeignKey(
                        name: "FK_what_if_daily_results_what_if_scenarios_ScenarioId",
                        column: x => x.ScenarioId,
                        principalSchema: "planning",
                        principalTable: "what_if_scenarios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "what_if_exceptions",
                schema: "planning",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ScenarioId = table.Column<Guid>(type: "uuid", nullable: false),
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
                    table.PrimaryKey("PK_what_if_exceptions", x => x.Id);
                    table.CheckConstraint("ck_what_if_exception_severity", "\"Severity\" IN ('ERROR','WARNING','INFO')");
                    table.ForeignKey(
                        name: "FK_what_if_exceptions_what_if_scenarios_ScenarioId",
                        column: x => x.ScenarioId,
                        principalSchema: "planning",
                        principalTable: "what_if_scenarios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "what_if_firm_supply_overrides",
                schema: "planning",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ScenarioId = table.Column<Guid>(type: "uuid", nullable: false),
                    DeliveryScheduleId = table.Column<Guid>(type: "uuid", nullable: false),
                    OpenQty = table.Column<decimal>(type: "numeric(28,12)", precision: 28, scale: 12, nullable: false),
                    Eta = table.Column<DateOnly>(type: "date", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_what_if_firm_supply_overrides", x => x.Id);
                    table.ForeignKey(
                        name: "FK_what_if_firm_supply_overrides_what_if_scenarios_ScenarioId",
                        column: x => x.ScenarioId,
                        principalSchema: "planning",
                        principalTable: "what_if_scenarios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "what_if_forecast_overrides",
                schema: "planning",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ScenarioId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    BusinessPeriodId = table.Column<Guid>(type: "uuid", nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric(28,12)", precision: 28, scale: 12, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_what_if_forecast_overrides", x => x.Id);
                    table.ForeignKey(
                        name: "FK_what_if_forecast_overrides_what_if_scenarios_ScenarioId",
                        column: x => x.ScenarioId,
                        principalSchema: "planning",
                        principalTable: "what_if_scenarios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "what_if_inventory_overrides",
                schema: "planning",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ScenarioId = table.Column<Guid>(type: "uuid", nullable: false),
                    ItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    OnHandQty = table.Column<decimal>(type: "numeric(28,12)", precision: 28, scale: 12, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_what_if_inventory_overrides", x => x.Id);
                    table.ForeignKey(
                        name: "FK_what_if_inventory_overrides_what_if_scenarios_ScenarioId",
                        column: x => x.ScenarioId,
                        principalSchema: "planning",
                        principalTable: "what_if_scenarios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "what_if_item_summaries",
                schema: "planning",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ScenarioId = table.Column<Guid>(type: "uuid", nullable: false),
                    ItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    StartingOnHand = table.Column<decimal>(type: "numeric(28,12)", precision: 28, scale: 12, nullable: false),
                    TotalGrossDemand = table.Column<decimal>(type: "numeric(28,12)", precision: 28, scale: 12, nullable: false),
                    TotalFirmIncoming = table.Column<decimal>(type: "numeric(28,12)", precision: 28, scale: 12, nullable: false),
                    TotalNetRequirement = table.Column<decimal>(type: "numeric(28,12)", precision: 28, scale: 12, nullable: false),
                    StockoutDate = table.Column<DateOnly>(type: "date", nullable: true),
                    SafetyBreachDate = table.Column<DateOnly>(type: "date", nullable: true),
                    Completeness = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_what_if_item_summaries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_what_if_item_summaries_what_if_scenarios_ScenarioId",
                        column: x => x.ScenarioId,
                        principalSchema: "planning",
                        principalTable: "what_if_scenarios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_planner_saved_views_OwnerIdentifier_NormalizedName",
                schema: "planning",
                table: "planner_saved_views",
                columns: new[] { "OwnerIdentifier", "NormalizedName" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_planner_work_item_events_AssignedUserId",
                schema: "planning",
                table: "planner_work_item_events",
                column: "AssignedUserId");

            migrationBuilder.CreateIndex(
                name: "IX_planner_work_item_events_WorkItemId_OccurredAtUtc",
                schema: "planning",
                table: "planner_work_item_events",
                columns: new[] { "WorkItemId", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_planner_work_items_AcknowledgedByUserId",
                schema: "planning",
                table: "planner_work_items",
                column: "AcknowledgedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_planner_work_items_AssignedUserId",
                schema: "planning",
                table: "planner_work_items",
                column: "AssignedUserId");

            migrationBuilder.CreateIndex(
                name: "IX_planner_work_items_ExceptionId",
                schema: "planning",
                table: "planner_work_items",
                column: "ExceptionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_planner_work_items_RunId",
                schema: "planning",
                table: "planner_work_items",
                column: "RunId");

            migrationBuilder.CreateIndex(
                name: "IX_planner_work_items_Status_AssignedUserId_UpdatedAtUtc",
                schema: "planning",
                table: "planner_work_items",
                columns: new[] { "Status", "AssignedUserId", "UpdatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_what_if_daily_results_ScenarioId_ItemId_Date",
                schema: "planning",
                table: "what_if_daily_results",
                columns: new[] { "ScenarioId", "ItemId", "Date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_what_if_exceptions_ScenarioId_ItemId_Date",
                schema: "planning",
                table: "what_if_exceptions",
                columns: new[] { "ScenarioId", "ItemId", "Date" });

            migrationBuilder.CreateIndex(
                name: "IX_what_if_firm_supply_overrides_ScenarioId_DeliveryScheduleId",
                schema: "planning",
                table: "what_if_firm_supply_overrides",
                columns: new[] { "ScenarioId", "DeliveryScheduleId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_what_if_forecast_overrides_ScenarioId_ProductId_BusinessPer~",
                schema: "planning",
                table: "what_if_forecast_overrides",
                columns: new[] { "ScenarioId", "ProductId", "BusinessPeriodId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_what_if_inventory_overrides_ScenarioId_ItemId",
                schema: "planning",
                table: "what_if_inventory_overrides",
                columns: new[] { "ScenarioId", "ItemId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_what_if_item_summaries_ScenarioId_ItemId",
                schema: "planning",
                table: "what_if_item_summaries",
                columns: new[] { "ScenarioId", "ItemId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_what_if_scenarios_BaseRunId",
                schema: "planning",
                table: "what_if_scenarios",
                column: "BaseRunId");

            migrationBuilder.CreateIndex(
                name: "IX_what_if_scenarios_OwnerIdentifier_CreatedAtUtc",
                schema: "planning",
                table: "what_if_scenarios",
                columns: new[] { "OwnerIdentifier", "CreatedAtUtc" });

            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION planning.reject_planning_collaboration_history_mutation()
                RETURNS trigger LANGUAGE plpgsql AS $body$
                BEGIN
                    RAISE EXCEPTION 'PLANNING_COLLABORATION_HISTORY_IMMUTABLE'
                        USING ERRCODE = '23514';
                END;
                $body$;

                CREATE TRIGGER trg_planner_work_item_events_immutable
                    BEFORE UPDATE OR DELETE ON planning.planner_work_item_events
                    FOR EACH ROW EXECUTE FUNCTION planning.reject_planning_collaboration_history_mutation();
                CREATE TRIGGER trg_what_if_scenarios_immutable
                    BEFORE UPDATE OR DELETE ON planning.what_if_scenarios
                    FOR EACH ROW EXECUTE FUNCTION planning.reject_planning_collaboration_history_mutation();
                CREATE TRIGGER trg_what_if_forecast_overrides_immutable
                    BEFORE UPDATE OR DELETE ON planning.what_if_forecast_overrides
                    FOR EACH ROW EXECUTE FUNCTION planning.reject_planning_collaboration_history_mutation();
                CREATE TRIGGER trg_what_if_inventory_overrides_immutable
                    BEFORE UPDATE OR DELETE ON planning.what_if_inventory_overrides
                    FOR EACH ROW EXECUTE FUNCTION planning.reject_planning_collaboration_history_mutation();
                CREATE TRIGGER trg_what_if_firm_supply_overrides_immutable
                    BEFORE UPDATE OR DELETE ON planning.what_if_firm_supply_overrides
                    FOR EACH ROW EXECUTE FUNCTION planning.reject_planning_collaboration_history_mutation();
                CREATE TRIGGER trg_what_if_daily_results_immutable
                    BEFORE UPDATE OR DELETE ON planning.what_if_daily_results
                    FOR EACH ROW EXECUTE FUNCTION planning.reject_planning_collaboration_history_mutation();
                CREATE TRIGGER trg_what_if_item_summaries_immutable
                    BEFORE UPDATE OR DELETE ON planning.what_if_item_summaries
                    FOR EACH ROW EXECUTE FUNCTION planning.reject_planning_collaboration_history_mutation();
                CREATE TRIGGER trg_what_if_exceptions_immutable
                    BEFORE UPDATE OR DELETE ON planning.what_if_exceptions
                    FOR EACH ROW EXECUTE FUNCTION planning.reject_planning_collaboration_history_mutation();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS planning.reject_planning_collaboration_history_mutation() CASCADE;");

            migrationBuilder.DropTable(
                name: "planner_saved_views",
                schema: "planning");

            migrationBuilder.DropTable(
                name: "planner_work_item_events",
                schema: "planning");

            migrationBuilder.DropTable(
                name: "what_if_daily_results",
                schema: "planning");

            migrationBuilder.DropTable(
                name: "what_if_exceptions",
                schema: "planning");

            migrationBuilder.DropTable(
                name: "what_if_firm_supply_overrides",
                schema: "planning");

            migrationBuilder.DropTable(
                name: "what_if_forecast_overrides",
                schema: "planning");

            migrationBuilder.DropTable(
                name: "what_if_inventory_overrides",
                schema: "planning");

            migrationBuilder.DropTable(
                name: "what_if_item_summaries",
                schema: "planning");

            migrationBuilder.DropTable(
                name: "planner_work_items",
                schema: "planning");

            migrationBuilder.DropTable(
                name: "what_if_scenarios",
                schema: "planning");
        }
    }
}
