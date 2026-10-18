using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hadron.Erp.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class GenerateReferenceValueCodes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateSequence(
                name: "business_unit_value_code_seq",
                schema: "reference_data");

            migrationBuilder.CreateSequence(
                name: "component_category_value_code_seq",
                schema: "reference_data");

            migrationBuilder.CreateSequence(
                name: "country_value_code_seq",
                schema: "reference_data");

            migrationBuilder.CreateSequence(
                name: "incoterm_value_code_seq",
                schema: "reference_data");

            migrationBuilder.CreateSequence(
                name: "part_category_value_code_seq",
                schema: "reference_data");

            migrationBuilder.CreateSequence(
                name: "payment_term_value_code_seq",
                schema: "reference_data");

            migrationBuilder.CreateSequence(
                name: "product_category_value_code_seq",
                schema: "reference_data");

            migrationBuilder.CreateSequence(
                name: "shipping_mode_value_code_seq",
                schema: "reference_data");

            Synchronize(migrationBuilder, "business_unit_value_code_seq", "BUSINESS_UNIT");
            Synchronize(migrationBuilder, "component_category_value_code_seq", "COMPONENT_CATEGORY");
            Synchronize(migrationBuilder, "country_value_code_seq", "COUNTRY");
            Synchronize(migrationBuilder, "incoterm_value_code_seq", "INCOTERM");
            Synchronize(migrationBuilder, "part_category_value_code_seq", "PART_CATEGORY");
            Synchronize(migrationBuilder, "payment_term_value_code_seq", "PAYMENT_TERM");
            Synchronize(migrationBuilder, "product_category_value_code_seq", "PRODUCT_CATEGORY");
            Synchronize(migrationBuilder, "shipping_mode_value_code_seq", "SHIPPING_MODE");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropSequence(
                name: "business_unit_value_code_seq",
                schema: "reference_data");

            migrationBuilder.DropSequence(
                name: "component_category_value_code_seq",
                schema: "reference_data");

            migrationBuilder.DropSequence(
                name: "country_value_code_seq",
                schema: "reference_data");

            migrationBuilder.DropSequence(
                name: "incoterm_value_code_seq",
                schema: "reference_data");

            migrationBuilder.DropSequence(
                name: "part_category_value_code_seq",
                schema: "reference_data");

            migrationBuilder.DropSequence(
                name: "payment_term_value_code_seq",
                schema: "reference_data");

            migrationBuilder.DropSequence(
                name: "product_category_value_code_seq",
                schema: "reference_data");

            migrationBuilder.DropSequence(
                name: "shipping_mode_value_code_seq",
                schema: "reference_data");
        }

        private static void Synchronize(MigrationBuilder migrationBuilder, string sequence, string typeCode)
        {
            migrationBuilder.Sql($$"""
                WITH safe_next AS (
                    SELECT GREATEST(
                        COALESCE((
                            SELECT MAX(value."Code"::bigint) + 1
                            FROM reference_data.reference_values AS value
                            INNER JOIN reference_data.reference_types AS type
                                ON type."Id" = value."ReferenceTypeId"
                            WHERE type."Code" = '{{typeCode}}'
                              AND value."Code" ~ '^[0-9]+$'
                              AND length(value."Code") <= 18
                        ), 1),
                        CASE WHEN sequence_state.is_called
                            THEN sequence_state.last_value + 1
                            ELSE sequence_state.last_value
                        END
                    ) AS value
                    FROM reference_data.{{sequence}} AS sequence_state
                )
                SELECT setval('reference_data.{{sequence}}', value, false)
                FROM safe_next;
                """);
        }
    }
}
