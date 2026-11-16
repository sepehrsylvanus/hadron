using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hadron.Erp.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class HardenMrpRunImmutabilityGuards : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION planning.guard_mrp_run_mutation()
                RETURNS trigger LANGUAGE plpgsql AS $function$
                BEGIN
                    IF TG_OP = 'DELETE' THEN
                        RAISE EXCEPTION 'MRP_RUN_IMMUTABLE';
                    END IF;
                    IF TG_OP = 'INSERT' THEN
                        IF NEW."Status" <> 'RUNNING' THEN RAISE EXCEPTION 'MRP_RUN_MUST_START_RUNNING'; END IF;
                        RETURN NEW;
                    END IF;
                    IF OLD."Status" <> 'RUNNING' THEN RAISE EXCEPTION 'MRP_RUN_IMMUTABLE'; END IF;
                    IF NEW."Id" <> OLD."Id" OR NEW."RunNumber" <> OLD."RunNumber"
                       OR NEW."AsOfDate" <> OLD."AsOfDate" OR NEW."HorizonEndDate" <> OLD."HorizonEndDate"
                       OR NEW."EngineVersion" <> OLD."EngineVersion" OR NEW."StartedAtUtc" <> OLD."StartedAtUtc"
                       OR NEW."ActorIdentifier" IS DISTINCT FROM OLD."ActorIdentifier"
                       OR NEW."Assumptions" <> OLD."Assumptions" THEN
                        RAISE EXCEPTION 'MRP_RUN_IDENTITY_IMMUTABLE';
                    END IF;
                    IF OLD."SnapshotCapturedAtUtc" IS NOT NULL
                       AND NEW."SnapshotCapturedAtUtc" IS DISTINCT FROM OLD."SnapshotCapturedAtUtc" THEN
                        RAISE EXCEPTION 'MRP_RUN_SNAPSHOT_TIME_IMMUTABLE';
                    END IF;
                    IF NEW."Status" = 'RUNNING' THEN
                        IF NEW."CompletedAtUtc" IS NOT NULL OR NEW."FailureCode" IS NOT NULL OR NEW."FailureMessage" IS NOT NULL
                           OR NEW."Completeness" <> 'COMPLETE' THEN RAISE EXCEPTION 'MRP_RUN_RUNNING_SHAPE_INVALID'; END IF;
                        RETURN NEW;
                    END IF;
                    IF NEW."Status" NOT IN ('COMPLETED','COMPLETED_WITH_EXCEPTIONS','FAILED') OR NEW."CompletedAtUtc" IS NULL
                       THEN RAISE EXCEPTION 'MRP_RUN_TERMINAL_SHAPE_INVALID'; END IF;
                    IF NEW."Status" IN ('COMPLETED','COMPLETED_WITH_EXCEPTIONS')
                       AND (NEW."SnapshotCapturedAtUtc" IS NULL OR NEW."FailureCode" IS NOT NULL OR NEW."FailureMessage" IS NOT NULL)
                       THEN RAISE EXCEPTION 'MRP_RUN_COMPLETION_SHAPE_INVALID'; END IF;
                    IF NEW."Status" = 'FAILED' AND NEW."FailureCode" IS NULL
                       THEN RAISE EXCEPTION 'MRP_RUN_FAILURE_SHAPE_INVALID'; END IF;
                    RETURN NEW;
                END $function$;

                CREATE OR REPLACE FUNCTION planning.guard_mrp_run_child()
                RETURNS trigger LANGUAGE plpgsql AS $function$
                DECLARE run_status text;
                BEGIN
                    IF TG_OP IN ('UPDATE','DELETE') THEN RAISE EXCEPTION 'MRP_RUN_CHILD_IMMUTABLE'; END IF;
                    SELECT r."Status" INTO run_status
                    FROM planning.mrp_runs r
                    WHERE r."Id" = NEW."RunId"
                    FOR UPDATE;
                    IF run_status IS DISTINCT FROM 'RUNNING' THEN RAISE EXCEPTION 'MRP_RUN_NOT_RUNNING'; END IF;
                    RETURN NEW;
                END $function$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION planning.guard_mrp_run_mutation()
                RETURNS trigger LANGUAGE plpgsql AS $function$
                BEGIN
                    IF TG_OP = 'DELETE' THEN RAISE EXCEPTION 'MRP_RUN_IMMUTABLE'; END IF;
                    IF TG_OP = 'INSERT' THEN
                        IF NEW."Status" <> 'RUNNING' THEN RAISE EXCEPTION 'MRP_RUN_MUST_START_RUNNING'; END IF;
                        RETURN NEW;
                    END IF;
                    IF OLD."Status" <> 'RUNNING' THEN RAISE EXCEPTION 'MRP_RUN_IMMUTABLE'; END IF;
                    IF NEW."Id" <> OLD."Id" OR NEW."RunNumber" <> OLD."RunNumber"
                       OR NEW."AsOfDate" <> OLD."AsOfDate" OR NEW."HorizonEndDate" <> OLD."HorizonEndDate"
                       OR NEW."EngineVersion" <> OLD."EngineVersion" OR NEW."StartedAtUtc" <> OLD."StartedAtUtc"
                       OR NEW."ActorIdentifier" IS DISTINCT FROM OLD."ActorIdentifier"
                       OR NEW."Assumptions" <> OLD."Assumptions" THEN RAISE EXCEPTION 'MRP_RUN_IDENTITY_IMMUTABLE'; END IF;
                    IF NEW."Status" = 'RUNNING' THEN
                        IF NEW."CompletedAtUtc" IS NOT NULL OR NEW."FailureCode" IS NOT NULL OR NEW."FailureMessage" IS NOT NULL
                           OR NEW."Completeness" <> 'COMPLETE' THEN RAISE EXCEPTION 'MRP_RUN_RUNNING_SHAPE_INVALID'; END IF;
                        IF OLD."SnapshotCapturedAtUtc" IS NOT NULL AND NEW."SnapshotCapturedAtUtc" IS DISTINCT FROM OLD."SnapshotCapturedAtUtc"
                           THEN RAISE EXCEPTION 'MRP_RUN_SNAPSHOT_TIME_IMMUTABLE'; END IF;
                        RETURN NEW;
                    END IF;
                    IF NEW."Status" NOT IN ('COMPLETED','COMPLETED_WITH_EXCEPTIONS','FAILED') OR NEW."CompletedAtUtc" IS NULL
                       THEN RAISE EXCEPTION 'MRP_RUN_TERMINAL_SHAPE_INVALID'; END IF;
                    RETURN NEW;
                END $function$;

                CREATE OR REPLACE FUNCTION planning.guard_mrp_run_child()
                RETURNS trigger LANGUAGE plpgsql AS $function$
                BEGIN
                    IF TG_OP IN ('UPDATE','DELETE') THEN RAISE EXCEPTION 'MRP_RUN_CHILD_IMMUTABLE'; END IF;
                    IF NOT EXISTS (SELECT 1 FROM planning.mrp_runs r WHERE r."Id" = NEW."RunId" AND r."Status" = 'RUNNING')
                       THEN RAISE EXCEPTION 'MRP_RUN_NOT_RUNNING'; END IF;
                    RETURN NEW;
                END $function$;
                """);
        }
    }
}
