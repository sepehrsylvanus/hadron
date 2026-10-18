using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hadron.Erp.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RemediateOrderPlanningAuditFindings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "FinalSourceSnapshot",
                schema: "planning",
                table: "order_recommendation_decisions",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "IncotermNameEn",
                schema: "planning",
                table: "order_recommendation_candidates",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "IncotermNameFa",
                schema: "planning",
                table: "order_recommendation_candidates",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PaymentTermCode",
                schema: "planning",
                table: "order_recommendation_candidates",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PaymentTermNameEn",
                schema: "planning",
                table: "order_recommendation_candidates",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PaymentTermNameFa",
                schema: "planning",
                table: "order_recommendation_candidates",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ShippingModeNameEn",
                schema: "planning",
                table: "order_recommendation_candidates",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ShippingModeNameFa",
                schema: "planning",
                table: "order_recommendation_candidates",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddUniqueConstraint(
                name: "AK_order_recommendation_candidates_Id_GenerationId_ItemId",
                schema: "planning",
                table: "order_recommendation_candidates",
                columns: new[] { "Id", "GenerationId", "ItemId" });

            migrationBuilder.CreateIndex(
                name: "IX_order_recommendations_SelectedCandidateId_GenerationId_Item~",
                schema: "planning",
                table: "order_recommendations",
                columns: new[] { "SelectedCandidateId", "GenerationId", "ItemId" });

            migrationBuilder.AddCheckConstraint(
                name: "ck_order_decision_version_positive",
                schema: "planning",
                table: "order_recommendation_decisions",
                sql: "\"DecisionVersion\" > 0");

            migrationBuilder.AddForeignKey(
                name: "FK_order_recommendations_order_recommendation_candidates_Selec~",
                schema: "planning",
                table: "order_recommendations",
                columns: new[] { "SelectedCandidateId", "GenerationId", "ItemId" },
                principalSchema: "planning",
                principalTable: "order_recommendation_candidates",
                principalColumns: new[] { "Id", "GenerationId", "ItemId" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION planning.enforce_order_decision_sequence()
                RETURNS trigger LANGUAGE plpgsql AS $function$
                DECLARE recommendation_id uuid; parent_version bigint; maximum_version bigint; version_count bigint;
                BEGIN
                    IF TG_TABLE_NAME = 'order_recommendations' THEN
                        recommendation_id := NEW."Id";
                    ELSE
                        recommendation_id := NEW."RecommendationId";
                    END IF;
                    SELECT "DecisionVersion" INTO parent_version FROM planning.order_recommendations WHERE "Id" = recommendation_id;
                    SELECT COALESCE(MAX("DecisionVersion"), 0), COUNT(*) INTO maximum_version, version_count
                    FROM planning.order_recommendation_decisions WHERE "RecommendationId" = recommendation_id;
                    IF parent_version IS NULL OR parent_version <> maximum_version OR maximum_version <> version_count THEN
                        RAISE EXCEPTION 'ORDER_DECISION_SEQUENCE_MISMATCH' USING ERRCODE = '23514';
                    END IF;
                    RETURN NULL;
                END;
                $function$;
                CREATE CONSTRAINT TRIGGER trg_order_decision_sequence_insert
                AFTER INSERT ON planning.order_recommendation_decisions
                DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION planning.enforce_order_decision_sequence();
                CREATE CONSTRAINT TRIGGER trg_order_recommendation_decision_version
                AFTER UPDATE ON planning.order_recommendations
                DEFERRABLE INITIALLY DEFERRED FOR EACH ROW
                WHEN (OLD."DecisionVersion" IS DISTINCT FROM NEW."DecisionVersion")
                EXECUTE FUNCTION planning.enforce_order_decision_sequence();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER IF EXISTS trg_order_recommendation_decision_version ON planning.order_recommendations;
                DROP TRIGGER IF EXISTS trg_order_decision_sequence_insert ON planning.order_recommendation_decisions;
                DROP FUNCTION IF EXISTS planning.enforce_order_decision_sequence();
                """);
            migrationBuilder.DropForeignKey(
                name: "FK_order_recommendations_order_recommendation_candidates_Selec~",
                schema: "planning",
                table: "order_recommendations");

            migrationBuilder.DropIndex(
                name: "IX_order_recommendations_SelectedCandidateId_GenerationId_Item~",
                schema: "planning",
                table: "order_recommendations");

            migrationBuilder.DropCheckConstraint(
                name: "ck_order_decision_version_positive",
                schema: "planning",
                table: "order_recommendation_decisions");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_order_recommendation_candidates_Id_GenerationId_ItemId",
                schema: "planning",
                table: "order_recommendation_candidates");

            migrationBuilder.DropColumn(
                name: "FinalSourceSnapshot",
                schema: "planning",
                table: "order_recommendation_decisions");

            migrationBuilder.DropColumn(
                name: "IncotermNameEn",
                schema: "planning",
                table: "order_recommendation_candidates");

            migrationBuilder.DropColumn(
                name: "IncotermNameFa",
                schema: "planning",
                table: "order_recommendation_candidates");

            migrationBuilder.DropColumn(
                name: "PaymentTermCode",
                schema: "planning",
                table: "order_recommendation_candidates");

            migrationBuilder.DropColumn(
                name: "PaymentTermNameEn",
                schema: "planning",
                table: "order_recommendation_candidates");

            migrationBuilder.DropColumn(
                name: "PaymentTermNameFa",
                schema: "planning",
                table: "order_recommendation_candidates");

            migrationBuilder.DropColumn(
                name: "ShippingModeNameEn",
                schema: "planning",
                table: "order_recommendation_candidates");

            migrationBuilder.DropColumn(
                name: "ShippingModeNameFa",
                schema: "planning",
                table: "order_recommendation_candidates");
        }
    }
}
