using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hadron.Erp.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPurchaseSupplyFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateSequence(
                name: "purchase_order_number_seq",
                schema: "procurement");

            migrationBuilder.CreateTable(
                name: "purchase_orders",
                schema: "procurement",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PurchaseOrderNumber = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    SupplierId = table.Column<Guid>(type: "uuid", nullable: false),
                    CurrencyId = table.Column<Guid>(type: "uuid", nullable: false),
                    PaymentTermId = table.Column<Guid>(type: "uuid", nullable: false),
                    AdvancePercent = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    BeforeShipmentPercent = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    CreditPercent = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    CreditDays = table.Column<int>(type: "integer", nullable: true),
                    CreditAnchor = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    ShippingModeId = table.Column<Guid>(type: "uuid", nullable: true),
                    IncotermId = table.Column<Guid>(type: "uuid", nullable: true),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    ApprovedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    OrderedDate = table.Column<DateOnly>(type: "date", nullable: true),
                    OrderedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_purchase_orders", x => x.Id);
                    table.CheckConstraint("ck_purchase_orders_credit", "(\"CreditPercent\" = 0 AND \"CreditDays\" IS NULL AND \"CreditAnchor\" IS NULL) OR (\"CreditPercent\" > 0 AND \"CreditDays\" > 0 AND \"CreditAnchor\" IN ('ORDERED','SHIPPED','WAREHOUSE_RECEIPT'))");
                    table.CheckConstraint("ck_purchase_orders_number", "\"PurchaseOrderNumber\" ~ '^PO-[0-9]{6,}$'");
                    table.CheckConstraint("ck_purchase_orders_ordered_fields", "(\"Status\" IN ('DRAFT','APPROVED') AND \"OrderedDate\" IS NULL AND \"OrderedAtUtc\" IS NULL) OR (\"Status\" NOT IN ('DRAFT','APPROVED') AND \"OrderedDate\" IS NOT NULL AND \"OrderedAtUtc\" IS NOT NULL)");
                    table.CheckConstraint("ck_purchase_orders_payment_percentages", "\"AdvancePercent\" BETWEEN 0 AND 100 AND \"BeforeShipmentPercent\" BETWEEN 0 AND 100 AND \"CreditPercent\" BETWEEN 0 AND 100 AND \"AdvancePercent\" + \"BeforeShipmentPercent\" + \"CreditPercent\" = 100");
                    table.CheckConstraint("ck_purchase_orders_status", "\"Status\" IN ('DRAFT','APPROVED','ORDERED','SHIPPED','CUSTOMS','PARTIALLY_RECEIVED','RECEIVED','CANCELLED')");
                    table.ForeignKey(
                        name: "FK_purchase_orders_currencies_CurrencyId",
                        column: x => x.CurrencyId,
                        principalSchema: "reference_data",
                        principalTable: "currencies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_purchase_orders_reference_values_IncotermId",
                        column: x => x.IncotermId,
                        principalSchema: "reference_data",
                        principalTable: "reference_values",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_purchase_orders_reference_values_PaymentTermId",
                        column: x => x.PaymentTermId,
                        principalSchema: "reference_data",
                        principalTable: "reference_values",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_purchase_orders_reference_values_ShippingModeId",
                        column: x => x.ShippingModeId,
                        principalSchema: "reference_data",
                        principalTable: "reference_values",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_purchase_orders_suppliers_SupplierId",
                        column: x => x.SupplierId,
                        principalSchema: "procurement",
                        principalTable: "suppliers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "purchase_order_lifecycle_events",
                schema: "procurement",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PurchaseOrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    FromStatus = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ToStatus = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    ActorIdentifier = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_purchase_order_lifecycle_events", x => x.Id);
                    table.ForeignKey(
                        name: "FK_purchase_order_lifecycle_events_purchase_orders_PurchaseOrd~",
                        column: x => x.PurchaseOrderId,
                        principalSchema: "procurement",
                        principalTable: "purchase_orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "purchase_order_lines",
                schema: "procurement",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PurchaseOrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    LineNumber = table.Column<int>(type: "integer", nullable: false),
                    ItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourcingOptionId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourcingPriceId = table.Column<Guid>(type: "uuid", nullable: false),
                    ItemBaseUnitId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderedQuantity = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    UnitPrice = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    MinimumOrderQuantity = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true),
                    SupplierLeadTimeDays = table.Column<int>(type: "integer", nullable: true),
                    ForwarderToShipmentDays = table.Column<int>(type: "integer", nullable: true),
                    ShippingPeriodDays = table.Column<int>(type: "integer", nullable: true),
                    DestinationCustomsDays = table.Column<int>(type: "integer", nullable: true),
                    Notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_purchase_order_lines", x => x.Id);
                    table.CheckConstraint("ck_purchase_order_lines_lead_times", "(\"SupplierLeadTimeDays\" IS NULL OR \"SupplierLeadTimeDays\" >= 0) AND (\"ForwarderToShipmentDays\" IS NULL OR \"ForwarderToShipmentDays\" >= 0) AND (\"ShippingPeriodDays\" IS NULL OR \"ShippingPeriodDays\" >= 0) AND (\"DestinationCustomsDays\" IS NULL OR \"DestinationCustomsDays\" >= 0)");
                    table.CheckConstraint("ck_purchase_order_lines_moq", "\"MinimumOrderQuantity\" IS NULL OR \"MinimumOrderQuantity\" >= 0");
                    table.CheckConstraint("ck_purchase_order_lines_number", "\"LineNumber\" > 0");
                    table.CheckConstraint("ck_purchase_order_lines_price", "\"UnitPrice\" >= 0");
                    table.CheckConstraint("ck_purchase_order_lines_quantity", "\"OrderedQuantity\" > 0");
                    table.ForeignKey(
                        name: "FK_purchase_order_lines_items_ItemId",
                        column: x => x.ItemId,
                        principalSchema: "catalog",
                        principalTable: "items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_purchase_order_lines_purchase_orders_PurchaseOrderId",
                        column: x => x.PurchaseOrderId,
                        principalSchema: "procurement",
                        principalTable: "purchase_orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_purchase_order_lines_sourcing_options_SourcingOptionId",
                        column: x => x.SourcingOptionId,
                        principalSchema: "procurement",
                        principalTable: "sourcing_options",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_purchase_order_lines_sourcing_prices_SourcingPriceId",
                        column: x => x.SourcingPriceId,
                        principalSchema: "procurement",
                        principalTable: "sourcing_prices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_purchase_order_lines_units_ItemBaseUnitId",
                        column: x => x.ItemBaseUnitId,
                        principalSchema: "reference_data",
                        principalTable: "units",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "delivery_schedules",
                schema: "procurement",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PurchaseOrderLineId = table.Column<Guid>(type: "uuid", nullable: false),
                    ScheduleNumber = table.Column<int>(type: "integer", nullable: false),
                    AllocatedQuantity = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    RequestedReceiptDate = table.Column<DateOnly>(type: "date", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_delivery_schedules", x => x.Id);
                    table.CheckConstraint("ck_delivery_schedules_number", "\"ScheduleNumber\" > 0");
                    table.CheckConstraint("ck_delivery_schedules_quantity", "\"AllocatedQuantity\" > 0");
                    table.ForeignKey(
                        name: "FK_delivery_schedules_purchase_order_lines_PurchaseOrderLineId",
                        column: x => x.PurchaseOrderLineId,
                        principalSchema: "procurement",
                        principalTable: "purchase_order_lines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "delivery_schedule_actual_milestones",
                schema: "procurement",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DeliveryScheduleId = table.Column<Guid>(type: "uuid", nullable: false),
                    MilestoneCode = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    ActualDate = table.Column<DateOnly>(type: "date", nullable: false),
                    SourceType = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    SourceId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorIdentifier = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_delivery_schedule_actual_milestones", x => x.Id);
                    table.CheckConstraint("ck_delivery_actual_milestone", "\"MilestoneCode\" IN ('SUPPLIER_READY','HANDOVER_TO_FORWARDER','SHIPPED','ARRIVED_DESTINATION','CUSTOMS_CLEARED','WAREHOUSE_RECEIPT')");
                    table.CheckConstraint("ck_delivery_actual_source", "length(btrim(\"SourceType\")) > 0");
                    table.ForeignKey(
                        name: "FK_delivery_schedule_actual_milestones_delivery_schedules_Deli~",
                        column: x => x.DeliveryScheduleId,
                        principalSchema: "procurement",
                        principalTable: "delivery_schedules",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "delivery_schedule_plan_revisions",
                schema: "procurement",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DeliveryScheduleId = table.Column<Guid>(type: "uuid", nullable: false),
                    PlanType = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    RevisionNumber = table.Column<int>(type: "integer", nullable: false),
                    SupplierReadyDate = table.Column<DateOnly>(type: "date", nullable: true),
                    HandoverToForwarderDate = table.Column<DateOnly>(type: "date", nullable: true),
                    ShippedDate = table.Column<DateOnly>(type: "date", nullable: true),
                    ArrivedDestinationDate = table.Column<DateOnly>(type: "date", nullable: true),
                    CustomsClearedDate = table.Column<DateOnly>(type: "date", nullable: true),
                    WarehouseReceiptDate = table.Column<DateOnly>(type: "date", nullable: true),
                    Reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    ActorIdentifier = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_delivery_schedule_plan_revisions", x => x.Id);
                    table.CheckConstraint("ck_delivery_plan_core_chronology", "(\"SupplierReadyDate\" IS NULL OR \"ShippedDate\" IS NULL OR \"SupplierReadyDate\" <= \"ShippedDate\") AND (\"ShippedDate\" IS NULL OR \"ArrivedDestinationDate\" IS NULL OR \"ShippedDate\" <= \"ArrivedDestinationDate\") AND (\"ArrivedDestinationDate\" IS NULL OR \"WarehouseReceiptDate\" IS NULL OR \"ArrivedDestinationDate\" <= \"WarehouseReceiptDate\")");
                    table.CheckConstraint("ck_delivery_original_diagnostics", "\"PlanType\" <> 'ORIGINAL' OR (\"HandoverToForwarderDate\" IS NULL AND \"CustomsClearedDate\" IS NULL)");
                    table.CheckConstraint("ck_delivery_plan_reason", "\"PlanType\" = 'ORIGINAL' OR length(btrim(\"Reason\")) > 0");
                    table.CheckConstraint("ck_delivery_plan_revision", "\"RevisionNumber\" > 0");
                    table.CheckConstraint("ck_delivery_plan_type", "\"PlanType\" IN ('ORIGINAL','REVISED')");
                    table.ForeignKey(
                        name: "FK_delivery_schedule_plan_revisions_delivery_schedules_Deliver~",
                        column: x => x.DeliveryScheduleId,
                        principalSchema: "procurement",
                        principalTable: "delivery_schedules",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_delivery_schedule_actual_milestones_DeliveryScheduleId_Mile~",
                schema: "procurement",
                table: "delivery_schedule_actual_milestones",
                columns: new[] { "DeliveryScheduleId", "MilestoneCode", "SourceType", "SourceId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_delivery_schedule_plan_revisions_DeliveryScheduleId_Revisio~",
                schema: "procurement",
                table: "delivery_schedule_plan_revisions",
                columns: new[] { "DeliveryScheduleId", "RevisionNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_delivery_schedules_PurchaseOrderLineId_ScheduleNumber",
                schema: "procurement",
                table: "delivery_schedules",
                columns: new[] { "PurchaseOrderLineId", "ScheduleNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_purchase_order_lifecycle_events_PurchaseOrderId_CreatedAtUtc",
                schema: "procurement",
                table: "purchase_order_lifecycle_events",
                columns: new[] { "PurchaseOrderId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_purchase_order_lines_ItemBaseUnitId",
                schema: "procurement",
                table: "purchase_order_lines",
                column: "ItemBaseUnitId");

            migrationBuilder.CreateIndex(
                name: "IX_purchase_order_lines_ItemId",
                schema: "procurement",
                table: "purchase_order_lines",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "IX_purchase_order_lines_PurchaseOrderId_LineNumber",
                schema: "procurement",
                table: "purchase_order_lines",
                columns: new[] { "PurchaseOrderId", "LineNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_purchase_order_lines_SourcingOptionId",
                schema: "procurement",
                table: "purchase_order_lines",
                column: "SourcingOptionId");

            migrationBuilder.CreateIndex(
                name: "IX_purchase_order_lines_SourcingPriceId",
                schema: "procurement",
                table: "purchase_order_lines",
                column: "SourcingPriceId");

            migrationBuilder.CreateIndex(
                name: "IX_purchase_orders_CurrencyId",
                schema: "procurement",
                table: "purchase_orders",
                column: "CurrencyId");

            migrationBuilder.CreateIndex(
                name: "IX_purchase_orders_IncotermId",
                schema: "procurement",
                table: "purchase_orders",
                column: "IncotermId");

            migrationBuilder.CreateIndex(
                name: "IX_purchase_orders_PaymentTermId",
                schema: "procurement",
                table: "purchase_orders",
                column: "PaymentTermId");

            migrationBuilder.CreateIndex(
                name: "IX_purchase_orders_PurchaseOrderNumber",
                schema: "procurement",
                table: "purchase_orders",
                column: "PurchaseOrderNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_purchase_orders_ShippingModeId",
                schema: "procurement",
                table: "purchase_orders",
                column: "ShippingModeId");

            migrationBuilder.CreateIndex(
                name: "IX_purchase_orders_Status_CurrencyId",
                schema: "procurement",
                table: "purchase_orders",
                columns: new[] { "Status", "CurrencyId" });

            migrationBuilder.CreateIndex(
                name: "IX_purchase_orders_Status_SupplierId",
                schema: "procurement",
                table: "purchase_orders",
                columns: new[] { "Status", "SupplierId" });

            migrationBuilder.CreateIndex(
                name: "IX_purchase_orders_SupplierId",
                schema: "procurement",
                table: "purchase_orders",
                column: "SupplierId");

            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION procurement.guard_purchase_order_update()
                RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                  IF NEW."PurchaseOrderNumber" IS DISTINCT FROM OLD."PurchaseOrderNumber"
                     OR NEW."SupplierId" IS DISTINCT FROM OLD."SupplierId"
                     OR NEW."CurrencyId" IS DISTINCT FROM OLD."CurrencyId" THEN
                    RAISE EXCEPTION 'PURCHASE_ORDER_IDENTITY_IMMUTABLE' USING ERRCODE = '23514';
                  END IF;
                  IF NEW."Version" <> OLD."Version" + 1 THEN
                    RAISE EXCEPTION 'PURCHASE_ORDER_VERSION_MUST_ADVANCE_ONCE' USING ERRCODE = '23514';
                  END IF;
                  IF OLD."Status" <> 'DRAFT' AND (
                     NEW."PaymentTermId" IS DISTINCT FROM OLD."PaymentTermId"
                     OR NEW."AdvancePercent" IS DISTINCT FROM OLD."AdvancePercent"
                     OR NEW."BeforeShipmentPercent" IS DISTINCT FROM OLD."BeforeShipmentPercent"
                     OR NEW."CreditPercent" IS DISTINCT FROM OLD."CreditPercent"
                     OR NEW."CreditDays" IS DISTINCT FROM OLD."CreditDays"
                     OR NEW."CreditAnchor" IS DISTINCT FROM OLD."CreditAnchor"
                     OR NEW."ShippingModeId" IS DISTINCT FROM OLD."ShippingModeId"
                     OR NEW."IncotermId" IS DISTINCT FROM OLD."IncotermId"
                     OR NEW."Notes" IS DISTINCT FROM OLD."Notes") THEN
                    RAISE EXCEPTION 'PURCHASE_ORDER_SNAPSHOT_LOCKED' USING ERRCODE = '23514';
                  END IF;
                  IF (OLD."Status" = 'DRAFT' AND NEW."Status" NOT IN ('DRAFT','APPROVED'))
                     OR (OLD."Status" = 'APPROVED' AND NEW."Status" NOT IN ('DRAFT','APPROVED','ORDERED'))
                     OR (OLD."Status" NOT IN ('DRAFT','APPROVED') AND NEW."Status" <> OLD."Status") THEN
                    RAISE EXCEPTION 'PURCHASE_ORDER_TRANSITION_NOT_ALLOWED_IN_MRP09' USING ERRCODE = '23514';
                  END IF;
                  IF OLD."Status" NOT IN ('DRAFT','APPROVED') AND (
                     NEW."OrderedDate" IS DISTINCT FROM OLD."OrderedDate"
                     OR NEW."OrderedAtUtc" IS DISTINCT FROM OLD."OrderedAtUtc") THEN
                    RAISE EXCEPTION 'PURCHASE_ORDER_COMMITMENT_IMMUTABLE' USING ERRCODE = '23514';
                  END IF;
                  RETURN NEW;
                END $$;
                CREATE TRIGGER trg_guard_purchase_order_update
                BEFORE UPDATE ON procurement.purchase_orders
                FOR EACH ROW EXECUTE FUNCTION procurement.guard_purchase_order_update();

                CREATE OR REPLACE FUNCTION procurement.guard_purchase_order_insert()
                RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                  IF NOT EXISTS (SELECT 1 FROM procurement.suppliers s WHERE s."Id" = NEW."SupplierId" AND s."IsActive") THEN
                    RAISE EXCEPTION 'ACTIVE_SUPPLIER_REQUIRED' USING ERRCODE = '23514';
                  END IF;
                  IF NOT EXISTS (SELECT 1 FROM reference_data.currencies c WHERE c."Id" = NEW."CurrencyId" AND c."IsActive") THEN
                    RAISE EXCEPTION 'ACTIVE_CURRENCY_REQUIRED' USING ERRCODE = '23514';
                  END IF;
                  IF NOT EXISTS (SELECT 1 FROM reference_data.reference_values rv JOIN reference_data.reference_types rt ON rt."Id"=rv."ReferenceTypeId" WHERE rv."Id"=NEW."PaymentTermId" AND rv."IsActive" AND rt."Code"='PAYMENT_TERM') THEN
                    RAISE EXCEPTION 'ACTIVE_PAYMENT_TERM_REQUIRED' USING ERRCODE = '23514';
                  END IF;
                  IF NEW."ShippingModeId" IS NOT NULL AND NOT EXISTS (SELECT 1 FROM reference_data.reference_values rv JOIN reference_data.reference_types rt ON rt."Id"=rv."ReferenceTypeId" WHERE rv."Id"=NEW."ShippingModeId" AND rv."IsActive" AND rt."Code"='SHIPPING_MODE') THEN
                    RAISE EXCEPTION 'ACTIVE_SHIPPING_MODE_REQUIRED' USING ERRCODE = '23514';
                  END IF;
                  IF NEW."IncotermId" IS NOT NULL AND NOT EXISTS (SELECT 1 FROM reference_data.reference_values rv JOIN reference_data.reference_types rt ON rt."Id"=rv."ReferenceTypeId" WHERE rv."Id"=NEW."IncotermId" AND rv."IsActive" AND rt."Code"='INCOTERM') THEN
                    RAISE EXCEPTION 'ACTIVE_INCOTERM_REQUIRED' USING ERRCODE = '23514';
                  END IF;
                  RETURN NEW;
                END $$;
                CREATE TRIGGER trg_guard_purchase_order_insert
                BEFORE INSERT ON procurement.purchase_orders
                FOR EACH ROW EXECUTE FUNCTION procurement.guard_purchase_order_insert();

                CREATE OR REPLACE FUNCTION procurement.guard_purchase_order_child_mutation()
                RETURNS trigger LANGUAGE plpgsql AS $$
                DECLARE po_status text; po_supplier uuid; po_currency uuid; po_shipping uuid; po_incoterm uuid;
                BEGIN
                  SELECT po."Status", po."SupplierId", po."CurrencyId", po."ShippingModeId", po."IncotermId"
                  INTO po_status, po_supplier, po_currency, po_shipping, po_incoterm
                  FROM procurement.purchase_orders po
                  WHERE po."Id" = COALESCE(NEW."PurchaseOrderId", OLD."PurchaseOrderId");
                  IF po_status <> 'DRAFT' THEN
                    RAISE EXCEPTION 'PURCHASE_ORDER_SNAPSHOT_LOCKED' USING ERRCODE = '23514';
                  END IF;
                  IF TG_OP <> 'DELETE' AND NOT EXISTS (
                    SELECT 1 FROM procurement.sourcing_options so
                    JOIN procurement.sourcing_prices sp ON sp."Id"=NEW."SourcingPriceId" AND sp."SourcingOptionId"=so."Id"
                    JOIN catalog.items i ON i."Id"=so."ItemId"
                    JOIN reference_data.units u ON u."Id"=so."ItemBaseUnitId"
                    WHERE so."Id"=NEW."SourcingOptionId" AND so."IsActive" AND so."SupplierId"=po_supplier
                      AND so."ItemId"=NEW."ItemId" AND so."ItemBaseUnitId"=NEW."ItemBaseUnitId"
                      AND so."ShippingModeId" IS NOT DISTINCT FROM po_shipping
                      AND so."IncotermId" IS NOT DISTINCT FROM po_incoterm
                      AND sp."CurrencyId"=po_currency AND i."IsActive" AND i."ItemType" IN ('PART','COMPONENT')
                      AND i."BaseUnitId"=NEW."ItemBaseUnitId" AND u."IsActive"
                  ) THEN
                    RAISE EXCEPTION 'PURCHASE_ORDER_LINE_SOURCE_INTEGRITY' USING ERRCODE = '23514';
                  END IF;
                  RETURN COALESCE(NEW, OLD);
                END $$;
                CREATE TRIGGER trg_guard_purchase_order_line
                BEFORE INSERT OR UPDATE OR DELETE ON procurement.purchase_order_lines
                FOR EACH ROW EXECUTE FUNCTION procurement.guard_purchase_order_child_mutation();

                CREATE OR REPLACE FUNCTION procurement.guard_delivery_schedule_mutation()
                RETURNS trigger LANGUAGE plpgsql AS $$
                DECLARE po_status text;
                BEGIN
                  SELECT po."Status" INTO po_status
                  FROM procurement.purchase_orders po
                  JOIN procurement.purchase_order_lines pol ON pol."PurchaseOrderId" = po."Id"
                  WHERE pol."Id" = COALESCE(NEW."PurchaseOrderLineId", OLD."PurchaseOrderLineId");
                  IF po_status <> 'DRAFT' THEN
                    RAISE EXCEPTION 'PURCHASE_ORDER_SCHEDULE_LOCKED' USING ERRCODE = '23514';
                  END IF;
                  RETURN COALESCE(NEW, OLD);
                END $$;
                CREATE TRIGGER trg_guard_delivery_schedule
                BEFORE INSERT OR UPDATE OR DELETE ON procurement.delivery_schedules
                FOR EACH ROW EXECUTE FUNCTION procurement.guard_delivery_schedule_mutation();

                CREATE OR REPLACE FUNCTION procurement.verify_schedule_reconciliation()
                RETURNS trigger LANGUAGE plpgsql AS $$
                DECLARE line_id uuid; ordered_qty numeric(18,6); scheduled_qty numeric(18,6);
                BEGIN
                  IF TG_TABLE_NAME = 'delivery_schedules' THEN
                    line_id := COALESCE(NEW."PurchaseOrderLineId", OLD."PurchaseOrderLineId");
                  ELSE
                    line_id := COALESCE(NEW."Id", OLD."Id");
                  END IF;
                  SELECT pol."OrderedQuantity", COALESCE(SUM(ds."AllocatedQuantity"), 0)
                  INTO ordered_qty, scheduled_qty
                  FROM procurement.purchase_order_lines pol
                  LEFT JOIN procurement.delivery_schedules ds ON ds."PurchaseOrderLineId" = pol."Id"
                  WHERE pol."Id" = line_id GROUP BY pol."OrderedQuantity";
                  IF ordered_qty IS NOT NULL AND ordered_qty <> scheduled_qty THEN
                    RAISE EXCEPTION 'PURCHASE_ORDER_SCHEDULE_QTY_MISMATCH' USING ERRCODE = '23514';
                  END IF;
                  RETURN COALESCE(NEW, OLD);
                END $$;
                CREATE CONSTRAINT TRIGGER trg_reconcile_schedule
                AFTER INSERT OR UPDATE OR DELETE ON procurement.delivery_schedules
                DEFERRABLE INITIALLY DEFERRED FOR EACH ROW
                EXECUTE FUNCTION procurement.verify_schedule_reconciliation();
                CREATE CONSTRAINT TRIGGER trg_reconcile_line
                AFTER INSERT OR UPDATE OF "OrderedQuantity" ON procurement.purchase_order_lines
                DEFERRABLE INITIALLY DEFERRED FOR EACH ROW
                EXECUTE FUNCTION procurement.verify_schedule_reconciliation();

                CREATE OR REPLACE FUNCTION procurement.guard_purchase_plan_revision()
                RETURNS trigger LANGUAGE plpgsql AS $$
                DECLARE po_status text;
                BEGIN
                  IF TG_OP <> 'INSERT' THEN
                    RAISE EXCEPTION 'PURCHASE_ORDER_PLAN_HISTORY_IMMUTABLE' USING ERRCODE = '23514';
                  END IF;
                  SELECT po."Status" INTO po_status
                  FROM procurement.purchase_orders po
                  JOIN procurement.purchase_order_lines pol ON pol."PurchaseOrderId" = po."Id"
                  JOIN procurement.delivery_schedules ds ON ds."PurchaseOrderLineId" = pol."Id"
                  WHERE ds."Id" = NEW."DeliveryScheduleId";
                  IF NEW."PlanType" = 'ORIGINAL' AND po_status <> 'ORDERED' THEN
                    RAISE EXCEPTION 'ORIGINAL_PLAN_REQUIRES_ORDERED' USING ERRCODE = '23514';
                  END IF;
                  IF NEW."PlanType" = 'REVISED' AND po_status NOT IN ('ORDERED','SHIPPED','CUSTOMS','PARTIALLY_RECEIVED') THEN
                    RAISE EXCEPTION 'REVISED_PLAN_REQUIRES_FIRM_ORDER' USING ERRCODE = '23514';
                  END IF;
                  RETURN NEW;
                END $$;
                CREATE TRIGGER trg_guard_purchase_plan_revision
                BEFORE INSERT OR UPDATE OR DELETE ON procurement.delivery_schedule_plan_revisions
                FOR EACH ROW EXECUTE FUNCTION procurement.guard_purchase_plan_revision();

                CREATE OR REPLACE FUNCTION procurement.guard_purchase_history_immutable()
                RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                  RAISE EXCEPTION 'PURCHASE_ORDER_HISTORY_IMMUTABLE' USING ERRCODE = '23514';
                END $$;
                CREATE TRIGGER trg_guard_purchase_lifecycle_history
                BEFORE UPDATE OR DELETE ON procurement.purchase_order_lifecycle_events
                FOR EACH ROW EXECUTE FUNCTION procurement.guard_purchase_history_immutable();
                CREATE TRIGGER trg_guard_purchase_actual_history
                BEFORE UPDATE OR DELETE ON procurement.delivery_schedule_actual_milestones
                FOR EACH ROW EXECUTE FUNCTION procurement.guard_purchase_history_immutable();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP FUNCTION IF EXISTS procurement.guard_purchase_history_immutable() CASCADE;
                DROP FUNCTION IF EXISTS procurement.guard_purchase_plan_revision() CASCADE;
                DROP FUNCTION IF EXISTS procurement.verify_schedule_reconciliation() CASCADE;
                DROP FUNCTION IF EXISTS procurement.guard_delivery_schedule_mutation() CASCADE;
                DROP FUNCTION IF EXISTS procurement.guard_purchase_order_child_mutation() CASCADE;
                DROP FUNCTION IF EXISTS procurement.guard_purchase_order_insert() CASCADE;
                DROP FUNCTION IF EXISTS procurement.guard_purchase_order_update() CASCADE;
                """);
            migrationBuilder.DropTable(
                name: "delivery_schedule_actual_milestones",
                schema: "procurement");

            migrationBuilder.DropTable(
                name: "delivery_schedule_plan_revisions",
                schema: "procurement");

            migrationBuilder.DropTable(
                name: "purchase_order_lifecycle_events",
                schema: "procurement");

            migrationBuilder.DropTable(
                name: "delivery_schedules",
                schema: "procurement");

            migrationBuilder.DropTable(
                name: "purchase_order_lines",
                schema: "procurement");

            migrationBuilder.DropTable(
                name: "purchase_orders",
                schema: "procurement");

            migrationBuilder.DropSequence(
                name: "purchase_order_number_seq",
                schema: "procurement");
        }
    }
}
