using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hadron.Erp.Api.Infrastructure.Migrations;

[DbContext(typeof(HadronDbContext))]
[Migration("20260907150000_AddCashFlowReportingFxRates")]
public partial class AddCashFlowReportingFxRates : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.EnsureSchema(name: "cash_flow");

        migrationBuilder.CreateTable(
            name: "reporting_fx_rate_sets",
            schema: "cash_flow",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                BaseCurrencyId = table.Column<Guid>(type: "uuid", nullable: false),
                EffectiveDate = table.Column<DateOnly>(type: "date", nullable: false),
                Revision = table.Column<int>(type: "integer", nullable: false),
                Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                SupersedesRateSetId = table.Column<Guid>(type: "uuid", nullable: true),
                ClientRequestId = table.Column<Guid>(type: "uuid", nullable: false),
                RequestFingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                ChangeReason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                CreatedByIdentifier = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false,
                    defaultValueSql: "CURRENT_TIMESTAMP"),
                UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false,
                    defaultValueSql: "CURRENT_TIMESTAMP"),
                PublishedByIdentifier = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                PublishedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                PublishReason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                Version = table.Column<long>(type: "bigint", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_reporting_fx_rate_sets", x => x.Id);
                table.CheckConstraint("ck_reporting_fx_rate_sets_not_self_superseding",
                    "\"SupersedesRateSetId\" IS NULL OR \"SupersedesRateSetId\" <> \"Id\"");
                table.CheckConstraint("ck_reporting_fx_rate_sets_publish_state",
                    "(\"Status\" = 'DRAFT' AND \"PublishedAtUtc\" IS NULL AND \"PublishReason\" IS NULL) OR " +
                    "(\"Status\" = 'PUBLISHED' AND \"PublishedAtUtc\" IS NOT NULL AND \"PublishReason\" IS NOT NULL)");
                table.CheckConstraint("ck_reporting_fx_rate_sets_revision", "\"Revision\" >= 1");
                table.CheckConstraint("ck_reporting_fx_rate_sets_status", "\"Status\" IN ('DRAFT','PUBLISHED')");
                table.CheckConstraint("ck_reporting_fx_rate_sets_version", "\"Version\" >= 1");
                table.ForeignKey(
                    name: "FK_reporting_fx_rate_sets_currencies_BaseCurrencyId",
                    column: x => x.BaseCurrencyId,
                    principalSchema: "reference_data",
                    principalTable: "currencies",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_reporting_fx_rate_sets_reporting_fx_rate_sets_SupersedesRateSetId",
                    column: x => x.SupersedesRateSetId,
                    principalSchema: "cash_flow",
                    principalTable: "reporting_fx_rate_sets",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "reporting_fx_rates",
            schema: "cash_flow",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                RateSetId = table.Column<Guid>(type: "uuid", nullable: false),
                CurrencyId = table.Column<Guid>(type: "uuid", nullable: false),
                UnitsPerUsd = table.Column<decimal>(type: "numeric(28,12)", precision: 28, scale: 12, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_reporting_fx_rates", x => x.Id);
                table.CheckConstraint("ck_reporting_fx_rates_positive", "\"UnitsPerUsd\" > 0");
                table.ForeignKey(
                    name: "FK_reporting_fx_rates_currencies_CurrencyId",
                    column: x => x.CurrencyId,
                    principalSchema: "reference_data",
                    principalTable: "currencies",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_reporting_fx_rates_reporting_fx_rate_sets_RateSetId",
                    column: x => x.RateSetId,
                    principalSchema: "cash_flow",
                    principalTable: "reporting_fx_rate_sets",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "reporting_fx_operation_receipts",
            schema: "cash_flow",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                RateSetId = table.Column<Guid>(type: "uuid", nullable: false),
                OperationType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                ClientRequestId = table.Column<Guid>(type: "uuid", nullable: false),
                RequestFingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                ResultVersion = table.Column<long>(type: "bigint", nullable: false),
                CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false,
                    defaultValueSql: "CURRENT_TIMESTAMP")
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_reporting_fx_operation_receipts", x => x.Id);
                table.CheckConstraint("ck_reporting_fx_operation_receipts_type",
                    "\"OperationType\" IN ('UPDATE_DRAFT','PUBLISH')");
                table.CheckConstraint("ck_reporting_fx_operation_receipts_version", "\"ResultVersion\" >= 1");
                table.ForeignKey(
                    name: "FK_reporting_fx_operation_receipts_reporting_fx_rate_sets_RateSetId",
                    column: x => x.RateSetId,
                    principalSchema: "cash_flow",
                    principalTable: "reporting_fx_rate_sets",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_reporting_fx_rate_sets_BaseCurrencyId_EffectiveDate",
            schema: "cash_flow",
            table: "reporting_fx_rate_sets",
            columns: new[] { "BaseCurrencyId", "EffectiveDate" },
            unique: true,
            filter: "\"Status\" = 'DRAFT'");
        migrationBuilder.CreateIndex(
            name: "IX_reporting_fx_rate_sets_BaseCurrencyId_EffectiveDate_Revision",
            schema: "cash_flow",
            table: "reporting_fx_rate_sets",
            columns: new[] { "BaseCurrencyId", "EffectiveDate", "Revision" },
            unique: true);
        migrationBuilder.CreateIndex(
            name: "IX_reporting_fx_rate_sets_BaseCurrencyId_Status_EffectiveDate_Revision",
            schema: "cash_flow",
            table: "reporting_fx_rate_sets",
            columns: new[] { "BaseCurrencyId", "Status", "EffectiveDate", "Revision" });
        migrationBuilder.CreateIndex(
            name: "IX_reporting_fx_rate_sets_ClientRequestId",
            schema: "cash_flow",
            table: "reporting_fx_rate_sets",
            column: "ClientRequestId",
            unique: true);
        migrationBuilder.CreateIndex(
            name: "IX_reporting_fx_rate_sets_SupersedesRateSetId",
            schema: "cash_flow",
            table: "reporting_fx_rate_sets",
            column: "SupersedesRateSetId",
            unique: true,
            filter: "\"SupersedesRateSetId\" IS NOT NULL");
        migrationBuilder.CreateIndex(
            name: "IX_reporting_fx_rates_CurrencyId",
            schema: "cash_flow",
            table: "reporting_fx_rates",
            column: "CurrencyId");
        migrationBuilder.CreateIndex(
            name: "IX_reporting_fx_rates_RateSetId_CurrencyId",
            schema: "cash_flow",
            table: "reporting_fx_rates",
            columns: new[] { "RateSetId", "CurrencyId" },
            unique: true);
        migrationBuilder.CreateIndex(
            name: "IX_reporting_fx_operation_receipts_OperationType_ClientRequestId",
            schema: "cash_flow",
            table: "reporting_fx_operation_receipts",
            columns: new[] { "OperationType", "ClientRequestId" },
            unique: true);
        migrationBuilder.CreateIndex(
            name: "IX_reporting_fx_operation_receipts_RateSetId",
            schema: "cash_flow",
            table: "reporting_fx_operation_receipts",
            column: "RateSetId");

        migrationBuilder.Sql("""
            CREATE OR REPLACE FUNCTION cash_flow.guard_published_reporting_fx_rate_set() RETURNS trigger AS $$
            BEGIN
                IF OLD."Status" = 'PUBLISHED' THEN
                    RAISE EXCEPTION 'Published reporting FX rate sets are immutable' USING ERRCODE = '23514';
                END IF;
                RETURN CASE WHEN TG_OP = 'DELETE' THEN OLD ELSE NEW END;
            END;
            $$ LANGUAGE plpgsql;

            CREATE TRIGGER trg_reporting_fx_rate_set_immutable
            BEFORE UPDATE OR DELETE ON cash_flow.reporting_fx_rate_sets
            FOR EACH ROW EXECUTE FUNCTION cash_flow.guard_published_reporting_fx_rate_set();

            CREATE OR REPLACE FUNCTION cash_flow.guard_published_reporting_fx_rate_line() RETURNS trigger AS $$
            BEGIN
                IF TG_OP = 'INSERT' THEN
                    IF EXISTS (SELECT 1 FROM cash_flow.reporting_fx_rate_sets
                               WHERE "Id" = NEW."RateSetId" AND "Status" = 'PUBLISHED') THEN
                        RAISE EXCEPTION 'Rates of a published reporting FX rate set are immutable' USING ERRCODE = '23514';
                    END IF;
                    RETURN NEW;
                ELSIF TG_OP = 'UPDATE' THEN
                    IF EXISTS (SELECT 1 FROM cash_flow.reporting_fx_rate_sets
                               WHERE "Id" IN (OLD."RateSetId", NEW."RateSetId") AND "Status" = 'PUBLISHED') THEN
                        RAISE EXCEPTION 'Rates of a published reporting FX rate set are immutable' USING ERRCODE = '23514';
                    END IF;
                    RETURN NEW;
                ELSE
                    IF EXISTS (SELECT 1 FROM cash_flow.reporting_fx_rate_sets
                               WHERE "Id" = OLD."RateSetId" AND "Status" = 'PUBLISHED') THEN
                        RAISE EXCEPTION 'Rates of a published reporting FX rate set are immutable' USING ERRCODE = '23514';
                    END IF;
                    RETURN OLD;
                END IF;
            END;
            $$ LANGUAGE plpgsql;

            CREATE TRIGGER trg_reporting_fx_rate_line_immutable
            BEFORE INSERT OR UPDATE OR DELETE ON cash_flow.reporting_fx_rates
            FOR EACH ROW EXECUTE FUNCTION cash_flow.guard_published_reporting_fx_rate_line();

            CREATE OR REPLACE FUNCTION cash_flow.guard_reporting_fx_operation_receipt() RETURNS trigger AS $$
            BEGIN
                RAISE EXCEPTION 'Reporting FX operation receipts are immutable' USING ERRCODE = '23514';
            END;
            $$ LANGUAGE plpgsql;

            CREATE TRIGGER trg_reporting_fx_operation_receipt_immutable
            BEFORE UPDATE OR DELETE ON cash_flow.reporting_fx_operation_receipts
            FOR EACH ROW EXECUTE FUNCTION cash_flow.guard_reporting_fx_operation_receipt();
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DROP TRIGGER IF EXISTS trg_reporting_fx_operation_receipt_immutable ON cash_flow.reporting_fx_operation_receipts;
            DROP FUNCTION IF EXISTS cash_flow.guard_reporting_fx_operation_receipt();
            DROP TRIGGER IF EXISTS trg_reporting_fx_rate_line_immutable ON cash_flow.reporting_fx_rates;
            DROP FUNCTION IF EXISTS cash_flow.guard_published_reporting_fx_rate_line();
            DROP TRIGGER IF EXISTS trg_reporting_fx_rate_set_immutable ON cash_flow.reporting_fx_rate_sets;
            DROP FUNCTION IF EXISTS cash_flow.guard_published_reporting_fx_rate_set();
            """);

        migrationBuilder.DropTable(name: "reporting_fx_operation_receipts", schema: "cash_flow");
        migrationBuilder.DropTable(name: "reporting_fx_rates", schema: "cash_flow");
        migrationBuilder.DropTable(name: "reporting_fx_rate_sets", schema: "cash_flow");
    }
}
