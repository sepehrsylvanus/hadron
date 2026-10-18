using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hadron.Erp.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddForecastActual : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "planning");

            migrationBuilder.CreateTable(
                name: "business_periods",
                schema: "planning",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    JalaliYear = table.Column<int>(type: "integer", nullable: false),
                    JalaliMonth = table.Column<int>(type: "integer", nullable: false),
                    StartDate = table.Column<DateOnly>(type: "date", nullable: false),
                    EndDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false, defaultValue: "OPEN"),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_business_periods", x => x.Id);
                    table.CheckConstraint("ck_business_periods_dates", "\"EndDate\" >= \"StartDate\"");
                    table.CheckConstraint("ck_business_periods_jalali_month", "\"JalaliMonth\" BETWEEN 1 AND 12");
                    table.CheckConstraint("ck_business_periods_status", "\"Status\" IN ('OPEN','CLOSED')");
                    table.CheckConstraint("ck_business_periods_version", "\"Version\" >= 1");
                });

            migrationBuilder.CreateTable(
                name: "period_lifecycle_events",
                schema: "planning",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BusinessPeriodId = table.Column<Guid>(type: "uuid", nullable: false),
                    FromStatus = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    ToStatus = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    ActorIdentifier = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    OccurredAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_period_lifecycle_events", x => x.Id);
                    table.CheckConstraint("ck_period_lifecycle_events_transition", "(\"FromStatus\" = 'OPEN' AND \"ToStatus\" = 'CLOSED') OR (\"FromStatus\" = 'CLOSED' AND \"ToStatus\" = 'OPEN')");
                    table.ForeignKey(
                        name: "FK_period_lifecycle_events_business_periods_BusinessPeriodId",
                        column: x => x.BusinessPeriodId,
                        principalSchema: "planning",
                        principalTable: "business_periods",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "daily_actual_revisions",
                schema: "planning",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DailyActualId = table.Column<Guid>(type: "uuid", nullable: false),
                    RevisionNumber = table.Column<int>(type: "integer", nullable: false),
                    ActualQty = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    Reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    ActorIdentifier = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_daily_actual_revisions", x => x.Id);
                    table.CheckConstraint("ck_daily_actual_revisions_number", "\"RevisionNumber\" >= 1");
                    table.CheckConstraint("ck_daily_actual_revisions_qty", "\"ActualQty\" >= 0");
                    table.CheckConstraint("ck_daily_actual_revisions_reason", "\"RevisionNumber\" = 1 OR (\"Reason\" IS NOT NULL AND btrim(\"Reason\") <> '')");
                });

            migrationBuilder.CreateTable(
                name: "daily_actuals",
                schema: "planning",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    BusinessPeriodId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductionDate = table.Column<DateOnly>(type: "date", nullable: false),
                    UnitId = table.Column<Guid>(type: "uuid", nullable: false),
                    CurrentRevisionId = table.Column<Guid>(type: "uuid", nullable: true),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_daily_actuals", x => x.Id);
                    table.CheckConstraint("ck_daily_actuals_version", "\"Version\" >= 1");
                    table.ForeignKey(
                        name: "FK_daily_actuals_business_periods_BusinessPeriodId",
                        column: x => x.BusinessPeriodId,
                        principalSchema: "planning",
                        principalTable: "business_periods",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_daily_actuals_daily_actual_revisions_CurrentRevisionId",
                        column: x => x.CurrentRevisionId,
                        principalSchema: "planning",
                        principalTable: "daily_actual_revisions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_daily_actuals_products_ProductId",
                        column: x => x.ProductId,
                        principalSchema: "catalog",
                        principalTable: "products",
                        principalColumn: "ItemId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_daily_actuals_units_UnitId",
                        column: x => x.UnitId,
                        principalSchema: "reference_data",
                        principalTable: "units",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "forecast_revisions",
                schema: "planning",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ForecastSeriesId = table.Column<Guid>(type: "uuid", nullable: false),
                    RevisionNumber = table.Column<int>(type: "integer", nullable: false),
                    ForecastQty = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    Reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    ActorIdentifier = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_forecast_revisions", x => x.Id);
                    table.CheckConstraint("ck_forecast_revisions_number", "\"RevisionNumber\" >= 1");
                    table.CheckConstraint("ck_forecast_revisions_qty", "\"ForecastQty\" >= 0");
                    table.CheckConstraint("ck_forecast_revisions_reason", "\"RevisionNumber\" = 1 OR (\"Reason\" IS NOT NULL AND btrim(\"Reason\") <> '')");
                });

            migrationBuilder.CreateTable(
                name: "forecast_series",
                schema: "planning",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    BusinessPeriodId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductBaseUnitId = table.Column<Guid>(type: "uuid", nullable: false),
                    CurrentRevisionId = table.Column<Guid>(type: "uuid", nullable: true),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_forecast_series", x => x.Id);
                    table.CheckConstraint("ck_forecast_series_version", "\"Version\" >= 1");
                    table.ForeignKey(
                        name: "FK_forecast_series_business_periods_BusinessPeriodId",
                        column: x => x.BusinessPeriodId,
                        principalSchema: "planning",
                        principalTable: "business_periods",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_forecast_series_forecast_revisions_CurrentRevisionId",
                        column: x => x.CurrentRevisionId,
                        principalSchema: "planning",
                        principalTable: "forecast_revisions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_forecast_series_products_ProductId",
                        column: x => x.ProductId,
                        principalSchema: "catalog",
                        principalTable: "products",
                        principalColumn: "ItemId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_forecast_series_units_ProductBaseUnitId",
                        column: x => x.ProductBaseUnitId,
                        principalSchema: "reference_data",
                        principalTable: "units",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_business_periods_JalaliYear_JalaliMonth",
                schema: "planning",
                table: "business_periods",
                columns: new[] { "JalaliYear", "JalaliMonth" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_business_periods_StartDate_EndDate",
                schema: "planning",
                table: "business_periods",
                columns: new[] { "StartDate", "EndDate" });

            migrationBuilder.CreateIndex(
                name: "IX_daily_actual_revisions_DailyActualId_RevisionNumber",
                schema: "planning",
                table: "daily_actual_revisions",
                columns: new[] { "DailyActualId", "RevisionNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_daily_actuals_BusinessPeriodId_ProductId",
                schema: "planning",
                table: "daily_actuals",
                columns: new[] { "BusinessPeriodId", "ProductId" });

            migrationBuilder.CreateIndex(
                name: "IX_daily_actuals_CurrentRevisionId",
                schema: "planning",
                table: "daily_actuals",
                column: "CurrentRevisionId");

            migrationBuilder.CreateIndex(
                name: "IX_daily_actuals_ProductId_ProductionDate",
                schema: "planning",
                table: "daily_actuals",
                columns: new[] { "ProductId", "ProductionDate" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_daily_actuals_UnitId",
                schema: "planning",
                table: "daily_actuals",
                column: "UnitId");

            migrationBuilder.CreateIndex(
                name: "IX_forecast_revisions_ForecastSeriesId_RevisionNumber",
                schema: "planning",
                table: "forecast_revisions",
                columns: new[] { "ForecastSeriesId", "RevisionNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_forecast_series_BusinessPeriodId",
                schema: "planning",
                table: "forecast_series",
                column: "BusinessPeriodId");

            migrationBuilder.CreateIndex(
                name: "IX_forecast_series_CurrentRevisionId",
                schema: "planning",
                table: "forecast_series",
                column: "CurrentRevisionId");

            migrationBuilder.CreateIndex(
                name: "IX_forecast_series_ProductBaseUnitId",
                schema: "planning",
                table: "forecast_series",
                column: "ProductBaseUnitId");

            migrationBuilder.CreateIndex(
                name: "IX_forecast_series_ProductId_BusinessPeriodId",
                schema: "planning",
                table: "forecast_series",
                columns: new[] { "ProductId", "BusinessPeriodId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_period_lifecycle_events_BusinessPeriodId_OccurredAtUtc",
                schema: "planning",
                table: "period_lifecycle_events",
                columns: new[] { "BusinessPeriodId", "OccurredAtUtc" });

            migrationBuilder.AddForeignKey(
                name: "FK_daily_actual_revisions_daily_actuals_DailyActualId",
                schema: "planning",
                table: "daily_actual_revisions",
                column: "DailyActualId",
                principalSchema: "planning",
                principalTable: "daily_actuals",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_forecast_revisions_forecast_series_ForecastSeriesId",
                schema: "planning",
                table: "forecast_revisions",
                column: "ForecastSeriesId",
                principalSchema: "planning",
                principalTable: "forecast_series",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.Sql("""
                CREATE FUNCTION planning.reject_immutable_history_mutation()
                RETURNS trigger LANGUAGE plpgsql AS $function$
                BEGIN
                    RAISE EXCEPTION '% is immutable', TG_TABLE_NAME USING ERRCODE = '23514';
                END;
                $function$;

                CREATE TRIGGER trg_forecast_revisions_immutable
                BEFORE UPDATE OR DELETE ON planning.forecast_revisions
                FOR EACH ROW EXECUTE FUNCTION planning.reject_immutable_history_mutation();

                CREATE TRIGGER trg_daily_actual_revisions_immutable
                BEFORE UPDATE OR DELETE ON planning.daily_actual_revisions
                FOR EACH ROW EXECUTE FUNCTION planning.reject_immutable_history_mutation();

                CREATE TRIGGER trg_period_lifecycle_events_immutable
                BEFORE UPDATE OR DELETE ON planning.period_lifecycle_events
                FOR EACH ROW EXECUTE FUNCTION planning.reject_immutable_history_mutation();

                CREATE FUNCTION planning.protect_business_period_identity()
                RETURNS trigger LANGUAGE plpgsql AS $function$
                BEGIN
                    IF TG_OP = 'DELETE' THEN
                        RAISE EXCEPTION 'business period cannot be deleted' USING ERRCODE = '23514';
                    END IF;
                    IF NEW."JalaliYear" IS DISTINCT FROM OLD."JalaliYear"
                       OR NEW."JalaliMonth" IS DISTINCT FROM OLD."JalaliMonth"
                       OR NEW."StartDate" IS DISTINCT FROM OLD."StartDate"
                       OR NEW."EndDate" IS DISTINCT FROM OLD."EndDate" THEN
                        RAISE EXCEPTION 'business period identity and boundaries are immutable' USING ERRCODE = '23514';
                    END IF;
                    RETURN NEW;
                END;
                $function$;

                CREATE TRIGGER trg_business_period_identity
                BEFORE UPDATE OR DELETE ON planning.business_periods
                FOR EACH ROW EXECUTE FUNCTION planning.protect_business_period_identity();

                CREATE FUNCTION planning.protect_forecast_series_identity()
                RETURNS trigger LANGUAGE plpgsql AS $function$
                BEGIN
                    IF TG_OP = 'DELETE' THEN
                        RAISE EXCEPTION 'forecast series cannot be deleted' USING ERRCODE = '23514';
                    END IF;
                    IF NEW."ProductId" IS DISTINCT FROM OLD."ProductId"
                       OR NEW."BusinessPeriodId" IS DISTINCT FROM OLD."BusinessPeriodId"
                       OR NEW."ProductBaseUnitId" IS DISTINCT FROM OLD."ProductBaseUnitId" THEN
                        RAISE EXCEPTION 'forecast series identity and base unit snapshot are immutable' USING ERRCODE = '23514';
                    END IF;
                    RETURN NEW;
                END;
                $function$;

                CREATE TRIGGER trg_forecast_series_identity
                BEFORE UPDATE OR DELETE ON planning.forecast_series
                FOR EACH ROW EXECUTE FUNCTION planning.protect_forecast_series_identity();

                CREATE FUNCTION planning.protect_daily_actual_identity()
                RETURNS trigger LANGUAGE plpgsql AS $function$
                BEGIN
                    IF TG_OP = 'DELETE' THEN
                        RAISE EXCEPTION 'daily actual cannot be deleted' USING ERRCODE = '23514';
                    END IF;
                    IF NEW."ProductId" IS DISTINCT FROM OLD."ProductId"
                       OR NEW."BusinessPeriodId" IS DISTINCT FROM OLD."BusinessPeriodId"
                       OR NEW."ProductionDate" IS DISTINCT FROM OLD."ProductionDate"
                       OR NEW."UnitId" IS DISTINCT FROM OLD."UnitId" THEN
                        RAISE EXCEPTION 'daily actual identity and unit snapshot are immutable' USING ERRCODE = '23514';
                    END IF;
                    RETURN NEW;
                END;
                $function$;

                CREATE TRIGGER trg_daily_actual_identity
                BEFORE UPDATE OR DELETE ON planning.daily_actuals
                FOR EACH ROW EXECUTE FUNCTION planning.protect_daily_actual_identity();

                CREATE FUNCTION planning.validate_current_revision_owner()
                RETURNS trigger LANGUAGE plpgsql AS $function$
                BEGIN
                    IF NEW."CurrentRevisionId" IS NULL THEN RETURN NEW; END IF;
                    IF TG_TABLE_NAME = 'forecast_series' THEN
                        IF NOT EXISTS (SELECT 1 FROM planning.forecast_revisions r
                                      WHERE r."Id" = NEW."CurrentRevisionId" AND r."ForecastSeriesId" = NEW."Id") THEN
                            RAISE EXCEPTION 'current forecast revision belongs to another series' USING ERRCODE = '23514';
                        END IF;
                    ELSIF TG_TABLE_NAME = 'daily_actuals' THEN
                        IF NOT EXISTS (SELECT 1 FROM planning.daily_actual_revisions r
                                      WHERE r."Id" = NEW."CurrentRevisionId" AND r."DailyActualId" = NEW."Id") THEN
                            RAISE EXCEPTION 'current actual revision belongs to another aggregate' USING ERRCODE = '23514';
                        END IF;
                    END IF;
                    RETURN NEW;
                END;
                $function$;

                CREATE TRIGGER trg_forecast_current_revision_owner
                BEFORE INSERT OR UPDATE OF "CurrentRevisionId" ON planning.forecast_series
                FOR EACH ROW EXECUTE FUNCTION planning.validate_current_revision_owner();

                CREATE TRIGGER trg_actual_current_revision_owner
                BEFORE INSERT OR UPDATE OF "CurrentRevisionId" ON planning.daily_actuals
                FOR EACH ROW EXECUTE FUNCTION planning.validate_current_revision_owner();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER IF EXISTS trg_actual_current_revision_owner ON planning.daily_actuals;
                DROP TRIGGER IF EXISTS trg_forecast_current_revision_owner ON planning.forecast_series;
                DROP TRIGGER IF EXISTS trg_daily_actual_identity ON planning.daily_actuals;
                DROP TRIGGER IF EXISTS trg_forecast_series_identity ON planning.forecast_series;
                DROP TRIGGER IF EXISTS trg_business_period_identity ON planning.business_periods;
                DROP TRIGGER IF EXISTS trg_period_lifecycle_events_immutable ON planning.period_lifecycle_events;
                DROP TRIGGER IF EXISTS trg_daily_actual_revisions_immutable ON planning.daily_actual_revisions;
                DROP TRIGGER IF EXISTS trg_forecast_revisions_immutable ON planning.forecast_revisions;
                DROP FUNCTION IF EXISTS planning.validate_current_revision_owner();
                DROP FUNCTION IF EXISTS planning.protect_daily_actual_identity();
                DROP FUNCTION IF EXISTS planning.protect_forecast_series_identity();
                DROP FUNCTION IF EXISTS planning.protect_business_period_identity();
                DROP FUNCTION IF EXISTS planning.reject_immutable_history_mutation();
                """);

            migrationBuilder.DropForeignKey(
                name: "FK_daily_actual_revisions_daily_actuals_DailyActualId",
                schema: "planning",
                table: "daily_actual_revisions");

            migrationBuilder.DropForeignKey(
                name: "FK_forecast_series_business_periods_BusinessPeriodId",
                schema: "planning",
                table: "forecast_series");

            migrationBuilder.DropForeignKey(
                name: "FK_forecast_revisions_forecast_series_ForecastSeriesId",
                schema: "planning",
                table: "forecast_revisions");

            migrationBuilder.DropTable(
                name: "period_lifecycle_events",
                schema: "planning");

            migrationBuilder.DropTable(
                name: "daily_actuals",
                schema: "planning");

            migrationBuilder.DropTable(
                name: "daily_actual_revisions",
                schema: "planning");

            migrationBuilder.DropTable(
                name: "business_periods",
                schema: "planning");

            migrationBuilder.DropTable(
                name: "forecast_series",
                schema: "planning");

            migrationBuilder.DropTable(
                name: "forecast_revisions",
                schema: "planning");
        }
    }
}
