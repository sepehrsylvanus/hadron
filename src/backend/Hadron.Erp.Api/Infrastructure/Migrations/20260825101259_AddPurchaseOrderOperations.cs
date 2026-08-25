using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hadron.Erp.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPurchaseOrderOperations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateSequence(
                name: "purchase_receipt_number_seq",
                schema: "procurement");

            migrationBuilder.AddColumn<string>(
                name: "CreationSource",
                schema: "procurement",
                table: "purchase_orders",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "MANUAL");

            migrationBuilder.AddColumn<Guid>(
                name: "ExpertDecisionId",
                schema: "procurement",
                table: "purchase_order_lines",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "MrpRunId",
                schema: "procurement",
                table: "purchase_order_lines",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "OrderPlanningGenerationId",
                schema: "procurement",
                table: "purchase_order_lines",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "RecommendationId",
                schema: "procurement",
                table: "purchase_order_lines",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Reason",
                schema: "procurement",
                table: "delivery_schedule_actual_milestones",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SupersedesMilestoneId",
                schema: "procurement",
                table: "delivery_schedule_actual_milestones",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "delivery_schedule_allocation_events",
                schema: "procurement",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PurchaseOrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    PurchaseOrderLineId = table.Column<Guid>(type: "uuid", nullable: false),
                    BeforeJson = table.Column<string>(type: "jsonb", nullable: false),
                    AfterJson = table.Column<string>(type: "jsonb", nullable: false),
                    Reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    ActorIdentifier = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_delivery_schedule_allocation_events", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "purchase_receipts",
                schema: "procurement",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ReceiptNumber = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    PurchaseOrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    WarehouseId = table.Column<Guid>(type: "uuid", nullable: false),
                    EffectiveDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    ActorIdentifier = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    PostedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ReversedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ReversalReason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_purchase_receipts", x => x.Id);
                    table.CheckConstraint("ck_purchase_receipt_number", "\"ReceiptNumber\" ~ '^RCV-[0-9]{6,}$'");
                    table.CheckConstraint("ck_purchase_receipt_status", "\"Status\" IN ('DRAFT','POSTED','REVERSED')");
                    table.ForeignKey(
                        name: "FK_purchase_receipts_purchase_orders_PurchaseOrderId",
                        column: x => x.PurchaseOrderId,
                        principalSchema: "procurement",
                        principalTable: "purchase_orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_purchase_receipts_warehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalSchema: "inventory",
                        principalTable: "warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "purchase_schedule_cancellations",
                schema: "procurement",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PurchaseOrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    PurchaseOrderLineId = table.Column<Guid>(type: "uuid", nullable: false),
                    DeliveryScheduleId = table.Column<Guid>(type: "uuid", nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    EffectiveDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    ActorIdentifier = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_purchase_schedule_cancellations", x => x.Id);
                    table.CheckConstraint("ck_purchase_cancellation_qty", "\"Quantity\" > 0");
                    table.ForeignKey(
                        name: "FK_purchase_schedule_cancellations_delivery_schedules_Delivery~",
                        column: x => x.DeliveryScheduleId,
                        principalSchema: "procurement",
                        principalTable: "delivery_schedules",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_purchase_schedule_cancellations_purchase_order_lines_Purcha~",
                        column: x => x.PurchaseOrderLineId,
                        principalSchema: "procurement",
                        principalTable: "purchase_order_lines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_purchase_schedule_cancellations_purchase_orders_PurchaseOrd~",
                        column: x => x.PurchaseOrderId,
                        principalSchema: "procurement",
                        principalTable: "purchase_orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "purchase_receipt_lines",
                schema: "procurement",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ReceiptId = table.Column<Guid>(type: "uuid", nullable: false),
                    PurchaseOrderLineId = table.Column<Guid>(type: "uuid", nullable: false),
                    DeliveryScheduleId = table.Column<Guid>(type: "uuid", nullable: false),
                    ItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    UnitId = table.Column<Guid>(type: "uuid", nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    InventoryTransactionId = table.Column<Guid>(type: "uuid", nullable: true),
                    ReversalInventoryTransactionId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_purchase_receipt_lines", x => x.Id);
                    table.CheckConstraint("ck_purchase_receipt_line_qty", "\"Quantity\" > 0");
                    table.ForeignKey(
                        name: "FK_purchase_receipt_lines_delivery_schedules_DeliveryScheduleId",
                        column: x => x.DeliveryScheduleId,
                        principalSchema: "procurement",
                        principalTable: "delivery_schedules",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_purchase_receipt_lines_items_ItemId",
                        column: x => x.ItemId,
                        principalSchema: "catalog",
                        principalTable: "items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_purchase_receipt_lines_purchase_order_lines_PurchaseOrderLi~",
                        column: x => x.PurchaseOrderLineId,
                        principalSchema: "procurement",
                        principalTable: "purchase_order_lines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_purchase_receipt_lines_purchase_receipts_ReceiptId",
                        column: x => x.ReceiptId,
                        principalSchema: "procurement",
                        principalTable: "purchase_receipts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_purchase_receipt_lines_transactions_InventoryTransactionId",
                        column: x => x.InventoryTransactionId,
                        principalSchema: "inventory",
                        principalTable: "transactions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_purchase_receipt_lines_transactions_ReversalInventoryTransa~",
                        column: x => x.ReversalInventoryTransactionId,
                        principalSchema: "inventory",
                        principalTable: "transactions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_purchase_receipt_lines_units_UnitId",
                        column: x => x.UnitId,
                        principalSchema: "reference_data",
                        principalTable: "units",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_purchase_order_lines_ExpertDecisionId",
                schema: "procurement",
                table: "purchase_order_lines",
                column: "ExpertDecisionId",
                unique: true,
                filter: "\"ExpertDecisionId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_purchase_order_lines_MrpRunId",
                schema: "procurement",
                table: "purchase_order_lines",
                column: "MrpRunId");

            migrationBuilder.CreateIndex(
                name: "IX_purchase_order_lines_OrderPlanningGenerationId",
                schema: "procurement",
                table: "purchase_order_lines",
                column: "OrderPlanningGenerationId");

            migrationBuilder.CreateIndex(
                name: "IX_purchase_order_lines_RecommendationId",
                schema: "procurement",
                table: "purchase_order_lines",
                column: "RecommendationId");

            migrationBuilder.CreateIndex(
                name: "IX_delivery_schedule_actual_milestones_SupersedesMilestoneId",
                schema: "procurement",
                table: "delivery_schedule_actual_milestones",
                column: "SupersedesMilestoneId",
                unique: true,
                filter: "\"SupersedesMilestoneId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_delivery_schedule_allocation_events_PurchaseOrderId_Created~",
                schema: "procurement",
                table: "delivery_schedule_allocation_events",
                columns: new[] { "PurchaseOrderId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_purchase_receipt_lines_DeliveryScheduleId",
                schema: "procurement",
                table: "purchase_receipt_lines",
                column: "DeliveryScheduleId");

            migrationBuilder.CreateIndex(
                name: "IX_purchase_receipt_lines_InventoryTransactionId",
                schema: "procurement",
                table: "purchase_receipt_lines",
                column: "InventoryTransactionId",
                unique: true,
                filter: "\"InventoryTransactionId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_purchase_receipt_lines_ItemId",
                schema: "procurement",
                table: "purchase_receipt_lines",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "IX_purchase_receipt_lines_PurchaseOrderLineId",
                schema: "procurement",
                table: "purchase_receipt_lines",
                column: "PurchaseOrderLineId");

            migrationBuilder.CreateIndex(
                name: "IX_purchase_receipt_lines_ReceiptId",
                schema: "procurement",
                table: "purchase_receipt_lines",
                column: "ReceiptId");

            migrationBuilder.CreateIndex(
                name: "IX_purchase_receipt_lines_ReversalInventoryTransactionId",
                schema: "procurement",
                table: "purchase_receipt_lines",
                column: "ReversalInventoryTransactionId",
                unique: true,
                filter: "\"ReversalInventoryTransactionId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_purchase_receipt_lines_UnitId",
                schema: "procurement",
                table: "purchase_receipt_lines",
                column: "UnitId");

            migrationBuilder.CreateIndex(
                name: "IX_purchase_receipts_PurchaseOrderId_Status",
                schema: "procurement",
                table: "purchase_receipts",
                columns: new[] { "PurchaseOrderId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_purchase_receipts_ReceiptNumber",
                schema: "procurement",
                table: "purchase_receipts",
                column: "ReceiptNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_purchase_receipts_WarehouseId",
                schema: "procurement",
                table: "purchase_receipts",
                column: "WarehouseId");

            migrationBuilder.CreateIndex(
                name: "IX_purchase_schedule_cancellations_DeliveryScheduleId",
                schema: "procurement",
                table: "purchase_schedule_cancellations",
                column: "DeliveryScheduleId");

            migrationBuilder.CreateIndex(
                name: "IX_purchase_schedule_cancellations_PurchaseOrderId",
                schema: "procurement",
                table: "purchase_schedule_cancellations",
                column: "PurchaseOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_purchase_schedule_cancellations_PurchaseOrderLineId",
                schema: "procurement",
                table: "purchase_schedule_cancellations",
                column: "PurchaseOrderLineId");

            migrationBuilder.AddForeignKey(
                name: "FK_delivery_schedule_actual_milestones_delivery_schedule_actua~",
                schema: "procurement",
                table: "delivery_schedule_actual_milestones",
                column: "SupersedesMilestoneId",
                principalSchema: "procurement",
                principalTable: "delivery_schedule_actual_milestones",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION procurement.guard_purchase_order_update()
                RETURNS trigger LANGUAGE plpgsql AS $$
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
                  ELSIF OLD."Status" NOT IN ('DRAFT','APPROVED') AND NEW."Status" <> OLD."Status"
                        AND current_setting('hadron.mrp13_operation', true) IS DISTINCT FROM 'on' THEN
                    RAISE EXCEPTION 'PURCHASE_ORDER_OPERATION_REQUIRES_COMMAND' USING ERRCODE='23514';
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
                DECLARE po_status text; po_supplier uuid; po_currency uuid; po_shipping uuid; po_incoterm uuid; po_source text;
                BEGIN
                  SELECT po."Status",po."SupplierId",po."CurrencyId",po."ShippingModeId",po."IncotermId",po."CreationSource"
                  INTO po_status,po_supplier,po_currency,po_shipping,po_incoterm,po_source
                  FROM procurement.purchase_orders po WHERE po."Id"=COALESCE(NEW."PurchaseOrderId",OLD."PurchaseOrderId");
                  IF po_status <> 'DRAFT' OR (po_source='EXPERT_DECISION' AND
                     current_setting('hadron.mrp13_conversion',true) IS DISTINCT FROM 'on') THEN
                    RAISE EXCEPTION 'PURCHASE_ORDER_SNAPSHOT_LOCKED' USING ERRCODE='23514';
                  END IF;
                  IF TG_OP <> 'DELETE' AND NOT EXISTS (
                    SELECT 1 FROM procurement.sourcing_options so
                    JOIN procurement.sourcing_prices sp ON sp."Id"=NEW."SourcingPriceId" AND sp."SourcingOptionId"=so."Id"
                    JOIN catalog.items i ON i."Id"=so."ItemId" JOIN reference_data.units u ON u."Id"=so."ItemBaseUnitId"
                    WHERE so."Id"=NEW."SourcingOptionId" AND so."IsActive" AND so."SupplierId"=po_supplier
                      AND so."ItemId"=NEW."ItemId" AND so."ItemBaseUnitId"=NEW."ItemBaseUnitId"
                      AND so."ShippingModeId" IS NOT DISTINCT FROM po_shipping AND so."IncotermId" IS NOT DISTINCT FROM po_incoterm
                      AND sp."CurrencyId"=po_currency AND i."IsActive" AND i."ItemType" IN ('PART','COMPONENT')
                      AND i."BaseUnitId"=NEW."ItemBaseUnitId" AND u."IsActive") THEN
                    RAISE EXCEPTION 'PURCHASE_ORDER_LINE_SOURCE_INTEGRITY' USING ERRCODE='23514';
                  END IF;
                  RETURN COALESCE(NEW,OLD);
                END $$;

                CREATE OR REPLACE FUNCTION procurement.guard_delivery_schedule_mutation()
                RETURNS trigger LANGUAGE plpgsql AS $$
                DECLARE po_status text;
                BEGIN
                  SELECT po."Status" INTO po_status FROM procurement.purchase_orders po
                  JOIN procurement.purchase_order_lines pol ON pol."PurchaseOrderId"=po."Id"
                  WHERE pol."Id"=COALESCE(NEW."PurchaseOrderLineId",OLD."PurchaseOrderLineId");
                  IF po_status <> 'DRAFT' AND current_setting('hadron.mrp13_schedule_change',true) IS DISTINCT FROM 'on' THEN
                    RAISE EXCEPTION 'PURCHASE_ORDER_SCHEDULE_LOCKED' USING ERRCODE='23514';
                  END IF;
                  IF po_status <> 'DRAFT' AND TG_OP='DELETE' THEN
                    RAISE EXCEPTION 'ORDERED_SCHEDULE_DELETE_FORBIDDEN' USING ERRCODE='23514';
                  END IF;
                  RETURN COALESCE(NEW,OLD);
                END $$;

                CREATE OR REPLACE FUNCTION procurement.guard_receipt_header()
                RETURNS trigger LANGUAGE plpgsql AS $$
                DECLARE ordered_date date; schedule_id uuid;
                BEGIN
                  IF TG_OP='DELETE' THEN RAISE EXCEPTION 'RECEIPT_HISTORY_IMMUTABLE' USING ERRCODE='23514'; END IF;
                  IF TG_OP='INSERT' THEN
                    IF NEW."Status"<>'DRAFT' OR NEW."Version"<>1 THEN RAISE EXCEPTION 'RECEIPT_MUST_START_DRAFT' USING ERRCODE='23514'; END IF;
                    SELECT po."OrderedDate" INTO ordered_date FROM procurement.purchase_orders po WHERE po."Id"=NEW."PurchaseOrderId";
                    IF ordered_date IS NULL OR NEW."EffectiveDate"<ordered_date OR NEW."EffectiveDate">timezone('Asia/Tehran',CURRENT_TIMESTAMP)::date THEN
                      RAISE EXCEPTION 'INVALID_RECEIPT_EFFECTIVE_DATE' USING ERRCODE='23514';
                    END IF;
                    RETURN NEW;
                  END IF;
                  IF NEW."ReceiptNumber" IS DISTINCT FROM OLD."ReceiptNumber" OR NEW."PurchaseOrderId" IS DISTINCT FROM OLD."PurchaseOrderId"
                     OR NEW."WarehouseId" IS DISTINCT FROM OLD."WarehouseId" OR NEW."EffectiveDate" IS DISTINCT FROM OLD."EffectiveDate"
                     OR NEW."Notes" IS DISTINCT FROM OLD."Notes" OR NEW."ActorIdentifier" IS DISTINCT FROM OLD."ActorIdentifier" THEN
                    RAISE EXCEPTION 'RECEIPT_IDENTITY_IMMUTABLE' USING ERRCODE='23514';
                  END IF;
                  IF NEW."Version"<>OLD."Version"+1 OR NOT ((OLD."Status"='DRAFT' AND NEW."Status"='POSTED') OR
                     (OLD."Status"='POSTED' AND NEW."Status"='REVERSED')) THEN
                    RAISE EXCEPTION 'INVALID_RECEIPT_TRANSITION' USING ERRCODE='23514';
                  END IF;
                  IF NEW."Status"='POSTED' THEN
                    FOR schedule_id IN SELECT DISTINCT "DeliveryScheduleId" FROM procurement.purchase_receipt_lines WHERE "ReceiptId"=NEW."Id" ORDER BY 1 LOOP
                      PERFORM 1 FROM procurement.delivery_schedules WHERE "Id"=schedule_id FOR UPDATE;
                      IF (SELECT COALESCE(SUM(prl."Quantity"),0) FROM procurement.purchase_receipt_lines prl
                          JOIN procurement.purchase_receipts pr ON pr."Id"=prl."ReceiptId"
                          WHERE prl."DeliveryScheduleId"=schedule_id AND (pr."Status"='POSTED' OR pr."Id"=NEW."Id"))
                         + (SELECT COALESCE(SUM(c."Quantity"),0) FROM procurement.purchase_schedule_cancellations c WHERE c."DeliveryScheduleId"=schedule_id)
                         > (SELECT "AllocatedQuantity" FROM procurement.delivery_schedules WHERE "Id"=schedule_id) THEN
                        RAISE EXCEPTION 'OVER_RECEIPT_NOT_ALLOWED' USING ERRCODE='23514';
                      END IF;
                    END LOOP;
                  END IF;
                  RETURN NEW;
                END $$;
                CREATE TRIGGER trg_guard_purchase_receipt BEFORE INSERT OR UPDATE OR DELETE ON procurement.purchase_receipts
                FOR EACH ROW EXECUTE FUNCTION procurement.guard_receipt_header();

                CREATE OR REPLACE FUNCTION procurement.guard_receipt_line()
                RETURNS trigger LANGUAGE plpgsql AS $$
                DECLARE receipt_status text; receipt_po uuid; line_po uuid; schedule_line uuid; line_item uuid; line_unit uuid;
                BEGIN
                  IF TG_OP='DELETE' THEN RAISE EXCEPTION 'RECEIPT_HISTORY_IMMUTABLE' USING ERRCODE='23514'; END IF;
                  SELECT "Status","PurchaseOrderId" INTO receipt_status,receipt_po FROM procurement.purchase_receipts WHERE "Id"=NEW."ReceiptId";
                  SELECT "PurchaseOrderId","ItemId","ItemBaseUnitId" INTO line_po,line_item,line_unit FROM procurement.purchase_order_lines WHERE "Id"=NEW."PurchaseOrderLineId";
                  SELECT "PurchaseOrderLineId" INTO schedule_line FROM procurement.delivery_schedules WHERE "Id"=NEW."DeliveryScheduleId";
                  IF receipt_po<>line_po OR schedule_line<>NEW."PurchaseOrderLineId" OR NEW."ItemId"<>line_item OR NEW."UnitId"<>line_unit THEN
                    RAISE EXCEPTION 'RECEIPT_LINE_SCOPE_INVALID' USING ERRCODE='23514';
                  END IF;
                  IF TG_OP='UPDATE' AND (NEW."ReceiptId"<>OLD."ReceiptId" OR NEW."PurchaseOrderLineId"<>OLD."PurchaseOrderLineId"
                     OR NEW."DeliveryScheduleId"<>OLD."DeliveryScheduleId" OR NEW."ItemId"<>OLD."ItemId"
                     OR NEW."UnitId"<>OLD."UnitId" OR NEW."Quantity"<>OLD."Quantity") THEN
                    RAISE EXCEPTION 'RECEIPT_LINE_IMMUTABLE' USING ERRCODE='23514';
                  END IF;
                  IF TG_OP='INSERT' AND receipt_status<>'DRAFT' THEN RAISE EXCEPTION 'RECEIPT_LINE_REQUIRES_DRAFT' USING ERRCODE='23514'; END IF;
                  RETURN NEW;
                END $$;
                CREATE TRIGGER trg_guard_purchase_receipt_line BEFORE INSERT OR UPDATE OR DELETE ON procurement.purchase_receipt_lines
                FOR EACH ROW EXECUTE FUNCTION procurement.guard_receipt_line();

                CREATE OR REPLACE FUNCTION procurement.verify_receipt_inventory_link()
                RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                  IF NEW."Status"='POSTED' AND EXISTS (
                    SELECT 1 FROM procurement.purchase_receipt_lines l
                    LEFT JOIN inventory.transactions t ON t."Id"=l."InventoryTransactionId"
                    WHERE l."ReceiptId"=NEW."Id" AND (t."Id" IS NULL OR t."TransactionType"<>'RECEIPT'
                      OR t."SourceDocumentType"<>'PURCHASE_RECEIPT' OR t."SourceDocumentId"<>l."Id"
                      OR t."ItemId"<>l."ItemId" OR t."UnitId"<>l."UnitId" OR t."Quantity"<>l."Quantity"
                      OR t."WarehouseId"<>NEW."WarehouseId" OR t."EffectiveDate"<>NEW."EffectiveDate")) THEN
                    RAISE EXCEPTION 'RECEIPT_INVENTORY_ATOMICITY_VIOLATION' USING ERRCODE='23514';
                  END IF;
                  IF NEW."Status"='REVERSED' AND EXISTS (
                    SELECT 1 FROM procurement.purchase_receipt_lines l
                    LEFT JOIN inventory.transactions t ON t."Id"=l."ReversalInventoryTransactionId"
                    WHERE l."ReceiptId"=NEW."Id" AND (t."Id" IS NULL OR t."TransactionType"<>'REVERSAL'
                      OR t."OriginalTransactionId"<>l."InventoryTransactionId")) THEN
                    RAISE EXCEPTION 'RECEIPT_REVERSAL_ATOMICITY_VIOLATION' USING ERRCODE='23514';
                  END IF;
                  RETURN NEW;
                END $$;
                CREATE CONSTRAINT TRIGGER trg_verify_receipt_inventory AFTER INSERT OR UPDATE ON procurement.purchase_receipts
                DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION procurement.verify_receipt_inventory_link();

                CREATE OR REPLACE FUNCTION procurement.guard_cancellation()
                RETURNS trigger LANGUAGE plpgsql AS $$
                DECLARE po_status text; line_po uuid; schedule_line uuid; open_qty numeric;
                BEGIN
                  IF TG_OP<>'INSERT' THEN RAISE EXCEPTION 'CANCELLATION_HISTORY_IMMUTABLE' USING ERRCODE='23514'; END IF;
                  SELECT "Status" INTO po_status FROM procurement.purchase_orders WHERE "Id"=NEW."PurchaseOrderId";
                  SELECT "PurchaseOrderId" INTO line_po FROM procurement.purchase_order_lines WHERE "Id"=NEW."PurchaseOrderLineId";
                  SELECT "PurchaseOrderLineId" INTO schedule_line FROM procurement.delivery_schedules WHERE "Id"=NEW."DeliveryScheduleId" FOR UPDATE;
                  IF po_status IN ('DRAFT','APPROVED') OR line_po<>NEW."PurchaseOrderId" OR schedule_line<>NEW."PurchaseOrderLineId" THEN
                    RAISE EXCEPTION 'CANCELLATION_SCOPE_INVALID' USING ERRCODE='23514';
                  END IF;
                  SELECT ds."AllocatedQuantity"-
                    COALESCE((SELECT SUM(l."Quantity") FROM procurement.purchase_receipt_lines l JOIN procurement.purchase_receipts r ON r."Id"=l."ReceiptId" WHERE l."DeliveryScheduleId"=ds."Id" AND r."Status"='POSTED'),0)-
                    COALESCE((SELECT SUM(c."Quantity") FROM procurement.purchase_schedule_cancellations c WHERE c."DeliveryScheduleId"=ds."Id"),0)
                  INTO open_qty FROM procurement.delivery_schedules ds WHERE ds."Id"=NEW."DeliveryScheduleId";
                  IF NEW."Quantity">open_qty THEN RAISE EXCEPTION 'CANCELLATION_EXCEEDS_OPEN_QTY' USING ERRCODE='23514'; END IF;
                  RETURN NEW;
                END $$;
                CREATE TRIGGER trg_guard_purchase_cancellation BEFORE INSERT OR UPDATE OR DELETE ON procurement.purchase_schedule_cancellations
                FOR EACH ROW EXECUTE FUNCTION procurement.guard_cancellation();

                CREATE OR REPLACE FUNCTION procurement.guard_allocation_history()
                RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN
                  IF TG_OP<>'INSERT' THEN RAISE EXCEPTION 'SCHEDULE_ALLOCATION_HISTORY_IMMUTABLE' USING ERRCODE='23514'; END IF;
                  RETURN NEW; END $$;
                CREATE TRIGGER trg_guard_schedule_allocation_history BEFORE UPDATE OR DELETE ON procurement.delivery_schedule_allocation_events
                FOR EACH ROW EXECUTE FUNCTION procurement.guard_allocation_history();

                CREATE OR REPLACE FUNCTION procurement.guard_actual_milestone_insert()
                RETURNS trigger LANGUAGE plpgsql AS $$
                DECLARE prior procurement.delivery_schedule_actual_milestones%ROWTYPE;
                BEGIN
                  IF NEW."ActualDate">timezone('Asia/Tehran',CURRENT_TIMESTAMP)::date THEN
                    RAISE EXCEPTION 'FUTURE_ACTUAL_MILESTONE_NOT_ALLOWED' USING ERRCODE='23514';
                  END IF;
                  IF NEW."MilestoneCode"='WAREHOUSE_RECEIPT' THEN
                    RAISE EXCEPTION 'WAREHOUSE_RECEIPT_MILESTONE_IS_RECEIPT_DERIVED' USING ERRCODE='23514';
                  END IF;
                  IF NEW."SupersedesMilestoneId" IS NOT NULL THEN
                    SELECT * INTO prior FROM procurement.delivery_schedule_actual_milestones
                    WHERE "Id"=NEW."SupersedesMilestoneId";
                    IF NOT FOUND OR prior."DeliveryScheduleId"<>NEW."DeliveryScheduleId"
                       OR prior."MilestoneCode"<>NEW."MilestoneCode" OR NULLIF(BTRIM(NEW."Reason"),'') IS NULL THEN
                      RAISE EXCEPTION 'INVALID_ACTUAL_MILESTONE_CORRECTION' USING ERRCODE='23514';
                    END IF;
                  ELSIF EXISTS (
                    SELECT 1 FROM procurement.delivery_schedule_actual_milestones current_row
                    WHERE current_row."DeliveryScheduleId"=NEW."DeliveryScheduleId"
                      AND current_row."MilestoneCode"=NEW."MilestoneCode"
                      AND NOT EXISTS (SELECT 1 FROM procurement.delivery_schedule_actual_milestones successor
                        WHERE successor."SupersedesMilestoneId"=current_row."Id")) THEN
                    RAISE EXCEPTION 'ACTUAL_MILESTONE_ALREADY_EXISTS' USING ERRCODE='23514';
                  END IF;
                  RETURN NEW;
                END $$;
                CREATE TRIGGER trg_guard_purchase_actual_insert
                BEFORE INSERT ON procurement.delivery_schedule_actual_milestones
                FOR EACH ROW EXECUTE FUNCTION procurement.guard_actual_milestone_insert();
                """);

            migrationBuilder.AddForeignKey(
                name: "FK_purchase_order_lines_mrp_runs_MrpRunId",
                schema: "procurement",
                table: "purchase_order_lines",
                column: "MrpRunId",
                principalSchema: "planning",
                principalTable: "mrp_runs",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_purchase_order_lines_order_planning_generations_OrderPlanni~",
                schema: "procurement",
                table: "purchase_order_lines",
                column: "OrderPlanningGenerationId",
                principalSchema: "planning",
                principalTable: "order_planning_generations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_purchase_order_lines_order_recommendation_decisions_ExpertD~",
                schema: "procurement",
                table: "purchase_order_lines",
                column: "ExpertDecisionId",
                principalSchema: "planning",
                principalTable: "order_recommendation_decisions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_purchase_order_lines_order_recommendations_RecommendationId",
                schema: "procurement",
                table: "purchase_order_lines",
                column: "RecommendationId",
                principalSchema: "planning",
                principalTable: "order_recommendations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION inventory.guard_transaction_insert()
                RETURNS trigger LANGUAGE plpgsql AS $$
                DECLARE
                    v_item_unit uuid; v_item_active boolean; v_unit_active boolean;
                    v_source_active boolean; v_destination_active boolean;
                    v_original inventory.transactions%ROWTYPE;
                    v_source_delta numeric := 0; v_destination_delta numeric := 0; v_warehouse uuid;
                BEGIN
                    PERFORM inventory.lock_item_inventory(NEW."ItemId");
                    FOR v_warehouse IN SELECT DISTINCT value FROM unnest(ARRAY[NEW."WarehouseId",NEW."DestinationWarehouseId"]) value
                        WHERE value IS NOT NULL ORDER BY value LOOP
                        PERFORM inventory.lock_warehouse_inventory(v_warehouse);
                    END LOOP;
                    IF NEW."EffectiveDate">timezone('Asia/Tehran',CURRENT_TIMESTAMP)::date THEN
                        RAISE EXCEPTION 'Future inventory posting is not allowed' USING ERRCODE='23514',CONSTRAINT='ck_inventory_future_date';
                    END IF;
                    SELECT i."BaseUnitId",i."IsActive" INTO v_item_unit,v_item_active FROM catalog.items i WHERE i."Id"=NEW."ItemId" FOR KEY SHARE;
                    SELECT u."IsActive" INTO v_unit_active FROM reference_data.units u WHERE u."Id"=NEW."UnitId" FOR KEY SHARE;
                    SELECT w."IsActive" INTO v_source_active FROM inventory.warehouses w WHERE w."Id"=NEW."WarehouseId" FOR KEY SHARE;
                    IF NEW."DestinationWarehouseId" IS NOT NULL THEN
                        SELECT w."IsActive" INTO v_destination_active FROM inventory.warehouses w WHERE w."Id"=NEW."DestinationWarehouseId" FOR KEY SHARE;
                    END IF;
                    IF NEW."TransactionType"='REVERSAL' THEN
                        SELECT * INTO v_original FROM inventory.transactions WHERE "Id"=NEW."OriginalTransactionId" FOR KEY SHARE;
                        IF NOT FOUND OR v_original."TransactionType"='REVERSAL' OR NEW."ItemId"<>v_original."ItemId"
                           OR NEW."WarehouseId"<>v_original."WarehouseId" OR NEW."DestinationWarehouseId" IS DISTINCT FROM v_original."DestinationWarehouseId"
                           OR NEW."Quantity"<>v_original."Quantity" OR NEW."UnitId"<>v_original."UnitId"
                           OR NEW."EffectiveDate"<>v_original."EffectiveDate" OR NEW."SourceDocumentType" IS NOT NULL OR NEW."SourceDocumentId" IS NOT NULL THEN
                            RAISE EXCEPTION 'Invalid inventory reversal' USING ERRCODE='23514',CONSTRAINT='ck_inventory_reversal_rules';
                        END IF;
                    ELSE
                        IF NEW."UnitId"<>v_item_unit THEN RAISE EXCEPTION 'Inventory unit must equal current item base unit' USING ERRCODE='23514',CONSTRAINT='ck_inventory_base_unit'; END IF;
                        IF (NOT COALESCE(v_item_active,false) AND NOT (NEW."TransactionType"='RECEIPT' AND NEW."SourceDocumentType"='PURCHASE_RECEIPT'))
                           OR NOT COALESCE(v_unit_active,false) OR NOT COALESCE(v_source_active,false)
                           OR (NEW."DestinationWarehouseId" IS NOT NULL AND NOT COALESCE(v_destination_active,false)) THEN
                            RAISE EXCEPTION 'Active item, unit and warehouse are required' USING ERRCODE='23514',CONSTRAINT='ck_inventory_active_assignment';
                        END IF;
                    END IF;
                    IF NEW."TransactionType" IN ('ISSUE','ADJ_OUT','TRANSFER') THEN v_source_delta := -NEW."Quantity";
                    ELSIF NEW."TransactionType"='REVERSAL' THEN
                        IF v_original."TransactionType" IN ('OPENING','RECEIPT','ADJ_IN') THEN v_source_delta := -NEW."Quantity";
                        ELSIF v_original."TransactionType" IN ('ISSUE','ADJ_OUT','TRANSFER') THEN v_source_delta := NEW."Quantity"; END IF;
                        IF v_original."TransactionType"='TRANSFER' THEN v_destination_delta := -NEW."Quantity"; END IF;
                    END IF;
                    IF v_source_delta<0 THEN PERFORM inventory.assert_nonnegative(NEW."ItemId",NEW."WarehouseId",NEW."EffectiveDate",v_source_delta); END IF;
                    IF v_destination_delta<0 THEN PERFORM inventory.assert_nonnegative(NEW."ItemId",NEW."DestinationWarehouseId",NEW."EffectiveDate",v_destination_delta); END IF;
                    RETURN NEW;
                END $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_delivery_schedule_actual_milestones_delivery_schedule_actua~",
                schema: "procurement",
                table: "delivery_schedule_actual_milestones");

            migrationBuilder.DropForeignKey(
                name: "FK_purchase_order_lines_mrp_runs_MrpRunId",
                schema: "procurement",
                table: "purchase_order_lines");

            migrationBuilder.DropForeignKey(
                name: "FK_purchase_order_lines_order_planning_generations_OrderPlanni~",
                schema: "procurement",
                table: "purchase_order_lines");

            migrationBuilder.DropForeignKey(
                name: "FK_purchase_order_lines_order_recommendation_decisions_ExpertD~",
                schema: "procurement",
                table: "purchase_order_lines");

            migrationBuilder.DropForeignKey(
                name: "FK_purchase_order_lines_order_recommendations_RecommendationId",
                schema: "procurement",
                table: "purchase_order_lines");

            migrationBuilder.DropTable(
                name: "delivery_schedule_allocation_events",
                schema: "procurement");

            migrationBuilder.DropTable(
                name: "purchase_receipt_lines",
                schema: "procurement");

            migrationBuilder.DropTable(
                name: "purchase_schedule_cancellations",
                schema: "procurement");

            migrationBuilder.DropTable(
                name: "purchase_receipts",
                schema: "procurement");

            migrationBuilder.DropIndex(
                name: "IX_purchase_order_lines_ExpertDecisionId",
                schema: "procurement",
                table: "purchase_order_lines");

            migrationBuilder.DropIndex(
                name: "IX_purchase_order_lines_MrpRunId",
                schema: "procurement",
                table: "purchase_order_lines");

            migrationBuilder.DropIndex(
                name: "IX_purchase_order_lines_OrderPlanningGenerationId",
                schema: "procurement",
                table: "purchase_order_lines");

            migrationBuilder.DropIndex(
                name: "IX_purchase_order_lines_RecommendationId",
                schema: "procurement",
                table: "purchase_order_lines");

            migrationBuilder.DropIndex(
                name: "IX_delivery_schedule_actual_milestones_SupersedesMilestoneId",
                schema: "procurement",
                table: "delivery_schedule_actual_milestones");

            migrationBuilder.DropColumn(
                name: "CreationSource",
                schema: "procurement",
                table: "purchase_orders");

            migrationBuilder.DropColumn(
                name: "ExpertDecisionId",
                schema: "procurement",
                table: "purchase_order_lines");

            migrationBuilder.DropColumn(
                name: "MrpRunId",
                schema: "procurement",
                table: "purchase_order_lines");

            migrationBuilder.DropColumn(
                name: "OrderPlanningGenerationId",
                schema: "procurement",
                table: "purchase_order_lines");

            migrationBuilder.DropColumn(
                name: "RecommendationId",
                schema: "procurement",
                table: "purchase_order_lines");

            migrationBuilder.DropColumn(
                name: "Reason",
                schema: "procurement",
                table: "delivery_schedule_actual_milestones");

            migrationBuilder.DropColumn(
                name: "SupersedesMilestoneId",
                schema: "procurement",
                table: "delivery_schedule_actual_milestones");

            migrationBuilder.DropSequence(
                name: "purchase_receipt_number_seq",
                schema: "procurement");
        }
    }
}
