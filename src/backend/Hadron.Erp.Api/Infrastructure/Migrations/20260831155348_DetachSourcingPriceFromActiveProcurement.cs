using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hadron.Erp.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class DetachSourcingPriceFromActiveProcurement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<Guid>(
                name: "SourcingPriceId",
                schema: "procurement",
                table: "purchase_order_lines",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION procurement.guard_purchase_order_child_mutation()
                RETURNS trigger LANGUAGE plpgsql AS $$
                DECLARE po procurement.purchase_orders%ROWTYPE; decision planning.order_recommendation_decisions%ROWTYPE;
                        recommendation planning.order_recommendations%ROWTYPE;
                        generation planning.order_planning_generations%ROWTYPE;
                        latest_run uuid; snapshot jsonb;
                BEGIN
                  SELECT * INTO po FROM procurement.purchase_orders
                  WHERE "Id"=COALESCE(NEW."PurchaseOrderId",OLD."PurchaseOrderId");
                  IF TG_OP<>'INSERT' THEN
                    IF po."Status"<>'DRAFT' OR po."CreationSource"='EXPERT_DECISION' THEN
                      RAISE EXCEPTION 'PURCHASE_ORDER_SNAPSHOT_LOCKED' USING ERRCODE='23514';
                    END IF;
                    RETURN COALESCE(NEW,OLD);
                  END IF;
                  IF po."Status"<>'DRAFT' THEN
                    RAISE EXCEPTION 'PURCHASE_ORDER_SNAPSHOT_LOCKED' USING ERRCODE='23514';
                  END IF;
                  IF NEW."SourcingPriceId" IS NOT NULL THEN
                    RAISE EXCEPTION 'SOURCING_PRICE_IS_ARCHIVE_ONLY' USING ERRCODE='23514';
                  END IF;
                  IF po."CreationSource"='EXPERT_DECISION' THEN
                    IF NEW."ExpertDecisionId" IS NULL OR NEW."RecommendationId" IS NULL OR
                       NEW."OrderPlanningGenerationId" IS NULL OR NEW."MrpRunId" IS NULL THEN
                      RAISE EXCEPTION 'EXPERT_DECISION_LINEAGE_REQUIRED' USING ERRCODE='23514';
                    END IF;
                    SELECT * INTO decision FROM planning.order_recommendation_decisions WHERE "Id"=NEW."ExpertDecisionId";
                    SELECT * INTO recommendation FROM planning.order_recommendations WHERE "Id"=NEW."RecommendationId";
                    SELECT * INTO generation FROM planning.order_planning_generations WHERE "Id"=NEW."OrderPlanningGenerationId";
                    SELECT r."Id" INTO latest_run FROM planning.mrp_runs r
                     WHERE r."Status" IN ('COMPLETED','COMPLETED_WITH_EXCEPTIONS')
                     ORDER BY r."CompletedAtUtc" DESC,r."StartedAtUtc" DESC LIMIT 1;
                    snapshot := decision."FinalSourceSnapshot";
                    IF decision."Id" IS NULL OR recommendation."Id" IS NULL OR generation."Id" IS NULL OR
                       decision."RecommendationId"<>recommendation."Id" OR recommendation."GenerationId"<>generation."Id" OR
                       decision."DecisionType" NOT IN ('ACCEPT','MODIFY') OR
                       decision."DecisionVersion"<>recommendation."DecisionVersion" OR
                       generation."Status"<>'COMPLETED' OR NOT generation."IsCurrent" OR generation."RunId"<>latest_run OR
                       NEW."MrpRunId"<>generation."RunId" OR NEW."ItemId"<>recommendation."ItemId" OR
                       NEW."OrderedQuantity"<>decision."FinalQty" OR snapshot IS NULL OR
                       NEW."SourcingOptionId"<>(snapshot->>'SourcingOptionId')::uuid OR
                       NEW."ItemBaseUnitId"<>(snapshot->>'ItemBaseUnitId')::uuid OR
                       NEW."UnitPrice"<>(snapshot->>'UnitPrice')::numeric OR
                       NEW."MinimumOrderQuantity" IS DISTINCT FROM (snapshot->>'MinimumOrderQuantity')::numeric OR
                       NEW."SupplierLeadTimeDays" IS DISTINCT FROM (snapshot->>'SupplierLeadTimeDays')::integer OR
                       NEW."ForwarderToShipmentDays" IS DISTINCT FROM (snapshot->>'ForwarderToShipmentDays')::integer OR
                       NEW."ShippingPeriodDays" IS DISTINCT FROM (snapshot->>'ShippingPeriodDays')::integer OR
                       NEW."DestinationCustomsDays" IS DISTINCT FROM (snapshot->>'DestinationCustomsDays')::integer OR
                       po."SupplierId"<>(snapshot->>'SupplierId')::uuid OR po."CurrencyId"<>(snapshot->>'CurrencyId')::uuid OR
                       po."PaymentTermId"<>(snapshot->>'PaymentTermId')::uuid OR
                       po."AdvancePercent"<>(snapshot->>'AdvancePercent')::numeric OR
                       po."BeforeShipmentPercent"<>(snapshot->>'BeforeShipmentPercent')::numeric OR
                       po."CreditPercent"<>(snapshot->>'CreditPercent')::numeric OR
                       po."CreditDays" IS DISTINCT FROM (snapshot->>'CreditDays')::integer OR
                       po."CreditAnchor" IS DISTINCT FROM NULLIF(snapshot->>'CreditAnchor','') OR
                       po."ShippingModeId" IS DISTINCT FROM (snapshot->>'ShippingModeId')::uuid OR
                       po."IncotermId" IS DISTINCT FROM (snapshot->>'IncotermId')::uuid THEN
                       RAISE EXCEPTION 'EXPERT_DECISION_CONVERSION_INTEGRITY' USING ERRCODE='23514';
                    END IF;
                  ELSIF NOT EXISTS (
                    SELECT 1 FROM procurement.sourcing_options so
                    JOIN catalog.items i ON i."Id"=so."ItemId"
                    JOIN reference_data.units u ON u."Id"=so."ItemBaseUnitId"
                    WHERE so."Id"=NEW."SourcingOptionId" AND so."IsActive" AND so."SupplierId"=po."SupplierId"
                      AND so."ItemId"=NEW."ItemId" AND so."ItemBaseUnitId"=NEW."ItemBaseUnitId"
                      AND so."ShippingModeId" IS NOT DISTINCT FROM po."ShippingModeId"
                      AND so."IncotermId" IS NOT DISTINCT FROM po."IncotermId"
                      AND i."IsActive" AND i."ItemType" IN ('PART','COMPONENT')
                      AND i."BaseUnitId"=NEW."ItemBaseUnitId" AND u."IsActive") THEN
                    RAISE EXCEPTION 'PURCHASE_ORDER_LINE_SOURCE_INTEGRITY' USING ERRCODE='23514';
                  END IF;
                  RETURN NEW;
                END $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<Guid>(
                name: "SourcingPriceId",
                schema: "procurement",
                table: "purchase_order_lines",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);
        }
    }
}
