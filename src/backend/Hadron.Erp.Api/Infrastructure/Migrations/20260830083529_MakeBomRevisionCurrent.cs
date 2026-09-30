using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hadron.Erp.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class MakeBomRevisionCurrent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF EXISTS (
                        SELECT 1
                        FROM engineering.bom_revisions
                        WHERE "Status" = 'APPROVED'
                        GROUP BY "ParentItemId"
                        HAVING COUNT(*) > 1
                    ) THEN
                        RAISE EXCEPTION 'Cannot enable current BOM invariant: more than one APPROVED revision exists for a Parent';
                    END IF;
                END;
                $$;

                DROP TRIGGER IF EXISTS trg_guard_released_bom_revision ON engineering.bom_revisions;
                DROP FUNCTION IF EXISTS engineering.guard_released_bom_revision();
                ALTER TABLE engineering.bom_revisions
                    DROP CONSTRAINT IF EXISTS ex_bom_revisions_released_effectivity;
                """);

            migrationBuilder.DropIndex(
                name: "IX_bom_revisions_ParentItemId_Status_EffectiveFrom_EffectiveTo",
                schema: "engineering",
                table: "bom_revisions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_bom_revisions_dates",
                schema: "engineering",
                table: "bom_revisions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_bom_revisions_released_from",
                schema: "engineering",
                table: "bom_revisions");

            migrationBuilder.CreateIndex(
                name: "IX_bom_revisions_ParentItemId_Status",
                schema: "engineering",
                table: "bom_revisions",
                columns: new[] { "ParentItemId", "Status" });

            migrationBuilder.CreateIndex(
                name: "UX_bom_revisions_one_approved_parent",
                schema: "engineering",
                table: "bom_revisions",
                column: "ParentItemId",
                unique: true,
                filter: "\"Status\" = 'APPROVED'");

            migrationBuilder.Sql("""
                CREATE FUNCTION engineering.guard_released_bom_revision() RETURNS trigger AS $$
                BEGIN
                    IF TG_OP = 'INSERT' THEN
                        IF NEW."Status" <> 'DRAFT' OR NEW."EffectiveFrom" IS NOT NULL OR NEW."EffectiveTo" IS NOT NULL THEN
                            RAISE EXCEPTION 'New BOM revisions must be undated Drafts' USING ERRCODE = '23514';
                        END IF;
                        RETURN NEW;
                    END IF;

                    IF TG_OP = 'DELETE' THEN
                        RAISE EXCEPTION 'BOM revision hard delete is prohibited' USING ERRCODE = '23514';
                    END IF;

                    IF (NEW."RevisionCode", NEW."ParentItemId", NEW."ParentBaseUnitId", NEW."CopiedFromRevisionId", NEW."CreatedAtUtc", NEW."EffectiveFrom", NEW."EffectiveTo") IS DISTINCT FROM
                       (OLD."RevisionCode", OLD."ParentItemId", OLD."ParentBaseUnitId", OLD."CopiedFromRevisionId", OLD."CreatedAtUtc", OLD."EffectiveFrom", OLD."EffectiveTo") THEN
                        RAISE EXCEPTION 'BOM identity, unit snapshot, and legacy effectivity history are immutable' USING ERRCODE = '23514';
                    END IF;

                    IF OLD."Status" = 'DRAFT' AND NEW."Status" = 'DRAFT' THEN
                        RETURN NEW;
                    END IF;

                    IF OLD."Status" = 'DRAFT' AND NEW."Status" = 'APPROVED' AND
                       (to_jsonb(OLD) - ARRAY['Status','Version','UpdatedAtUtc','ApprovedAtUtc','ApprovedByActorId']) =
                       (to_jsonb(NEW) - ARRAY['Status','Version','UpdatedAtUtc','ApprovedAtUtc','ApprovedByActorId']) AND
                       NEW."ApprovedAtUtc" IS NOT NULL AND NEW."ObsoletedAtUtc" IS NULL AND
                       NEW."Version" = OLD."Version" + 1 AND
                       EXISTS (SELECT 1 FROM engineering.bom_lines WHERE "BomRevisionId" = NEW."Id") THEN
                        RETURN NEW;
                    END IF;

                    IF OLD."Status" = 'APPROVED' AND NEW."Status" = 'OBSOLETE' AND
                       (to_jsonb(OLD) - ARRAY['Status','Version','UpdatedAtUtc','ObsoletedAtUtc','ObsoletedByActorId']) =
                       (to_jsonb(NEW) - ARRAY['Status','Version','UpdatedAtUtc','ObsoletedAtUtc','ObsoletedByActorId']) AND
                       NEW."ObsoletedAtUtc" IS NOT NULL AND NEW."Version" = OLD."Version" + 1 THEN
                        RETURN NEW;
                    END IF;

                    RAISE EXCEPTION 'Released BOM revision is immutable or lifecycle transition is invalid' USING ERRCODE = '23514';
                END;
                $$ LANGUAGE plpgsql;

                CREATE TRIGGER trg_guard_released_bom_revision
                BEFORE INSERT OR UPDATE OR DELETE ON engineering.bom_revisions
                FOR EACH ROW EXECUTE FUNCTION engineering.guard_released_bom_revision();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF EXISTS (
                        SELECT 1 FROM engineering.bom_revisions
                        WHERE "Status" IN ('APPROVED','OBSOLETE') AND "EffectiveFrom" IS NULL
                    ) THEN
                        RAISE EXCEPTION 'Safe rollback requires explicit legacy effectivity for every released BOM; no dates were invented';
                    END IF;
                END;
                $$;

                DROP TRIGGER IF EXISTS trg_guard_released_bom_revision ON engineering.bom_revisions;
                DROP FUNCTION IF EXISTS engineering.guard_released_bom_revision();
                """);

            migrationBuilder.DropIndex(
                name: "IX_bom_revisions_ParentItemId_Status",
                schema: "engineering",
                table: "bom_revisions");

            migrationBuilder.DropIndex(
                name: "UX_bom_revisions_one_approved_parent",
                schema: "engineering",
                table: "bom_revisions");

            migrationBuilder.CreateIndex(
                name: "IX_bom_revisions_ParentItemId_Status_EffectiveFrom_EffectiveTo",
                schema: "engineering",
                table: "bom_revisions",
                columns: new[] { "ParentItemId", "Status", "EffectiveFrom", "EffectiveTo" });

            migrationBuilder.AddCheckConstraint(
                name: "ck_bom_revisions_dates",
                schema: "engineering",
                table: "bom_revisions",
                sql: "\"EffectiveTo\" IS NULL OR \"EffectiveFrom\" IS NULL OR \"EffectiveTo\" >= \"EffectiveFrom\"");

            migrationBuilder.AddCheckConstraint(
                name: "ck_bom_revisions_released_from",
                schema: "engineering",
                table: "bom_revisions",
                sql: "\"Status\" = 'DRAFT' OR \"EffectiveFrom\" IS NOT NULL");

            migrationBuilder.Sql("""
                ALTER TABLE engineering.bom_revisions
                ADD CONSTRAINT ex_bom_revisions_released_effectivity
                EXCLUDE USING gist
                ("ParentItemId" WITH =, daterange("EffectiveFrom", "EffectiveTo", '[]') WITH &&)
                WHERE ("Status" IN ('APPROVED', 'OBSOLETE'))
                DEFERRABLE INITIALLY DEFERRED;

                CREATE FUNCTION engineering.guard_released_bom_revision() RETURNS trigger AS $$
                BEGIN
                    IF TG_OP = 'DELETE' THEN
                        RAISE EXCEPTION 'BOM revision hard delete is prohibited' USING ERRCODE = '23514';
                    END IF;
                    IF (NEW."RevisionCode", NEW."ParentItemId", NEW."ParentBaseUnitId", NEW."CopiedFromRevisionId", NEW."CreatedAtUtc") IS DISTINCT FROM
                       (OLD."RevisionCode", OLD."ParentItemId", OLD."ParentBaseUnitId", OLD."CopiedFromRevisionId", OLD."CreatedAtUtc") THEN
                        RAISE EXCEPTION 'BOM revision identity and unit snapshot are immutable' USING ERRCODE = '23514';
                    END IF;
                    IF OLD."Status" = 'DRAFT' AND NEW."Status" = 'DRAFT' THEN RETURN NEW; END IF;
                    IF OLD."Status" = 'DRAFT' AND NEW."Status" = 'APPROVED' AND
                       (to_jsonb(OLD) - ARRAY['Status','Version','UpdatedAtUtc','ApprovedAtUtc','ApprovedByActorId']) =
                       (to_jsonb(NEW) - ARRAY['Status','Version','UpdatedAtUtc','ApprovedAtUtc','ApprovedByActorId']) AND
                       NEW."EffectiveFrom" IS NOT NULL AND NEW."ApprovedAtUtc" IS NOT NULL AND
                       NEW."Version" = OLD."Version" + 1 AND
                       EXISTS (SELECT 1 FROM engineering.bom_lines WHERE "BomRevisionId" = NEW."Id") THEN RETURN NEW; END IF;
                    IF OLD."Status" = 'APPROVED' AND NEW."Status" = 'APPROVED' AND
                       (to_jsonb(OLD) - ARRAY['EffectiveTo','Version','UpdatedAtUtc']) =
                       (to_jsonb(NEW) - ARRAY['EffectiveTo','Version','UpdatedAtUtc']) AND
                       NEW."EffectiveTo" IS NOT NULL AND NEW."EffectiveTo" IS DISTINCT FROM OLD."EffectiveTo" AND
                       (OLD."EffectiveTo" IS NULL OR NEW."EffectiveTo" < OLD."EffectiveTo") AND
                       NEW."Version" = OLD."Version" + 1 THEN RETURN NEW; END IF;
                    IF OLD."Status" = 'APPROVED' AND NEW."Status" = 'OBSOLETE' AND
                       (to_jsonb(OLD) - ARRAY['Status','EffectiveTo','Version','UpdatedAtUtc','ObsoletedAtUtc','ObsoletedByActorId']) =
                       (to_jsonb(NEW) - ARRAY['Status','EffectiveTo','Version','UpdatedAtUtc','ObsoletedAtUtc','ObsoletedByActorId']) AND
                       NEW."EffectiveTo" IS NOT NULL AND (OLD."EffectiveTo" IS NULL OR NEW."EffectiveTo" <= OLD."EffectiveTo") AND
                       NEW."ObsoletedAtUtc" IS NOT NULL AND NEW."Version" = OLD."Version" + 1 THEN RETURN NEW; END IF;
                    RAISE EXCEPTION 'Released BOM revision is immutable' USING ERRCODE = '23514';
                END;
                $$ LANGUAGE plpgsql;

                CREATE TRIGGER trg_guard_released_bom_revision
                BEFORE UPDATE OR DELETE ON engineering.bom_revisions
                FOR EACH ROW EXECUTE FUNCTION engineering.guard_released_bom_revision();
                """);
        }
    }
}
