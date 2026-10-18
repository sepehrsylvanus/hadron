using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hadron.Erp.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddProductionExecutionAndInventoryUom : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_inventory_source_type",
                schema: "inventory",
                table: "transactions");

            migrationBuilder.EnsureSchema(
                name: "production");

            migrationBuilder.CreateSequence(
                name: "production_order_number_seq",
                schema: "production");

            migrationBuilder.AddColumn<bool>(
                name: "IncludeInMrpAvailable",
                schema: "inventory",
                table: "warehouses",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "WarehouseType",
                schema: "inventory",
                table: "warehouses",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<decimal>(
                name: "ConversionFactorToBase",
                schema: "inventory",
                table: "transfers",
                type: "numeric(28,12)",
                precision: 28,
                scale: 12,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "EnteredRequestedQuantity",
                schema: "inventory",
                table: "transfers",
                type: "numeric(18,6)",
                precision: 18,
                scale: 6,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<Guid>(
                name: "TransactionUnitId",
                schema: "inventory",
                table: "transfers",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<decimal>(
                name: "ConversionFactorToBase",
                schema: "inventory",
                table: "transfer_receipts",
                type: "numeric(28,12)",
                precision: 28,
                scale: 12,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "EnteredQuantity",
                schema: "inventory",
                table: "transfer_receipts",
                type: "numeric(18,6)",
                precision: 18,
                scale: 6,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<Guid>(
                name: "TransactionUnitId",
                schema: "inventory",
                table: "transfer_receipts",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<decimal>(
                name: "ConversionFactorToBase",
                schema: "inventory",
                table: "transactions",
                type: "numeric(28,12)",
                precision: 28,
                scale: 12,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "EnteredQuantity",
                schema: "inventory",
                table: "transactions",
                type: "numeric(18,6)",
                precision: 18,
                scale: 6,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<Guid>(
                name: "TransactionUnitId",
                schema: "inventory",
                table: "transactions",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            // Preserve every existing owner-entered ledger/transfer row. Before enforcing the
            // new snapshot constraints, legacy base-unit quantities become an explicit 1:1
            // transaction-unit snapshot and existing warehouses remain normal MRP-available stock.
            migrationBuilder.Sql(
                """
                UPDATE inventory.warehouses
                SET "WarehouseType" = 'STORAGE', "IncludeInMrpAvailable" = TRUE;

                UPDATE inventory.transactions
                SET "EnteredQuantity" = "Quantity",
                    "TransactionUnitId" = "UnitId",
                    "ConversionFactorToBase" = 1;

                UPDATE inventory.transfers
                SET "EnteredRequestedQuantity" = "RequestedQuantity",
                    "TransactionUnitId" = "UnitId",
                    "ConversionFactorToBase" = 1;

                UPDATE inventory.transfer_receipts AS receipt
                SET "EnteredQuantity" = receipt."Quantity",
                    "TransactionUnitId" = transfer."UnitId",
                    "ConversionFactorToBase" = 1
                FROM inventory.transfers AS transfer
                WHERE transfer."Id" = receipt."TransferId";
                """);

            migrationBuilder.CreateTable(
                name: "item_unit_conversions",
                schema: "inventory",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    TransactionUnitId = table.Column<Guid>(type: "uuid", nullable: false),
                    BaseUnitId = table.Column<Guid>(type: "uuid", nullable: false),
                    FactorToBase = table.Column<decimal>(type: "numeric(28,12)", precision: 28, scale: 12, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_item_unit_conversions", x => x.Id);
                    table.CheckConstraint("ck_inventory_item_unit_different", "\"TransactionUnitId\" <> \"BaseUnitId\"");
                    table.CheckConstraint("ck_inventory_item_unit_factor", "\"FactorToBase\" > 0");
                    table.CheckConstraint("ck_inventory_item_unit_version", "\"Version\" >= 1");
                    table.ForeignKey(
                        name: "FK_item_unit_conversions_items_ItemId",
                        column: x => x.ItemId,
                        principalSchema: "catalog",
                        principalTable: "items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_item_unit_conversions_units_BaseUnitId",
                        column: x => x.BaseUnitId,
                        principalSchema: "reference_data",
                        principalTable: "units",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_item_unit_conversions_units_TransactionUnitId",
                        column: x => x.TransactionUnitId,
                        principalSchema: "reference_data",
                        principalTable: "units",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "orders",
                schema: "production",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BusinessNumber = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductBaseUnitId = table.Column<Guid>(type: "uuid", nullable: false),
                    PlannedQuantity = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    ReportedGoodQuantity = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    ReportedRejectedQuantity = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    PlannedStartDate = table.Column<DateOnly>(type: "date", nullable: false),
                    PlannedEndDate = table.Column<DateOnly>(type: "date", nullable: false),
                    DestinationWarehouseId = table.Column<Guid>(type: "uuid", nullable: false),
                    ForecastSeriesId = table.Column<Guid>(type: "uuid", nullable: false),
                    ForecastRevisionId = table.Column<Guid>(type: "uuid", nullable: false),
                    BomRevisionId = table.Column<Guid>(type: "uuid", nullable: true),
                    Notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    ActorIdentifier = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ApprovedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ReleasedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CompletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_orders", x => x.Id);
                    table.CheckConstraint("ck_production_order_dates", "\"PlannedStartDate\" <= \"PlannedEndDate\"");
                    table.CheckConstraint("ck_production_order_number", "\"BusinessNumber\" ~ '^MO-[0-9]{6,}$'");
                    table.CheckConstraint("ck_production_order_quantity", "\"PlannedQuantity\" > 0 AND \"ReportedGoodQuantity\" >= 0 AND \"ReportedRejectedQuantity\" >= 0");
                    table.CheckConstraint("ck_production_order_release_bom", "(\"Status\" IN ('DRAFT','APPROVED')) = (\"BomRevisionId\" IS NULL)");
                    table.CheckConstraint("ck_production_order_status", "\"Status\" IN ('DRAFT','APPROVED','RELEASED','IN_PROGRESS','COMPLETED','CANCELLED')");
                    table.CheckConstraint("ck_production_order_version", "\"Version\" >= 1");
                    table.ForeignKey(
                        name: "FK_orders_bom_revisions_BomRevisionId",
                        column: x => x.BomRevisionId,
                        principalSchema: "engineering",
                        principalTable: "bom_revisions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_orders_forecast_revisions_ForecastRevisionId",
                        column: x => x.ForecastRevisionId,
                        principalSchema: "planning",
                        principalTable: "forecast_revisions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_orders_forecast_series_ForecastSeriesId",
                        column: x => x.ForecastSeriesId,
                        principalSchema: "planning",
                        principalTable: "forecast_series",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_orders_items_ProductId",
                        column: x => x.ProductId,
                        principalSchema: "catalog",
                        principalTable: "items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_orders_units_ProductBaseUnitId",
                        column: x => x.ProductBaseUnitId,
                        principalSchema: "reference_data",
                        principalTable: "units",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_orders_warehouses_DestinationWarehouseId",
                        column: x => x.DestinationWarehouseId,
                        principalSchema: "inventory",
                        principalTable: "warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "order_lifecycle_events",
                schema: "production",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductionOrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    FromStatus = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ToStatus = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    ActorIdentifier = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    OccurredAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_order_lifecycle_events", x => x.Id);
                    table.ForeignKey(
                        name: "FK_order_lifecycle_events_orders_ProductionOrderId",
                        column: x => x.ProductionOrderId,
                        principalSchema: "production",
                        principalTable: "orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "order_materials",
                schema: "production",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductionOrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    BomLineId = table.Column<Guid>(type: "uuid", nullable: false),
                    ItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    UnitId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequiredQuantity = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    UsageQty = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    BaseOutputQty = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_order_materials", x => x.Id);
                    table.CheckConstraint("ck_production_material_quantity", "\"RequiredQuantity\" > 0 AND \"UsageQty\" > 0 AND \"BaseOutputQty\" > 0");
                    table.ForeignKey(
                        name: "FK_order_materials_bom_lines_BomLineId",
                        column: x => x.BomLineId,
                        principalSchema: "engineering",
                        principalTable: "bom_lines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_order_materials_items_ItemId",
                        column: x => x.ItemId,
                        principalSchema: "catalog",
                        principalTable: "items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_order_materials_orders_ProductionOrderId",
                        column: x => x.ProductionOrderId,
                        principalSchema: "production",
                        principalTable: "orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_order_materials_units_UnitId",
                        column: x => x.UnitId,
                        principalSchema: "reference_data",
                        principalTable: "units",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "reports",
                schema: "production",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductionOrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductionDate = table.Column<DateOnly>(type: "date", nullable: false),
                    GoodQuantity = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    RejectedQuantity = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    DestinationWarehouseId = table.Column<Guid>(type: "uuid", nullable: false),
                    FinishedGoodsTransactionId = table.Column<Guid>(type: "uuid", nullable: false),
                    DailyActualId = table.Column<Guid>(type: "uuid", nullable: false),
                    DailyActualRevisionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    ActorIdentifier = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_reports", x => x.Id);
                    table.CheckConstraint("ck_production_report_quantity", "\"GoodQuantity\" > 0 AND \"RejectedQuantity\" >= 0");
                    table.ForeignKey(
                        name: "FK_reports_daily_actual_revisions_DailyActualRevisionId",
                        column: x => x.DailyActualRevisionId,
                        principalSchema: "planning",
                        principalTable: "daily_actual_revisions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_reports_daily_actuals_DailyActualId",
                        column: x => x.DailyActualId,
                        principalSchema: "planning",
                        principalTable: "daily_actuals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_reports_orders_ProductionOrderId",
                        column: x => x.ProductionOrderId,
                        principalSchema: "production",
                        principalTable: "orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_reports_transactions_FinishedGoodsTransactionId",
                        column: x => x.FinishedGoodsTransactionId,
                        principalSchema: "inventory",
                        principalTable: "transactions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_reports_warehouses_DestinationWarehouseId",
                        column: x => x.DestinationWarehouseId,
                        principalSchema: "inventory",
                        principalTable: "warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "material_stagings",
                schema: "production",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductionOrderMaterialId = table.Column<Guid>(type: "uuid", nullable: false),
                    InventoryTransferId = table.Column<Guid>(type: "uuid", nullable: false),
                    BaseQuantity = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_material_stagings", x => x.Id);
                    table.CheckConstraint("ck_production_staging_quantity", "\"BaseQuantity\" > 0");
                    table.ForeignKey(
                        name: "FK_material_stagings_order_materials_ProductionOrderMaterialId",
                        column: x => x.ProductionOrderMaterialId,
                        principalSchema: "production",
                        principalTable: "order_materials",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_material_stagings_transfers_InventoryTransferId",
                        column: x => x.InventoryTransferId,
                        principalSchema: "inventory",
                        principalTable: "transfers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "report_materials",
                schema: "production",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductionReportId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductionOrderMaterialId = table.Column<Guid>(type: "uuid", nullable: false),
                    StandardQuantity = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    ActualQuantity = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    VarianceReason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    InventoryTransactionId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_report_materials", x => x.Id);
                    table.CheckConstraint("ck_production_report_material_quantity", "\"StandardQuantity\" > 0 AND \"ActualQuantity\" > 0");
                    table.CheckConstraint("ck_production_report_material_variance", "\"StandardQuantity\" = \"ActualQuantity\" OR NULLIF(BTRIM(\"VarianceReason\"), '') IS NOT NULL");
                    table.ForeignKey(
                        name: "FK_report_materials_order_materials_ProductionOrderMaterialId",
                        column: x => x.ProductionOrderMaterialId,
                        principalSchema: "production",
                        principalTable: "order_materials",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_report_materials_reports_ProductionReportId",
                        column: x => x.ProductionReportId,
                        principalSchema: "production",
                        principalTable: "reports",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_report_materials_transactions_InventoryTransactionId",
                        column: x => x.InventoryTransactionId,
                        principalSchema: "inventory",
                        principalTable: "transactions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_warehouses_mrp_availability",
                schema: "inventory",
                table: "warehouses",
                sql: "\"IncludeInMrpAvailable\" = (\"WarehouseType\" <> 'PRODUCTION_STAGING')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_warehouses_type",
                schema: "inventory",
                table: "warehouses",
                sql: "\"WarehouseType\" IN ('STORAGE','PRODUCTION_STAGING','FINISHED_GOODS')");

            migrationBuilder.CreateIndex(
                name: "IX_transfers_TransactionUnitId",
                schema: "inventory",
                table: "transfers",
                column: "TransactionUnitId");

            migrationBuilder.AddCheckConstraint(
                name: "ck_inventory_transfer_conversion_factor",
                schema: "inventory",
                table: "transfers",
                sql: "\"ConversionFactorToBase\" > 0");

            migrationBuilder.AddCheckConstraint(
                name: "ck_inventory_transfer_conversion_math",
                schema: "inventory",
                table: "transfers",
                sql: "ROUND(\"EnteredRequestedQuantity\" * \"ConversionFactorToBase\", 6) = \"RequestedQuantity\"");

            migrationBuilder.AddCheckConstraint(
                name: "ck_inventory_transfer_entered_quantity",
                schema: "inventory",
                table: "transfers",
                sql: "\"EnteredRequestedQuantity\" > 0");

            migrationBuilder.CreateIndex(
                name: "IX_transfer_receipts_TransactionUnitId",
                schema: "inventory",
                table: "transfer_receipts",
                column: "TransactionUnitId");

            migrationBuilder.AddCheckConstraint(
                name: "ck_inventory_transfer_receipt_conversion_factor",
                schema: "inventory",
                table: "transfer_receipts",
                sql: "\"ConversionFactorToBase\" > 0");

            migrationBuilder.AddCheckConstraint(
                name: "ck_inventory_transfer_receipt_conversion_math",
                schema: "inventory",
                table: "transfer_receipts",
                sql: "ROUND(\"EnteredQuantity\" * \"ConversionFactorToBase\", 6) = \"Quantity\"");

            migrationBuilder.AddCheckConstraint(
                name: "ck_inventory_transfer_receipt_entered_quantity",
                schema: "inventory",
                table: "transfer_receipts",
                sql: "\"EnteredQuantity\" > 0");

            migrationBuilder.CreateIndex(
                name: "IX_transactions_TransactionUnitId",
                schema: "inventory",
                table: "transactions",
                column: "TransactionUnitId");

            migrationBuilder.AddCheckConstraint(
                name: "ck_inventory_conversion_factor",
                schema: "inventory",
                table: "transactions",
                sql: "\"ConversionFactorToBase\" > 0");

            migrationBuilder.AddCheckConstraint(
                name: "ck_inventory_conversion_math",
                schema: "inventory",
                table: "transactions",
                sql: "ROUND(\"EnteredQuantity\" * \"ConversionFactorToBase\", 6) = \"Quantity\"");

            migrationBuilder.AddCheckConstraint(
                name: "ck_inventory_entered_quantity",
                schema: "inventory",
                table: "transactions",
                sql: "\"EnteredQuantity\" > 0");

            migrationBuilder.AddCheckConstraint(
                name: "ck_inventory_source_type",
                schema: "inventory",
                table: "transactions",
                sql: "\"SourceDocumentType\" IS NULL OR \"SourceDocumentType\" IN ('PURCHASE_RECEIPT','PRODUCTION_ISSUE','PRODUCTION_RECEIPT','INTERNAL_TRANSFER_DISPATCH','INTERNAL_TRANSFER_RECEIPT','LEGACY_IMPORT')");

            migrationBuilder.CreateIndex(
                name: "IX_item_unit_conversions_BaseUnitId",
                schema: "inventory",
                table: "item_unit_conversions",
                column: "BaseUnitId");

            migrationBuilder.CreateIndex(
                name: "IX_item_unit_conversions_ItemId_TransactionUnitId",
                schema: "inventory",
                table: "item_unit_conversions",
                columns: new[] { "ItemId", "TransactionUnitId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_item_unit_conversions_TransactionUnitId",
                schema: "inventory",
                table: "item_unit_conversions",
                column: "TransactionUnitId");

            migrationBuilder.CreateIndex(
                name: "IX_material_stagings_InventoryTransferId",
                schema: "production",
                table: "material_stagings",
                column: "InventoryTransferId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_material_stagings_ProductionOrderMaterialId",
                schema: "production",
                table: "material_stagings",
                column: "ProductionOrderMaterialId");

            migrationBuilder.CreateIndex(
                name: "IX_order_lifecycle_events_ProductionOrderId_OccurredAtUtc",
                schema: "production",
                table: "order_lifecycle_events",
                columns: new[] { "ProductionOrderId", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_order_materials_BomLineId",
                schema: "production",
                table: "order_materials",
                column: "BomLineId");

            migrationBuilder.CreateIndex(
                name: "IX_order_materials_ItemId",
                schema: "production",
                table: "order_materials",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "IX_order_materials_ProductionOrderId_ItemId",
                schema: "production",
                table: "order_materials",
                columns: new[] { "ProductionOrderId", "ItemId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_order_materials_UnitId",
                schema: "production",
                table: "order_materials",
                column: "UnitId");

            migrationBuilder.CreateIndex(
                name: "IX_orders_BomRevisionId",
                schema: "production",
                table: "orders",
                column: "BomRevisionId");

            migrationBuilder.CreateIndex(
                name: "IX_orders_BusinessNumber",
                schema: "production",
                table: "orders",
                column: "BusinessNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_orders_DestinationWarehouseId",
                schema: "production",
                table: "orders",
                column: "DestinationWarehouseId");

            migrationBuilder.CreateIndex(
                name: "IX_orders_ForecastRevisionId",
                schema: "production",
                table: "orders",
                column: "ForecastRevisionId");

            migrationBuilder.CreateIndex(
                name: "IX_orders_ForecastSeriesId",
                schema: "production",
                table: "orders",
                column: "ForecastSeriesId");

            migrationBuilder.CreateIndex(
                name: "IX_orders_ProductBaseUnitId",
                schema: "production",
                table: "orders",
                column: "ProductBaseUnitId");

            migrationBuilder.CreateIndex(
                name: "IX_orders_ProductId_PlannedStartDate",
                schema: "production",
                table: "orders",
                columns: new[] { "ProductId", "PlannedStartDate" });

            migrationBuilder.CreateIndex(
                name: "IX_orders_Status_PlannedStartDate",
                schema: "production",
                table: "orders",
                columns: new[] { "Status", "PlannedStartDate" });

            migrationBuilder.CreateIndex(
                name: "IX_report_materials_InventoryTransactionId",
                schema: "production",
                table: "report_materials",
                column: "InventoryTransactionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_report_materials_ProductionOrderMaterialId",
                schema: "production",
                table: "report_materials",
                column: "ProductionOrderMaterialId");

            migrationBuilder.CreateIndex(
                name: "IX_report_materials_ProductionReportId_ProductionOrderMaterial~",
                schema: "production",
                table: "report_materials",
                columns: new[] { "ProductionReportId", "ProductionOrderMaterialId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_reports_DailyActualId",
                schema: "production",
                table: "reports",
                column: "DailyActualId");

            migrationBuilder.CreateIndex(
                name: "IX_reports_DailyActualRevisionId",
                schema: "production",
                table: "reports",
                column: "DailyActualRevisionId");

            migrationBuilder.CreateIndex(
                name: "IX_reports_DestinationWarehouseId",
                schema: "production",
                table: "reports",
                column: "DestinationWarehouseId");

            migrationBuilder.CreateIndex(
                name: "IX_reports_FinishedGoodsTransactionId",
                schema: "production",
                table: "reports",
                column: "FinishedGoodsTransactionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_reports_ProductionOrderId_ProductionDate",
                schema: "production",
                table: "reports",
                columns: new[] { "ProductionOrderId", "ProductionDate" });

            migrationBuilder.AddForeignKey(
                name: "FK_transactions_units_TransactionUnitId",
                schema: "inventory",
                table: "transactions",
                column: "TransactionUnitId",
                principalSchema: "reference_data",
                principalTable: "units",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_transfer_receipts_units_TransactionUnitId",
                schema: "inventory",
                table: "transfer_receipts",
                column: "TransactionUnitId",
                principalSchema: "reference_data",
                principalTable: "units",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_transfers_units_TransactionUnitId",
                schema: "inventory",
                table: "transfers",
                column: "TransactionUnitId",
                principalSchema: "reference_data",
                principalTable: "units",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.Sql(
                """
                CREATE OR REPLACE FUNCTION inventory.guard_item_unit_conversion()
                RETURNS trigger LANGUAGE plpgsql AS $fn$
                DECLARE current_base uuid;
                DECLARE transaction_unit_active boolean;
                DECLARE base_unit_active boolean;
                BEGIN
                    SELECT "BaseUnitId" INTO current_base
                    FROM catalog.items WHERE "Id" = NEW."ItemId";
                    IF current_base IS NULL OR current_base <> NEW."BaseUnitId" THEN
                        RAISE EXCEPTION 'Inventory conversion base unit must match Item base unit'
                            USING ERRCODE = '23514', CONSTRAINT = 'ck_inventory_conversion_base_unit';
                    END IF;
                    IF TG_OP = 'UPDATE' AND
                       (NEW."ItemId" <> OLD."ItemId" OR
                        NEW."TransactionUnitId" <> OLD."TransactionUnitId" OR
                        NEW."BaseUnitId" <> OLD."BaseUnitId") THEN
                        RAISE EXCEPTION 'Inventory conversion identity is immutable'
                            USING ERRCODE = '23514', CONSTRAINT = 'ck_inventory_conversion_identity_immutable';
                    END IF;
                    IF TG_OP = 'UPDATE' AND NEW."Version" <> OLD."Version" + 1 THEN
                        RAISE EXCEPTION 'Inventory conversion version must advance exactly once'
                            USING ERRCODE = '23514', CONSTRAINT = 'ck_inventory_conversion_version_advance';
                    END IF;
                    IF NEW."IsActive" THEN
                        SELECT "IsActive" INTO transaction_unit_active FROM reference_data.units
                        WHERE "Id" = NEW."TransactionUnitId";
                        SELECT "IsActive" INTO base_unit_active FROM reference_data.units
                        WHERE "Id" = NEW."BaseUnitId";
                        IF NOT COALESCE(transaction_unit_active, FALSE) OR
                           NOT COALESCE(base_unit_active, FALSE) THEN
                            RAISE EXCEPTION 'Active units are required for an active conversion'
                                USING ERRCODE = '23514', CONSTRAINT = 'ck_inventory_conversion_active_units';
                        END IF;
                    END IF;
                    RETURN NEW;
                END;
                $fn$;

                CREATE TRIGGER trg_inventory_item_unit_conversion
                BEFORE INSERT OR UPDATE ON inventory.item_unit_conversions
                FOR EACH ROW EXECUTE FUNCTION inventory.guard_item_unit_conversion();

                CREATE OR REPLACE FUNCTION inventory.guard_warehouse_stock_classification()
                RETURNS trigger LANGUAGE plpgsql AS $fn$
                BEGIN
                    IF NEW."WarehouseType" IS DISTINCT FROM OLD."WarehouseType" AND EXISTS (
                        SELECT 1 FROM inventory.ledger_effects
                        WHERE "WarehouseId" = OLD."Id") THEN
                        RAISE EXCEPTION 'Warehouse type is immutable after inventory history exists'
                            USING ERRCODE = '23514', CONSTRAINT = 'ck_warehouse_type_immutable_with_history';
                    END IF;
                    RETURN NEW;
                END;
                $fn$;

                CREATE TRIGGER trg_inventory_warehouse_stock_classification
                BEFORE UPDATE ON inventory.warehouses
                FOR EACH ROW EXECUTE FUNCTION inventory.guard_warehouse_stock_classification();

                CREATE OR REPLACE FUNCTION production.guard_order_update()
                RETURNS trigger LANGUAGE plpgsql AS $fn$
                BEGIN
                    IF TG_OP = 'DELETE' THEN
                        RAISE EXCEPTION 'Production Orders are immutable history'
                            USING ERRCODE = '23514', CONSTRAINT = 'ck_production_order_no_delete';
                    END IF;
                    IF NEW."BusinessNumber" <> OLD."BusinessNumber" OR
                       NEW."ProductId" <> OLD."ProductId" OR
                       NEW."ProductBaseUnitId" <> OLD."ProductBaseUnitId" OR
                       NEW."ForecastSeriesId" <> OLD."ForecastSeriesId" OR
                       NEW."ForecastRevisionId" <> OLD."ForecastRevisionId" OR
                       NEW."CreatedAtUtc" <> OLD."CreatedAtUtc" THEN
                        RAISE EXCEPTION 'Production Order identity and source snapshots are immutable'
                            USING ERRCODE = '23514', CONSTRAINT = 'ck_production_order_identity_immutable';
                    END IF;
                    IF OLD."Status" <> 'DRAFT' AND
                       (NEW."PlannedQuantity" <> OLD."PlannedQuantity" OR
                        NEW."PlannedStartDate" <> OLD."PlannedStartDate" OR
                        NEW."PlannedEndDate" <> OLD."PlannedEndDate" OR
                        NEW."DestinationWarehouseId" <> OLD."DestinationWarehouseId" OR
                        NEW."Notes" IS DISTINCT FROM OLD."Notes") THEN
                        RAISE EXCEPTION 'Released production plan is immutable'
                            USING ERRCODE = '23514', CONSTRAINT = 'ck_production_order_plan_immutable';
                    END IF;
                    IF NEW."Version" <> OLD."Version" + 1 THEN
                        RAISE EXCEPTION 'Production Order version must advance exactly once'
                            USING ERRCODE = '23514', CONSTRAINT = 'ck_production_order_version_advance';
                    END IF;
                    IF OLD."Status" = NEW."Status" THEN
                        IF OLD."Status" = 'DRAFT' AND
                           NEW."ReportedGoodQuantity" = 0 AND NEW."ReportedRejectedQuantity" = 0 AND
                           NEW."BomRevisionId" IS NULL AND NEW."ApprovedAtUtc" IS NULL AND
                           NEW."ReleasedAtUtc" IS NULL AND NEW."CompletedAtUtc" IS NULL THEN
                            RETURN NEW;
                        END IF;
                        IF OLD."Status" = 'RELEASED' AND
                           NEW."ReportedGoodQuantity" = OLD."ReportedGoodQuantity" AND
                           NEW."ReportedRejectedQuantity" = OLD."ReportedRejectedQuantity" AND
                           NEW."BomRevisionId" = OLD."BomRevisionId" AND
                           NEW."ApprovedAtUtc" = OLD."ApprovedAtUtc" AND
                           NEW."ReleasedAtUtc" = OLD."ReleasedAtUtc" AND
                           NEW."CompletedAtUtc" IS NOT DISTINCT FROM OLD."CompletedAtUtc" THEN
                            RETURN NEW;
                        END IF;
                        IF OLD."Status" = 'IN_PROGRESS' AND
                           NEW."ReportedGoodQuantity" >= OLD."ReportedGoodQuantity" AND
                           NEW."ReportedRejectedQuantity" >= OLD."ReportedRejectedQuantity" AND
                           (NEW."ReportedGoodQuantity" > OLD."ReportedGoodQuantity" OR
                            NEW."ReportedRejectedQuantity" > OLD."ReportedRejectedQuantity") AND
                           NEW."BomRevisionId" = OLD."BomRevisionId" AND
                           NEW."ApprovedAtUtc" = OLD."ApprovedAtUtc" AND
                           NEW."ReleasedAtUtc" = OLD."ReleasedAtUtc" AND
                           NEW."CompletedAtUtc" IS NOT DISTINCT FROM OLD."CompletedAtUtc" THEN
                            RETURN NEW;
                        END IF;
                        RAISE EXCEPTION 'Invalid production lifecycle mutation'
                            USING ERRCODE = '23514', CONSTRAINT = 'ck_production_order_transition';
                    END IF;
                    IF OLD."Status" = 'DRAFT' AND NEW."Status" = 'APPROVED' AND
                       NEW."ApprovedAtUtc" IS NOT NULL THEN RETURN NEW; END IF;
                    IF OLD."Status" = 'APPROVED' AND NEW."Status" = 'RELEASED' AND
                       NEW."ReleasedAtUtc" IS NOT NULL AND NEW."BomRevisionId" IS NOT NULL THEN
                       RETURN NEW;
                    END IF;
                    IF OLD."Status" IN ('RELEASED','IN_PROGRESS') AND
                       NEW."Status" IN ('IN_PROGRESS','COMPLETED') AND
                       NEW."ReportedGoodQuantity" >= OLD."ReportedGoodQuantity" AND
                       NEW."ReportedRejectedQuantity" >= OLD."ReportedRejectedQuantity" AND
                       (NEW."Status" <> 'COMPLETED' OR NEW."CompletedAtUtc" IS NOT NULL) THEN
                       RETURN NEW;
                    END IF;
                    RAISE EXCEPTION 'Invalid production lifecycle transition'
                        USING ERRCODE = '23514', CONSTRAINT = 'ck_production_order_transition';
                END;
                $fn$;

                CREATE TRIGGER trg_production_order_update
                BEFORE UPDATE OR DELETE ON production.orders
                FOR EACH ROW EXECUTE FUNCTION production.guard_order_update();

                CREATE OR REPLACE FUNCTION production.prevent_immutable_change()
                RETURNS trigger LANGUAGE plpgsql AS $fn$
                BEGIN
                    RAISE EXCEPTION 'Posted production snapshot/history is immutable'
                        USING ERRCODE = '23514', CONSTRAINT = 'ck_production_posted_immutable';
                END;
                $fn$;

                CREATE TRIGGER trg_production_order_material_immutable
                BEFORE UPDATE OR DELETE ON production.order_materials
                FOR EACH ROW EXECUTE FUNCTION production.prevent_immutable_change();
                CREATE TRIGGER trg_production_staging_immutable
                BEFORE UPDATE OR DELETE ON production.material_stagings
                FOR EACH ROW EXECUTE FUNCTION production.prevent_immutable_change();
                CREATE TRIGGER trg_production_report_immutable
                BEFORE UPDATE OR DELETE ON production.reports
                FOR EACH ROW EXECUTE FUNCTION production.prevent_immutable_change();
                CREATE TRIGGER trg_production_report_material_immutable
                BEFORE UPDATE OR DELETE ON production.report_materials
                FOR EACH ROW EXECUTE FUNCTION production.prevent_immutable_change();
                CREATE TRIGGER trg_production_lifecycle_immutable
                BEFORE UPDATE OR DELETE ON production.order_lifecycle_events
                FOR EACH ROW EXECUTE FUNCTION production.prevent_immutable_change();

                CREATE OR REPLACE FUNCTION production.guard_report_insert()
                RETURNS trigger LANGUAGE plpgsql AS $fn$
                DECLARE order_product uuid;
                DECLARE transaction_item uuid;
                DECLARE transaction_source text;
                DECLARE transaction_source_id uuid;
                BEGIN
                    IF NEW."ProductionDate" > (CURRENT_TIMESTAMP AT TIME ZONE 'Asia/Tehran')::date THEN
                        RAISE EXCEPTION 'Future production report is not allowed'
                            USING ERRCODE = '23514', CONSTRAINT = 'ck_production_report_future_date';
                    END IF;
                    SELECT "ProductId" INTO order_product FROM production.orders
                    WHERE "Id" = NEW."ProductionOrderId";
                    SELECT "ItemId", "SourceDocumentType", "SourceDocumentId"
                    INTO transaction_item, transaction_source, transaction_source_id
                    FROM inventory.transactions WHERE "Id" = NEW."FinishedGoodsTransactionId";
                    IF transaction_item <> order_product OR transaction_source <> 'PRODUCTION_RECEIPT' OR
                       transaction_source_id <> NEW."Id" THEN
                        RAISE EXCEPTION 'Finished-goods inventory lineage is invalid'
                            USING ERRCODE = '23514', CONSTRAINT = 'ck_production_report_inventory_lineage';
                    END IF;
                    RETURN NEW;
                END;
                $fn$;

                CREATE CONSTRAINT TRIGGER trg_production_report_insert
                AFTER INSERT ON production.reports DEFERRABLE INITIALLY DEFERRED
                FOR EACH ROW EXECUTE FUNCTION production.guard_report_insert();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DROP TRIGGER IF EXISTS trg_production_report_insert ON production.reports;
                DROP FUNCTION IF EXISTS production.guard_report_insert();
                DROP TRIGGER IF EXISTS trg_production_lifecycle_immutable ON production.order_lifecycle_events;
                DROP TRIGGER IF EXISTS trg_production_report_material_immutable ON production.report_materials;
                DROP TRIGGER IF EXISTS trg_production_report_immutable ON production.reports;
                DROP TRIGGER IF EXISTS trg_production_staging_immutable ON production.material_stagings;
                DROP TRIGGER IF EXISTS trg_production_order_material_immutable ON production.order_materials;
                DROP FUNCTION IF EXISTS production.prevent_immutable_change();
                DROP TRIGGER IF EXISTS trg_production_order_update ON production.orders;
                DROP FUNCTION IF EXISTS production.guard_order_update();
                DROP TRIGGER IF EXISTS trg_inventory_warehouse_stock_classification ON inventory.warehouses;
                DROP FUNCTION IF EXISTS inventory.guard_warehouse_stock_classification();
                DROP TRIGGER IF EXISTS trg_inventory_item_unit_conversion ON inventory.item_unit_conversions;
                DROP FUNCTION IF EXISTS inventory.guard_item_unit_conversion();
                """);
            migrationBuilder.DropForeignKey(
                name: "FK_transactions_units_TransactionUnitId",
                schema: "inventory",
                table: "transactions");

            migrationBuilder.DropForeignKey(
                name: "FK_transfer_receipts_units_TransactionUnitId",
                schema: "inventory",
                table: "transfer_receipts");

            migrationBuilder.DropForeignKey(
                name: "FK_transfers_units_TransactionUnitId",
                schema: "inventory",
                table: "transfers");

            migrationBuilder.DropTable(
                name: "item_unit_conversions",
                schema: "inventory");

            migrationBuilder.DropTable(
                name: "material_stagings",
                schema: "production");

            migrationBuilder.DropTable(
                name: "order_lifecycle_events",
                schema: "production");

            migrationBuilder.DropTable(
                name: "report_materials",
                schema: "production");

            migrationBuilder.DropTable(
                name: "order_materials",
                schema: "production");

            migrationBuilder.DropTable(
                name: "reports",
                schema: "production");

            migrationBuilder.DropTable(
                name: "orders",
                schema: "production");

            migrationBuilder.DropCheckConstraint(
                name: "ck_warehouses_mrp_availability",
                schema: "inventory",
                table: "warehouses");

            migrationBuilder.DropCheckConstraint(
                name: "ck_warehouses_type",
                schema: "inventory",
                table: "warehouses");

            migrationBuilder.DropIndex(
                name: "IX_transfers_TransactionUnitId",
                schema: "inventory",
                table: "transfers");

            migrationBuilder.DropCheckConstraint(
                name: "ck_inventory_transfer_conversion_factor",
                schema: "inventory",
                table: "transfers");

            migrationBuilder.DropCheckConstraint(
                name: "ck_inventory_transfer_conversion_math",
                schema: "inventory",
                table: "transfers");

            migrationBuilder.DropCheckConstraint(
                name: "ck_inventory_transfer_entered_quantity",
                schema: "inventory",
                table: "transfers");

            migrationBuilder.DropIndex(
                name: "IX_transfer_receipts_TransactionUnitId",
                schema: "inventory",
                table: "transfer_receipts");

            migrationBuilder.DropCheckConstraint(
                name: "ck_inventory_transfer_receipt_conversion_factor",
                schema: "inventory",
                table: "transfer_receipts");

            migrationBuilder.DropCheckConstraint(
                name: "ck_inventory_transfer_receipt_conversion_math",
                schema: "inventory",
                table: "transfer_receipts");

            migrationBuilder.DropCheckConstraint(
                name: "ck_inventory_transfer_receipt_entered_quantity",
                schema: "inventory",
                table: "transfer_receipts");

            migrationBuilder.DropIndex(
                name: "IX_transactions_TransactionUnitId",
                schema: "inventory",
                table: "transactions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_inventory_conversion_factor",
                schema: "inventory",
                table: "transactions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_inventory_conversion_math",
                schema: "inventory",
                table: "transactions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_inventory_entered_quantity",
                schema: "inventory",
                table: "transactions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_inventory_source_type",
                schema: "inventory",
                table: "transactions");

            migrationBuilder.DropColumn(
                name: "IncludeInMrpAvailable",
                schema: "inventory",
                table: "warehouses");

            migrationBuilder.DropColumn(
                name: "WarehouseType",
                schema: "inventory",
                table: "warehouses");

            migrationBuilder.DropColumn(
                name: "ConversionFactorToBase",
                schema: "inventory",
                table: "transfers");

            migrationBuilder.DropColumn(
                name: "EnteredRequestedQuantity",
                schema: "inventory",
                table: "transfers");

            migrationBuilder.DropColumn(
                name: "TransactionUnitId",
                schema: "inventory",
                table: "transfers");

            migrationBuilder.DropColumn(
                name: "ConversionFactorToBase",
                schema: "inventory",
                table: "transfer_receipts");

            migrationBuilder.DropColumn(
                name: "EnteredQuantity",
                schema: "inventory",
                table: "transfer_receipts");

            migrationBuilder.DropColumn(
                name: "TransactionUnitId",
                schema: "inventory",
                table: "transfer_receipts");

            migrationBuilder.DropColumn(
                name: "ConversionFactorToBase",
                schema: "inventory",
                table: "transactions");

            migrationBuilder.DropColumn(
                name: "EnteredQuantity",
                schema: "inventory",
                table: "transactions");

            migrationBuilder.DropColumn(
                name: "TransactionUnitId",
                schema: "inventory",
                table: "transactions");

            migrationBuilder.DropSequence(
                name: "production_order_number_seq",
                schema: "production");

            migrationBuilder.AddCheckConstraint(
                name: "ck_inventory_source_type",
                schema: "inventory",
                table: "transactions",
                sql: "\"SourceDocumentType\" IS NULL OR \"SourceDocumentType\" IN ('PURCHASE_RECEIPT','PRODUCTION_ISSUE','INTERNAL_TRANSFER_DISPATCH','INTERNAL_TRANSFER_RECEIPT','LEGACY_IMPORT')");
        }
    }
}
