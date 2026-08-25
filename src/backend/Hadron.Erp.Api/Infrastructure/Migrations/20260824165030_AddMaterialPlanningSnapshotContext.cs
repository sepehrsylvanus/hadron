using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hadron.Erp.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMaterialPlanningSnapshotContext : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CategoryCode",
                schema: "planning",
                table: "mrp_run_item_inputs",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CategoryId",
                schema: "planning",
                table: "mrp_run_item_inputs",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CategoryNameEn",
                schema: "planning",
                table: "mrp_run_item_inputs",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CategoryNameFa",
                schema: "planning",
                table: "mrp_run_item_inputs",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CriticalDays",
                schema: "planning",
                table: "mrp_run_item_inputs",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ExcessStartDays",
                schema: "planning",
                table: "mrp_run_item_inputs",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SafeStartDays",
                schema: "planning",
                table: "mrp_run_item_inputs",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SupplierId",
                schema: "planning",
                table: "mrp_run_firm_supply_inputs",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SupplierNameEn",
                schema: "planning",
                table: "mrp_run_firm_supply_inputs",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SupplierNameFa",
                schema: "planning",
                table: "mrp_run_firm_supply_inputs",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SupplierNumber",
                schema: "planning",
                table: "mrp_run_firm_supply_inputs",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CategoryCode",
                schema: "planning",
                table: "mrp_run_item_inputs");

            migrationBuilder.DropColumn(
                name: "CategoryId",
                schema: "planning",
                table: "mrp_run_item_inputs");

            migrationBuilder.DropColumn(
                name: "CategoryNameEn",
                schema: "planning",
                table: "mrp_run_item_inputs");

            migrationBuilder.DropColumn(
                name: "CategoryNameFa",
                schema: "planning",
                table: "mrp_run_item_inputs");

            migrationBuilder.DropColumn(
                name: "CriticalDays",
                schema: "planning",
                table: "mrp_run_item_inputs");

            migrationBuilder.DropColumn(
                name: "ExcessStartDays",
                schema: "planning",
                table: "mrp_run_item_inputs");

            migrationBuilder.DropColumn(
                name: "SafeStartDays",
                schema: "planning",
                table: "mrp_run_item_inputs");

            migrationBuilder.DropColumn(
                name: "SupplierId",
                schema: "planning",
                table: "mrp_run_firm_supply_inputs");

            migrationBuilder.DropColumn(
                name: "SupplierNameEn",
                schema: "planning",
                table: "mrp_run_firm_supply_inputs");

            migrationBuilder.DropColumn(
                name: "SupplierNameFa",
                schema: "planning",
                table: "mrp_run_firm_supply_inputs");

            migrationBuilder.DropColumn(
                name: "SupplierNumber",
                schema: "planning",
                table: "mrp_run_firm_supply_inputs");
        }
    }
}
