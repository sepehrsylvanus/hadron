using System.Net;
using System.Net.Http.Json;
using Hadron.Erp.Api.Infrastructure;
using Hadron.Erp.Api.Modules.Bom.Domain;
using Hadron.Erp.Api.Modules.Catalog.Domain;
using Hadron.Erp.Api.Modules.Mrp.Application;
using Hadron.Erp.Api.Modules.Mrp.Domain;
using Hadron.Erp.Api.Modules.Planning.Application;
using Hadron.Erp.Api.Modules.Planning.Domain;
using Hadron.Erp.Api.Modules.Products.Domain;
using Hadron.Erp.Api.Modules.ReferenceData.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Hadron.Erp.IntegrationTests;

[Collection("ReferenceData")]
public sealed class MrpRunTests : IClassFixture<FoundationApiFactory>, IAsyncLifetime
{
    private readonly FoundationApiFactory _factory;
    private readonly HttpClient _client;
    private readonly List<Guid> _items = [];
    private readonly List<Guid> _units = [];
    private readonly List<Guid> _categories = [];

    public MrpRunTests(FoundationApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    public async Task InitializeAsync()
    {
        await using var db = await Db();
        await db.Database.MigrateAsync();
        TestDatabaseSafety.EnsureSafeDatabaseName(db.Database.GetDbConnection().Database);
        await db.Database.ExecuteSqlRawAsync("""
            TRUNCATE planning.mrp_run_demand_traces, planning.mrp_run_daily_results,
              planning.mrp_run_item_summaries, planning.mrp_run_exceptions,
              planning.mrp_run_external_source_inputs, planning.mrp_run_forecast_period_inputs,
              planning.mrp_run_route_decisions, planning.mrp_run_route_availability_inputs,
              planning.mrp_run_firm_supply_inputs, planning.mrp_run_inventory_inputs,
              planning.mrp_run_bom_inputs, planning.mrp_run_forecast_inputs,
              planning.mrp_run_item_inputs, planning.mrp_runs RESTART IDENTITY CASCADE
            """);
        _factory.AuditSink.Clear();
    }

    public async Task DisposeAsync()
    {
        await using var db = await Db();
        await db.Database.ExecuteSqlRawAsync("""
            TRUNCATE planning.mrp_run_demand_traces, planning.mrp_run_daily_results,
              planning.mrp_run_item_summaries, planning.mrp_run_exceptions,
              planning.mrp_run_external_source_inputs, planning.mrp_run_forecast_period_inputs,
              planning.mrp_run_route_decisions, planning.mrp_run_route_availability_inputs,
              planning.mrp_run_firm_supply_inputs, planning.mrp_run_inventory_inputs,
              planning.mrp_run_bom_inputs, planning.mrp_run_forecast_inputs,
            planning.mrp_run_item_inputs, planning.mrp_runs RESTART IDENTITY CASCADE
            """);
        await db.Database.ExecuteSqlRawAsync("""
            TRUNCATE planning.period_lifecycle_events, planning.daily_actual_revisions,
              planning.daily_actuals, planning.forecast_revisions, planning.forecast_series,
              planning.business_periods RESTART IDENTITY CASCADE
            """);
        await db.Database.ExecuteSqlRawAsync(
            "TRUNCATE engineering.bom_lines, engineering.bom_revisions RESTART IDENTITY CASCADE");
        await db.MaterialItemProfiles.Where(x => _items.Contains(x.ItemId)).ExecuteDeleteAsync();
        await db.Products.Where(x => _items.Contains(x.ItemId)).ExecuteDeleteAsync();
        await db.Items.Where(x => _items.Contains(x.Id)).ExecuteDeleteAsync();
        await db.ReferenceValues.Where(x => _categories.Contains(x.Id)).ExecuteDeleteAsync();
        await db.Units.Where(x => _units.Contains(x.Id)).ExecuteDeleteAsync();
    }

    [Fact]
    public async Task Run_uses_server_number_default_twelve_period_horizon_and_locked_assumptions()
    {
        var asOf = new DateOnly(2026, 8, 24);
        var responses = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ =>
            _client.PostAsJsonAsync("/api/mrp-runs", new CreateMrpRunRequest(asOf, null, []))));

        Assert.All(responses, response => Assert.Equal(HttpStatusCode.Created, response.StatusCode));
        var runs = await Task.WhenAll(responses.Select(response =>
            response.Content.ReadFromJsonAsync<MrpRunResponse>()));
        Assert.All(runs, run => Assert.NotNull(run));
        Assert.Equal(4, runs.Select(run => run!.RunNumber).Distinct().Count());
        Assert.All(runs, run => Assert.Matches("^MRP-RUN-[0-9]{6,}$", run!.RunNumber));
        Assert.All(runs, run => Assert.Equal(MrpRunStatuses.Completed, run!.Status));
        Assert.All(runs, run => Assert.Contains("INTERNAL_PROCESS_LEAD_TIME_DAYS=0", run!.Assumptions));

        var calendar = _factory.Services.GetRequiredService<IPlanningCalendar>();
        var (year, month) = calendar.GetJalaliPeriod(asOf);
        for (var index = 0; index < 11; index++)
        {
            month++;
            if (month <= 12) continue;
            month = 1;
            year++;
        }
        var expectedEnd = calendar.GetPeriodBounds(year, month).EndDate;
        Assert.All(runs, run => Assert.Equal(expectedEnd, run!.HorizonEndDate));
        Assert.Equal(4, _factory.AuditSink.Snapshot().Count(x => x.Action == "EXECUTE"));
    }

    [Fact]
    public async Task Terminal_run_and_all_children_are_database_immutable_and_outputs_use_twelve_decimals()
    {
        var response = await _client.PostAsJsonAsync("/api/mrp-runs",
            new CreateMrpRunRequest(new DateOnly(2026, 8, 24), new DateOnly(2026, 8, 24), []));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var run = (await response.Content.ReadFromJsonAsync<MrpRunResponse>())!;

        await using var db = await Db();
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        await connection.OpenAsync();
        await using (var command = new NpgsqlCommand("""
            SELECT count(*)
            FROM information_schema.columns
            WHERE table_schema = 'planning'
              AND table_name IN ('mrp_run_daily_results','mrp_run_item_summaries','mrp_run_demand_traces')
              AND data_type = 'numeric' AND numeric_precision = 28 AND numeric_scale = 12
            """, connection))
        {
            Assert.Equal(16L, (long)(await command.ExecuteScalarAsync())!);
        }

        var rename = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE planning.mrp_runs SET \"RunNumber\" = {'X'} WHERE \"Id\" = {run.Id}"));
        Assert.Equal("P0001", rename.SqlState);

        var child = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO planning.mrp_run_exceptions
              ("Id","RunId","ItemId","Date","Code","Severity","AffectsDemand","Message","SourceReference")
            VALUES ({Guid.NewGuid()},{run.Id},NULL,NULL,{"LATE_WRITE"},{"ERROR"},TRUE,{"forbidden"},NULL)
            """));
        Assert.Equal("P0001", child.SqlState);

        var persisted = await db.MrpRuns.AsNoTracking().SingleAsync(x => x.Id == run.Id);
        Assert.Equal(run.RunNumber, persisted.RunNumber);
        Assert.Equal(MrpRunStatuses.Completed, persisted.Status);
    }

    [Fact]
    public async Task Validation_rejects_invalid_horizon_and_route_without_creating_a_run()
    {
        var before = await CountRuns();
        var invalidHorizon = await _client.PostAsJsonAsync("/api/mrp-runs",
            new CreateMrpRunRequest(new DateOnly(2026, 8, 24), new DateOnly(2026, 8, 23), []));
        Assert.Equal(HttpStatusCode.BadRequest, invalidHorizon.StatusCode);

        var invalidRoute = await _client.PostAsJsonAsync("/api/mrp-runs",
            new CreateMrpRunRequest(new DateOnly(2026, 8, 24), null,
                [new MrpRouteDecisionInput(Guid.NewGuid(), "SPLIT")]));
        Assert.Equal(HttpStatusCode.BadRequest, invalidRoute.StatusCode);
        Assert.Equal(before, await CountRuns());
        Assert.Empty(_factory.AuditSink.Snapshot());
    }

    [Fact]
    public async Task Missing_period_is_explicit_and_last_day_remainder_is_unallocatable_without_spill()
    {
        var product = await AddProduct();
        var calendar = _factory.Services.GetRequiredService<IPlanningCalendar>();
        var bounds = calendar.GetPeriodBounds(1412, 1);
        await AddForecast(product.ProductId, product.UnitId, 1412, 1, bounds.StartDate, bounds.EndDate, 10);

        var lastDayResponse = await _client.PostAsJsonAsync("/api/mrp-runs",
            new CreateMrpRunRequest(bounds.EndDate, bounds.EndDate, []));
        Assert.Equal(HttpStatusCode.Created, lastDayResponse.StatusCode);
        var lastDayRun = (await lastDayResponse.Content.ReadFromJsonAsync<MrpRunResponse>())!;
        Assert.Equal(MrpRunStatuses.CompletedWithExceptions, lastDayRun.Status);
        var lastDayExceptions = await _client.GetFromJsonAsync<MrpExceptionListResponse>(
            $"/api/mrp-runs/{lastDayRun.Id}/exceptions?page=1&pageSize=100");
        Assert.Contains(lastDayExceptions!.Items, x => x.Code == "CURRENT_PERIOD_REMAINDER_UNALLOCATABLE");
        await using (var db = await Db())
        {
            Assert.Empty(await db.MrpRunForecastInputs.Where(x => x.RunId == lastDayRun.Id).ToListAsync());
            var period = await db.MrpRunForecastPeriodInputs.SingleAsync(x => x.RunId == lastDayRun.Id);
            Assert.Equal("UNALLOCATABLE", period.AllocationState);
            Assert.Equal("CURRENT_PERIOD_REMAINDER_UNALLOCATABLE", period.ExceptionCode);
            Assert.Equal(10m, period.RemainingForecastQty);
        }

        var missingBounds = calendar.GetPeriodBounds(1413, 1);
        var missingResponse = await _client.PostAsJsonAsync("/api/mrp-runs",
            new CreateMrpRunRequest(missingBounds.StartDate, missingBounds.EndDate, []));
        Assert.Equal(HttpStatusCode.Created, missingResponse.StatusCode);
        var missingRun = (await missingResponse.Content.ReadFromJsonAsync<MrpRunResponse>())!;
        var missingExceptions = await _client.GetFromJsonAsync<MrpExceptionListResponse>(
            $"/api/mrp-runs/{missingRun.Id}/exceptions?page=1&pageSize=100");
        Assert.Contains(missingExceptions!.Items, x => x.ItemId is null &&
            x.Code == "MISSING_BUSINESS_PERIOD" && x.AffectsDemand);
    }

    [Fact]
    public async Task Period_snapshot_distinguishes_zero_missing_and_actual_greater_than_forecast()
    {
        var zero = await AddProduct();
        var missing = await AddProduct();
        var actualOver = await AddProduct();
        var calendar = _factory.Services.GetRequiredService<IPlanningCalendar>();
        var bounds = calendar.GetPeriodBounds(1405, 6);
        var asOf = bounds.StartDate.AddDays(2);
        await using (var db = await Db())
        {
            var period = new BusinessPeriod(1405, 6, bounds.StartDate, bounds.EndDate);
            db.BusinessPeriods.Add(period);
            await db.SaveChangesAsync();
            var zeroSeries = new ForecastSeries(zero.ProductId, period.Id, zero.UnitId);
            var actualSeries = new ForecastSeries(actualOver.ProductId, period.Id, actualOver.UnitId);
            db.ForecastSeries.AddRange(zeroSeries, actualSeries);
            await db.SaveChangesAsync();
            var zeroRevision = new ForecastRevision(zeroSeries.Id, 1, 0, null, null);
            var actualRevision = new ForecastRevision(actualSeries.Id, 1, 5, null, null);
            db.ForecastRevisions.AddRange(zeroRevision, actualRevision);
            await db.SaveChangesAsync();
            zeroSeries.SetCurrent(zeroRevision); actualSeries.SetCurrent(actualRevision);
            await db.SaveChangesAsync();
            var actual = new DailyActual(actualOver.ProductId, period.Id, asOf, actualOver.UnitId);
            db.DailyActuals.Add(actual);
            await db.SaveChangesAsync();
            var actualRevisionRow = new DailyActualRevision(actual.Id, 1, 8, null, null);
            db.DailyActualRevisions.Add(actualRevisionRow);
            await db.SaveChangesAsync();
            actual.SetCurrent(actualRevisionRow);
            await db.SaveChangesAsync();
            var missingActual = new DailyActual(missing.ProductId, period.Id, asOf, missing.UnitId);
            db.DailyActuals.Add(missingActual);
            await db.SaveChangesAsync();
            var missingActualRevision = new DailyActualRevision(missingActual.Id, 1, 0, null, null);
            db.DailyActualRevisions.Add(missingActualRevision);
            await db.SaveChangesAsync();
            missingActual.SetCurrent(missingActualRevision);
            await db.SaveChangesAsync();
        }

        var response = await _client.PostAsJsonAsync("/api/mrp-runs",
            new CreateMrpRunRequest(asOf, bounds.EndDate, []));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var run = (await response.Content.ReadFromJsonAsync<MrpRunResponse>())!;
        await using var verify = await Db();
        var snapshots = await verify.MrpRunForecastPeriodInputs.AsNoTracking()
            .Where(x => x.RunId == run.Id).ToListAsync();
        var zeroRow = snapshots.Single(x => x.ProductId == zero.ProductId);
        Assert.Equal(0m, zeroRow.OfficialForecastQty);
        Assert.Equal(0m, zeroRow.RemainingForecastQty);
        Assert.Equal("NO_ALLOCATION_REQUIRED", zeroRow.AllocationState);
        var missingRow = snapshots.Single(x => x.ProductId == missing.ProductId);
        Assert.Null(missingRow.OfficialForecastQty);
        Assert.Null(missingRow.ForecastSeriesId);
        Assert.Equal("MISSING_FORECAST", missingRow.AllocationState);
        var actualRow = snapshots.Single(x => x.ProductId == actualOver.ProductId);
        Assert.Equal(8m, actualRow.ActualMtdQty);
        Assert.Equal(0m, actualRow.RemainingForecastQty);
        Assert.Equal("NO_ALLOCATION_REQUIRED", actualRow.AllocationState);
        Assert.Empty(await verify.MrpRunForecastInputs.Where(x => x.RunId == run.Id).ToListAsync());
        var changed = "CHANGED";
        var immutable = await Assert.ThrowsAsync<PostgresException>(() => verify.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE planning.mrp_run_forecast_period_inputs SET \"AllocationState\" = {changed} WHERE \"Id\" = {zeroRow.Id}"));
        Assert.Equal("P0001", immutable.SqlState);
    }

    [Fact]
    public async Task Gd09_persisted_run_snapshot_output_and_trace_do_not_drift_after_current_forecast_changes()
    {
        var product = await AddProduct();
        var calendar = _factory.Services.GetRequiredService<IPlanningCalendar>();
        var bounds = calendar.GetPeriodBounds(1415, 1);
        await AddForecast(product.ProductId, product.UnitId, 1415, 1, bounds.StartDate, bounds.EndDate, 10);
        var response = await _client.PostAsJsonAsync("/api/mrp-runs",
            new CreateMrpRunRequest(bounds.StartDate.AddDays(-1), bounds.StartDate.AddDays(2), []));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var run = (await response.Content.ReadFromJsonAsync<MrpRunResponse>())!;

        string beforeTrace = await _client.GetStringAsync(
            $"/api/mrp-runs/{run.Id}/items/{product.ProductId}/trace?demandDate={bounds.StartDate:yyyy-MM-dd}");
        List<(DateOnly Date, decimal Gross, decimal Net)> beforeResults;
        List<(Guid? Revision, decimal? Official, decimal? Remaining, string State)> beforePeriods;
        List<(Guid? Category, string? Code, string? Fa, string? En, int? Critical, int? Safe, int? Excess)> beforeItemContext;
        await using (var beforeDb = await Db())
        {
            beforeResults = await beforeDb.MrpRunDailyResults.AsNoTracking().Where(x => x.RunId == run.Id)
                .OrderBy(x => x.Date).Select(x => new ValueTuple<DateOnly, decimal, decimal>(x.Date, x.GrossDemand, x.NetRequirement)).ToListAsync();
            beforePeriods = await beforeDb.MrpRunForecastPeriodInputs.AsNoTracking().Where(x => x.RunId == run.Id)
                .Select(x => new ValueTuple<Guid?, decimal?, decimal?, string>(x.ForecastRevisionId,
                    x.OfficialForecastQty, x.RemainingForecastQty, x.AllocationState)).ToListAsync();
            beforeItemContext = await beforeDb.MrpRunItemInputs.AsNoTracking().Where(x => x.RunId == run.Id)
                .Select(x => new ValueTuple<Guid?, string?, string?, string?, int?, int?, int?>(x.CategoryId,
                    x.CategoryCode, x.CategoryNameFa, x.CategoryNameEn, x.CriticalDays,
                    x.SafeStartDays, x.ExcessStartDays)).ToListAsync();
            Assert.All(beforeItemContext, x => Assert.NotNull(x.Category));
            Assert.All(beforeItemContext, x => Assert.False(string.IsNullOrWhiteSpace(x.Code)));
        }

        await using (var mutate = await Db())
        {
            var series = await mutate.ForecastSeries.Include(x => x.CurrentRevision)
                .SingleAsync(x => x.ProductId == product.ProductId);
            var revision = new ForecastRevision(series.Id, 2, 99, "source changed after run", null);
            mutate.ForecastRevisions.Add(revision);
            await mutate.SaveChangesAsync();
            series.SetCurrent(revision);
            await mutate.SaveChangesAsync();

            var categoryType = await mutate.ReferenceTypes.SingleAsync(x => x.Code == "PART_CATEGORY");
            var category = new ReferenceValue(categoryType.Id, $"GD09-{Guid.NewGuid():N}"[..20],
                $"GD09-{Guid.NewGuid():N}"[..20], "گروه GD09", "GD09 category", 0);
            var part = new Item(ItemTypes.Part, $"PRT-{Random.Shared.NextInt64(1_000_000, 9_999_999)}",
                "قطعه GD09", "GD09 part", product.UnitId, null);
            mutate.AddRange(category, part, new MaterialItemProfile(part.Id, category.Id, SupplyMethods.Buy,
                null, 0, 0, 0, 0, 0));
            await mutate.SaveChangesAsync();
            _categories.Add(category.Id); _items.Add(part.Id);
            var bom = new BomRevision($"BOM-GD09-{Guid.NewGuid():N}"[..28], product.ProductId,
                product.UnitId, 1, bounds.StartDate, null, "created after historical run");
            var line = new BomLine(bom.Id, part.Id, product.UnitId, 1, 0, 1);
            mutate.AddRange(bom, line);
            await mutate.SaveChangesAsync();
            bom.Approve(null);
            await mutate.SaveChangesAsync();
        }

        await using (var afterDb = await Db())
        {
            var afterResults = await afterDb.MrpRunDailyResults.AsNoTracking().Where(x => x.RunId == run.Id)
                .OrderBy(x => x.Date).Select(x => new ValueTuple<DateOnly, decimal, decimal>(x.Date, x.GrossDemand, x.NetRequirement)).ToListAsync();
            var afterPeriods = await afterDb.MrpRunForecastPeriodInputs.AsNoTracking().Where(x => x.RunId == run.Id)
                .Select(x => new ValueTuple<Guid?, decimal?, decimal?, string>(x.ForecastRevisionId,
                    x.OfficialForecastQty, x.RemainingForecastQty, x.AllocationState)).ToListAsync();
            var afterItemContext = await afterDb.MrpRunItemInputs.AsNoTracking().Where(x => x.RunId == run.Id)
                .Select(x => new ValueTuple<Guid?, string?, string?, string?, int?, int?, int?>(x.CategoryId,
                    x.CategoryCode, x.CategoryNameFa, x.CategoryNameEn, x.CriticalDays,
                    x.SafeStartDays, x.ExcessStartDays)).ToListAsync();
            Assert.Equal(beforeResults, afterResults);
            Assert.Equal(beforePeriods, afterPeriods);
            Assert.Equal(beforeItemContext, afterItemContext);
        }
        Assert.Equal(beforeTrace, await _client.GetStringAsync(
            $"/api/mrp-runs/{run.Id}/items/{product.ProductId}/trace?demandDate={bounds.StartDate:yyyy-MM-dd}"));
    }

    [Fact]
    public async Task Technical_failure_exposes_failed_run_without_partial_official_results_or_success_audit()
    {
        var product = await AddProduct();
        var calendar = _factory.Services.GetRequiredService<IPlanningCalendar>();
        var bounds = calendar.GetPeriodBounds(1416, 1);
        await AddForecast(product.ProductId, product.UnitId, 1416, 1, bounds.StartDate, bounds.EndDate, 3);
        await using (var guard = await Db())
            await guard.Database.ExecuteSqlRawAsync("""
                CREATE OR REPLACE FUNCTION planning.test_fail_mrp_results() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN RAISE EXCEPTION 'TEST_MRP_RESULT_FAILURE'; END $$;
                CREATE TRIGGER trg_test_fail_mrp_results BEFORE INSERT ON planning.mrp_run_daily_results
                FOR EACH ROW EXECUTE FUNCTION planning.test_fail_mrp_results();
                """);
        try
        {
            var response = await _client.PostAsJsonAsync("/api/mrp-runs",
                new CreateMrpRunRequest(bounds.StartDate.AddDays(-1), bounds.StartDate, []));
            Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
            await using var verify = await Db();
            var failed = await verify.MrpRuns.AsNoTracking().OrderByDescending(x => x.StartedAtUtc).FirstAsync();
            Assert.Equal(MrpRunStatuses.Failed, failed.Status);
            Assert.Empty(await verify.MrpRunDailyResults.Where(x => x.RunId == failed.Id).ToListAsync());
            Assert.Empty(await verify.MrpRunItemSummaries.Where(x => x.RunId == failed.Id).ToListAsync());
            Assert.Empty(await verify.MrpRunDemandTraces.Where(x => x.RunId == failed.Id).ToListAsync());
            Assert.DoesNotContain(_factory.AuditSink.Snapshot(), x => x.Action == "EXECUTE" && x.EntityId == failed.Id);
        }
        finally
        {
            await using var cleanup = await Db();
            await cleanup.Database.ExecuteSqlRawAsync("""
                DROP TRIGGER IF EXISTS trg_test_fail_mrp_results ON planning.mrp_run_daily_results;
                DROP FUNCTION IF EXISTS planning.test_fail_mrp_results();
                """);
        }
    }

    private async Task<(Guid ProductId, Guid UnitId)> AddProduct()
    {
        await using var db = await Db();
        var unit = new Unit($"MRP-{Guid.NewGuid():N}"[..20], $"MRP-{Guid.NewGuid():N}"[..20],
            "واحد MRP", "MRP unit", null, 0);
        var type = await db.ReferenceTypes.SingleAsync(x => x.Code == "PRODUCT_CATEGORY");
        var category = new ReferenceValue(type.Id, $"MRP-{Guid.NewGuid():N}"[..20],
            $"MRP-{Guid.NewGuid():N}"[..20], "گروه MRP", "MRP category", 0);
        var item = new Item(ItemTypes.Product, $"PRD-{Random.Shared.NextInt64(1_000_000, 9_999_999)}",
            "محصول MRP", "MRP product", unit.Id, null);
        db.AddRange(unit, category, item, new Product(item.Id, category.Id, null, 0, 0, 0, 0));
        await db.SaveChangesAsync();
        _units.Add(unit.Id); _categories.Add(category.Id); _items.Add(item.Id);
        return (item.Id, unit.Id);
    }

    private async Task AddForecast(Guid productId, Guid unitId, int year, int month,
        DateOnly start, DateOnly end, decimal quantity)
    {
        await using var db = await Db();
        var period = new BusinessPeriod(year, month, start, end);
        db.BusinessPeriods.Add(period);
        await db.SaveChangesAsync();
        var series = new ForecastSeries(productId, period.Id, unitId);
        db.ForecastSeries.Add(series);
        await db.SaveChangesAsync();
        var revision = new ForecastRevision(series.Id, 1, quantity, null, null);
        db.ForecastRevisions.Add(revision);
        await db.SaveChangesAsync();
        series.SetCurrent(revision);
        await db.SaveChangesAsync();
    }

    private async Task<int> CountRuns()
    {
        await using var db = await Db();
        return await db.MrpRuns.CountAsync();
    }

    private async Task<HadronDbContext> Db()
    {
        var scope = _factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IDbContextFactory<HadronDbContext>>()
            .CreateDbContextAsync();
    }
}
