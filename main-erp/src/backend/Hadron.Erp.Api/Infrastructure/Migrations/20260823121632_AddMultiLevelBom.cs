using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hadron.Erp.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMultiLevelBom : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "engineering");

            migrationBuilder.CreateSequence(
                name: "bom_revision_number_seq",
                schema: "engineering");

            migrationBuilder.CreateTable(
                name: "bom_revisions",
                schema: "engineering",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RevisionCode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ParentItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    ParentBaseUnitId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    BaseOutputQty = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    EffectiveFrom = table.Column<DateOnly>(type: "date", nullable: true),
                    EffectiveTo = table.Column<DateOnly>(type: "date", nullable: true),
                    RevisionNotes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    CopiedFromRevisionId = table.Column<Guid>(type: "uuid", nullable: true),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    ApprovedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ApprovedByActorId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ObsoletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ObsoletedByActorId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_bom_revisions", x => x.Id);
                    table.CheckConstraint("ck_bom_revisions_base_output", "\"BaseOutputQty\" > 0");
                    table.CheckConstraint("ck_bom_revisions_dates", "\"EffectiveTo\" IS NULL OR \"EffectiveFrom\" IS NULL OR \"EffectiveTo\" >= \"EffectiveFrom\"");
                    table.CheckConstraint("ck_bom_revisions_released_from", "\"Status\" = 'DRAFT' OR \"EffectiveFrom\" IS NOT NULL");
                    table.CheckConstraint("ck_bom_revisions_status", "\"Status\" IN ('DRAFT','APPROVED','OBSOLETE')");
                    table.CheckConstraint("ck_bom_revisions_version", "\"Version\" >= 1");
                    table.ForeignKey(
                        name: "FK_bom_revisions_bom_revisions_CopiedFromRevisionId",
                        column: x => x.CopiedFromRevisionId,
                        principalSchema: "engineering",
                        principalTable: "bom_revisions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_bom_revisions_items_ParentItemId",
                        column: x => x.ParentItemId,
                        principalSchema: "catalog",
                        principalTable: "items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_bom_revisions_units_ParentBaseUnitId",
                        column: x => x.ParentBaseUnitId,
                        principalSchema: "reference_data",
                        principalTable: "units",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "bom_lines",
                schema: "engineering",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BomRevisionId = table.Column<Guid>(type: "uuid", nullable: false),
                    ChildItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    ChildBaseUnitId = table.Column<Guid>(type: "uuid", nullable: false),
                    UsageQty = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    ScrapOverridePercent = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: true),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_bom_lines", x => x.Id);
                    table.CheckConstraint("ck_bom_lines_scrap", "\"ScrapOverridePercent\" IS NULL OR \"ScrapOverridePercent\" BETWEEN 0 AND 100");
                    table.CheckConstraint("ck_bom_lines_sort", "\"SortOrder\" >= 0");
                    table.CheckConstraint("ck_bom_lines_usage", "\"UsageQty\" > 0");
                    table.ForeignKey(
                        name: "FK_bom_lines_bom_revisions_BomRevisionId",
                        column: x => x.BomRevisionId,
                        principalSchema: "engineering",
                        principalTable: "bom_revisions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_bom_lines_items_ChildItemId",
                        column: x => x.ChildItemId,
                        principalSchema: "catalog",
                        principalTable: "items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_bom_lines_units_ChildBaseUnitId",
                        column: x => x.ChildBaseUnitId,
                        principalSchema: "reference_data",
                        principalTable: "units",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_bom_lines_BomRevisionId_ChildItemId",
                schema: "engineering",
                table: "bom_lines",
                columns: new[] { "BomRevisionId", "ChildItemId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_bom_lines_ChildBaseUnitId",
                schema: "engineering",
                table: "bom_lines",
                column: "ChildBaseUnitId");

            migrationBuilder.CreateIndex(
                name: "IX_bom_lines_ChildItemId",
                schema: "engineering",
                table: "bom_lines",
                column: "ChildItemId");

            migrationBuilder.CreateIndex(
                name: "IX_bom_revisions_CopiedFromRevisionId",
                schema: "engineering",
                table: "bom_revisions",
                column: "CopiedFromRevisionId");

            migrationBuilder.CreateIndex(
                name: "IX_bom_revisions_ParentBaseUnitId",
                schema: "engineering",
                table: "bom_revisions",
                column: "ParentBaseUnitId");

            migrationBuilder.CreateIndex(
                name: "IX_bom_revisions_ParentItemId_Status_EffectiveFrom_EffectiveTo",
                schema: "engineering",
                table: "bom_revisions",
                columns: new[] { "ParentItemId", "Status", "EffectiveFrom", "EffectiveTo" });

            migrationBuilder.CreateIndex(
                name: "IX_bom_revisions_RevisionCode",
                schema: "engineering",
                table: "bom_revisions",
                column: "RevisionCode",
                unique: true);

            migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS btree_gist;");
            migrationBuilder.Sql("""
                ALTER TABLE engineering.bom_revisions
                ADD CONSTRAINT ck_bom_revisions_lifecycle_fields CHECK (
                    ("Status" = 'DRAFT' AND "ApprovedAtUtc" IS NULL AND "ObsoletedAtUtc" IS NULL)
                    OR
                    ("Status" = 'APPROVED' AND "ApprovedAtUtc" IS NOT NULL AND "ObsoletedAtUtc" IS NULL)
                    OR
                    ("Status" = 'OBSOLETE' AND "ApprovedAtUtc" IS NOT NULL AND "ObsoletedAtUtc" IS NOT NULL)
                );
                """);
            migrationBuilder.Sql("""
                ALTER TABLE engineering.bom_revisions
                ADD CONSTRAINT ex_bom_revisions_released_effectivity
                EXCLUDE USING gist
                ("ParentItemId" WITH =, daterange("EffectiveFrom", "EffectiveTo", '[]') WITH &&)
                WHERE ("Status" IN ('APPROVED', 'OBSOLETE'))
                DEFERRABLE INITIALLY DEFERRED;
                """);
            migrationBuilder.Sql("""
                CREATE FUNCTION engineering.guard_released_bom_lines() RETURNS trigger AS $$
                DECLARE revision_status text;
                BEGIN
                    IF TG_OP = 'UPDATE' AND
                       (NEW."BomRevisionId", NEW."ChildItemId", NEW."ChildBaseUnitId", NEW."CreatedAtUtc") IS DISTINCT FROM
                       (OLD."BomRevisionId", OLD."ChildItemId", OLD."ChildBaseUnitId", OLD."CreatedAtUtc") THEN
                        RAISE EXCEPTION 'BOM line identity and unit snapshot are immutable' USING ERRCODE = '23514';
                    END IF;
                    SELECT "Status" INTO revision_status
                    FROM engineering.bom_revisions
                    WHERE "Id" = COALESCE(NEW."BomRevisionId", OLD."BomRevisionId");
                    IF revision_status <> 'DRAFT' THEN
                        RAISE EXCEPTION 'Released BOM lines are immutable' USING ERRCODE = '23514';
                    END IF;
                    IF TG_OP = 'DELETE' THEN RETURN OLD; END IF;
                    RETURN NEW;
                END;
                $$ LANGUAGE plpgsql;

                CREATE TRIGGER trg_guard_released_bom_lines
                BEFORE INSERT OR UPDATE OR DELETE ON engineering.bom_lines
                FOR EACH ROW EXECUTE FUNCTION engineering.guard_released_bom_lines();
                """);
            migrationBuilder.Sql("""
                CREATE FUNCTION engineering.guard_released_bom_revision() RETURNS trigger AS $$
                BEGIN
                    IF TG_OP = 'DELETE' THEN
                        RAISE EXCEPTION 'BOM revision hard delete is prohibited' USING ERRCODE = '23514';
                    END IF;
                    IF (NEW."RevisionCode", NEW."ParentItemId", NEW."ParentBaseUnitId", NEW."CopiedFromRevisionId", NEW."CreatedAtUtc") IS DISTINCT FROM
                       (OLD."RevisionCode", OLD."ParentItemId", OLD."ParentBaseUnitId", OLD."CopiedFromRevisionId", OLD."CreatedAtUtc") THEN
                        RAISE EXCEPTION 'BOM revision identity and unit snapshot are immutable' USING ERRCODE = '23514';
                    END IF;
                    IF OLD."Status" = 'DRAFT' AND NEW."Status" = 'DRAFT' THEN
                        RETURN NEW;
                    END IF;
                    IF OLD."Status" = 'DRAFT' AND NEW."Status" = 'APPROVED' AND
                       (to_jsonb(OLD) - ARRAY['Status','Version','UpdatedAtUtc','ApprovedAtUtc','ApprovedByActorId']) =
                       (to_jsonb(NEW) - ARRAY['Status','Version','UpdatedAtUtc','ApprovedAtUtc','ApprovedByActorId']) AND
                       NEW."EffectiveFrom" IS NOT NULL AND NEW."ApprovedAtUtc" IS NOT NULL AND
                       NEW."Version" = OLD."Version" + 1 AND
                       EXISTS (SELECT 1 FROM engineering.bom_lines WHERE "BomRevisionId" = NEW."Id") THEN
                        RETURN NEW;
                    END IF;
                    IF OLD."Status" = 'APPROVED' AND NEW."Status" = 'APPROVED' AND
                       (to_jsonb(OLD) - ARRAY['EffectiveTo','Version','UpdatedAtUtc']) =
                       (to_jsonb(NEW) - ARRAY['EffectiveTo','Version','UpdatedAtUtc']) AND
                       NEW."EffectiveTo" IS NOT NULL AND NEW."EffectiveTo" IS DISTINCT FROM OLD."EffectiveTo" AND
                       (OLD."EffectiveTo" IS NULL OR NEW."EffectiveTo" < OLD."EffectiveTo") AND
                       NEW."Version" = OLD."Version" + 1 THEN
                        RETURN NEW;
                    END IF;
                    IF OLD."Status" = 'APPROVED' AND NEW."Status" = 'OBSOLETE' AND
                       (to_jsonb(OLD) - ARRAY['Status','EffectiveTo','Version','UpdatedAtUtc','ObsoletedAtUtc','ObsoletedByActorId']) =
                       (to_jsonb(NEW) - ARRAY['Status','EffectiveTo','Version','UpdatedAtUtc','ObsoletedAtUtc','ObsoletedByActorId']) AND
                       NEW."EffectiveTo" IS NOT NULL AND
                       (OLD."EffectiveTo" IS NULL OR NEW."EffectiveTo" <= OLD."EffectiveTo") AND
                       NEW."ObsoletedAtUtc" IS NOT NULL AND NEW."Version" = OLD."Version" + 1 THEN
                        RETURN NEW;
                    END IF;
                    RAISE EXCEPTION 'Released BOM revision is immutable' USING ERRCODE = '23514';
                END;
                $$ LANGUAGE plpgsql;

                CREATE TRIGGER trg_guard_released_bom_revision
                BEFORE UPDATE OR DELETE ON engineering.bom_revisions
                FOR EACH ROW EXECUTE FUNCTION engineering.guard_released_bom_revision();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS trg_guard_released_bom_lines ON engineering.bom_lines;");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS engineering.guard_released_bom_lines();");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS trg_guard_released_bom_revision ON engineering.bom_revisions;");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS engineering.guard_released_bom_revision();");
            migrationBuilder.Sql("ALTER TABLE engineering.bom_revisions DROP CONSTRAINT IF EXISTS ex_bom_revisions_released_effectivity;");
            migrationBuilder.Sql("ALTER TABLE engineering.bom_revisions DROP CONSTRAINT IF EXISTS ck_bom_revisions_lifecycle_fields;");
            migrationBuilder.DropTable(
                name: "bom_lines",
                schema: "engineering");

            migrationBuilder.DropTable(
                name: "bom_revisions",
                schema: "engineering");

            migrationBuilder.DropSequence(
                name: "bom_revision_number_seq",
                schema: "engineering");
        }
    }
}
