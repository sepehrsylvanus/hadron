using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hadron.Erp.Api.Infrastructure.Migrations;

public partial class AddTradingProjectSetup : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Nullable for existing commercial history. New setup commands validate required facts in the service.
        migrationBuilder.AddColumn<DateOnly>(name: "StartDate", schema: "trading", table: "projects", type: "date", nullable: true);
        migrationBuilder.AddColumn<Guid>(name: "BusinessCardId", schema: "trading", table: "projects", type: "uuid", nullable: true);
        migrationBuilder.AddColumn<string>(name: "BusinessCardCodeSnapshot", schema: "trading", table: "projects", type: "character varying(40)", maxLength: 40, nullable: true);
        migrationBuilder.AddColumn<string>(name: "BusinessCardNameFaSnapshot", schema: "trading", table: "projects", type: "character varying(200)", maxLength: 200, nullable: true);
        migrationBuilder.AddColumn<string>(name: "BusinessCardNameEnSnapshot", schema: "trading", table: "projects", type: "character varying(200)", maxLength: 200, nullable: true);
        migrationBuilder.AddColumn<Guid>(name: "ResponsibleUserId", schema: "trading", table: "projects", type: "uuid", nullable: true);
        migrationBuilder.AddColumn<string>(name: "ResponsibleUserNameSnapshot", schema: "trading", table: "projects", type: "character varying(500)", maxLength: 500, nullable: true);
        migrationBuilder.CreateIndex(name: "IX_projects_BusinessCardId", schema: "trading", table: "projects", column: "BusinessCardId");
        migrationBuilder.CreateIndex(name: "IX_projects_ResponsibleUserId", schema: "trading", table: "projects", column: "ResponsibleUserId");
        migrationBuilder.AddForeignKey(name: "FK_projects_reference_values_BusinessCardId", schema: "trading", table: "projects", column: "BusinessCardId", principalSchema: "trading", principalTable: "reference_values", principalColumn: "Id", onDelete: ReferentialAction.Restrict);
        migrationBuilder.AddForeignKey(name: "FK_projects_users_ResponsibleUserId", schema: "trading", table: "projects", column: "ResponsibleUserId", principalSchema: "identity", principalTable: "users", principalColumn: "Id", onDelete: ReferentialAction.Restrict);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(name: "FK_projects_reference_values_BusinessCardId", schema: "trading", table: "projects");
        migrationBuilder.DropForeignKey(name: "FK_projects_users_ResponsibleUserId", schema: "trading", table: "projects");
        migrationBuilder.DropIndex(name: "IX_projects_BusinessCardId", schema: "trading", table: "projects");
        migrationBuilder.DropIndex(name: "IX_projects_ResponsibleUserId", schema: "trading", table: "projects");
        foreach (var column in new[] { "StartDate", "BusinessCardId", "BusinessCardCodeSnapshot", "BusinessCardNameFaSnapshot", "BusinessCardNameEnSnapshot", "ResponsibleUserId", "ResponsibleUserNameSnapshot" })
            migrationBuilder.DropColumn(name: column, schema: "trading", table: "projects");
    }
}
