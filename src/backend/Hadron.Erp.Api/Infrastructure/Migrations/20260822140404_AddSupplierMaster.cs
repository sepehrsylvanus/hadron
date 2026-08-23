using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hadron.Erp.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSupplierMaster : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "organization");

            migrationBuilder.EnsureSchema(
                name: "procurement");

            migrationBuilder.CreateSequence(
                name: "supplier_number_seq",
                schema: "procurement");

            migrationBuilder.CreateTable(
                name: "internal_people",
                schema: "organization",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DisplayNameFa = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    DisplayNameEn = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_internal_people", x => x.Id);
                    table.CheckConstraint("ck_internal_people_version", "\"Version\" >= 1");
                });

            migrationBuilder.CreateTable(
                name: "suppliers",
                schema: "procurement",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SupplierNumber = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    NormalizedName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CountryId = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerId = table.Column<Guid>(type: "uuid", nullable: true),
                    ExpertLeadTimeScore = table.Column<int>(type: "integer", nullable: true),
                    ExpertQualityScore = table.Column<int>(type: "integer", nullable: true),
                    Notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_suppliers", x => x.Id);
                    table.CheckConstraint("ck_suppliers_lead_score", "\"ExpertLeadTimeScore\" IS NULL OR \"ExpertLeadTimeScore\" BETWEEN 1 AND 10");
                    table.CheckConstraint("ck_suppliers_quality_score", "\"ExpertQualityScore\" IS NULL OR \"ExpertQualityScore\" BETWEEN 1 AND 10");
                    table.CheckConstraint("ck_suppliers_version", "\"Version\" >= 1");
                    table.ForeignKey(
                        name: "FK_suppliers_internal_people_OwnerId",
                        column: x => x.OwnerId,
                        principalSchema: "organization",
                        principalTable: "internal_people",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_suppliers_reference_values_CountryId",
                        column: x => x.CountryId,
                        principalSchema: "reference_data",
                        principalTable: "reference_values",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "supplier_contacts",
                schema: "procurement",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SupplierId = table.Column<Guid>(type: "uuid", nullable: false),
                    FullName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    JobTitle = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    Email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: true),
                    Phone = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    Mobile = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    IsPrimary = table.Column<bool>(type: "boolean", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_supplier_contacts", x => x.Id);
                    table.CheckConstraint("ck_supplier_contacts_primary_active", "NOT \"IsPrimary\" OR \"IsActive\"");
                    table.ForeignKey(
                        name: "FK_supplier_contacts_suppliers_SupplierId",
                        column: x => x.SupplierId,
                        principalSchema: "procurement",
                        principalTable: "suppliers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "supplier_payment_profiles",
                schema: "procurement",
                columns: table => new
                {
                    SupplierId = table.Column<Guid>(type: "uuid", nullable: false),
                    PaymentTermId = table.Column<Guid>(type: "uuid", nullable: true),
                    AdvancePercent = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    BeforeShipmentPercent = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    CreditPercent = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    CreditDays = table.Column<int>(type: "integer", nullable: true),
                    CreditAnchor = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_supplier_payment_profiles", x => x.SupplierId);
                    table.CheckConstraint("ck_supplier_credit", "(\"CreditPercent\" = 0 AND \"CreditDays\" IS NULL AND \"CreditAnchor\" IS NULL) OR (\"CreditPercent\" > 0 AND \"CreditDays\" > 0 AND \"CreditAnchor\" IN ('ORDERED','SHIPPED','WAREHOUSE_RECEIPT'))");
                    table.CheckConstraint("ck_supplier_payment_percentages", "\"AdvancePercent\" BETWEEN 0 AND 100 AND \"BeforeShipmentPercent\" BETWEEN 0 AND 100 AND \"CreditPercent\" BETWEEN 0 AND 100");
                    table.ForeignKey(
                        name: "FK_supplier_payment_profiles_reference_values_PaymentTermId",
                        column: x => x.PaymentTermId,
                        principalSchema: "reference_data",
                        principalTable: "reference_values",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_supplier_payment_profiles_suppliers_SupplierId",
                        column: x => x.SupplierId,
                        principalSchema: "procurement",
                        principalTable: "suppliers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_internal_people_IsActive_DisplayNameFa",
                schema: "organization",
                table: "internal_people",
                columns: new[] { "IsActive", "DisplayNameFa" });

            migrationBuilder.CreateIndex(
                name: "ux_supplier_contacts_active_primary",
                schema: "procurement",
                table: "supplier_contacts",
                column: "SupplierId",
                unique: true,
                filter: "\"IsActive\" AND \"IsPrimary\"");

            migrationBuilder.CreateIndex(
                name: "IX_supplier_payment_profiles_PaymentTermId",
                schema: "procurement",
                table: "supplier_payment_profiles",
                column: "PaymentTermId");

            migrationBuilder.CreateIndex(
                name: "IX_suppliers_CountryId",
                schema: "procurement",
                table: "suppliers",
                column: "CountryId");

            migrationBuilder.CreateIndex(
                name: "IX_suppliers_IsActive_Name",
                schema: "procurement",
                table: "suppliers",
                columns: new[] { "IsActive", "Name" });

            migrationBuilder.CreateIndex(
                name: "IX_suppliers_NormalizedName",
                schema: "procurement",
                table: "suppliers",
                column: "NormalizedName");

            migrationBuilder.CreateIndex(
                name: "IX_suppliers_OwnerId",
                schema: "procurement",
                table: "suppliers",
                column: "OwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_suppliers_SupplierNumber",
                schema: "procurement",
                table: "suppliers",
                column: "SupplierNumber",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "supplier_contacts",
                schema: "procurement");

            migrationBuilder.DropTable(
                name: "supplier_payment_profiles",
                schema: "procurement");

            migrationBuilder.DropTable(
                name: "suppliers",
                schema: "procurement");

            migrationBuilder.DropTable(
                name: "internal_people",
                schema: "organization");

            migrationBuilder.DropSequence(
                name: "supplier_number_seq",
                schema: "procurement");
        }
    }
}
