using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Hadron.Erp.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialReferenceData : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "reference_data");

            migrationBuilder.CreateTable(
                name: "currencies",
                schema: "reference_data",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Symbol = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    DecimalPrecision = table.Column<int>(type: "integer", nullable: false),
                    Code = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    NormalizedCode = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    NameFa = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    NameEn = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_currencies", x => x.Id);
                    table.CheckConstraint("ck_currencies_decimal_precision", "\"DecimalPrecision\" >= 0 AND \"DecimalPrecision\" <= 9");
                    table.CheckConstraint("ck_currencies_version", "\"Version\" >= 1");
                });

            migrationBuilder.CreateTable(
                name: "reference_types",
                schema: "reference_data",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    NameFa = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    NameEn = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_reference_types", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "units",
                schema: "reference_data",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Symbol = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    Code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    NormalizedCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    NameFa = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    NameEn = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_units", x => x.Id);
                    table.CheckConstraint("ck_units_sort_order", "\"SortOrder\" >= 0");
                    table.CheckConstraint("ck_units_version", "\"Version\" >= 1");
                });

            migrationBuilder.CreateTable(
                name: "reference_values",
                schema: "reference_data",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ReferenceTypeId = table.Column<Guid>(type: "uuid", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    Code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    NormalizedCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    NameFa = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    NameEn = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_reference_values", x => x.Id);
                    table.CheckConstraint("ck_reference_values_sort_order", "\"SortOrder\" >= 0");
                    table.CheckConstraint("ck_reference_values_version", "\"Version\" >= 1");
                    table.ForeignKey(
                        name: "FK_reference_values_reference_types_ReferenceTypeId",
                        column: x => x.ReferenceTypeId,
                        principalSchema: "reference_data",
                        principalTable: "reference_types",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                schema: "reference_data",
                table: "reference_types",
                columns: new[] { "Id", "Code", "NameEn", "NameFa" },
                values: new object[,]
                {
                    { new Guid("10000000-0000-0000-0000-000000000001"), "COUNTRY", "Country", "کشور" },
                    { new Guid("10000000-0000-0000-0000-000000000002"), "BUSINESS_UNIT", "Business Unit", "واحد سازمانی" },
                    { new Guid("10000000-0000-0000-0000-000000000003"), "PRODUCT_CATEGORY", "Product Category", "گروه محصول" },
                    { new Guid("10000000-0000-0000-0000-000000000004"), "PART_CATEGORY", "Part Category", "گروه قطعه" },
                    { new Guid("10000000-0000-0000-0000-000000000005"), "COMPONENT_CATEGORY", "Component Category", "گروه کامپوننت" },
                    { new Guid("10000000-0000-0000-0000-000000000006"), "PAYMENT_TERM", "Payment Term", "شرایط پرداخت" },
                    { new Guid("10000000-0000-0000-0000-000000000007"), "SHIPPING_MODE", "Shipping Mode", "روش حمل" },
                    { new Guid("10000000-0000-0000-0000-000000000008"), "INCOTERM", "Incoterm", "اینکوترمز" }
                });

            migrationBuilder.CreateIndex(
                name: "IX_currencies_IsActive",
                schema: "reference_data",
                table: "currencies",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_currencies_NormalizedCode",
                schema: "reference_data",
                table: "currencies",
                column: "NormalizedCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_reference_types_Code",
                schema: "reference_data",
                table: "reference_types",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_reference_values_ReferenceTypeId_IsActive_SortOrder",
                schema: "reference_data",
                table: "reference_values",
                columns: new[] { "ReferenceTypeId", "IsActive", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_reference_values_ReferenceTypeId_NormalizedCode",
                schema: "reference_data",
                table: "reference_values",
                columns: new[] { "ReferenceTypeId", "NormalizedCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_units_IsActive_SortOrder",
                schema: "reference_data",
                table: "units",
                columns: new[] { "IsActive", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_units_NormalizedCode",
                schema: "reference_data",
                table: "units",
                column: "NormalizedCode",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "currencies",
                schema: "reference_data");

            migrationBuilder.DropTable(
                name: "reference_values",
                schema: "reference_data");

            migrationBuilder.DropTable(
                name: "units",
                schema: "reference_data");

            migrationBuilder.DropTable(
                name: "reference_types",
                schema: "reference_data");
        }
    }
}
