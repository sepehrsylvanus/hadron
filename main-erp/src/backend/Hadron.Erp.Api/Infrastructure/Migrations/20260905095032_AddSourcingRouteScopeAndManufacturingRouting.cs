using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hadron.Erp.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSourcingRouteScopeAndManufacturingRouting : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateSequence(
                name: "manufacturing_route_number_seq",
                schema: "production");

            migrationBuilder.CreateSequence(
                name: "production_work_center_number_seq",
                schema: "production");

            migrationBuilder.AddColumn<string>(
                name: "RouteScope",
                schema: "procurement",
                table: "sourcing_options",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "IMPORT");

            migrationBuilder.CreateTable(
                name: "manufacturing_routes",
                schema: "production",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BusinessNumber = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    ItemBaseUnitId = table.Column<Guid>(type: "uuid", nullable: false),
                    RevisionNumber = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ApprovedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ObsoletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_manufacturing_routes", x => x.Id);
                    table.CheckConstraint("ck_manufacturing_route_number", "\"BusinessNumber\" ~ '^RTE-[0-9]{4,}$'");
                    table.CheckConstraint("ck_manufacturing_route_revision", "\"RevisionNumber\" >= 1");
                    table.CheckConstraint("ck_manufacturing_route_status", "\"Status\" IN ('DRAFT','APPROVED','OBSOLETE')");
                    table.CheckConstraint("ck_manufacturing_route_version", "\"Version\" >= 1");
                    table.ForeignKey(
                        name: "FK_manufacturing_routes_items_ItemId",
                        column: x => x.ItemId,
                        principalSchema: "catalog",
                        principalTable: "items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "work_centers",
                schema: "production",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BusinessNumber = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    NameFa = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    NameEn = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    OrganizationalUnitId = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkingWeekMask = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_work_centers", x => x.Id);
                    table.CheckConstraint("ck_production_work_center_number", "\"BusinessNumber\" ~ '^WC-[0-9]{4,}$'");
                    table.CheckConstraint("ck_production_work_center_version", "\"Version\" >= 1");
                    table.CheckConstraint("ck_production_work_center_week", "\"WorkingWeekMask\" BETWEEN 1 AND 127");
                    table.ForeignKey(
                        name: "FK_work_centers_organizational_units_OrganizationalUnitId",
                        column: x => x.OrganizationalUnitId,
                        principalSchema: "organization",
                        principalTable: "organizational_units",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "manufacturing_route_operations",
                schema: "production",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RouteId = table.Column<Guid>(type: "uuid", nullable: false),
                    Sequence = table.Column<int>(type: "integer", nullable: false),
                    WorkCenterId = table.Column<Guid>(type: "uuid", nullable: false),
                    PreparationWorkingDays = table.Column<int>(type: "integer", nullable: false),
                    ExecutionWorkingDays = table.Column<int>(type: "integer", nullable: false),
                    Notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_manufacturing_route_operations", x => x.Id);
                    table.CheckConstraint("ck_manufacturing_route_operation_days", "\"PreparationWorkingDays\" >= 0 AND \"ExecutionWorkingDays\" >= 0 AND (\"PreparationWorkingDays\" + \"ExecutionWorkingDays\") > 0");
                    table.CheckConstraint("ck_manufacturing_route_operation_sequence", "\"Sequence\" >= 10");
                    table.ForeignKey(
                        name: "FK_manufacturing_route_operations_manufacturing_routes_RouteId",
                        column: x => x.RouteId,
                        principalSchema: "production",
                        principalTable: "manufacturing_routes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_manufacturing_route_operations_work_centers_WorkCenterId",
                        column: x => x.WorkCenterId,
                        principalSchema: "production",
                        principalTable: "work_centers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_sourcing_options_domestic_fields",
                schema: "procurement",
                table: "sourcing_options",
                sql: "\"RouteScope\" <> 'DOMESTIC' OR (\"IncotermId\" IS NULL AND \"ForwarderToShipmentDays\" IS NULL AND \"DestinationCustomsDays\" IS NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_sourcing_options_route_scope",
                schema: "procurement",
                table: "sourcing_options",
                sql: "\"RouteScope\" IN ('DOMESTIC','IMPORT')");

            migrationBuilder.CreateIndex(
                name: "IX_manufacturing_route_operations_RouteId_Sequence",
                schema: "production",
                table: "manufacturing_route_operations",
                columns: new[] { "RouteId", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_manufacturing_route_operations_WorkCenterId",
                schema: "production",
                table: "manufacturing_route_operations",
                column: "WorkCenterId");

            migrationBuilder.CreateIndex(
                name: "IX_manufacturing_routes_BusinessNumber",
                schema: "production",
                table: "manufacturing_routes",
                column: "BusinessNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_manufacturing_routes_ItemId_RevisionNumber",
                schema: "production",
                table: "manufacturing_routes",
                columns: new[] { "ItemId", "RevisionNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_manufacturing_route_current_item",
                schema: "production",
                table: "manufacturing_routes",
                column: "ItemId",
                unique: true,
                filter: "\"Status\" = 'APPROVED'");

            migrationBuilder.CreateIndex(
                name: "IX_work_centers_BusinessNumber",
                schema: "production",
                table: "work_centers",
                column: "BusinessNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_work_centers_OrganizationalUnitId_IsActive",
                schema: "production",
                table: "work_centers",
                columns: new[] { "OrganizationalUnitId", "IsActive" });

            migrationBuilder.Sql("""
                CREATE FUNCTION production.guard_work_center_identity()
                RETURNS trigger LANGUAGE plpgsql AS $function$
                BEGIN
                    IF NEW."Id" <> OLD."Id" OR NEW."BusinessNumber" <> OLD."BusinessNumber"
                       OR NEW."CreatedAtUtc" <> OLD."CreatedAtUtc" THEN
                        RAISE EXCEPTION 'Production work-center identity is immutable.'
                            USING ERRCODE = '23514', CONSTRAINT = 'ck_production_work_center_identity_immutable';
                    END IF;
                    RETURN NEW;
                END $function$;
                CREATE TRIGGER trg_production_work_center_identity
                BEFORE UPDATE ON production.work_centers
                FOR EACH ROW EXECUTE FUNCTION production.guard_work_center_identity();

                CREATE FUNCTION production.guard_manufacturing_route_history()
                RETURNS trigger LANGUAGE plpgsql AS $function$
                BEGIN
                    IF TG_OP = 'DELETE' AND OLD."Status" <> 'DRAFT' THEN
                        RAISE EXCEPTION 'Approved manufacturing routes are immutable.'
                            USING ERRCODE = '23514', CONSTRAINT = 'ck_manufacturing_route_history_immutable';
                    END IF;
                    IF TG_OP = 'DELETE' THEN RETURN OLD; END IF;
                    IF NEW."Id" <> OLD."Id" OR NEW."BusinessNumber" <> OLD."BusinessNumber"
                       OR NEW."ItemId" <> OLD."ItemId" OR NEW."ItemBaseUnitId" <> OLD."ItemBaseUnitId"
                       OR NEW."RevisionNumber" <> OLD."RevisionNumber" OR NEW."CreatedAtUtc" <> OLD."CreatedAtUtc" THEN
                        RAISE EXCEPTION 'Manufacturing-route identity is immutable.'
                            USING ERRCODE = '23514', CONSTRAINT = 'ck_manufacturing_route_identity_immutable';
                    END IF;
                    IF OLD."Status" = 'OBSOLETE' OR
                       (OLD."Status" = 'APPROVED' AND
                        (NEW."Status" <> 'OBSOLETE' OR NEW."Notes" IS DISTINCT FROM OLD."Notes"
                         OR NEW."ApprovedAtUtc" IS DISTINCT FROM OLD."ApprovedAtUtc")) THEN
                        RAISE EXCEPTION 'Approved manufacturing routes are immutable.'
                            USING ERRCODE = '23514', CONSTRAINT = 'ck_manufacturing_route_history_immutable';
                    END IF;
                    RETURN NEW;
                END $function$;
                CREATE TRIGGER trg_manufacturing_route_history
                BEFORE UPDATE OR DELETE ON production.manufacturing_routes
                FOR EACH ROW EXECUTE FUNCTION production.guard_manufacturing_route_history();

                CREATE FUNCTION production.guard_manufacturing_route_operation_history()
                RETURNS trigger LANGUAGE plpgsql AS $function$
                DECLARE route_id uuid; route_status text;
                BEGIN
                    route_id := CASE WHEN TG_OP = 'DELETE' THEN OLD."RouteId" ELSE NEW."RouteId" END;
                    SELECT "Status" INTO route_status FROM production.manufacturing_routes WHERE "Id" = route_id;
                    IF route_status IS DISTINCT FROM 'DRAFT' THEN
                        RAISE EXCEPTION 'Operations of an approved manufacturing route are immutable.'
                            USING ERRCODE = '23514', CONSTRAINT = 'ck_manufacturing_route_operation_history_immutable';
                    END IF;
                    RETURN CASE WHEN TG_OP = 'DELETE' THEN OLD ELSE NEW END;
                END $function$;
                CREATE TRIGGER trg_manufacturing_route_operation_history
                BEFORE INSERT OR UPDATE OR DELETE ON production.manufacturing_route_operations
                FOR EACH ROW EXECUTE FUNCTION production.guard_manufacturing_route_operation_history();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER IF EXISTS trg_manufacturing_route_operation_history ON production.manufacturing_route_operations;
                DROP TRIGGER IF EXISTS trg_manufacturing_route_history ON production.manufacturing_routes;
                DROP TRIGGER IF EXISTS trg_production_work_center_identity ON production.work_centers;
                DROP FUNCTION IF EXISTS production.guard_manufacturing_route_operation_history();
                DROP FUNCTION IF EXISTS production.guard_manufacturing_route_history();
                DROP FUNCTION IF EXISTS production.guard_work_center_identity();
                """);
            migrationBuilder.DropTable(
                name: "manufacturing_route_operations",
                schema: "production");

            migrationBuilder.DropTable(
                name: "manufacturing_routes",
                schema: "production");

            migrationBuilder.DropTable(
                name: "work_centers",
                schema: "production");

            migrationBuilder.DropCheckConstraint(
                name: "ck_sourcing_options_domestic_fields",
                schema: "procurement",
                table: "sourcing_options");

            migrationBuilder.DropCheckConstraint(
                name: "ck_sourcing_options_route_scope",
                schema: "procurement",
                table: "sourcing_options");

            migrationBuilder.DropColumn(
                name: "RouteScope",
                schema: "procurement",
                table: "sourcing_options");

            migrationBuilder.DropSequence(
                name: "manufacturing_route_number_seq",
                schema: "production");

            migrationBuilder.DropSequence(
                name: "production_work_center_number_seq",
                schema: "production");
        }
    }
}
