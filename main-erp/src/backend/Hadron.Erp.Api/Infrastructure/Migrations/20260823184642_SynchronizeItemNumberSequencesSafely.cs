using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hadron.Erp.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SynchronizeItemNumberSequencesSafely : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                SELECT setval(
                    'catalog.product_number_seq',
                    GREATEST(
                        COALESCE(MAX(substring("BusinessNumber" FROM '[0-9]+$')::bigint), 0) + 1,
                        (SELECT CASE WHEN is_called THEN last_value + 1 ELSE last_value END
                         FROM catalog.product_number_seq)),
                    false)
                FROM catalog.items
                WHERE "ItemType" = 'PRODUCT';

                SELECT setval(
                    'catalog.part_number_seq',
                    GREATEST(
                        COALESCE(MAX(substring("BusinessNumber" FROM '[0-9]+$')::bigint), 0) + 1,
                        (SELECT CASE WHEN is_called THEN last_value + 1 ELSE last_value END
                         FROM catalog.part_number_seq)),
                    false)
                FROM catalog.items
                WHERE "ItemType" = 'PART';

                SELECT setval(
                    'catalog.component_number_seq',
                    GREATEST(
                        COALESCE(MAX(substring("BusinessNumber" FROM '[0-9]+$')::bigint), 0) + 1,
                        (SELECT CASE WHEN is_called THEN last_value + 1 ELSE last_value END
                         FROM catalog.component_number_seq)),
                    false)
                FROM catalog.items
                WHERE "ItemType" = 'COMPONENT';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Sequence advancement is intentionally irreversible: moving a sequence
            // backward could reuse an already-issued official Item code.
        }
    }
}
