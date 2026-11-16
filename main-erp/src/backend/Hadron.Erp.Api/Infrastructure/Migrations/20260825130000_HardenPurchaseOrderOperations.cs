using Hadron.Erp.Api.Infrastructure;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hadron.Erp.Api.Infrastructure.Migrations;

[DbContext(typeof(HadronDbContext))]
[Migration("20260825130000_HardenPurchaseOrderOperations")]
public sealed class HardenPurchaseOrderOperations : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            ALTER TABLE procurement.delivery_schedule_allocation_events
              ADD COLUMN IF NOT EXISTS "TransactionId" bigint NOT NULL DEFAULT txid_current();

            CREATE OR REPLACE FUNCTION procurement.purchase_order_expected_operational_status(order_id uuid)
            RETURNS text LANGUAGE plpgsql STABLE AS $$
            DECLARE received_qty numeric; cancelled_qty numeric; open_qty numeric;
                    has_customs boolean; has_shipped boolean;
            BEGIN
              SELECT COALESCE(SUM(ds."AllocatedQuantity"),0),
                     COALESCE(SUM((SELECT SUM(prl."Quantity")
                       FROM procurement.purchase_receipt_lines prl
                       JOIN procurement.purchase_receipts pr ON pr."Id"=prl."ReceiptId"
                       WHERE prl."DeliveryScheduleId"=ds."Id" AND pr."Status"='POSTED')),0),
                     COALESCE(SUM((SELECT SUM(c."Quantity")
                       FROM procurement.purchase_schedule_cancellations c
                       WHERE c."DeliveryScheduleId"=ds."Id")),0)
                INTO open_qty,received_qty,cancelled_qty
              FROM procurement.delivery_schedules ds
              JOIN procurement.purchase_order_lines pol ON pol."Id"=ds."PurchaseOrderLineId"
              WHERE pol."PurchaseOrderId"=order_id;
              open_qty := open_qty-received_qty-cancelled_qty;

              SELECT EXISTS(
                       SELECT 1 FROM procurement.delivery_schedule_actual_milestones m
                       JOIN procurement.delivery_schedules ds ON ds."Id"=m."DeliveryScheduleId"
                       JOIN procurement.purchase_order_lines pol ON pol."Id"=ds."PurchaseOrderLineId"
                       WHERE pol."PurchaseOrderId"=order_id AND m."MilestoneCode"='CUSTOMS_CLEARED'
                         AND NOT EXISTS (SELECT 1 FROM procurement.delivery_schedule_actual_milestones n
                                         WHERE n."SupersedesMilestoneId"=m."Id")),
                     EXISTS(
                       SELECT 1 FROM procurement.delivery_schedule_actual_milestones m
                       JOIN procurement.delivery_schedules ds ON ds."Id"=m."DeliveryScheduleId"
                       JOIN procurement.purchase_order_lines pol ON pol."Id"=ds."PurchaseOrderLineId"
                       WHERE pol."PurchaseOrderId"=order_id AND m."MilestoneCode"='SHIPPED'
                         AND NOT EXISTS (SELECT 1 FROM procurement.delivery_schedule_actual_milestones n
                                         WHERE n."SupersedesMilestoneId"=m."Id"))
                INTO has_customs,has_shipped;

              RETURN CASE
                WHEN open_qty=0 AND received_qty>0 THEN 'RECEIVED'
                WHEN open_qty=0 THEN 'CANCELLED'
                WHEN received_qty>0 THEN 'PARTIALLY_RECEIVED'
                WHEN has_customs THEN 'CUSTOMS'
                WHEN has_shipped THEN 'SHIPPED'
                ELSE 'ORDERED' END;
            END $$;

            CREATE OR REPLACE FUNCTION procurement.guard_purchase_order_update()
            RETURNS trigger LANGUAGE plpgsql AS $$
            DECLARE expected_status text;
            BEGIN
              IF NEW."PurchaseOrderNumber" IS DISTINCT FROM OLD."PurchaseOrderNumber"
                 OR NEW."SupplierId" IS DISTINCT FROM OLD."SupplierId"
                 OR NEW."CurrencyId" IS DISTINCT FROM OLD."CurrencyId"
                 OR NEW."CreationSource" IS DISTINCT FROM OLD."CreationSource" THEN
                RAISE EXCEPTION 'PURCHASE_ORDER_IDENTITY_IMMUTABLE' USING ERRCODE='23514';
              END IF;
              IF NEW."Version" <> OLD."Version" + 1 THEN
                RAISE EXCEPTION 'PURCHASE_ORDER_VERSION_MUST_ADVANCE_ONCE' USING ERRCODE='23514';
              END IF;
              IF (OLD."Status" <> 'DRAFT' OR OLD."CreationSource"='EXPERT_DECISION') AND (
                 NEW."PaymentTermId" IS DISTINCT FROM OLD."PaymentTermId" OR
                 NEW."AdvancePercent" IS DISTINCT FROM OLD."AdvancePercent" OR
                 NEW."BeforeShipmentPercent" IS DISTINCT FROM OLD."BeforeShipmentPercent" OR
                 NEW."CreditPercent" IS DISTINCT FROM OLD."CreditPercent" OR
                 NEW."CreditDays" IS DISTINCT FROM OLD."CreditDays" OR
                 NEW."CreditAnchor" IS DISTINCT FROM OLD."CreditAnchor" OR
                 NEW."ShippingModeId" IS DISTINCT FROM OLD."ShippingModeId" OR
                 NEW."IncotermId" IS DISTINCT FROM OLD."IncotermId") THEN
                RAISE EXCEPTION 'PURCHASE_ORDER_SNAPSHOT_LOCKED' USING ERRCODE='23514';
              END IF;
              IF OLD."Status"='DRAFT' AND NEW."Status" NOT IN ('DRAFT','APPROVED') THEN
                RAISE EXCEPTION 'PURCHASE_ORDER_TRANSITION_NOT_ALLOWED' USING ERRCODE='23514';
              ELSIF OLD."Status"='APPROVED' AND NEW."Status" NOT IN ('DRAFT','APPROVED','ORDERED') THEN
                RAISE EXCEPTION 'PURCHASE_ORDER_TRANSITION_NOT_ALLOWED' USING ERRCODE='23514';
              ELSIF OLD."Status" NOT IN ('DRAFT','APPROVED') AND NEW."Status" <> OLD."Status" THEN
                expected_status := procurement.purchase_order_expected_operational_status(OLD."Id");
                IF NEW."Status"<>expected_status THEN
                  RAISE EXCEPTION 'PURCHASE_ORDER_OPERATIONAL_STATUS_MISMATCH' USING ERRCODE='23514';
                END IF;
              END IF;
              IF OLD."Status" NOT IN ('DRAFT','APPROVED') AND (
                 NEW."OrderedDate" IS DISTINCT FROM OLD."OrderedDate" OR
                 NEW."OrderedAtUtc" IS DISTINCT FROM OLD."OrderedAtUtc" OR
                 NEW."Notes" IS DISTINCT FROM OLD."Notes") THEN
                RAISE EXCEPTION 'PURCHASE_ORDER_COMMITMENT_IMMUTABLE' USING ERRCODE='23514';
              END IF;
              RETURN NEW;
            END $$;

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
                   NEW."SourcingPriceId"<>(snapshot->>'PriceId')::uuid OR
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
                JOIN procurement.sourcing_prices sp ON sp."Id"=NEW."SourcingPriceId" AND sp."SourcingOptionId"=so."Id"
                JOIN catalog.items i ON i."Id"=so."ItemId" JOIN reference_data.units u ON u."Id"=so."ItemBaseUnitId"
                WHERE so."Id"=NEW."SourcingOptionId" AND so."IsActive" AND so."SupplierId"=po."SupplierId"
                  AND so."ItemId"=NEW."ItemId" AND so."ItemBaseUnitId"=NEW."ItemBaseUnitId"
                  AND so."ShippingModeId" IS NOT DISTINCT FROM po."ShippingModeId"
                  AND so."IncotermId" IS NOT DISTINCT FROM po."IncotermId" AND sp."CurrencyId"=po."CurrencyId"
                  AND i."IsActive" AND i."ItemType" IN ('PART','COMPONENT')
                  AND i."BaseUnitId"=NEW."ItemBaseUnitId" AND u."IsActive") THEN
                RAISE EXCEPTION 'PURCHASE_ORDER_LINE_SOURCE_INTEGRITY' USING ERRCODE='23514';
              END IF;
              RETURN NEW;
            END $$;

            CREATE OR REPLACE FUNCTION procurement.verify_expert_purchase_order()
            RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN
              IF NEW."CreationSource"='EXPERT_DECISION' AND NOT EXISTS (
                   SELECT 1 FROM procurement.purchase_order_lines l
                   WHERE l."PurchaseOrderId"=NEW."Id" AND l."ExpertDecisionId" IS NOT NULL) THEN
                RAISE EXCEPTION 'EXPERT_DECISION_LINEAGE_REQUIRED' USING ERRCODE='23514';
              END IF;
              RETURN NEW;
            END $$;
            DROP TRIGGER IF EXISTS trg_verify_expert_purchase_order ON procurement.purchase_orders;
            CREATE CONSTRAINT TRIGGER trg_verify_expert_purchase_order
            AFTER INSERT OR UPDATE ON procurement.purchase_orders DEFERRABLE INITIALLY DEFERRED
            FOR EACH ROW EXECUTE FUNCTION procurement.verify_expert_purchase_order();

            CREATE OR REPLACE FUNCTION procurement.guard_delivery_schedule_mutation()
            RETURNS trigger LANGUAGE plpgsql AS $$
            DECLARE po_status text;
            BEGIN
              SELECT po."Status" INTO po_status FROM procurement.purchase_orders po
              JOIN procurement.purchase_order_lines pol ON pol."PurchaseOrderId"=po."Id"
              WHERE pol."Id"=COALESCE(NEW."PurchaseOrderLineId",OLD."PurchaseOrderLineId");
              IF TG_OP='DELETE' AND po_status<>'DRAFT' THEN
                RAISE EXCEPTION 'ORDERED_SCHEDULE_DELETE_FORBIDDEN' USING ERRCODE='23514';
              END IF;
              IF TG_OP='UPDATE' AND NEW."PurchaseOrderLineId" IS DISTINCT FROM OLD."PurchaseOrderLineId" THEN
                RAISE EXCEPTION 'PURCHASE_ORDER_SCHEDULE_IDENTITY_IMMUTABLE' USING ERRCODE='23514';
              END IF;
              RETURN COALESCE(NEW,OLD);
            END $$;

            CREATE OR REPLACE FUNCTION procurement.verify_delivery_schedule_change()
            RETURNS trigger LANGUAGE plpgsql AS $$
            DECLARE line_id uuid; po_source text; po_status text; schedule_count integer;
            BEGIN
              line_id:=COALESCE(NEW."PurchaseOrderLineId",OLD."PurchaseOrderLineId");
              SELECT po."CreationSource",po."Status" INTO po_source,po_status
              FROM procurement.purchase_orders po JOIN procurement.purchase_order_lines pol ON pol."PurchaseOrderId"=po."Id"
              WHERE pol."Id"=line_id;
              SELECT COUNT(*) INTO schedule_count FROM procurement.delivery_schedules WHERE "PurchaseOrderLineId"=line_id;
              IF (po_status<>'DRAFT' OR (po_source='EXPERT_DECISION' AND (TG_OP<>'INSERT' OR schedule_count>1)))
                 AND NOT EXISTS (SELECT 1 FROM procurement.delivery_schedule_allocation_events e
                   WHERE e."PurchaseOrderLineId"=line_id AND e."TransactionId"=txid_current()
                     AND NULLIF(BTRIM(e."Reason"),'') IS NOT NULL) THEN
                RAISE EXCEPTION 'PURCHASE_ORDER_SCHEDULE_CHANGE_REQUIRES_HISTORY' USING ERRCODE='23514';
              END IF;
              RETURN COALESCE(NEW,OLD);
            END $$;
            DROP TRIGGER IF EXISTS trg_verify_delivery_schedule_change ON procurement.delivery_schedules;
            CREATE CONSTRAINT TRIGGER trg_verify_delivery_schedule_change
            AFTER INSERT OR UPDATE OR DELETE ON procurement.delivery_schedules DEFERRABLE INITIALLY DEFERRED
            FOR EACH ROW EXECUTE FUNCTION procurement.verify_delivery_schedule_change();

            CREATE OR REPLACE FUNCTION procurement.guard_receipt_line()
            RETURNS trigger LANGUAGE plpgsql AS $$
            DECLARE receipt procurement.purchase_receipts%ROWTYPE; line_po uuid; schedule_line uuid;
                    line_item uuid; line_unit uuid; inventory_row inventory.transactions%ROWTYPE;
            BEGIN
              IF TG_OP='DELETE' THEN RAISE EXCEPTION 'RECEIPT_HISTORY_IMMUTABLE' USING ERRCODE='23514'; END IF;
              SELECT * INTO receipt FROM procurement.purchase_receipts WHERE "Id"=NEW."ReceiptId";
              SELECT "PurchaseOrderId","ItemId","ItemBaseUnitId" INTO line_po,line_item,line_unit
                FROM procurement.purchase_order_lines WHERE "Id"=NEW."PurchaseOrderLineId";
              SELECT "PurchaseOrderLineId" INTO schedule_line FROM procurement.delivery_schedules WHERE "Id"=NEW."DeliveryScheduleId";
              IF receipt."PurchaseOrderId"<>line_po OR schedule_line<>NEW."PurchaseOrderLineId"
                 OR NEW."ItemId"<>line_item OR NEW."UnitId"<>line_unit THEN
                RAISE EXCEPTION 'RECEIPT_LINE_SCOPE_INVALID' USING ERRCODE='23514';
              END IF;
              IF TG_OP='INSERT' THEN
                IF receipt."Status"<>'DRAFT' OR NEW."InventoryTransactionId" IS NOT NULL
                   OR NEW."ReversalInventoryTransactionId" IS NOT NULL THEN
                  RAISE EXCEPTION 'RECEIPT_LINE_REQUIRES_CLEAN_DRAFT' USING ERRCODE='23514';
                END IF;
                RETURN NEW;
              END IF;
              IF NEW."ReceiptId" IS DISTINCT FROM OLD."ReceiptId" OR
                 NEW."PurchaseOrderLineId" IS DISTINCT FROM OLD."PurchaseOrderLineId" OR
                 NEW."DeliveryScheduleId" IS DISTINCT FROM OLD."DeliveryScheduleId" OR
                 NEW."ItemId" IS DISTINCT FROM OLD."ItemId" OR NEW."UnitId" IS DISTINCT FROM OLD."UnitId" OR
                 NEW."Quantity" IS DISTINCT FROM OLD."Quantity" OR
                 (OLD."InventoryTransactionId" IS NOT NULL AND NEW."InventoryTransactionId" IS DISTINCT FROM OLD."InventoryTransactionId") OR
                 (OLD."ReversalInventoryTransactionId" IS NOT NULL AND NEW."ReversalInventoryTransactionId" IS DISTINCT FROM OLD."ReversalInventoryTransactionId") THEN
                RAISE EXCEPTION 'RECEIPT_LINE_IMMUTABLE' USING ERRCODE='23514';
              END IF;
              IF OLD."InventoryTransactionId" IS NULL AND NEW."InventoryTransactionId" IS NOT NULL THEN
                SELECT * INTO inventory_row FROM inventory.transactions WHERE "Id"=NEW."InventoryTransactionId";
                IF receipt."Status" NOT IN ('POSTED','REVERSED') OR inventory_row."Id" IS NULL OR
                   inventory_row."TransactionType"<>'RECEIPT' OR inventory_row."SourceDocumentType"<>'PURCHASE_RECEIPT' OR
                   inventory_row."SourceDocumentId"<>NEW."Id" OR inventory_row."ItemId"<>NEW."ItemId" OR
                   inventory_row."UnitId"<>NEW."UnitId" OR inventory_row."Quantity"<>NEW."Quantity" OR
                   inventory_row."WarehouseId"<>receipt."WarehouseId" OR inventory_row."EffectiveDate"<>receipt."EffectiveDate" THEN
                  RAISE EXCEPTION 'RECEIPT_INVENTORY_ATOMICITY_VIOLATION' USING ERRCODE='23514';
                END IF;
              END IF;
              IF OLD."ReversalInventoryTransactionId" IS NULL AND NEW."ReversalInventoryTransactionId" IS NOT NULL THEN
                SELECT * INTO inventory_row FROM inventory.transactions WHERE "Id"=NEW."ReversalInventoryTransactionId";
                IF receipt."Status" NOT IN ('POSTED','REVERSED') OR inventory_row."Id" IS NULL OR
                   inventory_row."TransactionType"<>'REVERSAL' OR
                   inventory_row."OriginalTransactionId" IS DISTINCT FROM NEW."InventoryTransactionId" THEN
                  RAISE EXCEPTION 'RECEIPT_REVERSAL_ATOMICITY_VIOLATION' USING ERRCODE='23514';
                END IF;
              END IF;
              RETURN NEW;
            END $$;

            CREATE OR REPLACE FUNCTION procurement.verify_receipt_inventory_link()
            RETURNS trigger LANGUAGE plpgsql AS $$
            DECLARE receipt procurement.purchase_receipts%ROWTYPE;
            BEGIN
              IF TG_TABLE_NAME='purchase_receipts' THEN
                receipt:=NEW;
              ELSE
                SELECT * INTO receipt FROM procurement.purchase_receipts WHERE "Id"=NEW."ReceiptId";
              END IF;
              IF receipt."Status"='DRAFT' AND EXISTS (SELECT 1 FROM procurement.purchase_receipt_lines l
                 WHERE l."ReceiptId"=receipt."Id" AND (l."InventoryTransactionId" IS NOT NULL OR l."ReversalInventoryTransactionId" IS NOT NULL)) THEN
                RAISE EXCEPTION 'DRAFT_RECEIPT_INVENTORY_LINK_FORBIDDEN' USING ERRCODE='23514';
              END IF;
              IF receipt."Status" IN ('POSTED','REVERSED') AND EXISTS (
                SELECT 1 FROM procurement.purchase_receipt_lines l
                LEFT JOIN inventory.transactions t ON t."Id"=l."InventoryTransactionId"
                WHERE l."ReceiptId"=receipt."Id" AND (t."Id" IS NULL OR t."TransactionType"<>'RECEIPT'
                  OR t."SourceDocumentType"<>'PURCHASE_RECEIPT' OR t."SourceDocumentId"<>l."Id"
                  OR t."ItemId"<>l."ItemId" OR t."UnitId"<>l."UnitId" OR t."Quantity"<>l."Quantity"
                  OR t."WarehouseId"<>receipt."WarehouseId" OR t."EffectiveDate"<>receipt."EffectiveDate")) THEN
                RAISE EXCEPTION 'RECEIPT_INVENTORY_ATOMICITY_VIOLATION' USING ERRCODE='23514';
              END IF;
              IF receipt."Status"='REVERSED' AND EXISTS (
                SELECT 1 FROM procurement.purchase_receipt_lines l
                LEFT JOIN inventory.transactions t ON t."Id"=l."ReversalInventoryTransactionId"
                WHERE l."ReceiptId"=receipt."Id" AND (t."Id" IS NULL OR t."TransactionType"<>'REVERSAL'
                  OR t."OriginalTransactionId" IS DISTINCT FROM l."InventoryTransactionId")) THEN
                RAISE EXCEPTION 'RECEIPT_REVERSAL_ATOMICITY_VIOLATION' USING ERRCODE='23514';
              END IF;
              RETURN NEW;
            END $$;
            DROP TRIGGER IF EXISTS trg_verify_receipt_inventory_line ON procurement.purchase_receipt_lines;
            CREATE CONSTRAINT TRIGGER trg_verify_receipt_inventory_line
            AFTER INSERT OR UPDATE ON procurement.purchase_receipt_lines DEFERRABLE INITIALLY DEFERRED
            FOR EACH ROW EXECUTE FUNCTION procurement.verify_receipt_inventory_link();

            CREATE OR REPLACE FUNCTION procurement.verify_operational_fact_status()
            RETURNS trigger LANGUAGE plpgsql AS $$
            DECLARE order_id uuid; current_status text; expected_status text;
            BEGIN
              IF TG_TABLE_NAME='purchase_receipts' THEN order_id:=NEW."PurchaseOrderId";
              ELSIF TG_TABLE_NAME='purchase_schedule_cancellations' THEN order_id:=NEW."PurchaseOrderId";
              ELSE
                SELECT pol."PurchaseOrderId" INTO order_id FROM procurement.delivery_schedules ds
                JOIN procurement.purchase_order_lines pol ON pol."Id"=ds."PurchaseOrderLineId"
                WHERE ds."Id"=NEW."DeliveryScheduleId";
              END IF;
              SELECT "Status" INTO current_status FROM procurement.purchase_orders WHERE "Id"=order_id;
              IF current_status NOT IN ('DRAFT','APPROVED') THEN
                expected_status:=procurement.purchase_order_expected_operational_status(order_id);
                IF current_status<>expected_status THEN
                  RAISE EXCEPTION 'PURCHASE_ORDER_OPERATIONAL_STATUS_MISMATCH' USING ERRCODE='23514';
                END IF;
              END IF;
              RETURN NEW;
            END $$;
            DROP TRIGGER IF EXISTS trg_verify_receipt_order_status ON procurement.purchase_receipts;
            CREATE CONSTRAINT TRIGGER trg_verify_receipt_order_status AFTER INSERT OR UPDATE ON procurement.purchase_receipts
            DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION procurement.verify_operational_fact_status();
            DROP TRIGGER IF EXISTS trg_verify_cancellation_order_status ON procurement.purchase_schedule_cancellations;
            CREATE CONSTRAINT TRIGGER trg_verify_cancellation_order_status AFTER INSERT ON procurement.purchase_schedule_cancellations
            DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION procurement.verify_operational_fact_status();
            DROP TRIGGER IF EXISTS trg_verify_actual_order_status ON procurement.delivery_schedule_actual_milestones;
            CREATE CONSTRAINT TRIGGER trg_verify_actual_order_status AFTER INSERT ON procurement.delivery_schedule_actual_milestones
            DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION procurement.verify_operational_fact_status();
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DROP TRIGGER IF EXISTS trg_verify_actual_order_status ON procurement.delivery_schedule_actual_milestones;
            DROP TRIGGER IF EXISTS trg_verify_cancellation_order_status ON procurement.purchase_schedule_cancellations;
            DROP TRIGGER IF EXISTS trg_verify_receipt_order_status ON procurement.purchase_receipts;
            DROP FUNCTION IF EXISTS procurement.verify_operational_fact_status();
            DROP TRIGGER IF EXISTS trg_verify_receipt_inventory_line ON procurement.purchase_receipt_lines;
            DROP TRIGGER IF EXISTS trg_verify_delivery_schedule_change ON procurement.delivery_schedules;
            DROP FUNCTION IF EXISTS procurement.verify_delivery_schedule_change();
            DROP TRIGGER IF EXISTS trg_verify_expert_purchase_order ON procurement.purchase_orders;
            DROP FUNCTION IF EXISTS procurement.verify_expert_purchase_order();
            ALTER TABLE procurement.delivery_schedule_allocation_events DROP COLUMN IF EXISTS "TransactionId";
            """);
    }
}
