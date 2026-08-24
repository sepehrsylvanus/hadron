using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hadron.Erp.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddInventoryFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "inventory");

            migrationBuilder.CreateSequence(
                name: "warehouse_number_seq",
                schema: "inventory");

            migrationBuilder.CreateTable(
                name: "warehouses",
                schema: "inventory",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BusinessNumber = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    NameFa = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    NameEn = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    Notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_warehouses", x => x.Id);
                    table.CheckConstraint("ck_warehouses_business_number_format", "\"BusinessNumber\" ~ '^WH-[0-9]{4,}$'");
                    table.CheckConstraint("ck_warehouses_version", "\"Version\" >= 1");
                });

            migrationBuilder.CreateTable(
                name: "transactions",
                schema: "inventory",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TransactionType = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    ItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    WarehouseId = table.Column<Guid>(type: "uuid", nullable: false),
                    DestinationWarehouseId = table.Column<Guid>(type: "uuid", nullable: true),
                    Quantity = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    UnitId = table.Column<Guid>(type: "uuid", nullable: false),
                    EffectiveDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    OriginalTransactionId = table.Column<Guid>(type: "uuid", nullable: true),
                    SourceDocumentType = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    SourceDocumentId = table.Column<Guid>(type: "uuid", nullable: true),
                    ActorIdentifier = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_transactions", x => x.Id);
                    table.CheckConstraint("ck_inventory_original_shape", "(\"TransactionType\" = 'REVERSAL') = (\"OriginalTransactionId\" IS NOT NULL)");
                    table.CheckConstraint("ck_inventory_quantity", "\"Quantity\" > 0");
                    table.CheckConstraint("ck_inventory_reason", "\"TransactionType\" NOT IN ('ADJ_IN','ADJ_OUT','REVERSAL') OR NULLIF(BTRIM(\"Reason\"), '') IS NOT NULL");
                    table.CheckConstraint("ck_inventory_source_pair", "(\"SourceDocumentType\" IS NULL) = (\"SourceDocumentId\" IS NULL)");
                    table.CheckConstraint("ck_inventory_source_type", "\"SourceDocumentType\" IS NULL OR \"SourceDocumentType\" IN ('PURCHASE_RECEIPT','PRODUCTION_ISSUE','LEGACY_IMPORT')");
                    table.CheckConstraint("ck_inventory_transaction_type", "\"TransactionType\" IN ('OPENING','RECEIPT','ISSUE','ADJ_IN','ADJ_OUT','TRANSFER','REVERSAL')");
                    table.CheckConstraint("ck_inventory_transfer_shape", "(\"TransactionType\" = 'TRANSFER' AND \"DestinationWarehouseId\" IS NOT NULL AND \"DestinationWarehouseId\" <> \"WarehouseId\") OR (\"TransactionType\" <> 'TRANSFER' AND \"TransactionType\" <> 'REVERSAL' AND \"DestinationWarehouseId\" IS NULL) OR (\"TransactionType\" = 'REVERSAL')");
                    table.ForeignKey(
                        name: "FK_transactions_items_ItemId",
                        column: x => x.ItemId,
                        principalSchema: "catalog",
                        principalTable: "items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_transactions_transactions_OriginalTransactionId",
                        column: x => x.OriginalTransactionId,
                        principalSchema: "inventory",
                        principalTable: "transactions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_transactions_units_UnitId",
                        column: x => x.UnitId,
                        principalSchema: "reference_data",
                        principalTable: "units",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_transactions_warehouses_DestinationWarehouseId",
                        column: x => x.DestinationWarehouseId,
                        principalSchema: "inventory",
                        principalTable: "warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_transactions_warehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalSchema: "inventory",
                        principalTable: "warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_transactions_DestinationWarehouseId",
                schema: "inventory",
                table: "transactions",
                column: "DestinationWarehouseId");

            migrationBuilder.CreateIndex(
                name: "IX_transactions_EffectiveDate_TransactionType",
                schema: "inventory",
                table: "transactions",
                columns: new[] { "EffectiveDate", "TransactionType" });

            migrationBuilder.CreateIndex(
                name: "IX_transactions_ItemId_DestinationWarehouseId_EffectiveDate",
                schema: "inventory",
                table: "transactions",
                columns: new[] { "ItemId", "DestinationWarehouseId", "EffectiveDate" });

            migrationBuilder.CreateIndex(
                name: "IX_transactions_ItemId_WarehouseId",
                schema: "inventory",
                table: "transactions",
                columns: new[] { "ItemId", "WarehouseId" },
                unique: true,
                filter: "\"TransactionType\" = 'OPENING'");

            migrationBuilder.CreateIndex(
                name: "IX_transactions_ItemId_WarehouseId_EffectiveDate",
                schema: "inventory",
                table: "transactions",
                columns: new[] { "ItemId", "WarehouseId", "EffectiveDate" });

            migrationBuilder.CreateIndex(
                name: "IX_transactions_OriginalTransactionId",
                schema: "inventory",
                table: "transactions",
                column: "OriginalTransactionId",
                unique: true,
                filter: "\"OriginalTransactionId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_transactions_SourceDocumentId",
                schema: "inventory",
                table: "transactions",
                column: "SourceDocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_transactions_UnitId",
                schema: "inventory",
                table: "transactions",
                column: "UnitId");

            migrationBuilder.CreateIndex(
                name: "IX_transactions_WarehouseId",
                schema: "inventory",
                table: "transactions",
                column: "WarehouseId");

            migrationBuilder.CreateIndex(
                name: "IX_warehouses_BusinessNumber",
                schema: "inventory",
                table: "warehouses",
                column: "BusinessNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_warehouses_IsActive_BusinessNumber",
                schema: "inventory",
                table: "warehouses",
                columns: new[] { "IsActive", "BusinessNumber" });

            migrationBuilder.Sql("""
                CREATE VIEW inventory.ledger_effects AS
                SELECT t."Id" AS "TransactionId", t."ItemId", t."WarehouseId", t."UnitId",
                       t."EffectiveDate",
                       CASE
                         WHEN t."TransactionType" IN ('OPENING','RECEIPT','ADJ_IN') THEN t."Quantity"
                         WHEN t."TransactionType" IN ('ISSUE','ADJ_OUT','TRANSFER') THEN -t."Quantity"
                         WHEN t."TransactionType" = 'REVERSAL' AND o."TransactionType" IN ('OPENING','RECEIPT','ADJ_IN') THEN -t."Quantity"
                         WHEN t."TransactionType" = 'REVERSAL' AND o."TransactionType" IN ('ISSUE','ADJ_OUT','TRANSFER') THEN t."Quantity"
                       END::numeric(18,6) AS "QuantityEffect"
                FROM inventory.transactions t
                LEFT JOIN inventory.transactions o ON o."Id" = t."OriginalTransactionId"
                UNION ALL
                SELECT t."Id", t."ItemId", t."DestinationWarehouseId", t."UnitId", t."EffectiveDate",
                       CASE WHEN t."TransactionType" = 'TRANSFER' THEN t."Quantity" ELSE -t."Quantity" END::numeric(18,6)
                FROM inventory.transactions t
                LEFT JOIN inventory.transactions o ON o."Id" = t."OriginalTransactionId"
                WHERE t."TransactionType" = 'TRANSFER'
                   OR (t."TransactionType" = 'REVERSAL' AND o."TransactionType" = 'TRANSFER');

                CREATE FUNCTION inventory.assert_nonnegative(
                    p_item uuid, p_warehouse uuid, p_date date, p_delta numeric)
                RETURNS void LANGUAGE plpgsql AS $$
                DECLARE v_min numeric;
                BEGIN
                    WITH dates AS (
                        SELECT p_date AS d
                        UNION
                        SELECT e."EffectiveDate"
                        FROM inventory.ledger_effects e
                        WHERE e."ItemId" = p_item AND e."WarehouseId" = p_warehouse
                          AND e."EffectiveDate" >= p_date
                    ), balances AS (
                        SELECT d.d,
                               COALESCE((SELECT SUM(e."QuantityEffect")
                                         FROM inventory.ledger_effects e
                                         WHERE e."ItemId" = p_item
                                           AND e."WarehouseId" = p_warehouse
                                           AND e."EffectiveDate" <= d.d), 0) + p_delta AS balance
                        FROM dates d
                    )
                    SELECT MIN(balance) INTO v_min FROM balances;
                    IF v_min < 0 THEN
                        RAISE EXCEPTION 'Inventory balance cannot become negative'
                            USING ERRCODE = '23514', CONSTRAINT = 'ck_inventory_nonnegative';
                    END IF;
                END $$;

                CREATE FUNCTION inventory.guard_transaction_insert()
                RETURNS trigger LANGUAGE plpgsql AS $$
                DECLARE
                    v_item_unit uuid;
                    v_item_active boolean;
                    v_unit_active boolean;
                    v_source_active boolean;
                    v_destination_active boolean;
                    v_original inventory.transactions%ROWTYPE;
                    v_source_delta numeric := 0;
                    v_destination_delta numeric := 0;
                    v_warehouse uuid;
                BEGIN
                    IF NEW."EffectiveDate" > timezone('Asia/Tehran', CURRENT_TIMESTAMP)::date THEN
                        RAISE EXCEPTION 'Future inventory posting is not allowed'
                            USING ERRCODE = '23514', CONSTRAINT = 'ck_inventory_future_date';
                    END IF;

                    SELECT i."BaseUnitId", i."IsActive" INTO v_item_unit, v_item_active
                    FROM catalog.items i WHERE i."Id" = NEW."ItemId" FOR KEY SHARE;
                    SELECT u."IsActive" INTO v_unit_active
                    FROM reference_data.units u WHERE u."Id" = NEW."UnitId" FOR KEY SHARE;
                    SELECT w."IsActive" INTO v_source_active
                    FROM inventory.warehouses w WHERE w."Id" = NEW."WarehouseId" FOR KEY SHARE;
                    IF NEW."DestinationWarehouseId" IS NOT NULL THEN
                        SELECT w."IsActive" INTO v_destination_active
                        FROM inventory.warehouses w WHERE w."Id" = NEW."DestinationWarehouseId" FOR KEY SHARE;
                    END IF;

                    IF NEW."TransactionType" = 'REVERSAL' THEN
                        SELECT * INTO v_original FROM inventory.transactions
                        WHERE "Id" = NEW."OriginalTransactionId" FOR KEY SHARE;
                        IF NOT FOUND OR v_original."TransactionType" = 'REVERSAL'
                           OR NEW."ItemId" <> v_original."ItemId"
                           OR NEW."WarehouseId" <> v_original."WarehouseId"
                           OR NEW."DestinationWarehouseId" IS DISTINCT FROM v_original."DestinationWarehouseId"
                           OR NEW."Quantity" <> v_original."Quantity"
                           OR NEW."UnitId" <> v_original."UnitId"
                           OR NEW."EffectiveDate" <> v_original."EffectiveDate"
                           OR NEW."SourceDocumentType" IS NOT NULL OR NEW."SourceDocumentId" IS NOT NULL THEN
                            RAISE EXCEPTION 'Invalid inventory reversal'
                                USING ERRCODE = '23514', CONSTRAINT = 'ck_inventory_reversal_rules';
                        END IF;
                    ELSE
                        IF NEW."UnitId" <> v_item_unit THEN
                            RAISE EXCEPTION 'Inventory unit must equal current item base unit'
                                USING ERRCODE = '23514', CONSTRAINT = 'ck_inventory_base_unit';
                        END IF;
                        IF NOT COALESCE(v_item_active, false) OR NOT COALESCE(v_unit_active, false)
                           OR NOT COALESCE(v_source_active, false)
                           OR (NEW."DestinationWarehouseId" IS NOT NULL AND NOT COALESCE(v_destination_active, false)) THEN
                            RAISE EXCEPTION 'Active item, unit and warehouse are required'
                                USING ERRCODE = '23514', CONSTRAINT = 'ck_inventory_active_assignment';
                        END IF;
                    END IF;

                    FOR v_warehouse IN
                        SELECT DISTINCT value
                        FROM unnest(ARRAY[NEW."WarehouseId", NEW."DestinationWarehouseId"]) value
                        WHERE value IS NOT NULL ORDER BY value
                    LOOP
                        PERFORM pg_advisory_xact_lock(hashtextextended(NEW."ItemId"::text || ':' || v_warehouse::text, 0));
                    END LOOP;

                    IF NEW."TransactionType" IN ('ISSUE','ADJ_OUT','TRANSFER') THEN
                        v_source_delta := -NEW."Quantity";
                    ELSIF NEW."TransactionType" = 'REVERSAL' THEN
                        IF v_original."TransactionType" IN ('OPENING','RECEIPT','ADJ_IN') THEN
                            v_source_delta := -NEW."Quantity";
                        ELSIF v_original."TransactionType" IN ('ISSUE','ADJ_OUT','TRANSFER') THEN
                            v_source_delta := NEW."Quantity";
                        END IF;
                        IF v_original."TransactionType" = 'TRANSFER' THEN
                            v_destination_delta := -NEW."Quantity";
                        END IF;
                    END IF;

                    IF v_source_delta < 0 THEN
                        PERFORM inventory.assert_nonnegative(NEW."ItemId", NEW."WarehouseId",
                            NEW."EffectiveDate", v_source_delta);
                    END IF;
                    IF v_destination_delta < 0 THEN
                        PERFORM inventory.assert_nonnegative(NEW."ItemId", NEW."DestinationWarehouseId",
                            NEW."EffectiveDate", v_destination_delta);
                    END IF;
                    RETURN NEW;
                END $$;

                CREATE TRIGGER trg_inventory_transaction_insert
                BEFORE INSERT ON inventory.transactions
                FOR EACH ROW EXECUTE FUNCTION inventory.guard_transaction_insert();

                CREATE FUNCTION inventory.block_posted_transaction_mutation()
                RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    RAISE EXCEPTION 'Posted inventory transactions are immutable'
                        USING ERRCODE = '23514', CONSTRAINT = 'ck_inventory_posted_immutable';
                END $$;

                CREATE TRIGGER trg_inventory_transaction_immutable
                BEFORE UPDATE OR DELETE ON inventory.transactions
                FOR EACH ROW EXECUTE FUNCTION inventory.block_posted_transaction_mutation();

                CREATE FUNCTION inventory.guard_warehouse_update()
                RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    IF NEW."BusinessNumber" <> OLD."BusinessNumber" THEN
                        RAISE EXCEPTION 'Warehouse business number is immutable'
                            USING ERRCODE = '23514', CONSTRAINT = 'ck_warehouse_code_immutable';
                    END IF;
                    IF OLD."IsActive" AND NOT NEW."IsActive" AND EXISTS (
                        SELECT 1 FROM inventory.ledger_effects e
                        WHERE e."WarehouseId" = OLD."Id"
                        GROUP BY e."ItemId" HAVING SUM(e."QuantityEffect") <> 0
                    ) THEN
                        RAISE EXCEPTION 'Warehouse has nonzero stock'
                            USING ERRCODE = '23514', CONSTRAINT = 'ck_warehouse_nonzero_stock';
                    END IF;
                    RETURN NEW;
                END $$;

                CREATE TRIGGER trg_inventory_warehouse_update
                BEFORE UPDATE ON inventory.warehouses
                FOR EACH ROW EXECUTE FUNCTION inventory.guard_warehouse_update();

                CREATE FUNCTION inventory.guard_item_base_unit_update()
                RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    IF NEW."BaseUnitId" <> OLD."BaseUnitId" AND EXISTS (
                        SELECT 1 FROM inventory.transactions t WHERE t."ItemId" = OLD."Id"
                    ) THEN
                        RAISE EXCEPTION 'Item base unit is locked by inventory history'
                            USING ERRCODE = '23514', CONSTRAINT = 'ck_item_base_unit_inventory';
                    END IF;
                    RETURN NEW;
                END $$;

                CREATE TRIGGER trg_inventory_item_base_unit
                BEFORE UPDATE OF "BaseUnitId" ON catalog.items
                FOR EACH ROW EXECUTE FUNCTION inventory.guard_item_base_unit_update();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER IF EXISTS trg_inventory_item_base_unit ON catalog.items;
                DROP FUNCTION IF EXISTS inventory.guard_item_base_unit_update();
                DROP TRIGGER IF EXISTS trg_inventory_warehouse_update ON inventory.warehouses;
                DROP FUNCTION IF EXISTS inventory.guard_warehouse_update();
                DROP TRIGGER IF EXISTS trg_inventory_transaction_immutable ON inventory.transactions;
                DROP FUNCTION IF EXISTS inventory.block_posted_transaction_mutation();
                DROP TRIGGER IF EXISTS trg_inventory_transaction_insert ON inventory.transactions;
                DROP FUNCTION IF EXISTS inventory.guard_transaction_insert();
                DROP FUNCTION IF EXISTS inventory.assert_nonnegative(uuid, uuid, date, numeric);
                DROP VIEW IF EXISTS inventory.ledger_effects;
                """);

            migrationBuilder.DropTable(
                name: "transactions",
                schema: "inventory");

            migrationBuilder.DropTable(
                name: "warehouses",
                schema: "inventory");

            migrationBuilder.DropSequence(
                name: "warehouse_number_seq",
                schema: "inventory");
        }
    }
}
