using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hadron.Erp.Api.Infrastructure.Migrations;

[DbContext(typeof(HadronDbContext))]
[Migration("20260824120500_EnforceDailyActualPeriodDate")]
public partial class EnforceDailyActualPeriodDate : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            CREATE FUNCTION planning.validate_daily_actual_period_date()
            RETURNS trigger LANGUAGE plpgsql AS $function$
            BEGIN
                IF NOT EXISTS (
                    SELECT 1
                    FROM planning.business_periods p
                    WHERE p."Id" = NEW."BusinessPeriodId"
                      AND NEW."ProductionDate" BETWEEN p."StartDate" AND p."EndDate"
                ) THEN
                    RAISE EXCEPTION 'ProductionDate must belong to the referenced BusinessPeriod'
                        USING ERRCODE = '23514', CONSTRAINT = 'ck_daily_actual_period_date';
                END IF;
                RETURN NEW;
            END;
            $function$;

            CREATE TRIGGER trg_daily_actual_period_date
            BEFORE INSERT OR UPDATE OF "BusinessPeriodId", "ProductionDate"
            ON planning.daily_actuals
            FOR EACH ROW EXECUTE FUNCTION planning.validate_daily_actual_period_date();
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DROP TRIGGER IF EXISTS trg_daily_actual_period_date ON planning.daily_actuals;
            DROP FUNCTION IF EXISTS planning.validate_daily_actual_period_date();
            """);
    }
}
