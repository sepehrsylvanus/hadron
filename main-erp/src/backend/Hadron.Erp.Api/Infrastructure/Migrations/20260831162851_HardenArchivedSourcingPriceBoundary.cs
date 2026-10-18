using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hadron.Erp.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class HardenArchivedSourcingPriceBoundary : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION procurement.guard_archived_sourcing_price_link()
                RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                  IF NEW."SourcingPriceId" IS DISTINCT FROM OLD."SourcingPriceId" THEN
                    RAISE EXCEPTION 'SOURCING_PRICE_IS_ARCHIVE_ONLY' USING ERRCODE='23514';
                  END IF;
                  RETURN NEW;
                END $$;

                DROP TRIGGER IF EXISTS trg_guard_archived_sourcing_price_link
                  ON procurement.purchase_order_lines;
                CREATE TRIGGER trg_guard_archived_sourcing_price_link
                  BEFORE UPDATE OF "SourcingPriceId" ON procurement.purchase_order_lines
                  FOR EACH ROW EXECUTE FUNCTION procurement.guard_archived_sourcing_price_link();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER IF EXISTS trg_guard_archived_sourcing_price_link
                  ON procurement.purchase_order_lines;
                DROP FUNCTION IF EXISTS procurement.guard_archived_sourcing_price_link();
                """);
        }
    }
}
