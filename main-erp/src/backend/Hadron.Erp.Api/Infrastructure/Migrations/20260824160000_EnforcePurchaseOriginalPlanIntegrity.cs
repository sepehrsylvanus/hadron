using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hadron.Erp.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class EnforcePurchaseOriginalPlanIntegrity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                  IF EXISTS (
                    SELECT 1
                    FROM procurement.delivery_schedule_plan_revisions
                    GROUP BY "DeliveryScheduleId"
                    HAVING COUNT(*) FILTER (WHERE "PlanType" = 'ORIGINAL') > 1
                  ) OR EXISTS (
                    SELECT 1
                    FROM procurement.delivery_schedule_plan_revisions
                    WHERE ("PlanType" = 'ORIGINAL' AND "RevisionNumber" <> 1)
                       OR ("PlanType" = 'REVISED' AND "RevisionNumber" = 1)
                  ) THEN
                    RAISE EXCEPTION 'INVALID_EXISTING_PURCHASE_PLAN_REVISION_DATA';
                  END IF;
                END $$;
                """);

            migrationBuilder.CreateIndex(
                name: "ux_delivery_schedule_single_original",
                schema: "procurement",
                table: "delivery_schedule_plan_revisions",
                column: "DeliveryScheduleId",
                unique: true,
                filter: "\"PlanType\" = 'ORIGINAL'");

            migrationBuilder.AddCheckConstraint(
                name: "ck_delivery_plan_revision_kind",
                schema: "procurement",
                table: "delivery_schedule_plan_revisions",
                sql: "(\"PlanType\" = 'ORIGINAL' AND \"RevisionNumber\" = 1) OR (\"PlanType\" = 'REVISED' AND \"RevisionNumber\" <> 1)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_delivery_schedule_single_original",
                schema: "procurement",
                table: "delivery_schedule_plan_revisions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_delivery_plan_revision_kind",
                schema: "procurement",
                table: "delivery_schedule_plan_revisions");
        }
    }
}
