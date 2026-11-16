using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hadron.Erp.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSourcingCommercialTerms : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateSequence(
                name: "sourcing_option_number_seq",
                schema: "procurement");

            migrationBuilder.CreateTable(
                name: "sourcing_options",
                schema: "procurement",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SourcingNumber = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    SupplierId = table.Column<Guid>(type: "uuid", nullable: false),
                    ItemBaseUnitId = table.Column<Guid>(type: "uuid", nullable: false),
                    MinimumOrderQuantity = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true),
                    ShippingModeId = table.Column<Guid>(type: "uuid", nullable: true),
                    IncotermId = table.Column<Guid>(type: "uuid", nullable: true),
                    SupplierLeadTimeDays = table.Column<int>(type: "integer", nullable: true),
                    ForwarderToShipmentDays = table.Column<int>(type: "integer", nullable: true),
                    ShippingPeriodDays = table.Column<int>(type: "integer", nullable: true),
                    DestinationCustomsDays = table.Column<int>(type: "integer", nullable: true),
                    IsPreferred = table.Column<bool>(type: "boolean", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sourcing_options", x => x.Id);
                    table.CheckConstraint("ck_sourcing_options_lead_times", "(\"SupplierLeadTimeDays\" IS NULL OR \"SupplierLeadTimeDays\" >= 0) AND (\"ForwarderToShipmentDays\" IS NULL OR \"ForwarderToShipmentDays\" >= 0) AND (\"ShippingPeriodDays\" IS NULL OR \"ShippingPeriodDays\" >= 0) AND (\"DestinationCustomsDays\" IS NULL OR \"DestinationCustomsDays\" >= 0)");
                    table.CheckConstraint("ck_sourcing_options_moq", "\"MinimumOrderQuantity\" IS NULL OR \"MinimumOrderQuantity\" >= 0");
                    table.CheckConstraint("ck_sourcing_options_preferred_active", "NOT \"IsPreferred\" OR \"IsActive\"");
                    table.CheckConstraint("ck_sourcing_options_version", "\"Version\" >= 1");
                    table.ForeignKey(
                        name: "FK_sourcing_options_items_ItemId",
                        column: x => x.ItemId,
                        principalSchema: "catalog",
                        principalTable: "items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_sourcing_options_reference_values_IncotermId",
                        column: x => x.IncotermId,
                        principalSchema: "reference_data",
                        principalTable: "reference_values",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_sourcing_options_reference_values_ShippingModeId",
                        column: x => x.ShippingModeId,
                        principalSchema: "reference_data",
                        principalTable: "reference_values",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_sourcing_options_suppliers_SupplierId",
                        column: x => x.SupplierId,
                        principalSchema: "procurement",
                        principalTable: "suppliers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_sourcing_options_units_ItemBaseUnitId",
                        column: x => x.ItemBaseUnitId,
                        principalSchema: "reference_data",
                        principalTable: "units",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "sourcing_prices",
                schema: "procurement",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SourcingOptionId = table.Column<Guid>(type: "uuid", nullable: false),
                    UnitPrice = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    CurrencyId = table.Column<Guid>(type: "uuid", nullable: false),
                    EffectiveFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    EffectiveTo = table.Column<DateOnly>(type: "date", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sourcing_prices", x => x.Id);
                    table.CheckConstraint("ck_sourcing_prices_period", "\"EffectiveTo\" IS NULL OR \"EffectiveTo\" >= \"EffectiveFrom\"");
                    table.CheckConstraint("ck_sourcing_prices_unit_price", "\"UnitPrice\" >= 0");
                    table.ForeignKey(
                        name: "FK_sourcing_prices_currencies_CurrencyId",
                        column: x => x.CurrencyId,
                        principalSchema: "reference_data",
                        principalTable: "currencies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_sourcing_prices_sourcing_options_SourcingOptionId",
                        column: x => x.SourcingOptionId,
                        principalSchema: "procurement",
                        principalTable: "sourcing_options",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_sourcing_options_IncotermId",
                schema: "procurement",
                table: "sourcing_options",
                column: "IncotermId");

            migrationBuilder.CreateIndex(
                name: "IX_sourcing_options_ItemBaseUnitId",
                schema: "procurement",
                table: "sourcing_options",
                column: "ItemBaseUnitId");

            migrationBuilder.CreateIndex(
                name: "IX_sourcing_options_ItemId_IsActive",
                schema: "procurement",
                table: "sourcing_options",
                columns: new[] { "ItemId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_sourcing_options_ShippingModeId",
                schema: "procurement",
                table: "sourcing_options",
                column: "ShippingModeId");

            migrationBuilder.CreateIndex(
                name: "IX_sourcing_options_SourcingNumber",
                schema: "procurement",
                table: "sourcing_options",
                column: "SourcingNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_sourcing_options_SupplierId_IsActive",
                schema: "procurement",
                table: "sourcing_options",
                columns: new[] { "SupplierId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "ux_sourcing_options_active_preferred_item",
                schema: "procurement",
                table: "sourcing_options",
                column: "ItemId",
                unique: true,
                filter: "\"IsActive\" AND \"IsPreferred\"");

            migrationBuilder.CreateIndex(
                name: "IX_sourcing_prices_CurrencyId",
                schema: "procurement",
                table: "sourcing_prices",
                column: "CurrencyId");

            migrationBuilder.CreateIndex(
                name: "IX_sourcing_prices_SourcingOptionId_EffectiveFrom_EffectiveTo",
                schema: "procurement",
                table: "sourcing_prices",
                columns: new[] { "SourcingOptionId", "EffectiveFrom", "EffectiveTo" });

            migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS btree_gist;");
            migrationBuilder.Sql("""
                ALTER TABLE procurement.sourcing_prices
                ADD CONSTRAINT ex_sourcing_prices_no_overlap
                EXCLUDE USING gist
                ("SourcingOptionId" WITH =,
                 daterange("EffectiveFrom", COALESCE("EffectiveTo", 'infinity'::date), '[]') WITH &&);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "sourcing_prices",
                schema: "procurement");

            migrationBuilder.DropTable(
                name: "sourcing_options",
                schema: "procurement");

            migrationBuilder.DropSequence(
                name: "sourcing_option_number_seq",
                schema: "procurement");
        }
    }
}
