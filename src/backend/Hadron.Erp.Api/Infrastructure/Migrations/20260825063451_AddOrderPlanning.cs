using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hadron.Erp.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddOrderPlanning : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "mrp_run_resolved_route_results",
                schema: "planning",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RunId = table.Column<Guid>(type: "uuid", nullable: false),
                    ItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    DemandDate = table.Column<DateOnly>(type: "date", nullable: false),
                    ResolvedPlanningRoute = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    RouteOrigin = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    ReasonCode = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_mrp_run_resolved_route_results", x => x.Id);
                    table.CheckConstraint("ck_mrp_resolved_route", "\"ResolvedPlanningRoute\" IN ('EXTERNAL','INTERNAL_BOM','INCOMPLETE')");
                    table.CheckConstraint("ck_mrp_route_origin", "\"RouteOrigin\" IS NULL OR \"RouteOrigin\" IN ('BUY','MAKE','OUTSOURCE_EXTERNAL','OUTSOURCE_BOM','HYBRID_EXTERNAL','HYBRID_INTERNAL')");
                    table.ForeignKey(
                        name: "FK_mrp_run_resolved_route_results_mrp_runs_RunId",
                        column: x => x.RunId,
                        principalSchema: "planning",
                        principalTable: "mrp_runs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "order_planning_generations",
                schema: "planning",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RunId = table.Column<Guid>(type: "uuid", nullable: false),
                    GenerationSequence = table.Column<int>(type: "integer", nullable: false),
                    EvaluationDate = table.Column<DateOnly>(type: "date", nullable: false),
                    GeneratedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    IsCurrent = table.Column<bool>(type: "boolean", nullable: false),
                    CompletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    FailureCode = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    FailureMessage = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    ActorIdentifier = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_order_planning_generations", x => x.Id);
                    table.CheckConstraint("ck_order_planning_generation_status", "\"Status\" IN ('RUNNING','COMPLETED','FAILED')");
                    table.ForeignKey(
                        name: "FK_order_planning_generations_mrp_runs_RunId",
                        column: x => x.RunId,
                        principalSchema: "planning",
                        principalTable: "mrp_runs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "order_recommendation_candidates",
                schema: "planning",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    GenerationId = table.Column<Guid>(type: "uuid", nullable: false),
                    ItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourcingOptionId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourcingNumber = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    SupplierId = table.Column<Guid>(type: "uuid", nullable: false),
                    SupplierNumber = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    SupplierNameFa = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    SupplierNameEn = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    IsPreferred = table.Column<bool>(type: "boolean", nullable: false),
                    IsEligible = table.Column<bool>(type: "boolean", nullable: false),
                    ReasonCodes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    ItemBaseUnitId = table.Column<Guid>(type: "uuid", nullable: false),
                    MinimumOrderQuantity = table.Column<decimal>(type: "numeric(28,12)", precision: 28, scale: 12, nullable: true),
                    ShippingModeId = table.Column<Guid>(type: "uuid", nullable: true),
                    ShippingModeCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    IncotermId = table.Column<Guid>(type: "uuid", nullable: true),
                    IncotermCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    SupplierLeadTimeDays = table.Column<int>(type: "integer", nullable: true),
                    ForwarderToShipmentDays = table.Column<int>(type: "integer", nullable: true),
                    ShippingPeriodDays = table.Column<int>(type: "integer", nullable: true),
                    DestinationCustomsDays = table.Column<int>(type: "integer", nullable: true),
                    TotalLeadTimeDays = table.Column<int>(type: "integer", nullable: true),
                    EarliestFeasibleReceiptDate = table.Column<DateOnly>(type: "date", nullable: true),
                    CanMeetNeedBy = table.Column<bool>(type: "boolean", nullable: false),
                    PriceId = table.Column<Guid>(type: "uuid", nullable: true),
                    UnitPrice = table.Column<decimal>(type: "numeric(28,12)", precision: 28, scale: 12, nullable: true),
                    CurrencyId = table.Column<Guid>(type: "uuid", nullable: true),
                    CurrencyCode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    CurrencyNameFa = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    CurrencyNameEn = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    PriceEffectiveFrom = table.Column<DateOnly>(type: "date", nullable: true),
                    PriceEffectiveTo = table.Column<DateOnly>(type: "date", nullable: true),
                    PaymentTermId = table.Column<Guid>(type: "uuid", nullable: true),
                    AdvancePercent = table.Column<decimal>(type: "numeric(28,12)", precision: 28, scale: 12, nullable: true),
                    BeforeShipmentPercent = table.Column<decimal>(type: "numeric(28,12)", precision: 28, scale: 12, nullable: true),
                    CreditPercent = table.Column<decimal>(type: "numeric(28,12)", precision: 28, scale: 12, nullable: true),
                    CreditDays = table.Column<int>(type: "integer", nullable: true),
                    CreditAnchor = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_order_recommendation_candidates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_order_recommendation_candidates_order_planning_generations_~",
                        column: x => x.GenerationId,
                        principalSchema: "planning",
                        principalTable: "order_planning_generations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "order_recommendations",
                schema: "planning",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    GenerationId = table.Column<Guid>(type: "uuid", nullable: false),
                    ItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    ItemCode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ItemType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ItemNameFa = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ItemNameEn = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ItemBaseUnitId = table.Column<Guid>(type: "uuid", nullable: false),
                    UnitCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    NeedByDate = table.Column<DateOnly>(type: "date", nullable: false),
                    RouteOrigin = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    NetRequirementQty = table.Column<decimal>(type: "numeric(28,12)", precision: 28, scale: 12, nullable: false),
                    SafetyStockQty = table.Column<decimal>(type: "numeric(28,12)", precision: 28, scale: 12, nullable: false),
                    CriticalDays = table.Column<int>(type: "integer", nullable: false),
                    SafeStartDays = table.Column<int>(type: "integer", nullable: false),
                    ExcessStartDays = table.Column<int>(type: "integer", nullable: false),
                    State = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    TimingState = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    EarliestFeasibleReceiptDate = table.Column<DateOnly>(type: "date", nullable: true),
                    TargetReceiptDate = table.Column<DateOnly>(type: "date", nullable: true),
                    OrderByDate = table.Column<DateOnly>(type: "date", nullable: true),
                    DecisionHorizonDate = table.Column<DateOnly>(type: "date", nullable: true),
                    LateDays = table.Column<int>(type: "integer", nullable: true),
                    MinimumBaseline = table.Column<decimal>(type: "numeric(28,12)", precision: 28, scale: 12, nullable: true),
                    BaseRecommendedQty = table.Column<decimal>(type: "numeric(28,12)", precision: 28, scale: 12, nullable: true),
                    SystemRecommendedQty = table.Column<decimal>(type: "numeric(28,12)", precision: 28, scale: 12, nullable: true),
                    Priority = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    NextAction = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    SelectedCandidateId = table.Column<Guid>(type: "uuid", nullable: true),
                    EstimatedValue = table.Column<decimal>(type: "numeric(28,12)", precision: 28, scale: 12, nullable: true),
                    ReasonCodes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    SourceSelectionReason = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    CalculationTrace = table.Column<string>(type: "jsonb", nullable: false),
                    AlgorithmVersion = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    DecisionVersion = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_order_recommendations", x => x.Id);
                    table.CheckConstraint("ck_order_recommendation_qty", "\"NetRequirementQty\" > 0 AND (\"BaseRecommendedQty\" IS NULL OR \"BaseRecommendedQty\" >= 0) AND (\"SystemRecommendedQty\" IS NULL OR \"SystemRecommendedQty\" > 0)");
                    table.CheckConstraint("ck_order_recommendation_state", "\"State\" IN ('READY','BLOCKED')");
                    table.CheckConstraint("ck_order_recommendation_timing", "\"TimingState\" IN ('ON_TIME','LATE')");
                    table.ForeignKey(
                        name: "FK_order_recommendations_order_planning_generations_Generation~",
                        column: x => x.GenerationId,
                        principalSchema: "planning",
                        principalTable: "order_planning_generations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "order_recommendation_decisions",
                schema: "planning",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RecommendationId = table.Column<Guid>(type: "uuid", nullable: false),
                    DecisionVersion = table.Column<long>(type: "bigint", nullable: false),
                    DecisionType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    DecisionDate = table.Column<DateOnly>(type: "date", nullable: false),
                    DecidedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ActorIdentifier = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ReviewDate = table.Column<DateOnly>(type: "date", nullable: true),
                    FinalSourcingOptionId = table.Column<Guid>(type: "uuid", nullable: true),
                    FinalSourcingNumber = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    FinalSupplierId = table.Column<Guid>(type: "uuid", nullable: true),
                    FinalSupplierNumber = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    FinalSupplierNameFa = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    FinalQty = table.Column<decimal>(type: "numeric(28,12)", precision: 28, scale: 12, nullable: true),
                    FinalTargetReceiptDate = table.Column<DateOnly>(type: "date", nullable: true),
                    FinalOrderByDate = table.Column<DateOnly>(type: "date", nullable: true),
                    FinalPriceId = table.Column<Guid>(type: "uuid", nullable: true),
                    FinalUnitPrice = table.Column<decimal>(type: "numeric(28,12)", precision: 28, scale: 12, nullable: true),
                    FinalCurrencyId = table.Column<Guid>(type: "uuid", nullable: true),
                    FinalCurrencyCode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    FinalMinimumOrderQuantity = table.Column<decimal>(type: "numeric(28,12)", precision: 28, scale: 12, nullable: true),
                    FinalTotalLeadTimeDays = table.Column<int>(type: "integer", nullable: true),
                    FinalBaseUnitId = table.Column<Guid>(type: "uuid", nullable: true),
                    WarningCodes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_order_recommendation_decisions", x => x.Id);
                    table.CheckConstraint("ck_order_decision_reason", "\"DecisionType\" = 'ACCEPT' OR (\"Reason\" IS NOT NULL AND length(btrim(\"Reason\")) > 0)");
                    table.CheckConstraint("ck_order_decision_review", "\"DecisionType\" <> 'DEFER' OR \"ReviewDate\" IS NOT NULL");
                    table.CheckConstraint("ck_order_decision_type", "\"DecisionType\" IN ('ACCEPT','MODIFY','REJECT','DEFER')");
                    table.ForeignKey(
                        name: "FK_order_recommendation_decisions_order_recommendations_Recomm~",
                        column: x => x.RecommendationId,
                        principalSchema: "planning",
                        principalTable: "order_recommendations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_mrp_run_resolved_route_results_RunId_ItemId_DemandDate",
                schema: "planning",
                table: "mrp_run_resolved_route_results",
                columns: new[] { "RunId", "ItemId", "DemandDate" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_order_planning_generations_RunId",
                schema: "planning",
                table: "order_planning_generations",
                column: "RunId",
                unique: true,
                filter: "\"IsCurrent\"");

            migrationBuilder.CreateIndex(
                name: "IX_order_planning_generations_RunId_GenerationSequence",
                schema: "planning",
                table: "order_planning_generations",
                columns: new[] { "RunId", "GenerationSequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_order_recommendation_candidates_GenerationId_ItemId_Sourcin~",
                schema: "planning",
                table: "order_recommendation_candidates",
                columns: new[] { "GenerationId", "ItemId", "SourcingOptionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_order_recommendation_decisions_RecommendationId_DecisionVer~",
                schema: "planning",
                table: "order_recommendation_decisions",
                columns: new[] { "RecommendationId", "DecisionVersion" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_order_recommendations_GenerationId_ItemId",
                schema: "planning",
                table: "order_recommendations",
                columns: new[] { "GenerationId", "ItemId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_order_recommendations_GenerationId_State_Priority_NeedByDate",
                schema: "planning",
                table: "order_recommendations",
                columns: new[] { "GenerationId", "State", "Priority", "NeedByDate" });

            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION planning.guard_mrp_resolved_route_snapshot()
                RETURNS trigger LANGUAGE plpgsql AS $body$
                BEGIN
                    IF TG_OP <> 'INSERT' THEN
                        RAISE EXCEPTION 'MRP resolved route snapshots are immutable.' USING ERRCODE = '23514';
                    END IF;
                    IF NOT EXISTS (SELECT 1 FROM planning.mrp_runs r WHERE r."Id" = NEW."RunId" AND r."Status" = 'RUNNING') THEN
                        RAISE EXCEPTION 'Resolved route snapshots may only be added while the MRP Run is RUNNING.' USING ERRCODE = '23514';
                    END IF;
                    RETURN NEW;
                END $body$;
                CREATE TRIGGER trg_guard_mrp_resolved_route_snapshot
                BEFORE INSERT OR UPDATE OR DELETE ON planning.mrp_run_resolved_route_results
                FOR EACH ROW EXECUTE FUNCTION planning.guard_mrp_resolved_route_snapshot();

                CREATE OR REPLACE FUNCTION planning.guard_order_planning_generation()
                RETURNS trigger LANGUAGE plpgsql AS $body$
                BEGIN
                    IF OLD."Status" IN ('COMPLETED','FAILED') THEN
                        IF OLD."Status" = 'COMPLETED' AND OLD."IsCurrent" AND NOT NEW."IsCurrent"
                           AND (to_jsonb(NEW) - 'IsCurrent') = (to_jsonb(OLD) - 'IsCurrent') THEN
                            RETURN NEW;
                        END IF;
                        RAISE EXCEPTION 'Finalized Order Planning Generations are immutable.' USING ERRCODE = '23514';
                    END IF;
                    IF OLD."Status" = 'RUNNING' AND NEW."Status" NOT IN ('COMPLETED','FAILED') THEN
                        RAISE EXCEPTION 'Invalid Order Planning Generation transition.' USING ERRCODE = '23514';
                    END IF;
                    IF NEW."RunId" <> OLD."RunId" OR NEW."GenerationSequence" <> OLD."GenerationSequence"
                       OR NEW."EvaluationDate" <> OLD."EvaluationDate" OR NEW."GeneratedAtUtc" <> OLD."GeneratedAtUtc" THEN
                        RAISE EXCEPTION 'Generation identity and time context are immutable.' USING ERRCODE = '23514';
                    END IF;
                    RETURN NEW;
                END $body$;
                CREATE TRIGGER trg_guard_order_planning_generation
                BEFORE UPDATE OR DELETE ON planning.order_planning_generations
                FOR EACH ROW EXECUTE FUNCTION planning.guard_order_planning_generation();

                CREATE OR REPLACE FUNCTION planning.guard_order_planning_snapshot()
                RETURNS trigger LANGUAGE plpgsql AS $body$
                BEGIN
                    IF TG_OP <> 'INSERT' THEN
                        RAISE EXCEPTION 'Order Planning candidate snapshots are immutable.' USING ERRCODE = '23514';
                    END IF;
                    IF NOT EXISTS (SELECT 1 FROM planning.order_planning_generations g
                        WHERE g."Id" = NEW."GenerationId" AND g."Status" = 'RUNNING') THEN
                        RAISE EXCEPTION 'Official snapshots may only be inserted into a RUNNING Generation.' USING ERRCODE = '23514';
                    END IF;
                    RETURN NEW;
                END $body$;
                CREATE TRIGGER trg_guard_order_recommendation_candidate
                BEFORE INSERT OR UPDATE OR DELETE ON planning.order_recommendation_candidates
                FOR EACH ROW EXECUTE FUNCTION planning.guard_order_planning_snapshot();

                CREATE OR REPLACE FUNCTION planning.guard_order_recommendation()
                RETURNS trigger LANGUAGE plpgsql AS $body$
                BEGIN
                    IF TG_OP = 'INSERT' THEN
                        IF NOT EXISTS (SELECT 1 FROM planning.order_planning_generations g
                            WHERE g."Id" = NEW."GenerationId" AND g."Status" = 'RUNNING') THEN
                            RAISE EXCEPTION 'Recommendations may only be inserted into a RUNNING Generation.' USING ERRCODE = '23514';
                        END IF;
                        RETURN NEW;
                    END IF;
                    IF TG_OP = 'DELETE' THEN
                        RAISE EXCEPTION 'Machine Recommendations are immutable.' USING ERRCODE = '23514';
                    END IF;
                    IF NEW."DecisionVersion" = OLD."DecisionVersion" + 1
                       AND (to_jsonb(NEW) - 'DecisionVersion') = (to_jsonb(OLD) - 'DecisionVersion') THEN
                        RETURN NEW;
                    END IF;
                    RAISE EXCEPTION 'Machine Recommendation fields are immutable.' USING ERRCODE = '23514';
                END $body$;
                CREATE TRIGGER trg_guard_order_recommendation
                BEFORE INSERT OR UPDATE OR DELETE ON planning.order_recommendations
                FOR EACH ROW EXECUTE FUNCTION planning.guard_order_recommendation();

                CREATE OR REPLACE FUNCTION planning.guard_order_decision_history()
                RETURNS trigger LANGUAGE plpgsql AS $body$
                BEGIN
                    RAISE EXCEPTION 'Expert Decision history is append-only.' USING ERRCODE = '23514';
                END $body$;
                CREATE TRIGGER trg_guard_order_decision_history
                BEFORE UPDATE OR DELETE ON planning.order_recommendation_decisions
                FOR EACH ROW EXECUTE FUNCTION planning.guard_order_decision_history();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER IF EXISTS trg_guard_order_decision_history ON planning.order_recommendation_decisions;
                DROP TRIGGER IF EXISTS trg_guard_order_recommendation ON planning.order_recommendations;
                DROP TRIGGER IF EXISTS trg_guard_order_recommendation_candidate ON planning.order_recommendation_candidates;
                DROP TRIGGER IF EXISTS trg_guard_order_planning_generation ON planning.order_planning_generations;
                DROP TRIGGER IF EXISTS trg_guard_mrp_resolved_route_snapshot ON planning.mrp_run_resolved_route_results;
                DROP FUNCTION IF EXISTS planning.guard_order_decision_history();
                DROP FUNCTION IF EXISTS planning.guard_order_recommendation();
                DROP FUNCTION IF EXISTS planning.guard_order_planning_snapshot();
                DROP FUNCTION IF EXISTS planning.guard_order_planning_generation();
                DROP FUNCTION IF EXISTS planning.guard_mrp_resolved_route_snapshot();
                """);
            migrationBuilder.DropTable(
                name: "mrp_run_resolved_route_results",
                schema: "planning");

            migrationBuilder.DropTable(
                name: "order_recommendation_candidates",
                schema: "planning");

            migrationBuilder.DropTable(
                name: "order_recommendation_decisions",
                schema: "planning");

            migrationBuilder.DropTable(
                name: "order_recommendations",
                schema: "planning");

            migrationBuilder.DropTable(
                name: "order_planning_generations",
                schema: "planning");
        }
    }
}
