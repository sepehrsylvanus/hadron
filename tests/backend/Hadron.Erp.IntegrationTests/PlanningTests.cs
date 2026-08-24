using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Hadron.Erp.Api.Infrastructure;
using Hadron.Erp.Api.Modules.Bom.Domain;
using Hadron.Erp.Api.Modules.Catalog.Domain;
using Hadron.Erp.Api.Modules.Planning.Application;
using Hadron.Erp.Api.Modules.Products.Domain;
using Hadron.Erp.Api.Modules.ReferenceData.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Hadron.Erp.IntegrationTests;

[Collection("ReferenceData")]
public sealed class PlanningTests : IClassFixture<FoundationApiFactory>, IAsyncLifetime
{
    private readonly FoundationApiFactory _factory;
    private readonly HttpClient _client;
    private Guid _unit;
    private Guid _category;
    private readonly List<Guid> _products = [];
    private readonly List<Guid> _extraUnits = [];

    public PlanningTests(FoundationApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    public async Task InitializeAsync()
    {
        await using var db = await Db();
        await db.Database.MigrateAsync();
        TestDatabaseSafety.EnsureSafeDatabaseName(db.Database.GetDbConnection().Database);
        await db.Database.ExecuteSqlRawAsync("TRUNCATE planning.period_lifecycle_events, planning.daily_actual_revisions, planning.daily_actuals, planning.forecast_revisions, planning.forecast_series, planning.business_periods RESTART IDENTITY CASCADE");
        await db.Database.ExecuteSqlRawAsync("TRUNCATE engineering.bom_lines, engineering.bom_revisions RESTART IDENTITY CASCADE");
        var unit = new Unit($"PLN-{Guid.NewGuid():N}"[..20], $"PLN-{Guid.NewGuid():N}"[..20],
            "واحد برنامه", "Planning unit", "u", 0);
        var categoryType = await db.ReferenceTypes.SingleAsync(x => x.Code == "PRODUCT_CATEGORY");
        var category = new ReferenceValue(categoryType.Id, $"PLN-{Guid.NewGuid():N}"[..20],
            $"PLN-{Guid.NewGuid():N}"[..20], "گروه برنامه", "Planning category", 0);
        db.AddRange(unit, category);
        await db.SaveChangesAsync();
        _unit = unit.Id;
        _category = category.Id;
        _factory.AuditSink.Clear();
    }

    public async Task DisposeAsync()
    {
        await using var db = await Db();
        TestDatabaseSafety.EnsureSafeDatabaseName(db.Database.GetDbConnection().Database);
        await db.Database.ExecuteSqlRawAsync("TRUNCATE planning.period_lifecycle_events, planning.daily_actual_revisions, planning.daily_actuals, planning.forecast_revisions, planning.forecast_series, planning.business_periods RESTART IDENTITY CASCADE");
        await db.Database.ExecuteSqlRawAsync("TRUNCATE engineering.bom_lines, engineering.bom_revisions RESTART IDENTITY CASCADE");
        await db.Products.Where(x => _products.Contains(x.ItemId)).ExecuteDeleteAsync();
        await db.Items.Where(x => _products.Contains(x.Id)).ExecuteDeleteAsync();
        await db.ReferenceValues.Where(x => x.Id == _category).ExecuteDeleteAsync();
        await db.Units.Where(x => _extraUnits.Contains(x.Id)).ExecuteDeleteAsync();
        await db.Units.Where(x => x.Id == _unit).ExecuteDeleteAsync();
    }

    [Fact]
    public void Planning_calendar_covers_all_month_shapes_round_trip_and_Tehran_midnight()
    {
        var beforeMidnight = new PlanningCalendar(new FixedTimeProvider(
            new DateTimeOffset(2026, 8, 23, 20, 29, 0, TimeSpan.Zero)));
        var afterMidnight = new PlanningCalendar(new FixedTimeProvider(
            new DateTimeOffset(2026, 8, 23, 20, 31, 0, TimeSpan.Zero)));
        Assert.Equal(new DateOnly(2026, 8, 23), beforeMidnight.TehranToday);
        Assert.Equal(new DateOnly(2026, 8, 24), afterMidnight.TehranToday);

        var farvardin = beforeMidnight.GetPeriodBounds(1405, 1);
        var thirtyDayMonth = beforeMidnight.GetPeriodBounds(1405, 7);
        var leapEsfand = beforeMidnight.GetPeriodBounds(1403, 12);
        var normalEsfand = beforeMidnight.GetPeriodBounds(1404, 12);
        Assert.Equal(31, farvardin.EndDate.DayNumber - farvardin.StartDate.DayNumber + 1);
        Assert.Equal(30, thirtyDayMonth.EndDate.DayNumber - thirtyDayMonth.StartDate.DayNumber + 1);
        Assert.Equal(30, leapEsfand.EndDate.DayNumber - leapEsfand.StartDate.DayNumber + 1);
        Assert.Equal(29, normalEsfand.EndDate.DayNumber - normalEsfand.StartDate.DayNumber + 1);
        Assert.Equal((1405, 1), beforeMidnight.GetJalaliPeriod(farvardin.StartDate));
        Assert.Equal((1405, 1), beforeMidnight.GetJalaliPeriod(farvardin.EndDate));
    }

    [Fact]
    public async Task Jalali_period_boundaries_are_server_generated_and_support_leap_Esfand()
    {
        var leap = await CreatePeriod(1403, 12);
        var normal = await CreatePeriod(1404, 12);
        Assert.Equal(29, leap.EndDate.DayNumber - leap.StartDate.DayNumber);
        Assert.Equal(28, normal.EndDate.DayNumber - normal.StartDate.DayNumber);
        Assert.Equal("OPEN", leap.Status);
        Assert.Equal(1, leap.Version);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await _client.PostAsJsonAsync("/api/planning/periods", new { jalaliYear = 1405, jalaliMonth = 13 })).StatusCode);
        var duplicate = await _client.PostAsJsonAsync("/api/planning/periods", new { jalaliYear = 1403, jalaliMonth = 12 });
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.Equal("BUSINESS_PERIOD_EXISTS", await Code(duplicate));
    }

    [Fact]
    public async Task Forecast_revisions_are_immutable_current_and_require_reason_from_revision_two()
    {
        var product = await AddProduct("محصول پیش‌بینی", "Forecast product");
        var period = await CreatePeriod(1405, 7);
        var initial = await CreateForecast(product, period.Id, 100, null);
        Assert.Equal(1, initial.CurrentRevision.RevisionNumber);
        Assert.Equal(100, initial.CurrentRevision.ForecastQty);
        Assert.Equal(_unit, initial.ProductBaseUnit.Id);

        var successfulAudits = _factory.AuditSink.Snapshot().Count(x => x.EntityType == "ForecastSeries" && x.Action == "REVISE");
        var missingReason = await _client.PostAsJsonAsync($"/api/planning/forecast/{initial.SeriesId}/revisions",
            new { forecastQty = 120, reason = (string?)null, expectedVersion = initial.Version });
        Assert.Equal(HttpStatusCode.BadRequest, missingReason.StatusCode);
        Assert.Single((await ForecastHistory(initial.SeriesId)).Revisions);
        Assert.Equal(successfulAudits, _factory.AuditSink.Snapshot().Count(x => x.EntityType == "ForecastSeries" && x.Action == "REVISE"));

        var revisedResponse = await _client.PostAsJsonAsync($"/api/planning/forecast/{initial.SeriesId}/revisions",
            new { forecastQty = 120.123456m, reason = "اصلاح برنامه", expectedVersion = initial.Version });
        Assert.Equal(HttpStatusCode.OK, revisedResponse.StatusCode);
        var revised = (await revisedResponse.Content.ReadFromJsonAsync<ForecastMutationDto>())!;
        Assert.Equal(2, revised.CurrentRevision.RevisionNumber);
        Assert.Equal(120.123456m, revised.CurrentRevision.ForecastQty);
        Assert.True(revised.Version > initial.Version);
        var history = await ForecastHistory(initial.SeriesId);
        Assert.Equal([2, 1], history.Revisions.Select(x => x.RevisionNumber));
        Assert.Equal([120.123456m, 100m], history.Revisions.Select(x => x.ForecastQty));

        await AssertDatabaseCheck(async db => await db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE planning.forecast_revisions SET \"ForecastQty\" = 999 WHERE \"Id\" = {history.Revisions[1].Id}"));
        Assert.Equal(100m, (await ForecastHistory(initial.SeriesId)).Revisions.Single(x => x.RevisionNumber == 1).ForecastQty);
    }

    [Fact]
    public async Task Forecast_is_product_only_and_missing_is_distinct_from_explicit_zero()
    {
        var missingProduct = await AddProduct("بدون پیش‌بینی", null);
        var zeroProduct = await AddProduct("پیش‌بینی صفر", "Zero forecast");
        var part = await AddNonProduct();
        var period = await CreatePeriod(1405, 8);
        await CreateForecast(zeroProduct, period.Id, 0, null);
        var rejected = await _client.PostAsJsonAsync("/api/planning/forecast", new
        { productId = part, businessPeriodId = period.Id, forecastQty = 1, reason = (string?)null });
        Assert.Equal(HttpStatusCode.NotFound, rejected.StatusCode);

        var list = await Planning(period.Id, period.StartDate);
        var missing = list.Items.Single(x => x.Product.Id == missingProduct);
        var zero = list.Items.Single(x => x.Product.Id == zeroProduct);
        Assert.Equal("MISSING", missing.ForecastState);
        Assert.Null(missing.ForecastQty);
        Assert.Null(missing.PlanningQty);
        Assert.Equal("EXPLICIT_ZERO", zero.ForecastState);
        Assert.Equal(0, zero.ForecastQty);
        Assert.Equal(0, zero.PlanningQty);
        Assert.Matches("^PRD-[0-9]{4,}$", zero.Product.Code);
        Assert.Equal("Zero forecast", zero.Product.NameEn);
    }

    [Fact]
    public async Task Actual_initial_and_correction_history_future_date_and_current_truth_are_enforced()
    {
        var product = await AddProduct("محصول واقعی", "Actual product");
        var businessDate = await _client.GetFromJsonAsync<BusinessDateDto>("/api/planning/business-date");
        var period = await CreatePeriod(businessDate!.JalaliYear, businessDate.JalaliMonth);
        var forecast = await CreateForecast(product, period.Id, 100, null);
        var actualResponse = await _client.PostAsJsonAsync("/api/planning/actuals", new
        { productId = product, productionDate = businessDate.TehranBusinessDate, actualQty = 30.25m });
        Assert.Equal(HttpStatusCode.Created, actualResponse.StatusCode);
        var actual = (await actualResponse.Content.ReadFromJsonAsync<ActualDto>())!;
        Assert.Null(actual.CurrentRevision.Reason);
        Assert.Equal(_unit, actual.Unit.Id);

        var missingReason = await _client.PostAsJsonAsync($"/api/planning/actuals/{actual.Id}/corrections",
            new { actualQty = 35, reason = (string?)null, expectedVersion = actual.Version });
        Assert.Equal(HttpStatusCode.BadRequest, missingReason.StatusCode);
        Assert.DoesNotContain(_factory.AuditSink.Snapshot(),
            x => x.EntityType == "DailyActual" && x.Action == "CORRECT");
        var correctedResponse = await _client.PostAsJsonAsync($"/api/planning/actuals/{actual.Id}/corrections",
            new { actualQty = 35, reason = "اصلاح تولید", expectedVersion = actual.Version });
        Assert.Equal(HttpStatusCode.OK, correctedResponse.StatusCode);
        var corrected = (await correctedResponse.Content.ReadFromJsonAsync<ActualDto>())!;
        Assert.Equal(2, corrected.CurrentRevision.RevisionNumber);
        Assert.Equal(35, corrected.CurrentRevision.ActualQty);
        var history = await _client.GetFromJsonAsync<ActualHistoryDto>($"/api/planning/actuals/{actual.Id}/revisions");
        Assert.Equal([2, 1], history!.Revisions.Select(x => x.RevisionNumber));

        var future = await _client.PostAsJsonAsync("/api/planning/actuals", new
        { productId = product, productionDate = businessDate.TehranBusinessDate.AddDays(1), actualQty = 1 });
        Assert.Equal(HttpStatusCode.Conflict, future.StatusCode);
        Assert.Equal("FUTURE_PRODUCTION_ACTUAL_NOT_ALLOWED", await Code(future));
        var planning = await Planning(period.Id, businessDate.TehranBusinessDate);
        var row = planning.Items.Single(x => x.Product.Id == product);
        Assert.Equal(35, row.ActualMtd);
        Assert.Equal(65, row.RemainingForecast);
        Assert.Equal(businessDate.TehranBusinessDate.AddDays(1), row.EligibleAllocationStart);
        Assert.Equal(forecast.SeriesId, row.SeriesId);
        Assert.False(await ResponseContains(planning, "dailyAllocations"));
    }

    [Fact]
    public async Task Actual_MTD_is_inclusive_actual_above_forecast_is_preserved_and_later_actual_is_excluded()
    {
        var product = await AddProduct("مرز واقعی", "Actual boundary");
        var period = await CreatePeriod(1398, 1);
        await CreateForecast(product, period.Id, 10, null);
        var evaluationDate = period.StartDate.AddDays(10);
        foreach (var entry in new[]
        {
            (Date: period.StartDate, Qty: 4m),
            (Date: evaluationDate, Qty: 7m),
            (Date: evaluationDate.AddDays(1), Qty: 20m)
        })
        {
            var response = await _client.PostAsJsonAsync("/api/planning/actuals",
                new { productId = product, productionDate = entry.Date, actualQty = entry.Qty });
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        }

        var row = (await Planning(period.Id, evaluationDate)).Items.Single(x => x.Product.Id == product);
        Assert.Equal(31, row.MonthlyActual);
        Assert.Equal(11, row.ActualMtd);
        Assert.Equal(0, row.RemainingForecast);
        Assert.Equal(0, row.PlanningQty);
        Assert.Equal(evaluationDate.AddDays(1), row.EligibleAllocationStart);
    }

    [Fact]
    public async Task Last_day_returns_unallocatable_without_spill_and_past_future_truth_is_correct()
    {
        var product = await AddProduct("مرز دوره", "Period boundary");
        var period = await CreatePeriod(1405, 5);
        await CreateForecast(product, period.Id, 80, null);
        var lastDay = await Planning(period.Id, period.EndDate);
        var current = lastDay.Items.Single(x => x.Product.Id == product);
        Assert.Equal("CURRENT", current.PeriodPosition);
        Assert.Equal(80, current.RemainingForecast);
        Assert.Equal(period.EndDate.AddDays(1), current.EligibleAllocationStart);
        Assert.Equal("CURRENT_PERIOD_REMAINDER_UNALLOCATABLE", current.PlanningExceptionCode);

        var future = await Planning(period.Id, period.StartDate.AddDays(-1));
        Assert.Equal(80, future.Items.Single(x => x.Product.Id == product).PlanningQty);
        var past = await Planning(period.Id, period.EndDate.AddDays(1));
        Assert.Equal(0, past.Items.Single(x => x.Product.Id == product).PlanningQty);
        Assert.Equal("NOT_APPLICABLE", past.Items.Single(x => x.Product.Id == product).BomReadiness.State);
    }

    [Fact]
    public async Task Base_unit_drift_blocks_forecast_revision_and_actual_without_history_rewrite()
    {
        var product = await AddProduct("تغییر واحد", "Unit drift");
        var businessDate = await _client.GetFromJsonAsync<BusinessDateDto>("/api/planning/business-date");
        var period = await CreatePeriod(businessDate!.JalaliYear, businessDate.JalaliMonth);
        var forecast = await CreateForecast(product, period.Id, 10, null);
        await using (var db = await Db())
        {
            var replacement = await AddUnit(db, "New unit");
            var item = await db.Items.SingleAsync(x => x.Id == product);
            item.Edit(item.NameFa, item.NameEn, replacement.Id, item.Notes);
            await db.SaveChangesAsync();
        }
        var revision = await _client.PostAsJsonAsync($"/api/planning/forecast/{forecast.SeriesId}/revisions",
            new { forecastQty = 11, reason = "drift", expectedVersion = forecast.Version });
        Assert.Equal(HttpStatusCode.Conflict, revision.StatusCode);
        Assert.Equal("BASE_UNIT_CHANGED", await Code(revision));
        Assert.Single((await ForecastHistory(forecast.SeriesId)).Revisions);
        var actual = await _client.PostAsJsonAsync("/api/planning/actuals", new
        { productId = product, productionDate = businessDate.TehranBusinessDate, actualQty = 1 });
        Assert.Equal(HttpStatusCode.Conflict, actual.StatusCode);
        Assert.Equal("BASE_UNIT_CHANGED", await Code(actual));
        await using (var db = await Db())
        {
            Assert.False(await db.DailyActuals.AnyAsync(x => x.ProductId == product));
            Assert.False(await db.DailyActualRevisions.AnyAsync(x => x.DailyActual.ProductId == product));
        }
        Assert.DoesNotContain(_factory.AuditSink.Snapshot(),
            x => x.EntityType == "DailyActual" && x.Action == "CREATE");
        await AssertDatabaseCheck(async db => await db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE planning.forecast_series SET \"ProductBaseUnitId\" = {Guid.NewGuid()} WHERE \"Id\" = {forecast.SeriesId}"));
    }

    [Fact]
    public async Task Product_period_unit_is_symmetric_and_daily_actual_snapshot_is_database_immutable()
    {
        var product = await AddProduct("تقارن واحد", "Unit symmetry");
        var businessDate = await _client.GetFromJsonAsync<BusinessDateDto>("/api/planning/business-date");
        var period = await CreatePeriod(businessDate!.JalaliYear, businessDate.JalaliMonth);
        var actualResponse = await _client.PostAsJsonAsync("/api/planning/actuals",
            new { productId = product, productionDate = businessDate.TehranBusinessDate, actualQty = 2 });
        Assert.Equal(HttpStatusCode.Created, actualResponse.StatusCode);
        var actual = (await actualResponse.Content.ReadFromJsonAsync<ActualDto>())!;
        var replacementUnit = await ChangeProductUnit(product);
        var auditCount = _factory.AuditSink.Snapshot().Count(x => x.EntityType == "ForecastSeries" && x.Action == "CREATE");

        var forecast = await _client.PostAsJsonAsync("/api/planning/forecast",
            new { productId = product, businessPeriodId = period.Id, forecastQty = 5, reason = (string?)null });
        Assert.Equal(HttpStatusCode.Conflict, forecast.StatusCode);
        Assert.Equal("BASE_UNIT_CHANGED", await Code(forecast));
        await using (var db = await Db())
        {
            Assert.False(await db.ForecastSeries.AnyAsync(x => x.ProductId == product && x.BusinessPeriodId == period.Id));
            Assert.Empty(await db.ForecastRevisions.Where(x => x.ForecastSeries.ProductId == product).ToListAsync());
        }
        Assert.Equal(auditCount, _factory.AuditSink.Snapshot().Count(x => x.EntityType == "ForecastSeries" && x.Action == "CREATE"));

        await AssertDatabaseCheck(async db => await db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE planning.daily_actuals SET \"UnitId\" = {replacementUnit} WHERE \"Id\" = {actual.Id}"));
        var persisted = (await _client.GetFromJsonAsync<ActualHistoryDto>($"/api/planning/actuals/{actual.Id}/revisions"))!;
        Assert.Equal(_unit, persisted.Actual.Unit.Id);
        Assert.Single(persisted.Revisions);
    }

    [Fact]
    public async Task Period_close_reopen_lifecycle_is_durable_and_blocks_normal_mutation()
    {
        var product = await AddProduct("چرخه دوره", "Period lifecycle");
        var period = await CreatePeriod(1405, 9);
        var forecast = await CreateForecast(product, period.Id, 5, null);
        var closedResponse = await _client.PostAsJsonAsync($"/api/planning/periods/{period.Id}/close",
            new { expectedVersion = period.Version, reason = "بستن" });
        Assert.Equal(HttpStatusCode.OK, closedResponse.StatusCode);
        var closed = (await closedResponse.Content.ReadFromJsonAsync<PeriodDto>())!;
        var blocked = await _client.PostAsJsonAsync($"/api/planning/forecast/{forecast.SeriesId}/revisions",
            new { forecastQty = 6, reason = "blocked", expectedVersion = forecast.Version });
        Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);
        Assert.Equal("PERIOD_CLOSED", await Code(blocked));
        Assert.Single((await ForecastHistory(forecast.SeriesId)).Revisions);
        Assert.DoesNotContain(_factory.AuditSink.Snapshot(),
            x => x.EntityType == "ForecastSeries" && x.Action == "REVISE");

        var noReason = await _client.PostAsJsonAsync($"/api/planning/periods/{period.Id}/reopen",
            new { expectedVersion = closed.Version, reason = (string?)null });
        Assert.Equal(HttpStatusCode.BadRequest, noReason.StatusCode);
        await using (var historyDb = await Db())
            Assert.Single(await historyDb.PeriodLifecycleEvents.Where(x => x.BusinessPeriodId == period.Id).ToListAsync());
        var reopenedResponse = await _client.PostAsJsonAsync($"/api/planning/periods/{period.Id}/reopen",
            new { expectedVersion = closed.Version, reason = "اصلاح" });
        Assert.Equal(HttpStatusCode.OK, reopenedResponse.StatusCode);
        var reopened = (await reopenedResponse.Content.ReadFromJsonAsync<PeriodDto>())!;
        await using var lockDb = await Db();
        await using var lockTransaction = await lockDb.Database.BeginTransactionAsync();
        await lockDb.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT \"Id\" FROM planning.business_periods WHERE \"Id\" = {period.Id} FOR UPDATE");
        var closeTask = _client.PostAsJsonAsync($"/api/planning/periods/{period.Id}/close",
            new { expectedVersion = reopened.Version, reason = "race" });
        await Task.Delay(100);
        var editTask = _client.PostAsJsonAsync($"/api/planning/forecast/{forecast.SeriesId}/revisions",
            new { forecastQty = 7, reason = "race", expectedVersion = forecast.Version });
        await Task.Delay(100);
        await lockTransaction.CommitAsync();
        var deterministicClose = await closeTask;
        var deterministicEdit = await editTask;
        Assert.Equal(HttpStatusCode.OK, deterministicClose.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, deterministicEdit.StatusCode);
        Assert.Equal("PERIOD_CLOSED", await Code(deterministicEdit));
        var staleReopen = await _client.PostAsJsonAsync($"/api/planning/periods/{period.Id}/reopen",
            new { expectedVersion = reopened.Version, reason = "stale" });
        Assert.Equal(HttpStatusCode.Conflict, staleReopen.StatusCode);
        Assert.Equal("STALE_VERSION", await Code(staleReopen));
        await using var db = await Db();
        var events = await db.PeriodLifecycleEvents.Where(x => x.BusinessPeriodId == period.Id)
            .OrderBy(x => x.OccurredAtUtc).ToListAsync();
        Assert.Equal(["CLOSED", "OPEN", "CLOSED"], events.Select(x => x.ToStatus));
        Assert.Equal("اصلاح", events[1].Reason);
        Assert.All(events, x => Assert.Null(x.ActorIdentifier));
        Assert.Single((await ForecastHistory(forecast.SeriesId)).Revisions);
    }

    [Fact]
    public async Task Bom_readiness_uses_full_future_window_current_future_segment_and_never_blocks_forecast()
    {
        var product = await AddProduct("آمادگی BOM", "BOM readiness");
        var period = await CreatePeriod(1405, 10);
        await CreateForecast(product, period.Id, 50, null);
        var beforeBom = await Planning(period.Id, period.StartDate.AddDays(-1));
        Assert.Equal("MISSING_BOM", beforeBom.Items.Single(x => x.Product.Id == product).BomReadiness.State);

        await AddApprovedBom(product, period.StartDate, period.EndDate);
        var future = await Planning(period.Id, period.StartDate.AddDays(-1));
        var futureReady = future.Items.Single(x => x.Product.Id == product).BomReadiness;
        Assert.Equal("READY", futureReady.State);
        Assert.Equal(period.StartDate, futureReady.EvaluationStart);
        Assert.Equal(period.EndDate, futureReady.EvaluationEnd);
        var currentDate = period.StartDate.AddDays(10);
        var current = await Planning(period.Id, currentDate);
        Assert.Equal(currentDate.AddDays(1), current.Items.Single(x => x.Product.Id == product).BomReadiness.EvaluationStart);
    }

    [Fact]
    public async Task Bom_readiness_reports_partial_inactive_and_unit_drift_without_using_pre_AsOf_dates()
    {
        var partialProduct = await AddProduct("پوشش ناقص", "Partial coverage");
        var inactiveProduct = await AddProduct("فرزند غیرفعال", "Inactive child");
        var driftProduct = await AddProduct("انحراف BOM", "BOM drift");
        var parentDriftProduct = await AddProduct("انحراف والد", "Parent drift");
        var currentProduct = await AddProduct("پنجره جاری", "Current window");
        var period = await CreatePeriod(1406, 4);

        await AddApprovedBom(partialProduct, period.StartDate, period.StartDate.AddDays(5));
        var inactiveBom = await AddApprovedBom(inactiveProduct, period.StartDate, period.EndDate);
        var driftBom = await AddApprovedBom(driftProduct, period.StartDate, period.EndDate);
        await AddApprovedBom(parentDriftProduct, period.StartDate, period.EndDate);
        var asOf = period.StartDate.AddDays(10);
        await AddApprovedBom(currentProduct, asOf.AddDays(1), period.EndDate);

        await using (var db = await Db())
        {
            var inactiveChild = await db.Items.SingleAsync(x => x.Id == inactiveBom.ChildId);
            inactiveChild.ChangeState(false);
            var replacement = await AddUnit(db, "BOM drift unit");
            var driftChild = await db.Items.SingleAsync(x => x.Id == driftBom.ChildId);
            driftChild.Edit(driftChild.NameFa, driftChild.NameEn, replacement.Id, driftChild.Notes);
            await db.SaveChangesAsync();
        }
        await ChangeProductUnit(parentDriftProduct);

        var future = await Planning(period.Id, period.StartDate.AddDays(-1));
        Assert.Equal("PARTIAL_PERIOD_BOM_COVERAGE",
            future.Items.Single(x => x.Product.Id == partialProduct).BomReadiness.State);
        var inactive = future.Items.Single(x => x.Product.Id == inactiveProduct).BomReadiness;
        Assert.Equal("BLOCKED", inactive.State);
        Assert.Contains("CHILD_INACTIVE", inactive.ReasonCodes);
        var drift = future.Items.Single(x => x.Product.Id == driftProduct).BomReadiness;
        Assert.Equal("BLOCKED", drift.State);
        Assert.Contains("BASE_UNIT_CHANGED", drift.ReasonCodes);
        var parentDrift = future.Items.Single(x => x.Product.Id == parentDriftProduct).BomReadiness;
        Assert.Equal("BLOCKED", parentDrift.State);
        Assert.Contains("BASE_UNIT_CHANGED", parentDrift.ReasonCodes);
        var current = await Planning(period.Id, asOf);
        Assert.Equal("READY", current.Items.Single(x => x.Product.Id == currentProduct).BomReadiness.State);
    }

    [Fact]
    public async Task Database_guards_daily_actual_period_date_for_insert_and_update()
    {
        var product = await AddProduct("نگهبان تاریخ", "Date guard");
        var period = await CreatePeriod(1400, 1);
        var other = await CreatePeriod(1400, 2);
        var acceptedId = Guid.NewGuid();
        await using (var db = await Db())
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO planning.daily_actuals
                    ("Id", "ProductId", "BusinessPeriodId", "ProductionDate", "UnitId", "CurrentRevisionId", "Version")
                VALUES ({acceptedId}, {product}, {period.Id}, {period.StartDate}, {_unit}, NULL, 1)
                """);
            Assert.True(await db.DailyActuals.AnyAsync(x => x.Id == acceptedId));
        }

        await AssertPeriodDateGuard(product, period.Id, period.StartDate.AddDays(-1));
        await AssertPeriodDateGuard(product, period.Id, period.EndDate.AddDays(1));
        await AssertPeriodDateGuard(product, other.Id, period.StartDate);
        await AssertPeriodDateGuardUpdate(acceptedId, period.EndDate.AddDays(1));
    }

    [Fact]
    public async Task Concurrent_logical_creates_keep_one_forecast_and_one_daily_actual()
    {
        var product = await AddProduct("همزمانی", "Concurrency");
        var businessDate = await _client.GetFromJsonAsync<BusinessDateDto>("/api/planning/business-date");
        var period = await CreatePeriod(businessDate!.JalaliYear, businessDate.JalaliMonth);
        var forecasts = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => _client.PostAsJsonAsync(
            "/api/planning/forecast", new { productId = product, businessPeriodId = period.Id, forecastQty = 1, reason = (string?)null })));
        Assert.Equal(1, forecasts.Count(x => x.StatusCode == HttpStatusCode.Created));
        Assert.Equal(1, forecasts.Count(x => x.StatusCode == HttpStatusCode.Conflict));
        var forecast = (await forecasts.Single(x => x.StatusCode == HttpStatusCode.Created)
            .Content.ReadFromJsonAsync<ForecastMutationDto>())!;
        var revisions = await Task.WhenAll(Enumerable.Range(0, 2).Select(index => _client.PostAsJsonAsync(
            $"/api/planning/forecast/{forecast.SeriesId}/revisions",
            new { forecastQty = 2 + index, reason = "concurrent", expectedVersion = forecast.Version })));
        Assert.Equal(1, revisions.Count(x => x.StatusCode == HttpStatusCode.OK));
        Assert.Equal(1, revisions.Count(x => x.StatusCode == HttpStatusCode.Conflict));
        Assert.Equal("STALE_VERSION", await Code(revisions.Single(x => x.StatusCode == HttpStatusCode.Conflict)));
        var actuals = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => _client.PostAsJsonAsync(
            "/api/planning/actuals", new { productId = product, productionDate = businessDate.TehranBusinessDate, actualQty = 1 })));
        Assert.Equal(1, actuals.Count(x => x.StatusCode == HttpStatusCode.Created));
        Assert.Equal(1, actuals.Count(x => x.StatusCode == HttpStatusCode.Conflict));
        var actual = (await actuals.Single(x => x.StatusCode == HttpStatusCode.Created)
            .Content.ReadFromJsonAsync<ActualDto>())!;
        var corrections = await Task.WhenAll(Enumerable.Range(0, 2).Select(index => _client.PostAsJsonAsync(
            $"/api/planning/actuals/{actual.Id}/corrections",
            new { actualQty = 2 + index, reason = "concurrent", expectedVersion = actual.Version })));
        Assert.Equal(1, corrections.Count(x => x.StatusCode == HttpStatusCode.OK));
        Assert.Equal(1, corrections.Count(x => x.StatusCode == HttpStatusCode.Conflict));
        Assert.Equal("STALE_VERSION", await Code(corrections.Single(x => x.StatusCode == HttpStatusCode.Conflict)));
        await using var db = await Db();
        Assert.Equal(1, await db.ForecastSeries.CountAsync(x => x.ProductId == product && x.BusinessPeriodId == period.Id));
        Assert.Equal(1, await db.DailyActuals.CountAsync(x => x.ProductId == product && x.ProductionDate == businessDate.TehranBusinessDate));
    }

    [Fact]
    public async Task Post_commit_audit_failure_does_not_undo_durable_planning_history()
    {
        _factory.AuditSink.ThrowOnWrite = true;
        var response = await _client.PostAsJsonAsync("/api/planning/periods",
            new { jalaliYear = 1410, jalaliMonth = 1 });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        _factory.AuditSink.ThrowOnWrite = false;
        var periods = await _client.GetFromJsonAsync<PeriodListDto>("/api/planning/periods?page=1&pageSize=100");
        Assert.Contains(periods!.Items, x => x.JalaliYear == 1410 && x.JalaliMonth == 1);
    }

    private async Task<Guid> AddProduct(string nameFa, string? nameEn)
    {
        await using var db = await Db();
        var item = new Item(ItemTypes.Product, $"PRD-{Random.Shared.NextInt64(1_000_000, 9_999_999)}",
            nameFa, nameEn, _unit, null);
        db.Items.Add(item);
        db.Products.Add(new Product(item.Id, _category, null, 0, 0, 0, 0));
        await db.SaveChangesAsync();
        _products.Add(item.Id);
        return item.Id;
    }

    private async Task<Guid> AddNonProduct()
    {
        await using var db = await Db();
        var item = new Item(ItemTypes.Part, $"PRT-{Random.Shared.NextInt64(1_000_000, 9_999_999)}",
            "قطعه غیرمحصول", "Not a product", _unit, null);
        db.Items.Add(item);
        await db.SaveChangesAsync();
        _products.Add(item.Id);
        return item.Id;
    }

    private async Task<(Guid RevisionId, Guid ChildId)> AddApprovedBom(Guid productId, DateOnly from, DateOnly to)
    {
        var childId = await AddNonProduct();
        await using var db = await Db();
        var revision = new BomRevision($"BOM-{Random.Shared.NextInt64(1_000_000, 9_999_999)}",
            productId, _unit, 1, from, to, null);
        db.BomRevisions.Add(revision);
        await db.SaveChangesAsync();
        db.BomLines.Add(new BomLine(revision.Id, childId, _unit, 1, null, 0));
        await db.SaveChangesAsync();
        revision.Approve(null);
        await db.SaveChangesAsync();
        return (revision.Id, childId);
    }

    private async Task<Guid> ChangeProductUnit(Guid productId)
    {
        await using var db = await Db();
        var unit = await AddUnit(db, "Replacement unit");
        var item = await db.Items.SingleAsync(x => x.Id == productId);
        item.Edit(item.NameFa, item.NameEn, unit.Id, item.Notes);
        await db.SaveChangesAsync();
        return unit.Id;
    }

    private async Task<Unit> AddUnit(HadronDbContext db, string nameEn)
    {
        var unit = new Unit($"PLN-{Guid.NewGuid():N}"[..20], $"PLN-{Guid.NewGuid():N}"[..20],
            "واحد جایگزین", nameEn, null, 0);
        db.Units.Add(unit);
        _extraUnits.Add(unit.Id);
        await db.SaveChangesAsync();
        return unit;
    }

    private async Task AssertPeriodDateGuard(Guid productId, Guid periodId, DateOnly productionDate)
    {
        await using var db = await Db();
        await using var transaction = await db.Database.BeginTransactionAsync();
        var id = Guid.NewGuid();
        var exception = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO planning.daily_actuals
                ("Id", "ProductId", "BusinessPeriodId", "ProductionDate", "UnitId", "CurrentRevisionId", "Version")
            VALUES ({id}, {productId}, {periodId}, {productionDate}, {_unit}, NULL, 1)
            """));
        Assert.Equal(PostgresErrorCodes.CheckViolation, exception.SqlState);
        Assert.Equal("ck_daily_actual_period_date", exception.ConstraintName);
        await transaction.RollbackAsync();
    }

    private async Task AssertPeriodDateGuardUpdate(Guid actualId, DateOnly productionDate)
    {
        await using var db = await Db();
        await using var transaction = await db.Database.BeginTransactionAsync();
        var exception = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE planning.daily_actuals SET \"ProductionDate\" = {productionDate} WHERE \"Id\" = {actualId}"));
        Assert.Equal(PostgresErrorCodes.CheckViolation, exception.SqlState);
        await transaction.RollbackAsync();
    }

    private async Task<PeriodDto> CreatePeriod(int year, int month)
    {
        var response = await _client.PostAsJsonAsync("/api/planning/periods", new { jalaliYear = year, jalaliMonth = month });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<PeriodDto>())!;
    }

    private async Task<ForecastMutationDto> CreateForecast(Guid productId, Guid periodId, decimal qty, string? reason)
    {
        var response = await _client.PostAsJsonAsync("/api/planning/forecast", new
        { productId, businessPeriodId = periodId, forecastQty = qty, reason });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ForecastMutationDto>())!;
    }

    private async Task<ForecastHistoryDto> ForecastHistory(Guid seriesId) =>
        (await _client.GetFromJsonAsync<ForecastHistoryDto>($"/api/planning/forecast/{seriesId}/revisions"))!;

    private async Task<PlanningListDto> Planning(Guid periodId, DateOnly evaluationDate) =>
        (await _client.GetFromJsonAsync<PlanningListDto>($"/api/planning/forecast?periodId={periodId}&evaluationDate={evaluationDate:yyyy-MM-dd}&includeInactive=true&page=1&pageSize=100"))!;

    private static Task<bool> ResponseContains(PlanningListDto response, string property)
    {
        var json = JsonSerializer.Serialize(response);
        using var document = JsonDocument.Parse(json);
        return Task.FromResult(document.RootElement.TryGetProperty(property, out _));
    }

    private async Task AssertDatabaseCheck(Func<HadronDbContext, Task> mutation)
    {
        await using var db = await Db();
        await using var transaction = await db.Database.BeginTransactionAsync();
        var exception = await Assert.ThrowsAsync<PostgresException>(() => mutation(db));
        Assert.Equal(PostgresErrorCodes.CheckViolation, exception.SqlState);
        await transaction.RollbackAsync();
    }

    private static async Task<string?> Code(HttpResponseMessage response)
    {
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.TryGetProperty("code", out var value) ? value.GetString() : null;
    }

    private async Task<HadronDbContext> Db()
    {
        var scope = _factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IDbContextFactory<HadronDbContext>>()
            .CreateDbContextAsync();
    }

    private sealed record UnitDto(Guid Id, string Code, string NameFa, string NameEn, bool IsActive);
    private sealed record ProductDto(Guid Id, string Code, string NameFa, string? NameEn, bool IsActive);
    private sealed record PeriodDto(Guid Id, int JalaliYear, int JalaliMonth, DateOnly StartDate,
        DateOnly EndDate, string Status, long Version);
    private sealed record PeriodListDto(PeriodDto[] Items, int TotalCount);
    private sealed record BusinessDateDto(DateOnly TehranBusinessDate, int JalaliYear, int JalaliMonth);
    private sealed record ForecastRevisionDto(Guid Id, int RevisionNumber, decimal ForecastQty, string? Reason);
    private sealed record ForecastMutationDto(Guid SeriesId, Guid ProductId, Guid BusinessPeriodId,
        UnitDto ProductBaseUnit, ForecastRevisionDto CurrentRevision, long Version);
    private sealed record ForecastHistoryDto(Guid SeriesId, ForecastRevisionDto[] Revisions);
    private sealed record BomReadinessDto(string State, string[] ReasonCodes, DateOnly? EvaluationStart,
        DateOnly? EvaluationEnd);
    private sealed record PlanningRowDto(ProductDto Product, Guid? SeriesId, decimal? ForecastQty,
        decimal MonthlyActual, decimal ActualMtd, decimal? RemainingForecast, decimal? PlanningQty,
        string PeriodPosition, string ForecastState, DateOnly? EligibleAllocationStart,
        string? PlanningExceptionCode, BomReadinessDto BomReadiness);
    private sealed record PlanningListDto(PeriodDto Period, DateOnly EvaluationDate,
        PlanningRowDto[] Items, int TotalCount);
    private sealed record ActualRevisionDto(Guid Id, int RevisionNumber, decimal ActualQty, string? Reason);
    private sealed record ActualDto(Guid Id, ProductDto Product, PeriodDto Period, DateOnly ProductionDate,
        UnitDto Unit, ActualRevisionDto CurrentRevision, long Version);
    private sealed record ActualHistoryDto(ActualDto Actual, ActualRevisionDto[] Revisions);

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
