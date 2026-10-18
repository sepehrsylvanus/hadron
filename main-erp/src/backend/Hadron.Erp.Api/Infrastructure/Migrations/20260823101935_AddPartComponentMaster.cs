using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hadron.Erp.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPartComponentMaster : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_items_IsActive_Code",
                schema: "catalog",
                table: "items");

            migrationBuilder.DropIndex(
                name: "IX_items_NormalizedCode",
                schema: "catalog",
                table: "items");

            migrationBuilder.DropCheckConstraint(
                name: "ck_items_item_type",
                schema: "catalog",
                table: "items");

            migrationBuilder.CreateSequence(
                name: "component_number_seq",
                schema: "catalog");

            migrationBuilder.CreateSequence(
                name: "part_number_seq",
                schema: "catalog");

            migrationBuilder.AlterColumn<string>(
                name: "NormalizedCode",
                schema: "catalog",
                table: "items",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64);

            migrationBuilder.AlterColumn<string>(
                name: "Code",
                schema: "catalog",
                table: "items",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64);

            migrationBuilder.CreateTable(
                name: "material_item_profiles",
                schema: "catalog",
                columns: table => new
                {
                    ItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    CategoryId = table.Column<Guid>(type: "uuid", nullable: false),
                    SupplyMethod = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    NetWeightKg = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true),
                    DefaultPlanningScrapPercent = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    SafetyStockQty = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    CriticalDays = table.Column<int>(type: "integer", nullable: false),
                    SafeStartDays = table.Column<int>(type: "integer", nullable: false),
                    ExcessStartDays = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_material_item_profiles", x => x.ItemId);
                    table.CheckConstraint("ck_material_profiles_net_weight", "\"NetWeightKg\" IS NULL OR \"NetWeightKg\" >= 0");
                    table.CheckConstraint("ck_material_profiles_safety_stock", "\"SafetyStockQty\" >= 0");
                    table.CheckConstraint("ck_material_profiles_scrap", "\"DefaultPlanningScrapPercent\" BETWEEN 0 AND 100");
                    table.CheckConstraint("ck_material_profiles_supply_method", "\"SupplyMethod\" IN ('BUY','MAKE','OUTSOURCE','HYBRID')");
                    table.CheckConstraint("ck_material_profiles_thresholds", "\"CriticalDays\" >= 0 AND \"CriticalDays\" <= \"SafeStartDays\" AND \"SafeStartDays\" <= \"ExcessStartDays\"");
                    table.ForeignKey(
                        name: "FK_material_item_profiles_items_ItemId",
                        column: x => x.ItemId,
                        principalSchema: "catalog",
                        principalTable: "items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_material_item_profiles_reference_values_CategoryId",
                        column: x => x.CategoryId,
                        principalSchema: "reference_data",
                        principalTable: "reference_values",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_items_ItemType_IsActive",
                schema: "catalog",
                table: "items",
                columns: new[] { "ItemType", "IsActive" });

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
                name: "ck_items_item_type",
                schema: "catalog",
                table: "items",
                sql: "\"ItemType\" IN ('PRODUCT','PART','COMPONENT')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_items_product_code",
                schema: "catalog",
                table: "items",
                sql: "\"ItemType\" <> 'PRODUCT' OR (\"Code\" IS NOT NULL AND \"NormalizedCode\" IS NOT NULL)");

            migrationBuilder.CreateIndex(
                name: "IX_material_item_profiles_CategoryId",
                schema: "catalog",
                table: "material_item_profiles",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_material_item_profiles_SupplyMethod",
                schema: "catalog",
                table: "material_item_profiles",
                column: "SupplyMethod");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM catalog.items WHERE "ItemType" IN ('PART','COMPONENT')) THEN
                        RAISE EXCEPTION 'Cannot roll back MRP-04 while Part or Component data exists.';
                    END IF;
                END $$;
                """);

            migrationBuilder.DropTable(
                name: "material_item_profiles",
                schema: "catalog");

            migrationBuilder.DropIndex(
                name: "IX_items_ItemType_IsActive",
                schema: "catalog",
                table: "items");

            migrationBuilder.DropIndex(
                name: "IX_items_NormalizedCode",
                schema: "catalog",
                table: "items");

            migrationBuilder.DropCheckConstraint(
                name: "ck_items_code_pair",
                schema: "catalog",
                table: "items");

            migrationBuilder.DropCheckConstraint(
                name: "ck_items_item_type",
                schema: "catalog",
                table: "items");

            migrationBuilder.DropCheckConstraint(
                name: "ck_items_product_code",
                schema: "catalog",
                table: "items");

            migrationBuilder.DropSequence(
                name: "component_number_seq",
                schema: "catalog");

            migrationBuilder.DropSequence(
                name: "part_number_seq",
                schema: "catalog");

            migrationBuilder.AlterColumn<string>(
                name: "NormalizedCode",
                schema: "catalog",
                table: "items",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Code",
                schema: "catalog",
                table: "items",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64,
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_items_IsActive_Code",
                schema: "catalog",
                table: "items",
                columns: new[] { "IsActive", "Code" });

            migrationBuilder.CreateIndex(
                name: "IX_items_NormalizedCode",
                schema: "catalog",
                table: "items",
                column: "NormalizedCode",
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_items_item_type",
                schema: "catalog",
                table: "items",
                sql: "\"ItemType\" = 'PRODUCT'");
        }
    }
}
