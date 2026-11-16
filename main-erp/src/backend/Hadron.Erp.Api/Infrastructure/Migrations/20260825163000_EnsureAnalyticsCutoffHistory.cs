using Hadron.Erp.Api.Infrastructure;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hadron.Erp.Api.Infrastructure.Migrations;

[DbContext(typeof(HadronDbContext))]
[Migration("20260825163000_EnsureAnalyticsCutoffHistory")]
public sealed class EnsureAnalyticsCutoffHistory : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            CREATE SCHEMA IF NOT EXISTS analytics;

            CREATE TABLE analytics.master_context_history (
              "Id" bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
              "EntityType" text NOT NULL,
              "EntityId" uuid NOT NULL,
              "Code" text NOT NULL,
              "NameFa" text NOT NULL,
              "NameEn" text NULL,
              "IsActive" boolean NOT NULL,
              "EffectiveAtUtc" timestamptz NOT NULL,
              CONSTRAINT ck_analytics_master_context_type CHECK
                ("EntityType" IN ('ITEM','SUPPLIER','UNIT','WAREHOUSE','CURRENCY'))
            );
            CREATE INDEX ix_analytics_master_context_effective
              ON analytics.master_context_history
              ("EntityType", "EntityId", "EffectiveAtUtc" DESC, "Id" DESC);

            CREATE TABLE analytics.sourcing_option_context_history (
              "Id" bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
              "SourcingOptionId" uuid NOT NULL,
              "Version" bigint NOT NULL,
              "SupplierLeadTimeDays" integer NULL,
              "ForwarderToShipmentDays" integer NULL,
              "ShippingPeriodDays" integer NULL,
              "DestinationCustomsDays" integer NULL,
              "EffectiveAtUtc" timestamptz NOT NULL
            );
            CREATE INDEX ix_analytics_sourcing_context_effective
              ON analytics.sourcing_option_context_history
              ("SourcingOptionId", "EffectiveAtUtc" DESC, "Id" DESC);

            INSERT INTO analytics.master_context_history
              ("EntityType","EntityId","Code","NameFa","NameEn","IsActive","EffectiveAtUtc")
            SELECT 'ITEM', "Id", "BusinessNumber", "NameFa", "NameEn", "IsActive", "CreatedAtUtc"
            FROM catalog.items;
            INSERT INTO analytics.master_context_history
              ("EntityType","EntityId","Code","NameFa","NameEn","IsActive","EffectiveAtUtc")
            SELECT 'SUPPLIER', "Id", "SupplierNumber", "Name", NULL, "IsActive", "CreatedAtUtc"
            FROM procurement.suppliers;
            INSERT INTO analytics.master_context_history
              ("EntityType","EntityId","Code","NameFa","NameEn","IsActive","EffectiveAtUtc")
            SELECT 'UNIT', "Id", "Code", "NameFa", NULLIF("NameEn",''), "IsActive", "CreatedAtUtc"
            FROM reference_data.units;
            INSERT INTO analytics.master_context_history
              ("EntityType","EntityId","Code","NameFa","NameEn","IsActive","EffectiveAtUtc")
            SELECT 'WAREHOUSE', "Id", "BusinessNumber", "NameFa", "NameEn", "IsActive", "CreatedAtUtc"
            FROM inventory.warehouses;
            INSERT INTO analytics.master_context_history
              ("EntityType","EntityId","Code","NameFa","NameEn","IsActive","EffectiveAtUtc")
            SELECT 'CURRENCY', "Id", "Code", "NameFa", NULLIF("NameEn",''), "IsActive", "CreatedAtUtc"
            FROM reference_data.currencies;

            INSERT INTO analytics.sourcing_option_context_history
              ("SourcingOptionId","Version","SupplierLeadTimeDays","ForwarderToShipmentDays",
               "ShippingPeriodDays","DestinationCustomsDays","EffectiveAtUtc")
            SELECT "Id", "Version", "SupplierLeadTimeDays", "ForwarderToShipmentDays",
                   "ShippingPeriodDays", "DestinationCustomsDays", "CreatedAtUtc"
            FROM procurement.sourcing_options;

            CREATE OR REPLACE FUNCTION analytics.capture_master_context()
            RETURNS trigger LANGUAGE plpgsql AS $$
            DECLARE payload jsonb := to_jsonb(NEW); entity_type text := TG_ARGV[0];
                    entity_code text; entity_name_fa text; entity_name_en text; entity_active boolean;
            BEGIN
              entity_code := COALESCE(payload->>'BusinessNumber', payload->>'SupplierNumber', payload->>'Code');
              entity_name_fa := COALESCE(payload->>'NameFa', payload->>'Name');
              entity_name_en := NULLIF(payload->>'NameEn','');
              entity_active := COALESCE((payload->>'IsActive')::boolean, true);
              IF NOT EXISTS (
                SELECT 1 FROM analytics.master_context_history h
                WHERE h."EntityType"=entity_type AND h."EntityId"=NEW."Id"
                  AND h."Code"=entity_code AND h."NameFa"=entity_name_fa
                  AND h."NameEn" IS NOT DISTINCT FROM entity_name_en
                  AND h."IsActive"=entity_active
                ORDER BY h."EffectiveAtUtc" DESC,h."Id" DESC LIMIT 1
              ) THEN
                INSERT INTO analytics.master_context_history
                  ("EntityType","EntityId","Code","NameFa","NameEn","IsActive","EffectiveAtUtc")
                VALUES (entity_type,NEW."Id",entity_code,entity_name_fa,entity_name_en,entity_active,clock_timestamp());
              END IF;
              RETURN NEW;
            END $$;

            CREATE TRIGGER tr_analytics_item_context AFTER INSERT OR UPDATE ON catalog.items
              FOR EACH ROW EXECUTE FUNCTION analytics.capture_master_context('ITEM');
            CREATE TRIGGER tr_analytics_supplier_context AFTER INSERT OR UPDATE ON procurement.suppliers
              FOR EACH ROW EXECUTE FUNCTION analytics.capture_master_context('SUPPLIER');
            CREATE TRIGGER tr_analytics_unit_context AFTER INSERT OR UPDATE ON reference_data.units
              FOR EACH ROW EXECUTE FUNCTION analytics.capture_master_context('UNIT');
            CREATE TRIGGER tr_analytics_warehouse_context AFTER INSERT OR UPDATE ON inventory.warehouses
              FOR EACH ROW EXECUTE FUNCTION analytics.capture_master_context('WAREHOUSE');
            CREATE TRIGGER tr_analytics_currency_context AFTER INSERT OR UPDATE ON reference_data.currencies
              FOR EACH ROW EXECUTE FUNCTION analytics.capture_master_context('CURRENCY');

            CREATE OR REPLACE FUNCTION analytics.capture_sourcing_option_context()
            RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN
              IF NOT EXISTS (
                SELECT 1 FROM analytics.sourcing_option_context_history h
                WHERE h."SourcingOptionId"=NEW."Id" AND h."Version"=NEW."Version"
                  AND h."SupplierLeadTimeDays" IS NOT DISTINCT FROM NEW."SupplierLeadTimeDays"
                  AND h."ForwarderToShipmentDays" IS NOT DISTINCT FROM NEW."ForwarderToShipmentDays"
                  AND h."ShippingPeriodDays" IS NOT DISTINCT FROM NEW."ShippingPeriodDays"
                  AND h."DestinationCustomsDays" IS NOT DISTINCT FROM NEW."DestinationCustomsDays"
                ORDER BY h."EffectiveAtUtc" DESC,h."Id" DESC LIMIT 1
              ) THEN
                INSERT INTO analytics.sourcing_option_context_history
                  ("SourcingOptionId","Version","SupplierLeadTimeDays","ForwarderToShipmentDays",
                   "ShippingPeriodDays","DestinationCustomsDays","EffectiveAtUtc")
                VALUES (NEW."Id",NEW."Version",NEW."SupplierLeadTimeDays",NEW."ForwarderToShipmentDays",
                        NEW."ShippingPeriodDays",NEW."DestinationCustomsDays",clock_timestamp());
              END IF;
              RETURN NEW;
            END $$;
            CREATE TRIGGER tr_analytics_sourcing_context AFTER INSERT OR UPDATE ON procurement.sourcing_options
              FOR EACH ROW EXECUTE FUNCTION analytics.capture_sourcing_option_context();

            CREATE OR REPLACE FUNCTION analytics.reject_context_history_mutation()
            RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN
              RAISE EXCEPTION 'ANALYTICS_CONTEXT_HISTORY_IMMUTABLE' USING ERRCODE='23514';
            END $$;
            CREATE TRIGGER tr_analytics_master_context_immutable
              BEFORE UPDATE OR DELETE ON analytics.master_context_history
              FOR EACH ROW EXECUTE FUNCTION analytics.reject_context_history_mutation();
            CREATE TRIGGER tr_analytics_sourcing_context_immutable
              BEFORE UPDATE OR DELETE ON analytics.sourcing_option_context_history
              FOR EACH ROW EXECUTE FUNCTION analytics.reject_context_history_mutation();
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DROP TRIGGER IF EXISTS tr_analytics_sourcing_context ON procurement.sourcing_options;
            DROP TRIGGER IF EXISTS tr_analytics_currency_context ON reference_data.currencies;
            DROP TRIGGER IF EXISTS tr_analytics_warehouse_context ON inventory.warehouses;
            DROP TRIGGER IF EXISTS tr_analytics_unit_context ON reference_data.units;
            DROP TRIGGER IF EXISTS tr_analytics_supplier_context ON procurement.suppliers;
            DROP TRIGGER IF EXISTS tr_analytics_item_context ON catalog.items;
            DROP FUNCTION IF EXISTS analytics.capture_sourcing_option_context();
            DROP FUNCTION IF EXISTS analytics.capture_master_context();
            DROP TABLE IF EXISTS analytics.sourcing_option_context_history;
            DROP TABLE IF EXISTS analytics.master_context_history;
            DROP FUNCTION IF EXISTS analytics.reject_context_history_mutation();
            """);
    }
}
