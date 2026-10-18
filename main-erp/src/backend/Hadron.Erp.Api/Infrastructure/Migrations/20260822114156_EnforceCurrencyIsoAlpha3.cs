using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hadron.Erp.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class EnforceCurrencyIsoAlpha3 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddCheckConstraint(
                name: "ck_currencies_iso_alpha3",
                schema: "reference_data",
                table: "currencies",
                sql: "\"Code\" ~ '^[A-Z]{3}$' AND \"NormalizedCode\" = \"Code\"");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_currencies_iso_alpha3",
                schema: "reference_data",
                table: "currencies");
        }
    }
}
