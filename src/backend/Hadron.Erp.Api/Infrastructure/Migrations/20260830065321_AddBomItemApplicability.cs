using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hadron.Erp.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddBomItemApplicability : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "bom_item_applicabilities",
                schema: "engineering",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ParentItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    ChildItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_bom_item_applicabilities", x => x.Id);
                    table.CheckConstraint("ck_bom_item_applicabilities_version", "\"Version\" >= 1");
                    table.ForeignKey(
                        name: "FK_bom_item_applicabilities_items_ChildItemId",
                        column: x => x.ChildItemId,
                        principalSchema: "catalog",
                        principalTable: "items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_bom_item_applicabilities_items_ParentItemId",
                        column: x => x.ParentItemId,
                        principalSchema: "catalog",
                        principalTable: "items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_bom_item_applicabilities_ChildItemId_IsActive",
                schema: "engineering",
                table: "bom_item_applicabilities",
                columns: new[] { "ChildItemId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_bom_item_applicabilities_ParentItemId_ChildItemId",
                schema: "engineering",
                table: "bom_item_applicabilities",
                columns: new[] { "ParentItemId", "ChildItemId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_bom_item_applicabilities_ParentItemId_IsActive",
                schema: "engineering",
                table: "bom_item_applicabilities",
                columns: new[] { "ParentItemId", "IsActive" });

            migrationBuilder.Sql(
                """
                INSERT INTO engineering.bom_item_applicabilities
                    ("Id", "ParentItemId", "ChildItemId", "IsActive", "Version", "CreatedAtUtc", "UpdatedAtUtc")
                SELECT
                    gen_random_uuid(),
                    r."ParentItemId",
                    l."ChildItemId",
                    TRUE,
                    1,
                    MIN(l."CreatedAtUtc"),
                    CURRENT_TIMESTAMP
                FROM engineering.bom_lines l
                JOIN engineering.bom_revisions r ON r."Id" = l."BomRevisionId"
                GROUP BY r."ParentItemId", l."ChildItemId"
                ON CONFLICT ("ParentItemId", "ChildItemId") DO NOTHING;

                CREATE FUNCTION engineering.guard_bom_item_applicability() RETURNS trigger AS $$
                DECLARE
                    parent_type text;
                    child_type text;
                BEGIN
                    IF TG_OP = 'DELETE' THEN
                        RAISE EXCEPTION 'BOM_ITEM_APPLICABILITY_DELETE_FORBIDDEN';
                    END IF;

                    IF TG_OP = 'UPDATE' AND
                       (NEW."ParentItemId" IS DISTINCT FROM OLD."ParentItemId" OR
                        NEW."ChildItemId" IS DISTINCT FROM OLD."ChildItemId") THEN
                        RAISE EXCEPTION 'BOM_ITEM_APPLICABILITY_IDENTITY_IMMUTABLE';
                    END IF;

                    IF NEW."ParentItemId" = NEW."ChildItemId" THEN
                        RAISE EXCEPTION 'BOM_ITEM_APPLICABILITY_SELF_REFERENCE';
                    END IF;

                    SELECT "ItemType" INTO parent_type
                    FROM catalog.items
                    WHERE "Id" = NEW."ParentItemId";

                    SELECT "ItemType" INTO child_type
                    FROM catalog.items
                    WHERE "Id" = NEW."ChildItemId";

                    IF parent_type IS NULL OR child_type IS NULL OR NOT (
                        (parent_type = 'PRODUCT' AND child_type = 'PART') OR
                        (parent_type = 'PART' AND child_type = 'COMPONENT') OR
                        (parent_type = 'COMPONENT' AND child_type = 'COMPONENT')
                    ) THEN
                        RAISE EXCEPTION 'BOM_ITEM_APPLICABILITY_TYPE_INVALID';
                    END IF;

                    RETURN NEW;
                END;
                $$ LANGUAGE plpgsql;

                CREATE TRIGGER trg_guard_bom_item_applicability
                BEFORE INSERT OR UPDATE OR DELETE ON engineering.bom_item_applicabilities
                FOR EACH ROW EXECUTE FUNCTION engineering.guard_bom_item_applicability();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DROP TRIGGER IF EXISTS trg_guard_bom_item_applicability
                    ON engineering.bom_item_applicabilities;
                DROP FUNCTION IF EXISTS engineering.guard_bom_item_applicability();
                """);

            migrationBuilder.DropTable(
                name: "bom_item_applicabilities",
                schema: "engineering");
        }
    }
}
