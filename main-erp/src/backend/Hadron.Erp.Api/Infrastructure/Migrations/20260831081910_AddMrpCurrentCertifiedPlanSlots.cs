using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hadron.Erp.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMrpCurrentCertifiedPlanSlots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_mrp_refresh_outbox_mrp_runs_RunId",
                schema: "planning",
                table: "mrp_refresh_outbox");

            migrationBuilder.AddColumn<string>(
                name: "Purpose",
                schema: "planning",
                table: "mrp_runs",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "LEGACY");

            migrationBuilder.CreateTable(
                name: "mrp_daily_certificates",
                schema: "planning",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BusinessDate = table.Column<DateOnly>(type: "date", nullable: false),
                    SourceRunId = table.Column<Guid>(type: "uuid", nullable: false),
                    RunNumber = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    AsOfDate = table.Column<DateOnly>(type: "date", nullable: false),
                    HorizonEndDate = table.Column<DateOnly>(type: "date", nullable: false),
                    SourceCutoffUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CertifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EngineVersion = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Completeness = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    ItemCount = table.Column<int>(type: "integer", nullable: false),
                    DailyResultCount = table.Column<int>(type: "integer", nullable: false),
                    ExceptionCount = table.Column<int>(type: "integer", nullable: false),
                    ResultHash = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_mrp_daily_certificates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "mrp_plan_slots",
                schema: "planning",
                columns: table => new
                {
                    SlotName = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    RunId = table.Column<Guid>(type: "uuid", nullable: false),
                    BusinessDate = table.Column<DateOnly>(type: "date", nullable: false),
                    SourceCutoffUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ActivatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_mrp_plan_slots", x => x.SlotName);
                    table.CheckConstraint("ck_mrp_plan_slots_name", "\"SlotName\" IN ('CURRENT','CERTIFIED')");
                    table.ForeignKey(
                        name: "FK_mrp_plan_slots_mrp_runs_RunId",
                        column: x => x.RunId,
                        principalSchema: "planning",
                        principalTable: "mrp_runs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_mrp_runs_purpose",
                schema: "planning",
                table: "mrp_runs",
                sql: "\"Purpose\" IN ('CURRENT','CERTIFIED','PUBLISHED','LEGACY')");

            migrationBuilder.CreateIndex(
                name: "IX_mrp_daily_certificates_BusinessDate",
                schema: "planning",
                table: "mrp_daily_certificates",
                column: "BusinessDate",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_mrp_daily_certificates_CertifiedAtUtc",
                schema: "planning",
                table: "mrp_daily_certificates",
                column: "CertifiedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_mrp_plan_slots_RunId",
                schema: "planning",
                table: "mrp_plan_slots",
                column: "RunId");

            migrationBuilder.AddForeignKey(
                name: "FK_mrp_refresh_outbox_mrp_runs_RunId",
                schema: "planning",
                table: "mrp_refresh_outbox",
                column: "RunId",
                principalSchema: "planning",
                principalTable: "mrp_runs",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION planning.guard_mrp_run_mutation()
                RETURNS trigger LANGUAGE plpgsql AS $function$
                BEGIN
                    IF TG_OP = 'DELETE' THEN
                        IF OLD."Purpose" NOT IN ('CURRENT','CERTIFIED','LEGACY')
                           OR EXISTS (SELECT 1 FROM planning.mrp_plan_slots s WHERE s."RunId" = OLD."Id") THEN
                            RAISE EXCEPTION 'MRP_RUN_IMMUTABLE';
                        END IF;
                        RETURN OLD;
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
                       OR NEW."Assumptions" <> OLD."Assumptions" OR NEW."Purpose" <> OLD."Purpose" THEN
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
                DECLARE run_status text; run_purpose text; child_run_id uuid;
                BEGIN
                    child_run_id := CASE WHEN TG_OP = 'DELETE' THEN OLD."RunId" ELSE NEW."RunId" END;
                    IF TG_OP = 'UPDATE' THEN RAISE EXCEPTION 'MRP_RUN_CHILD_IMMUTABLE'; END IF;
                    SELECT r."Status", r."Purpose" INTO run_status, run_purpose
                    FROM planning.mrp_runs r WHERE r."Id" = child_run_id FOR UPDATE;
                    IF TG_OP = 'DELETE' THEN
                        IF run_purpose NOT IN ('CURRENT','CERTIFIED','LEGACY')
                           OR EXISTS (SELECT 1 FROM planning.mrp_plan_slots s WHERE s."RunId" = child_run_id) THEN
                            RAISE EXCEPTION 'MRP_RUN_CHILD_IMMUTABLE';
                        END IF;
                        RETURN OLD;
                    END IF;
                    IF run_status IS DISTINCT FROM 'RUNNING' THEN RAISE EXCEPTION 'MRP_RUN_NOT_RUNNING'; END IF;
                    RETURN NEW;
                END $function$;

                CREATE OR REPLACE FUNCTION planning.guard_mrp_plan_slot()
                RETURNS trigger LANGUAGE plpgsql AS $function$
                BEGIN
                    PERFORM pg_advisory_xact_lock(hashtextextended('hadron:planning:mrp-plan-slots:v1', 0));
                    IF TG_OP = 'DELETE' THEN RAISE EXCEPTION 'MRP_PLAN_SLOT_DELETE_FORBIDDEN'; END IF;
                    IF TG_OP = 'UPDATE' AND (NEW."SlotName" <> OLD."SlotName" OR NEW."Version" <> OLD."Version" + 1)
                       THEN RAISE EXCEPTION 'MRP_PLAN_SLOT_VERSION_INVALID'; END IF;
                    IF NOT EXISTS (SELECT 1 FROM planning.mrp_runs r WHERE r."Id" = NEW."RunId"
                        AND r."Status" IN ('COMPLETED','COMPLETED_WITH_EXCEPTIONS')
                        AND r."SnapshotCapturedAtUtc" IS NOT NULL AND r."CompletedAtUtc" IS NOT NULL)
                       THEN RAISE EXCEPTION 'MRP_PLAN_SLOT_RUN_INVALID'; END IF;
                    RETURN NEW;
                END $function$;
                CREATE TRIGGER trg_guard_mrp_plan_slot
                BEFORE INSERT OR UPDATE OR DELETE ON planning.mrp_plan_slots
                FOR EACH ROW EXECUTE FUNCTION planning.guard_mrp_plan_slot();

                CREATE OR REPLACE FUNCTION planning.guard_mrp_daily_certificate()
                RETURNS trigger LANGUAGE plpgsql AS $function$
                BEGIN
                    IF TG_OP IN ('UPDATE','DELETE') THEN RAISE EXCEPTION 'MRP_DAILY_CERTIFICATE_IMMUTABLE'; END IF;
                    IF NOT EXISTS (SELECT 1 FROM planning.mrp_runs r WHERE r."Id" = NEW."SourceRunId"
                        AND r."RunNumber" = NEW."RunNumber" AND r."AsOfDate" = NEW."BusinessDate"
                        AND r."Status" IN ('COMPLETED','COMPLETED_WITH_EXCEPTIONS'))
                       THEN RAISE EXCEPTION 'MRP_DAILY_CERTIFICATE_SOURCE_INVALID'; END IF;
                    RETURN NEW;
                END $function$;
                CREATE TRIGGER trg_guard_mrp_daily_certificate
                BEFORE INSERT OR UPDATE OR DELETE ON planning.mrp_daily_certificates
                FOR EACH ROW EXECUTE FUNCTION planning.guard_mrp_daily_certificate();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_mrp_refresh_outbox_mrp_runs_RunId",
                schema: "planning",
                table: "mrp_refresh_outbox");

            migrationBuilder.DropTable(
                name: "mrp_daily_certificates",
                schema: "planning");

            migrationBuilder.DropTable(
                name: "mrp_plan_slots",
                schema: "planning");

            migrationBuilder.DropCheckConstraint(
                name: "ck_mrp_runs_purpose",
                schema: "planning",
                table: "mrp_runs");

            migrationBuilder.DropColumn(
                name: "Purpose",
                schema: "planning",
                table: "mrp_runs");

            migrationBuilder.AddForeignKey(
                name: "FK_mrp_refresh_outbox_mrp_runs_RunId",
                schema: "planning",
                table: "mrp_refresh_outbox",
                column: "RunId",
                principalSchema: "planning",
                principalTable: "mrp_runs",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.Sql("""
                DROP FUNCTION IF EXISTS planning.guard_mrp_daily_certificate() CASCADE;
                DROP FUNCTION IF EXISTS planning.guard_mrp_plan_slot() CASCADE;
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
                    IF OLD."SnapshotCapturedAtUtc" IS NOT NULL AND NEW."SnapshotCapturedAtUtc" IS DISTINCT FROM OLD."SnapshotCapturedAtUtc"
                       THEN RAISE EXCEPTION 'MRP_RUN_SNAPSHOT_TIME_IMMUTABLE'; END IF;
                    IF NEW."Status" = 'RUNNING' THEN
                        IF NEW."CompletedAtUtc" IS NOT NULL OR NEW."FailureCode" IS NOT NULL OR NEW."FailureMessage" IS NOT NULL
                           OR NEW."Completeness" <> 'COMPLETE' THEN RAISE EXCEPTION 'MRP_RUN_RUNNING_SHAPE_INVALID'; END IF;
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
