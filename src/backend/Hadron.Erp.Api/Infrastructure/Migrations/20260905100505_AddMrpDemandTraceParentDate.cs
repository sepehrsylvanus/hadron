using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hadron.Erp.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMrpDemandTraceParentDate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "ParentDemandDate",
                schema: "planning",
                table: "mrp_run_demand_traces",
                type: "date",
                nullable: true);

            migrationBuilder.Sql("""
                ALTER TABLE planning.mrp_run_demand_traces
                    DISABLE TRIGGER trg_guard_mrp_run_demand_traces;
                UPDATE planning.mrp_run_demand_traces
                    SET "ParentDemandDate" = "Date"
                    WHERE "ParentDemandDate" IS NULL;
                ALTER TABLE planning.mrp_run_demand_traces
                    ENABLE TRIGGER trg_guard_mrp_run_demand_traces;
                """);

            migrationBuilder.AlterColumn<DateOnly>(
                name: "ParentDemandDate",
                schema: "planning",
                table: "mrp_run_demand_traces",
                type: "date",
                nullable: false,
                oldClrType: typeof(DateOnly),
                oldType: "date",
                oldNullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ParentDemandDate",
                schema: "planning",
                table: "mrp_run_demand_traces");
        }
    }
}
