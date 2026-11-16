using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hadron.Erp.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMaterialCostBooks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateSequence(
                name: "material_cost_book_number_seq",
                schema: "cash_flow");

            migrationBuilder.CreateTable(
                name: "material_cost_books",
                schema: "cash_flow",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BookNumber = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CreatedByIdentifier = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_material_cost_books", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "material_cost_book_revisions",
                schema: "cash_flow",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MaterialCostBookId = table.Column<Guid>(type: "uuid", nullable: false),
                    Revision = table.Column<int>(type: "integer", nullable: false),
                    NameFa = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: false),
                    NameEn = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: true),
                    EffectiveDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    SupersedesRevisionId = table.Column<Guid>(type: "uuid", nullable: true),
                    ClientRequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestFingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ChangeReason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CreatedByIdentifier = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    UpdatedByIdentifier = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    PublishedByIdentifier = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    PublishedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    PublishReason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_material_cost_book_revisions", x => x.Id);
                    table.CheckConstraint("ck_material_cost_book_revisions_not_self_superseding", "\"SupersedesRevisionId\" IS NULL OR \"SupersedesRevisionId\" <> \"Id\"");
                    table.CheckConstraint("ck_material_cost_book_revisions_publish_state", "(\"Status\" = 'DRAFT' AND \"PublishedAtUtc\" IS NULL AND \"PublishReason\" IS NULL) OR (\"Status\" = 'PUBLISHED' AND \"PublishedAtUtc\" IS NOT NULL AND NULLIF(BTRIM(\"PublishReason\"), '') IS NOT NULL)");
                    table.CheckConstraint("ck_material_cost_book_revisions_revision", "\"Revision\" >= 1");
                    table.CheckConstraint("ck_material_cost_book_revisions_status", "\"Status\" IN ('DRAFT','PUBLISHED')");
                    table.CheckConstraint("ck_material_cost_book_revisions_version", "\"Version\" >= 1");
                    table.ForeignKey(
                        name: "FK_material_cost_book_revisions_material_cost_book_revisions_S~",
                        column: x => x.SupersedesRevisionId,
                        principalSchema: "cash_flow",
                        principalTable: "material_cost_book_revisions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_material_cost_book_revisions_material_cost_books_MaterialCo~",
                        column: x => x.MaterialCostBookId,
                        principalSchema: "cash_flow",
                        principalTable: "material_cost_books",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "material_cost_book_lines",
                schema: "cash_flow",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MaterialCostBookRevisionId = table.Column<Guid>(type: "uuid", nullable: false),
                    ItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    ItemCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ItemNameFa = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: false),
                    ItemNameEn = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: true),
                    CurrencyId = table.Column<Guid>(type: "uuid", nullable: false),
                    CurrencyCode = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    CurrencyNameFa = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    CurrencyNameEn = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    CurrencyDecimalPrecision = table.Column<int>(type: "integer", nullable: false),
                    OriginalPriceAmount = table.Column<decimal>(type: "numeric(28,12)", precision: 28, scale: 12, nullable: false),
                    PriceBasisQuantity = table.Column<decimal>(type: "numeric(28,12)", precision: 28, scale: 12, nullable: false),
                    PriceUnitId = table.Column<Guid>(type: "uuid", nullable: false),
                    PriceUnitCode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    PriceUnitNameFa = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    PriceUnitNameEn = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    PriceUnitFactorToBase = table.Column<decimal>(type: "numeric(28,12)", precision: 28, scale: 12, nullable: false),
                    ItemBaseUnitId = table.Column<Guid>(type: "uuid", nullable: false),
                    ItemBaseUnitCode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ItemBaseUnitNameFa = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    ItemBaseUnitNameEn = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    SupplierId = table.Column<Guid>(type: "uuid", nullable: true),
                    SupplierNumber = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    SupplierName = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: true),
                    SourceReference = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: true),
                    Notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_material_cost_book_lines", x => x.Id);
                    table.CheckConstraint("ck_material_cost_book_lines_basis", "\"PriceBasisQuantity\" > 0");
                    table.CheckConstraint("ck_material_cost_book_lines_currency_precision", "\"CurrencyDecimalPrecision\" BETWEEN 0 AND 12");
                    table.CheckConstraint("ck_material_cost_book_lines_factor", "\"PriceUnitFactorToBase\" > 0");
                    table.CheckConstraint("ck_material_cost_book_lines_price", "\"OriginalPriceAmount\" > 0");
                    table.CheckConstraint("ck_material_cost_book_lines_supplier_snapshot", "(\"SupplierId\" IS NULL AND \"SupplierNumber\" IS NULL AND \"SupplierName\" IS NULL) OR (\"SupplierId\" IS NOT NULL AND NULLIF(BTRIM(\"SupplierNumber\"), '') IS NOT NULL AND NULLIF(BTRIM(\"SupplierName\"), '') IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_material_cost_book_lines_currencies_CurrencyId",
                        column: x => x.CurrencyId,
                        principalSchema: "reference_data",
                        principalTable: "currencies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_material_cost_book_lines_items_ItemId",
                        column: x => x.ItemId,
                        principalSchema: "catalog",
                        principalTable: "items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_material_cost_book_lines_material_cost_book_revisions_Mater~",
                        column: x => x.MaterialCostBookRevisionId,
                        principalSchema: "cash_flow",
                        principalTable: "material_cost_book_revisions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_material_cost_book_lines_suppliers_SupplierId",
                        column: x => x.SupplierId,
                        principalSchema: "procurement",
                        principalTable: "suppliers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_material_cost_book_lines_units_ItemBaseUnitId",
                        column: x => x.ItemBaseUnitId,
                        principalSchema: "reference_data",
                        principalTable: "units",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_material_cost_book_lines_units_PriceUnitId",
                        column: x => x.PriceUnitId,
                        principalSchema: "reference_data",
                        principalTable: "units",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "material_cost_book_operation_receipts",
                schema: "cash_flow",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MaterialCostBookRevisionId = table.Column<Guid>(type: "uuid", nullable: false),
                    OperationType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ClientRequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestFingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ResultVersion = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_material_cost_book_operation_receipts", x => x.Id);
                    table.CheckConstraint("ck_material_cost_book_operation_receipts_type", "\"OperationType\" IN ('UPDATE_DRAFT','UPSERT_LINE','REMOVE_LINE','PUBLISH')");
                    table.CheckConstraint("ck_material_cost_book_operation_receipts_version", "\"ResultVersion\" >= 1");
                    table.ForeignKey(
                        name: "FK_material_cost_book_operation_receipts_material_cost_book_re~",
                        column: x => x.MaterialCostBookRevisionId,
                        principalSchema: "cash_flow",
                        principalTable: "material_cost_book_revisions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_material_cost_book_lines_CurrencyId",
                schema: "cash_flow",
                table: "material_cost_book_lines",
                column: "CurrencyId");

            migrationBuilder.CreateIndex(
                name: "IX_material_cost_book_lines_ItemBaseUnitId",
                schema: "cash_flow",
                table: "material_cost_book_lines",
                column: "ItemBaseUnitId");

            migrationBuilder.CreateIndex(
                name: "IX_material_cost_book_lines_ItemId",
                schema: "cash_flow",
                table: "material_cost_book_lines",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "IX_material_cost_book_lines_MaterialCostBookRevisionId_ItemId",
                schema: "cash_flow",
                table: "material_cost_book_lines",
                columns: new[] { "MaterialCostBookRevisionId", "ItemId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_material_cost_book_lines_PriceUnitId",
                schema: "cash_flow",
                table: "material_cost_book_lines",
                column: "PriceUnitId");

            migrationBuilder.CreateIndex(
                name: "IX_material_cost_book_lines_SupplierId",
                schema: "cash_flow",
                table: "material_cost_book_lines",
                column: "SupplierId");

            migrationBuilder.CreateIndex(
                name: "IX_material_cost_book_operation_receipts_MaterialCostBookRevis~",
                schema: "cash_flow",
                table: "material_cost_book_operation_receipts",
                column: "MaterialCostBookRevisionId");

            migrationBuilder.CreateIndex(
                name: "IX_material_cost_book_operation_receipts_OperationType_ClientR~",
                schema: "cash_flow",
                table: "material_cost_book_operation_receipts",
                columns: new[] { "OperationType", "ClientRequestId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_material_cost_book_revisions_ClientRequestId",
                schema: "cash_flow",
                table: "material_cost_book_revisions",
                column: "ClientRequestId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_material_cost_book_revisions_MaterialCostBookId",
                schema: "cash_flow",
                table: "material_cost_book_revisions",
                column: "MaterialCostBookId",
                unique: true,
                filter: "\"Status\" = 'DRAFT'");

            migrationBuilder.CreateIndex(
                name: "IX_material_cost_book_revisions_MaterialCostBookId_Revision",
                schema: "cash_flow",
                table: "material_cost_book_revisions",
                columns: new[] { "MaterialCostBookId", "Revision" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_material_cost_book_revisions_Status_EffectiveDate_Revision",
                schema: "cash_flow",
                table: "material_cost_book_revisions",
                columns: new[] { "Status", "EffectiveDate", "Revision" });

            migrationBuilder.CreateIndex(
                name: "IX_material_cost_book_revisions_SupersedesRevisionId",
                schema: "cash_flow",
                table: "material_cost_book_revisions",
                column: "SupersedesRevisionId",
                unique: true,
                filter: "\"SupersedesRevisionId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_material_cost_books_BookNumber",
                schema: "cash_flow",
                table: "material_cost_books",
                column: "BookNumber",
                unique: true);

            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION cash_flow.reject_published_material_cost_book_revision_mutation()
                RETURNS trigger AS $$
                BEGIN
                    IF OLD."Status" = 'PUBLISHED' THEN
                        RAISE EXCEPTION 'Published material cost-book revisions are immutable.'
                            USING ERRCODE = '23514';
                    END IF;
                    RETURN CASE WHEN TG_OP = 'DELETE' THEN OLD ELSE NEW END;
                END;
                $$ LANGUAGE plpgsql;

                CREATE TRIGGER trg_material_cost_book_revision_immutable
                BEFORE UPDATE OR DELETE ON cash_flow.material_cost_book_revisions
                FOR EACH ROW EXECUTE FUNCTION
                    cash_flow.reject_published_material_cost_book_revision_mutation();

                CREATE OR REPLACE FUNCTION cash_flow.reject_published_material_cost_book_line_mutation()
                RETURNS trigger AS $$
                DECLARE
                    revision_id uuid;
                    revision_status text;
                BEGIN
                    revision_id := CASE WHEN TG_OP = 'INSERT'
                        THEN NEW."MaterialCostBookRevisionId"
                        ELSE OLD."MaterialCostBookRevisionId" END;
                    SELECT "Status" INTO revision_status
                    FROM cash_flow.material_cost_book_revisions
                    WHERE "Id" = revision_id;
                    IF revision_status = 'PUBLISHED' THEN
                        RAISE EXCEPTION 'Lines of a published material cost-book revision are immutable.'
                            USING ERRCODE = '23514';
                    END IF;
                    IF TG_OP = 'UPDATE' AND
                       NEW."MaterialCostBookRevisionId" <> OLD."MaterialCostBookRevisionId" THEN
                        SELECT "Status" INTO revision_status
                        FROM cash_flow.material_cost_book_revisions
                        WHERE "Id" = NEW."MaterialCostBookRevisionId";
                        IF revision_status = 'PUBLISHED' THEN
                            RAISE EXCEPTION 'Lines cannot be moved into a published material cost-book revision.'
                                USING ERRCODE = '23514';
                        END IF;
                    END IF;
                    RETURN CASE WHEN TG_OP = 'DELETE' THEN OLD ELSE NEW END;
                END;
                $$ LANGUAGE plpgsql;

                CREATE TRIGGER trg_material_cost_book_line_immutable
                BEFORE INSERT OR UPDATE OR DELETE ON cash_flow.material_cost_book_lines
                FOR EACH ROW EXECUTE FUNCTION
                    cash_flow.reject_published_material_cost_book_line_mutation();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER IF EXISTS trg_material_cost_book_line_immutable
                    ON cash_flow.material_cost_book_lines;
                DROP FUNCTION IF EXISTS cash_flow.reject_published_material_cost_book_line_mutation();
                DROP TRIGGER IF EXISTS trg_material_cost_book_revision_immutable
                    ON cash_flow.material_cost_book_revisions;
                DROP FUNCTION IF EXISTS cash_flow.reject_published_material_cost_book_revision_mutation();
                """);

            migrationBuilder.DropTable(
                name: "material_cost_book_lines",
                schema: "cash_flow");

            migrationBuilder.DropTable(
                name: "material_cost_book_operation_receipts",
                schema: "cash_flow");

            migrationBuilder.DropTable(
                name: "material_cost_book_revisions",
                schema: "cash_flow");

            migrationBuilder.DropTable(
                name: "material_cost_books",
                schema: "cash_flow");

            migrationBuilder.DropSequence(
                name: "material_cost_book_number_seq",
                schema: "cash_flow");
        }
    }
}
