using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hadron.Erp.Api.Infrastructure.Migrations;

[DbContext(typeof(HadronDbContext))]
[Migration("20260905120000_AllowControlledDraftBomDiscard")]
public partial class AllowControlledDraftBomDiscard : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            CREATE OR REPLACE FUNCTION engineering.guard_released_bom_revision() RETURNS trigger AS $$
            BEGIN
                IF TG_OP = 'INSERT' THEN
                    IF NEW."Status" <> 'DRAFT' OR NEW."EffectiveFrom" IS NOT NULL OR NEW."EffectiveTo" IS NOT NULL THEN
                        RAISE EXCEPTION 'New BOM revisions must be undated Drafts' USING ERRCODE = '23514';
                    END IF;
                    RETURN NEW;
                END IF;

                IF TG_OP = 'DELETE' THEN
                    IF OLD."Status" = 'DRAFT' THEN
                        RETURN OLD;
                    END IF;
                    RAISE EXCEPTION 'Released BOM revision hard delete is prohibited' USING ERRCODE = '23514';
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
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            CREATE OR REPLACE FUNCTION engineering.guard_released_bom_revision() RETURNS trigger AS $$
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
            """);
    }
}
