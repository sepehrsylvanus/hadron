using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hadron.Erp.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class CorrectItemIdentity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                CREATE TABLE catalog.item_legacy_codes (
                    "ItemId" uuid PRIMARY KEY,
                    "LegacyCode" character varying(64) NULL,
                    "LegacyNormalizedCode" character varying(64) NULL,
                    "ArchivedAtUtc" timestamp with time zone NOT NULL,
                    CONSTRAINT "FK_item_legacy_codes_items_ItemId"
                        FOREIGN KEY ("ItemId") REFERENCES catalog.items ("Id") ON DELETE RESTRICT
                );

                INSERT INTO catalog.item_legacy_codes
                    ("ItemId", "LegacyCode", "LegacyNormalizedCode", "ArchivedAtUtc")
                SELECT "Id", "Code", "NormalizedCode", CURRENT_TIMESTAMP
                FROM catalog.items
                WHERE "Code" IS NOT NULL OR "NormalizedCode" IS NOT NULL;
                """);

            migrationBuilder.DropIndex(
                name: "IX_items_NormalizedCode",
                schema: "catalog",
                table: "items");

            migrationBuilder.DropCheckConstraint(
                name: "ck_items_code_pair",
                schema: "catalog",
                table: "items");

            migrationBuilder.DropCheckConstraint(
                name: "ck_items_product_code",
                schema: "catalog",
                table: "items");

            migrationBuilder.DropColumn(
                name: "Code",
                schema: "catalog",
                table: "items");

            migrationBuilder.DropColumn(
                name: "NormalizedCode",
                schema: "catalog",
                table: "items");

            migrationBuilder.AddCheckConstraint(
                name: "ck_items_business_number_format",
                schema: "catalog",
                table: "items",
                sql: "(\"ItemType\" = 'PRODUCT' AND \"BusinessNumber\" ~ '^PRD-[0-9]{4,}$') OR (\"ItemType\" = 'PART' AND \"BusinessNumber\" ~ '^PRT-[0-9]{4,}$') OR (\"ItemType\" = 'COMPONENT' AND \"BusinessNumber\" ~ '^CMP-[0-9]{4,}$')");

            migrationBuilder.Sql(
                """
                SELECT setval('catalog.product_number_seq', maximum_suffix, true)
                FROM (
                    SELECT COALESCE(MAX(substring("BusinessNumber" FROM '[0-9]+$')::bigint), 0) AS maximum_suffix
                    FROM catalog.items WHERE "ItemType" = 'PRODUCT'
                ) values_to_apply
                WHERE maximum_suffix > (SELECT last_value FROM catalog.product_number_seq);

                SELECT setval('catalog.part_number_seq', maximum_suffix, true)
                FROM (
                    SELECT COALESCE(MAX(substring("BusinessNumber" FROM '[0-9]+$')::bigint), 0) AS maximum_suffix
                    FROM catalog.items WHERE "ItemType" = 'PART'
                ) values_to_apply
                WHERE maximum_suffix > (SELECT last_value FROM catalog.part_number_seq);

                SELECT setval('catalog.component_number_seq', maximum_suffix, true)
                FROM (
                    SELECT COALESCE(MAX(substring("BusinessNumber" FROM '[0-9]+$')::bigint), 0) AS maximum_suffix
                    FROM catalog.items WHERE "ItemType" = 'COMPONENT'
                ) values_to_apply
                WHERE maximum_suffix > (SELECT last_value FROM catalog.component_number_seq);

                CREATE FUNCTION catalog.prevent_item_business_number_change()
                RETURNS trigger
                LANGUAGE plpgsql
                AS $function$
                BEGIN
                    IF NEW."BusinessNumber" IS DISTINCT FROM OLD."BusinessNumber" THEN
                        RAISE EXCEPTION 'Item BusinessNumber is immutable'
                            USING ERRCODE = '23514', CONSTRAINT = 'ck_items_business_number_immutable';
                    END IF;
                    RETURN NEW;
                END;
                $function$;

                CREATE TRIGGER trg_items_business_number_immutable
                BEFORE UPDATE OF "BusinessNumber" ON catalog.items
                FOR EACH ROW EXECUTE FUNCTION catalog.prevent_item_business_number_change();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DROP TRIGGER IF EXISTS trg_items_business_number_immutable ON catalog.items;
                DROP FUNCTION IF EXISTS catalog.prevent_item_business_number_change();
                """);

            migrationBuilder.DropCheckConstraint(
                name: "ck_items_business_number_format",
                schema: "catalog",
                table: "items");

            migrationBuilder.AddColumn<string>(
                name: "Code",
                schema: "catalog",
                table: "items",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.Sql(
                """
                UPDATE catalog.items AS item
                SET "Code" = COALESCE(legacy."LegacyCode", item."BusinessNumber"),
                    "NormalizedCode" = COALESCE(legacy."LegacyNormalizedCode", item."BusinessNumber")
                FROM catalog.item_legacy_codes AS legacy
                WHERE legacy."ItemId" = item."Id";

                UPDATE catalog.items
                SET "Code" = "BusinessNumber", "NormalizedCode" = "BusinessNumber"
                WHERE "Code" IS NULL AND "ItemType" = 'PRODUCT';
                """);

            migrationBuilder.AddColumn<string>(
                name: "NormalizedCode",
                schema: "catalog",
                table: "items",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_items_NormalizedCode",
                schema: "catalog",
                table: "items",
                column: "NormalizedCode",
                unique: true,
                filter: "\"NormalizedCode\" IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_items_code_pair",
                schema: "catalog",
                table: "items",
                sql: "(\"Code\" IS NULL) = (\"NormalizedCode\" IS NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_items_product_code",
                schema: "catalog",
                table: "items",
                sql: "\"ItemType\" <> 'PRODUCT' OR (\"Code\" IS NOT NULL AND \"NormalizedCode\" IS NOT NULL)");

            migrationBuilder.Sql("DROP TABLE catalog.item_legacy_codes;");
        }
    }
}
