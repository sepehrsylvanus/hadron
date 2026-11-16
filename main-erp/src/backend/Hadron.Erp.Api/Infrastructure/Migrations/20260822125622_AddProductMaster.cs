using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hadron.Erp.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddProductMaster : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "catalog");

            migrationBuilder.CreateSequence<long>(
                name: "product_number_seq",
                schema: "catalog");

            migrationBuilder.CreateTable(
                name: "items",
                schema: "catalog",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ItemType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    BusinessNumber = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    NormalizedCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    NameFa = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    NameEn = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    BaseUnitId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    Notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_items", x => x.Id);
                    table.CheckConstraint("ck_items_item_type", "\"ItemType\" = 'PRODUCT'");
                    table.CheckConstraint("ck_items_version", "\"Version\" >= 1");
                    table.ForeignKey(
                        name: "FK_items_units_BaseUnitId",
                        column: x => x.BaseUnitId,
                        principalSchema: "reference_data",
                        principalTable: "units",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "products",
                schema: "catalog",
                columns: table => new
                {
                    ItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductCategoryId = table.Column<Guid>(type: "uuid", nullable: false),
                    BusinessUnitId = table.Column<Guid>(type: "uuid", nullable: true),
                    SafetyStockQty = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    CriticalDays = table.Column<int>(type: "integer", nullable: false),
                    SafeStartDays = table.Column<int>(type: "integer", nullable: false),
                    ExcessStartDays = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_products", x => x.ItemId);
                    table.CheckConstraint("ck_products_safety_stock", "\"SafetyStockQty\" >= 0");
                    table.CheckConstraint("ck_products_thresholds", "\"CriticalDays\" >= 0 AND \"CriticalDays\" <= \"SafeStartDays\" AND \"SafeStartDays\" <= \"ExcessStartDays\"");
                    table.ForeignKey(
                        name: "FK_products_items_ItemId",
                        column: x => x.ItemId,
                        principalSchema: "catalog",
                        principalTable: "items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_products_reference_values_BusinessUnitId",
                        column: x => x.BusinessUnitId,
                        principalSchema: "reference_data",
                        principalTable: "reference_values",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_products_reference_values_ProductCategoryId",
                        column: x => x.ProductCategoryId,
                        principalSchema: "reference_data",
                        principalTable: "reference_values",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_items_BaseUnitId",
                schema: "catalog",
                table: "items",
                column: "BaseUnitId");

            migrationBuilder.CreateIndex(
                name: "IX_items_BusinessNumber",
                schema: "catalog",
                table: "items",
                column: "BusinessNumber",
                unique: true);

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

            migrationBuilder.CreateIndex(
                name: "IX_products_BusinessUnitId",
                schema: "catalog",
                table: "products",
                column: "BusinessUnitId");

            migrationBuilder.CreateIndex(
                name: "IX_products_ProductCategoryId",
                schema: "catalog",
                table: "products",
                column: "ProductCategoryId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "products",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "items",
                schema: "catalog");

            migrationBuilder.DropSequence(
                name: "product_number_seq",
                schema: "catalog");
        }
    }
}
