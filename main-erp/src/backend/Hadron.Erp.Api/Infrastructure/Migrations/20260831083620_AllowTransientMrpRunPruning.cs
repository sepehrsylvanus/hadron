using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hadron.Erp.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AllowTransientMrpRunPruning : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION planning.guard_mrp_resolved_route_snapshot()
                RETURNS trigger LANGUAGE plpgsql AS $body$
                DECLARE run_status text; run_purpose text; child_run_id uuid;
                BEGIN
                    child_run_id := CASE WHEN TG_OP = 'DELETE' THEN OLD."RunId" ELSE NEW."RunId" END;
                    SELECT r."Status", r."Purpose" INTO run_status, run_purpose
                    FROM planning.mrp_runs r WHERE r."Id" = child_run_id FOR UPDATE;
                    IF TG_OP = 'DELETE' THEN
                        IF run_purpose NOT IN ('CURRENT','CERTIFIED','LEGACY')
                           OR EXISTS (SELECT 1 FROM planning.mrp_plan_slots s WHERE s."RunId" = child_run_id) THEN
                            RAISE EXCEPTION 'MRP resolved route snapshots are immutable.' USING ERRCODE = '23514';
                        END IF;
                        RETURN OLD;
                    END IF;
                    IF TG_OP = 'UPDATE' THEN
                        RAISE EXCEPTION 'MRP resolved route snapshots are immutable.' USING ERRCODE = '23514';
                    END IF;
                    IF run_status IS DISTINCT FROM 'RUNNING' THEN
                        RAISE EXCEPTION 'Resolved route snapshots may only be added while the MRP Run is RUNNING.' USING ERRCODE = '23514';
                    END IF;
                    RETURN NEW;
                END $body$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION planning.guard_mrp_resolved_route_snapshot()
                RETURNS trigger LANGUAGE plpgsql AS $body$
                BEGIN
                    IF TG_OP <> 'INSERT' THEN
                        RAISE EXCEPTION 'MRP resolved route snapshots are immutable.' USING ERRCODE = '23514';
                    END IF;
                    IF NOT EXISTS (SELECT 1 FROM planning.mrp_runs r WHERE r."Id" = NEW."RunId" AND r."Status" = 'RUNNING') THEN
                        RAISE EXCEPTION 'Resolved route snapshots may only be added while the MRP Run is RUNNING.' USING ERRCODE = '23514';
                    END IF;
                    RETURN NEW;
                END $body$;
                """);
        }
    }
}
