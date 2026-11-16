using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hadron.Erp.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SynchronizeInventoryConcurrencyLocks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE FUNCTION inventory.lock_item_inventory(p_item_id uuid)
                RETURNS void LANGUAGE plpgsql AS $$
                BEGIN
                    PERFORM pg_advisory_xact_lock(
                        hashtextextended('inventory:item:' || p_item_id::text, 0));
                END $$;

                CREATE FUNCTION inventory.lock_warehouse_inventory(p_warehouse_id uuid)
                RETURNS void LANGUAGE plpgsql AS $$
                BEGIN
                    PERFORM pg_advisory_xact_lock(
                        hashtextextended('inventory:warehouse:' || p_warehouse_id::text, 0));
                END $$;

                CREATE OR REPLACE FUNCTION inventory.guard_transaction_insert()
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
                    PERFORM inventory.lock_item_inventory(NEW."ItemId");
                    FOR v_warehouse IN
                        SELECT DISTINCT value
                        FROM unnest(ARRAY[NEW."WarehouseId", NEW."DestinationWarehouseId"]) value
                        WHERE value IS NOT NULL ORDER BY value
                    LOOP
                        PERFORM inventory.lock_warehouse_inventory(v_warehouse);
                    END LOOP;

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

                CREATE OR REPLACE FUNCTION inventory.guard_warehouse_update()
                RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    IF NEW."BusinessNumber" <> OLD."BusinessNumber" THEN
                        RAISE EXCEPTION 'Warehouse business number is immutable'
                            USING ERRCODE = '23514', CONSTRAINT = 'ck_warehouse_code_immutable';
                    END IF;
                    IF OLD."IsActive" AND NOT NEW."IsActive" THEN
                        PERFORM inventory.lock_warehouse_inventory(OLD."Id");
                        IF EXISTS (
                            SELECT 1 FROM inventory.ledger_effects e
                            WHERE e."WarehouseId" = OLD."Id"
                            GROUP BY e."ItemId" HAVING SUM(e."QuantityEffect") <> 0
                        ) THEN
                            RAISE EXCEPTION 'Warehouse has nonzero stock'
                                USING ERRCODE = '23514', CONSTRAINT = 'ck_warehouse_nonzero_stock';
                        END IF;
                    END IF;
                    RETURN NEW;
                END $$;

                CREATE OR REPLACE FUNCTION inventory.guard_item_base_unit_update()
                RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    IF NEW."BaseUnitId" <> OLD."BaseUnitId" THEN
                        PERFORM inventory.lock_item_inventory(OLD."Id");
                        IF EXISTS (
                            SELECT 1 FROM inventory.transactions t WHERE t."ItemId" = OLD."Id"
                        ) THEN
                            RAISE EXCEPTION 'Item base unit is locked by inventory history'
                                USING ERRCODE = '23514', CONSTRAINT = 'ck_item_base_unit_inventory';
                        END IF;
                    END IF;
                    RETURN NEW;
                END $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION inventory.guard_transaction_insert()
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
                        PERFORM pg_advisory_xact_lock(hashtextextended(
                            NEW."ItemId"::text || ':' || v_warehouse::text, 0));
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

                CREATE OR REPLACE FUNCTION inventory.guard_warehouse_update()
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

                CREATE OR REPLACE FUNCTION inventory.guard_item_base_unit_update()
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

                DROP FUNCTION inventory.lock_warehouse_inventory(uuid);
                DROP FUNCTION inventory.lock_item_inventory(uuid);
                """);
        }
    }
}
