using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hadron.Erp.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMrpManufacturingRouteSnapshot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "mrp_run_manufacturing_operation_inputs",
                schema: "planning",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RunId = table.Column<Guid>(type: "uuid", nullable: false),
                    RouteId = table.Column<Guid>(type: "uuid", nullable: false),
                    RouteCode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    OperationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Sequence = table.Column<int>(type: "integer", nullable: false),
                    WorkCenterId = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkCenterCode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    WorkingWeekMask = table.Column<int>(type: "integer", nullable: false),
                    PreparationWorkingDays = table.Column<int>(type: "integer", nullable: false),
                    ExecutionWorkingDays = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_mrp_run_manufacturing_operation_inputs", x => x.Id);
                    table.CheckConstraint("ck_mrp_manufacturing_operation_days", "\"PreparationWorkingDays\" >= 0 AND \"ExecutionWorkingDays\" >= 0 AND (\"PreparationWorkingDays\" + \"ExecutionWorkingDays\") > 0");
                    table.CheckConstraint("ck_mrp_manufacturing_operation_week", "\"WorkingWeekMask\" BETWEEN 1 AND 127");
                    table.ForeignKey(
                        name: "FK_mrp_run_manufacturing_operation_inputs_mrp_runs_RunId",
                        column: x => x.RunId,
                        principalSchema: "planning",
                        principalTable: "mrp_runs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_mrp_run_manufacturing_operation_inputs_RunId_ItemId_Sequence",
                schema: "planning",
                table: "mrp_run_manufacturing_operation_inputs",
                columns: new[] { "RunId", "ItemId", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_mrp_run_manufacturing_operation_inputs_RunId_RouteId_Operat~",
                schema: "planning",
                table: "mrp_run_manufacturing_operation_inputs",
                columns: new[] { "RunId", "RouteId", "OperationId" },
                unique: true);

            migrationBuilder.Sql("""
                CREATE TRIGGER trg_mrp_run_manufacturing_operation_inputs_immutable
                BEFORE INSERT OR UPDATE OR DELETE ON planning.mrp_run_manufacturing_operation_inputs
                FOR EACH ROW EXECUTE FUNCTION planning.guard_mrp_run_child();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER IF EXISTS trg_mrp_run_manufacturing_operation_inputs_immutable
                ON planning.mrp_run_manufacturing_operation_inputs;
                """);
            migrationBuilder.DropTable(
                name: "mrp_run_manufacturing_operation_inputs",
                schema: "planning");
        }
    }
}
