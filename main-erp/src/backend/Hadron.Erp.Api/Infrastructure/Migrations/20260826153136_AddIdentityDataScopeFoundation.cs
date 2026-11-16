using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hadron.Erp.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddIdentityDataScopeFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "user_data_scopes",
                schema: "identity",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ScopeType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ScopeValue = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_data_scopes", x => x.Id);
                    table.CheckConstraint("ck_identity_user_data_scope_v1", "\"ScopeType\" = 'ALL' AND \"ScopeValue\" IS NULL");
                    table.ForeignKey(
                        name: "FK_user_data_scopes_users_UserId",
                        column: x => x.UserId,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_user_data_scopes_UserId_ScopeType",
                schema: "identity",
                table: "user_data_scopes",
                columns: new[] { "UserId", "ScopeType" },
                unique: true);

            migrationBuilder.Sql(
                """
                INSERT INTO identity.user_data_scopes
                    ("Id", "UserId", "ScopeType", "ScopeValue", "CreatedAtUtc")
                SELECT gen_random_uuid(), u."Id", 'ALL', NULL, NOW()
                FROM identity.users AS u
                WHERE NOT EXISTS (
                    SELECT 1
                    FROM identity.user_data_scopes AS s
                    WHERE s."UserId" = u."Id"
                      AND s."ScopeType" = 'ALL');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "user_data_scopes",
                schema: "identity");
        }
    }
}
