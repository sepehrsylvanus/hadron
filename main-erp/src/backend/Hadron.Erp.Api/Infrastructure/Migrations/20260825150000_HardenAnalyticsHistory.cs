using Hadron.Erp.Api.Infrastructure;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hadron.Erp.Api.Infrastructure.Migrations;

[DbContext(typeof(HadronDbContext))]
[Migration("20260825150000_HardenAnalyticsHistory")]
public sealed class HardenAnalyticsHistory : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            CREATE OR REPLACE FUNCTION procurement.reject_lead_time_application_mutation()
            RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN
              RAISE EXCEPTION 'LEAD_TIME_APPLICATION_IMMUTABLE' USING ERRCODE = '23514';
            END;
            $$;

            CREATE TRIGGER tr_lead_time_application_immutable
            BEFORE UPDATE OR DELETE ON procurement.lead_time_recommendation_applications
            FOR EACH ROW EXECUTE FUNCTION procurement.reject_lead_time_application_mutation();
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DROP TRIGGER IF EXISTS tr_lead_time_application_immutable
              ON procurement.lead_time_recommendation_applications;
            DROP FUNCTION IF EXISTS procurement.reject_lead_time_application_mutation();
            """);
    }
}
