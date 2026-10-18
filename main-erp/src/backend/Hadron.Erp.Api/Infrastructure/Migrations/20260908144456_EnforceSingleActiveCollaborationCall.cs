using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hadron.Erp.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class EnforceSingleActiveCollaborationCall : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                WITH ranked_active_calls AS (
                    SELECT "Id",
                           ROW_NUMBER() OVER (
                               PARTITION BY "ConversationId"
                               ORDER BY "StartedAtUtc" DESC, "Id" DESC
                           ) AS active_rank
                    FROM collaboration.call_sessions
                    WHERE "Status" = 'ACTIVE'
                )
                UPDATE collaboration.call_sessions AS calls
                SET "Status" = 'ENDED',
                    "EndedAtUtc" = COALESCE(calls."EndedAtUtc", CURRENT_TIMESTAMP),
                    "Version" = calls."Version" + 1
                FROM ranked_active_calls AS ranked
                WHERE calls."Id" = ranked."Id"
                  AND ranked.active_rank > 1;
                """);

            migrationBuilder.CreateIndex(
                name: "ux_call_sessions_one_active_per_conversation",
                schema: "collaboration",
                table: "call_sessions",
                column: "ConversationId",
                unique: true,
                filter: "\"Status\" = 'ACTIVE'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_call_sessions_one_active_per_conversation",
                schema: "collaboration",
                table: "call_sessions");
        }
    }
}
