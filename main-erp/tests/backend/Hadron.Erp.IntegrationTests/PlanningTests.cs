using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Hadron.Erp.Api.Infrastructure;
using Hadron.Erp.Api.Modules.Bom.Domain;
using Hadron.Erp.Api.Modules.Catalog.Domain;
using Hadron.Erp.Api.Modules.Planning.Application;
using Hadron.Erp.Api.Modules.Planning.Domain;
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
        await db.Database.ExecuteSqlRawAsync("TRUNCATE planning.forecast_plan_entries, planning.forecast_plans, planning.period_lifecycle_events, planning.daily_actual_revisions, planning.daily_actuals, planning.forecast_revisions, planning.forecast_series, planning.business_periods RESTART IDENTITY CASCADE");
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
        await db.Database.ExecuteSqlRawAsync("TRUNCATE planning.forecast_plan_entries, planning.forecast_plans, planning.period_lifecycle_events, planning.daily_actual_revisions, planning.daily_actuals, planning.forecast_revisions, planning.forecast_series, planning.business_periods RESTART IDENTITY CASCADE");
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
        var exactGregorian = beforeMidnight.GetGregorianDate(1405, 6, 8);
        Assert.Equal(new DateOnly(2026, 8, 30), exactGregorian);
        Assert.Equal((1405, 6, 8), beforeMidnight.GetJalaliDate(exactGregorian));
    }

    [Fact]
    public async Task Jalali_period_boundaries_are_server_generated_and_support_leap_Esfand()
    {
        var exact = await _client.PostAsJsonAsync("/api/planning/resolve-jalali-date",
            new ResolveJalaliDateRequest("۱۴۰۵۰۶۰۸"));
        Assert.Equal(HttpStatusCode.OK, exact.StatusCode);
        var resolved = (await exact.Content.ReadFromJsonAsync<ResolveJalaliDateResponse>())!;
        Assert.Equal("14050608", resolved.JalaliDate);
        Assert.Equal(new DateOnly(2026, 8, 30), resolved.GregorianDate);

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
    public async Task Forecast_entry_grid_previews_without_writing_and_batch_save_is_atomic_and_revision_safe()
    {
        var firstProduct = await AddProduct("محصول جدول اول", "First grid product");
        var secondProduct = await AddProduct("محصول جدول دوم", "Second grid product");
        var rejectedProduct = await AddProduct("محصول rollback", "Rollback product");

        var preview = await _client.GetFromJsonAsync<ForecastEntryGridDto>(
            "/api/planning/forecast-entry-grid?periodCode=140509&page=1&pageSize=100");
        Assert.NotNull(preview);
        Assert.False(preview.Period.Exists);
        Assert.Equal("140509", preview.Period.PeriodCode);
        Assert.Equal(1405, preview.Period.JalaliYear);
        Assert.Equal(9, preview.Period.JalaliMonth);
        Assert.True(preview.Period.EndDate >= preview.Period.StartDate);
        Assert.Contains(preview.Items, x => x.Product.Id == firstProduct && x.CurrentForecastQty is null);
        await using (var previewDb = await Db())
            Assert.False(await previewDb.BusinessPeriods.AnyAsync(x => x.JalaliYear == 1405 && x.JalaliMonth == 9));

        var createdResponse = await _client.PostAsJsonAsync("/api/planning/forecast-entry-grid", new
        {
            periodCode = "140509",
            reason = (string?)null,
            entries = new object[]
            {
                new { productId = firstProduct, forecastQty = 10m, expectedVersion = (long?)null },
                new { productId = secondProduct, forecastQty = 0m, expectedVersion = (long?)null }
            }
        });
        Assert.Equal(HttpStatusCode.OK, createdResponse.StatusCode);
        var created = (await createdResponse.Content.ReadFromJsonAsync<ForecastBatchDto>())!;
        Assert.Equal(2, created.CreatedCount);
        Assert.Equal(0, created.RevisedCount);
        Assert.Equal("OPEN", created.Period.Status);

        var registered = await _client.GetFromJsonAsync<ForecastEntryGridDto>(
            "/api/planning/forecast-entry-grid?periodCode=140509&page=1&pageSize=100");
        var first = registered!.Items.Single(x => x.Product.Id == firstProduct);
        Assert.Equal(10m, first.CurrentForecastQty);
        Assert.Equal(1, first.CurrentRevisionNumber);

        var noReason = await _client.PostAsJsonAsync("/api/planning/forecast-entry-grid", new
        {
            periodCode = "140509",
            reason = (string?)null,
            entries = new[] { new { productId = firstProduct, forecastQty = 12m, expectedVersion = first.SeriesVersion } }
        });
        Assert.Equal(HttpStatusCode.BadRequest, noReason.StatusCode);
        Assert.Single((await ForecastHistory(first.SeriesId!.Value)).Revisions);

        var revisedResponse = await _client.PostAsJsonAsync("/api/planning/forecast-entry-grid", new
        {
            periodCode = "140509",
            reason = "اصلاح برنامه ماهانه",
            entries = new[] { new { productId = firstProduct, forecastQty = 12m, expectedVersion = first.SeriesVersion } }
        });
        Assert.Equal(HttpStatusCode.OK, revisedResponse.StatusCode);
        var revised = (await revisedResponse.Content.ReadFromJsonAsync<ForecastBatchDto>())!;
        Assert.Equal(1, revised.RevisedCount);
        var history = await ForecastHistory(first.SeriesId.Value);
        Assert.Equal([2, 1], history.Revisions.Select(x => x.RevisionNumber));
        Assert.Equal("اصلاح برنامه ماهانه", history.Revisions[0].Reason);

        var staleAtomic = await _client.PostAsJsonAsync("/api/planning/forecast-entry-grid", new
        {
            periodCode = "140509",
            reason = "درخواست قدیمی",
            entries = new object[]
            {
                new { productId = firstProduct, forecastQty = 15m, expectedVersion = first.SeriesVersion },
                new { productId = rejectedProduct, forecastQty = 8m, expectedVersion = (long?)null }
            }
        });
        Assert.Equal(HttpStatusCode.Conflict, staleAtomic.StatusCode);
        Assert.Equal("STALE_VERSION", await Code(staleAtomic));
        await using var db = await Db();
        Assert.False(await db.ForecastSeries.AnyAsync(x => x.ProductId == rejectedProduct));
        Assert.Equal(2, await db.ForecastRevisions.CountAsync(x => x.ForecastSeriesId == first.SeriesId));
    }

    [Fact]
    public async Task Named_forecast_plan_builds_inclusive_month_matrix_and_handles_same_or_conflicting_values_explicitly()
    {
        var firstProduct = await AddProduct("محصول برنامه اول", "First plan product");
        var secondProduct = await AddProduct("محصول برنامه دوم", "Second plan product");

        var productOptions = await _client.GetFromJsonAsync<ForecastProductListDto>(
            "/api/planning/forecast-products?search=plan%20product&page=1&pageSize=20");
        Assert.NotNull(productOptions);
        Assert.Equal(2, productOptions.TotalCount);
        Assert.All(productOptions.Items, item => Assert.True(item.IsActive));

        var previewResponse = await _client.PostAsJsonAsync("/api/planning/forecast-plans/preview", new
        {
            name = "برنامه بهار ۱۴۰۵",
            startJalaliDate = "14050101",
            endJalaliDate = "14050301",
            productIds = new[] { firstProduct, secondProduct }
        });
        previewResponse.EnsureSuccessStatusCode();
        var preview = (await previewResponse.Content.ReadFromJsonAsync<ForecastPlanPreviewDto>())!;
        Assert.Equal(["140501", "140502", "140503"], preview.Months.Select(x => x.PeriodCode));
        Assert.Equal(6, preview.Cells.Length);
        Assert.All(preview.Cells, cell => Assert.Null(cell.CurrentForecastQty));
        Assert.True(preview.StartDate < preview.EndDate);
        await using (var previewDb = await Db())
        {
            Assert.Empty(await previewDb.BusinessPeriods.ToListAsync());
            Assert.Empty(await previewDb.ForecastPlans.ToListAsync());
        }

        var initialEntries = preview.Months.SelectMany((month, monthIndex) => preview.Products.Select((product, productIndex) => new
        {
            productId = product.Id,
            periodCode = month.PeriodCode,
            forecastQty = decimal.Parse($"{productIndex + 1}{monthIndex + 1}0"),
            expectedVersion = (long?)null
        })).ToArray();
        var createResponse = await _client.PostAsJsonAsync("/api/planning/forecast-plans", new
        {
            name = preview.Name,
            startJalaliDate = preview.StartJalaliDate,
            endJalaliDate = preview.EndJalaliDate,
            reason = (string?)null,
            confirmDifferences = false,
            entries = initialEntries
        });
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var created = (await createResponse.Content.ReadFromJsonAsync<ForecastPlanSaveDto>())!;
        Assert.Equal(6, created.CreatedCount);
        Assert.Equal(0, created.RevisedCount);
        Assert.Equal(0, created.UnchangedCount);
        Assert.Equal(6, created.EntryCount);

        var registeredPreviewResponse = await _client.PostAsJsonAsync("/api/planning/forecast-plans/preview", new
        {
            name = "برنامه بدون تغییر",
            startJalaliDate = "14050101",
            endJalaliDate = "14050301",
            productIds = new[] { firstProduct, secondProduct }
        });
        registeredPreviewResponse.EnsureSuccessStatusCode();
        var registered = (await registeredPreviewResponse.Content.ReadFromJsonAsync<ForecastPlanPreviewDto>())!;
        var registeredMap = registered.Cells.ToDictionary(x => (x.ProductId, x.PeriodCode));
        var sameEntries = initialEntries.Select(entry => new
        {
            entry.productId,
            entry.periodCode,
            entry.forecastQty,
            expectedVersion = registeredMap[(entry.productId, entry.periodCode)].SeriesVersion
        }).ToArray();
        var sameResponse = await _client.PostAsJsonAsync("/api/planning/forecast-plans", new
        {
            name = "برنامه بدون تغییر",
            startJalaliDate = "14050101",
            endJalaliDate = "14050301",
            reason = (string?)null,
            confirmDifferences = false,
            entries = sameEntries
        });
        Assert.Equal(HttpStatusCode.Created, sameResponse.StatusCode);
        var same = (await sameResponse.Content.ReadFromJsonAsync<ForecastPlanSaveDto>())!;
        Assert.Equal(6, same.UnchangedCount);
        Assert.Equal(0, same.CreatedCount);
        Assert.Equal(0, same.RevisedCount);

        var changedEntries = sameEntries.Select(entry => new
        {
            entry.productId,
            entry.periodCode,
            forecastQty = entry.productId == firstProduct && entry.periodCode == "140501"
                ? entry.forecastQty + 7m : entry.forecastQty,
            entry.expectedVersion
        }).ToArray();
        var changedValue = changedEntries.Single(x => x.productId == firstProduct && x.periodCode == "140501")
            .forecastQty;
        var conflictResponse = await _client.PostAsJsonAsync("/api/planning/forecast-plans", new
        {
            name = "برنامه اصلاحی",
            startJalaliDate = "14050101",
            endJalaliDate = "14050301",
            reason = (string?)null,
            confirmDifferences = false,
            entries = changedEntries
        });
        Assert.Equal(HttpStatusCode.Conflict, conflictResponse.StatusCode);
        Assert.Equal("FORECAST_VALUE_CONFLICT", await Code(conflictResponse));
        using (var conflictJson = JsonDocument.Parse(await conflictResponse.Content.ReadAsStringAsync()))
        {
            var conflict = Assert.Single(conflictJson.RootElement.GetProperty("details").EnumerateArray());
            Assert.Equal(firstProduct, conflict.GetProperty("productId").GetGuid());
            Assert.Equal("140501", conflict.GetProperty("periodCode").GetString());
            Assert.Equal(changedValue, conflict.GetProperty("proposedQty").GetDecimal());
            Assert.True(conflict.GetProperty("seriesVersion").GetInt64() >= 1);
        }
        await using (var conflictDb = await Db())
        {
            Assert.Equal(2, await conflictDb.ForecastPlans.CountAsync());
            Assert.Equal(6, await conflictDb.ForecastRevisions.CountAsync());
        }

        var confirmedResponse = await _client.PostAsJsonAsync("/api/planning/forecast-plans", new
        {
            name = "برنامه اصلاحی",
            startJalaliDate = "14050101",
            endJalaliDate = "14050301",
            reason = "اصلاح پیش‌بینی پس از اخطار",
            confirmDifferences = true,
            entries = changedEntries
        });
        Assert.Equal(HttpStatusCode.Created, confirmedResponse.StatusCode);
        var confirmed = (await confirmedResponse.Content.ReadFromJsonAsync<ForecastPlanSaveDto>())!;
        Assert.Equal(1, confirmed.RevisedCount);
        Assert.Equal(5, confirmed.UnchangedCount);
        await using var db = await Db();
        Assert.Equal(3, await db.ForecastPlans.CountAsync());
        Assert.Equal(18, await db.ForecastPlanEntries.CountAsync());
        Assert.Equal(7, await db.ForecastRevisions.CountAsync());
        var confirmedEntry = await db.ForecastPlanEntries.SingleAsync(x => x.ForecastPlanId == confirmed.PlanId &&
            x.ProductId == firstProduct && x.BusinessPeriod.JalaliYear == 1405 && x.BusinessPeriod.JalaliMonth == 1);
        Assert.Equal(changedValue, confirmedEntry.ForecastQty);
        Assert.Equal(changedValue,
            (await db.ForecastRevisions.SingleAsync(x => x.Id == confirmedEntry.ForecastRevisionId)).ForecastQty);
    }

    [Fact]
    public async Task Named_forecast_plan_rejects_invalid_range_incomplete_matrix_and_direct_history_mutation()
    {
        var product = await AddProduct("محصول حفاظت برنامه", "Protected plan product");
        var invalidDay = await _client.PostAsJsonAsync("/api/planning/forecast-plans/preview", new
        {
            name = "بازه نامعتبر",
            startJalaliDate = "14050102",
            endJalaliDate = "14050201",
            productIds = new[] { product }
        });
        Assert.Equal(HttpStatusCode.BadRequest, invalidDay.StatusCode);

        var incomplete = await _client.PostAsJsonAsync("/api/planning/forecast-plans", new
        {
            name = "ماتریس ناقص",
            startJalaliDate = "14050101",
            endJalaliDate = "14050201",
            reason = (string?)null,
            confirmDifferences = false,
            entries = new[] { new { productId = product, periodCode = "140501", forecastQty = 1m, expectedVersion = (long?)null } }
        });
        Assert.Equal(HttpStatusCode.BadRequest, incomplete.StatusCode);
        await using (var noWriteDb = await Db())
            Assert.Empty(await noWriteDb.ForecastPlans.ToListAsync());

        var savedResponse = await _client.PostAsJsonAsync("/api/planning/forecast-plans", new
        {
            name = "برنامه حفاظت‌شده",
            startJalaliDate = "14050101",
            endJalaliDate = "14050101",
            reason = (string?)null,
            confirmDifferences = false,
            entries = new[] { new { productId = product, periodCode = "140501", forecastQty = 1m, expectedVersion = (long?)null } }
        });
        savedResponse.EnsureSuccessStatusCode();
        var saved = (await savedResponse.Content.ReadFromJsonAsync<ForecastPlanSaveDto>())!;
        await using var db = await Db();
        await using var transaction = await db.Database.BeginTransactionAsync();
        var exception = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE planning.forecast_plan_entries SET \"ForecastQty\" = 2 WHERE \"ForecastPlanId\" = {saved.PlanId}"));
        Assert.Contains("FORECAST_PLAN_HISTORY_IMMUTABLE", exception.MessageText);
        await transaction.RollbackAsync();
    }

    [Fact]
    public async Task Saved_forecast_plan_is_listed_opened_and_corrected_with_append_only_history()
    {
        var product = await AddProduct("محصول برنامه قابل ویرایش", "Editable plan product");
        var createResponse = await _client.PostAsJsonAsync("/api/planning/forecast-plans", new
        {
            name = "برنامه قابل ویرایش ۱۴۰۵",
            startJalaliDate = "14050401",
            endJalaliDate = "14050401",
            reason = (string?)null,
            confirmDifferences = false,
            entries = new[]
            {
                new { productId = product, periodCode = "140504", forecastQty = 10m,
                    expectedVersion = (long?)null }
            }
        });
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var created = (await createResponse.Content.ReadFromJsonAsync<ForecastPlanSaveDto>())!;

        var periodBounds = new PlanningCalendar(TimeProvider.System).GetPeriodBounds(1405, 4);
        var firstActualResponse = await _client.PostAsJsonAsync("/api/planning/actuals", new
        {
            productId = product,
            productionDate = periodBounds.StartDate,
            actualQty = 3m
        });
        Assert.Equal(HttpStatusCode.Created, firstActualResponse.StatusCode);
        var firstActual = (await firstActualResponse.Content.ReadFromJsonAsync<ActualDto>())!;
        var correctedActualResponse = await _client.PostAsJsonAsync(
            $"/api/planning/actuals/{firstActual.Id}/corrections", new
            {
                actualQty = 5m,
                reason = "اصلاح تولید واقعی روزانه",
                expectedVersion = firstActual.Version
            });
        correctedActualResponse.EnsureSuccessStatusCode();
        var secondActualResponse = await _client.PostAsJsonAsync("/api/planning/actuals", new
        {
            productId = product,
            productionDate = periodBounds.StartDate.AddDays(1),
            actualQty = 4m
        });
        Assert.Equal(HttpStatusCode.Created, secondActualResponse.StatusCode);

        var plans = await _client.GetFromJsonAsync<ForecastPlanListDto>(
            "/api/planning/forecast-plans?search=Editable%20plan%20product&page=1&pageSize=25");
        Assert.NotNull(plans);
        var plan = Assert.Single(plans.Items);
        Assert.Equal(created.PlanId, plan.Id);
        Assert.Equal("برنامه قابل ویرایش ۱۴۰۵", plan.Name);
        Assert.Equal(1, plan.ProductCount);
        Assert.Equal(1, plan.MonthCount);
        Assert.Equal(1, plan.EntryCount);

        var detail = await _client.GetFromJsonAsync<ForecastPlanDetailDto>(
            $"/api/planning/forecast-plans/{created.PlanId}?page=1&pageSize=20");
        Assert.NotNull(detail);
        Assert.Equal("140504", Assert.Single(detail.Months).PeriodCode);
        Assert.Equal(product, Assert.Single(detail.Products).Id);
        var original = Assert.Single(detail.Cells);
        Assert.Equal(10m, original.OriginalForecastQty);
        Assert.Equal(10m, original.ForecastQty);
        Assert.Equal(1, original.PlanRevisionNumber);
        Assert.True(original.IsOfficialCurrent);
        Assert.Equal(9m, original.MonthlyActualQty);
        Assert.Equal(2, original.DailyActualEntryCount);
        Assert.Equal([10m], original.History.Select(x => x.ForecastQty));

        var correctedResponse = await _client.PostAsJsonAsync(
            $"/api/planning/forecast-plans/{created.PlanId}/entries/{original.EntryId}/corrections", new
            {
                forecastQty = 12m,
                reason = "اصلاح مقدار ماهانه",
                expectedSeriesVersion = original.SeriesVersion,
                expectedPlanRevisionNumber = original.PlanRevisionNumber,
                confirmDifference = false
            });
        Assert.Equal(HttpStatusCode.OK, correctedResponse.StatusCode);
        var corrected = (await correctedResponse.Content.ReadFromJsonAsync<ForecastPlanCorrectionDto>())!;
        Assert.Equal(12m, corrected.ForecastQty);
        Assert.Equal(2, corrected.PlanRevisionNumber);
        Assert.True(corrected.ForecastRevisionCreated);
        Assert.Equal("اصلاح مقدار ماهانه", corrected.History.Reason);

        var revisedDetail = await _client.GetFromJsonAsync<ForecastPlanDetailDto>(
            $"/api/planning/forecast-plans/{created.PlanId}?page=1&pageSize=20");
        var revised = Assert.Single(revisedDetail!.Cells);
        Assert.Equal(10m, revised.OriginalForecastQty);
        Assert.Equal(12m, revised.ForecastQty);
        Assert.Equal(2, revised.PlanRevisionNumber);
        Assert.True(revised.IsOfficialCurrent);
        Assert.Equal(9m, revised.MonthlyActualQty);
        Assert.Equal(2, revised.DailyActualEntryCount);
        Assert.Equal([1, 2], revised.History.Select(x => x.RevisionNumber));
        Assert.Equal([10m, 12m], revised.History.Select(x => x.ForecastQty));

        var staleResponse = await _client.PostAsJsonAsync(
            $"/api/planning/forecast-plans/{created.PlanId}/entries/{original.EntryId}/corrections", new
            {
                forecastQty = 13m,
                reason = "درخواست قدیمی",
                expectedSeriesVersion = corrected.SeriesVersion,
                expectedPlanRevisionNumber = 1,
                confirmDifference = false
            });
        Assert.Equal(HttpStatusCode.Conflict, staleResponse.StatusCode);
        Assert.Equal("STALE_VERSION", await Code(staleResponse));

        Guid seriesId;
        await using (var seriesDb = await Db())
        {
            seriesId = await seriesDb.ForecastPlanEntries.Where(x => x.Id == original.EntryId)
                .Select(x => x.ForecastRevision.ForecastSeriesId).SingleAsync();
        }
        var externalRevisionResponse = await _client.PostAsJsonAsync(
            $"/api/planning/forecast/{seriesId}/revisions", new
            {
                forecastQty = 13m,
                reason = "اصلاح خارج از برنامه ذخیره‌شده",
                expectedVersion = corrected.SeriesVersion
            });
        externalRevisionResponse.EnsureSuccessStatusCode();
        var externalRevision = (await externalRevisionResponse.Content.ReadFromJsonAsync<ForecastMutationDto>())!;

        var conflictResponse = await _client.PostAsJsonAsync(
            $"/api/planning/forecast-plans/{created.PlanId}/entries/{original.EntryId}/corrections", new
            {
                forecastQty = 14m,
                reason = "اصلاح با آگاهی از Forecast جدید",
                expectedSeriesVersion = externalRevision.Version,
                expectedPlanRevisionNumber = 2,
                confirmDifference = false
            });
        Assert.Equal(HttpStatusCode.Conflict, conflictResponse.StatusCode);
        Assert.Equal("FORECAST_VALUE_CONFLICT", await Code(conflictResponse));
        using (var conflictJson = JsonDocument.Parse(await conflictResponse.Content.ReadAsStringAsync()))
        {
            var conflict = Assert.Single(conflictJson.RootElement.GetProperty("details").EnumerateArray());
            Assert.Equal(12m, conflict.GetProperty("planForecastQty").GetDecimal());
            Assert.Equal(13m, conflict.GetProperty("officialForecastQty").GetDecimal());
            Assert.Equal(14m, conflict.GetProperty("proposedQty").GetDecimal());
        }

        var confirmedResponse = await _client.PostAsJsonAsync(
            $"/api/planning/forecast-plans/{created.PlanId}/entries/{original.EntryId}/corrections", new
            {
                forecastQty = 14m,
                reason = "اصلاح با آگاهی از Forecast جدید",
                expectedSeriesVersion = externalRevision.Version,
                expectedPlanRevisionNumber = 2,
                confirmDifference = true
            });
        confirmedResponse.EnsureSuccessStatusCode();
        var confirmed = (await confirmedResponse.Content.ReadFromJsonAsync<ForecastPlanCorrectionDto>())!;
        Assert.Equal(3, confirmed.PlanRevisionNumber);
        Assert.Equal(14m, confirmed.ForecastQty);

        await using var db = await Db();
        var storedOriginal = await db.ForecastPlanEntries.SingleAsync(x => x.Id == original.EntryId);
        Assert.Equal(10m, storedOriginal.ForecastQty);
        var storedCorrections = await db.ForecastPlanEntryCorrections
            .Where(x => x.ForecastPlanEntryId == original.EntryId)
            .OrderBy(x => x.RevisionNumber).ToListAsync();
        Assert.Equal([12m, 14m], storedCorrections.Select(x => x.ForecastQty));
        Assert.Equal([2, 3], storedCorrections.Select(x => x.RevisionNumber));
        Assert.Equal("اصلاح مقدار ماهانه", storedCorrections[0].Reason);
        var storedCorrection = storedCorrections[0];

        await using var transaction = await db.Database.BeginTransactionAsync();
        var immutable = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE planning.forecast_plan_entry_corrections SET \"ForecastQty\" = 99 WHERE \"Id\" = {storedCorrection.Id}"));
        Assert.Contains("FORECAST_PLAN_HISTORY_IMMUTABLE", immutable.MessageText);
        await transaction.RollbackAsync();
    }

    [Fact]
    public async Task Forecast_batch_rejects_a_corrupt_overlapping_business_period_without_partial_data()
    {
        var product = await AddProduct("محصول هم‌پوشانی", "Overlap product");
        var calendar = new PlanningCalendar(TimeProvider.System);
        var target = calendar.GetPeriodBounds(1405, 10);
        await using (var db = await Db())
        {
            db.BusinessPeriods.Add(new BusinessPeriod(1500, 1, target.StartDate, target.EndDate));
            await db.SaveChangesAsync();
        }
        var response = await _client.PostAsJsonAsync("/api/planning/forecast-entry-grid", new
        {
            periodCode = "140510",
            reason = (string?)null,
            entries = new[] { new { productId = product, forecastQty = 10m, expectedVersion = (long?)null } }
        });
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("BUSINESS_PERIOD_DATE_OVERLAP", await Code(response));
        await using var verify = await Db();
        Assert.False(await verify.BusinessPeriods.AnyAsync(x => x.JalaliYear == 1405 && x.JalaliMonth == 10));
        Assert.False(await verify.ForecastSeries.AnyAsync(x => x.ProductId == product));
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
    public async Task Daily_actual_date_creates_its_missing_jalali_period_and_is_listed_without_period_filter()
    {
        var product = await AddProduct("تولید بدون انتخاب دوره", "Period-free actual entry");
        var businessDate = await _client.GetFromJsonAsync<BusinessDateDto>("/api/planning/business-date");
        await using (var before = await Db())
            Assert.False(await before.BusinessPeriods.AnyAsync(x => x.JalaliYear == businessDate!.JalaliYear &&
                x.JalaliMonth == businessDate.JalaliMonth));

        var response = await _client.PostAsJsonAsync("/api/planning/actuals", new
        {
            productId = product,
            productionDate = businessDate!.TehranBusinessDate,
            actualQty = 12.5m
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = (await response.Content.ReadFromJsonAsync<ActualDto>())!;
        Assert.Equal(businessDate.JalaliYear, created.Period.JalaliYear);
        Assert.Equal(businessDate.JalaliMonth, created.Period.JalaliMonth);
        Assert.Equal("OPEN", created.Period.Status);
        Assert.InRange(businessDate.TehranBusinessDate, created.Period.StartDate, created.Period.EndDate);
        Assert.Equal(12.5m, created.CurrentRevision.ActualQty);

        var list = await _client.GetFromJsonAsync<ActualListDto>("/api/planning/actuals?page=1&pageSize=25");
        Assert.Contains(list!.Items, x => x.Id == created.Id && x.Product.Id == product);
        await using (var after = await Db())
            Assert.Equal(1, await after.BusinessPeriods.CountAsync(x => x.JalaliYear == businessDate.JalaliYear &&
                x.JalaliMonth == businessDate.JalaliMonth));
        Assert.Contains(_factory.AuditSink.Snapshot(), x => x.EntityType == "BusinessPeriod" &&
            x.EntityId == created.Period.Id && x.Action == "CREATE");
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
        Assert.All(events, x => Assert.Equal(
            "00000000-0000-0000-0000-000000000001", x.ActorIdentifier));
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

        await AddApprovedBom(product);
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
    public async Task Bom_readiness_uses_current_bom_and_reports_inactive_and_unit_drift()
    {
        var partialProduct = await AddProduct("پوشش ناقص", "Partial coverage");
        var inactiveProduct = await AddProduct("فرزند غیرفعال", "Inactive child");
        var driftProduct = await AddProduct("انحراف BOM", "BOM drift");
        var parentDriftProduct = await AddProduct("انحراف والد", "Parent drift");
        var currentProduct = await AddProduct("پنجره جاری", "Current window");
        var period = await CreatePeriod(1406, 4);

        await AddApprovedBom(partialProduct);
        var inactiveBom = await AddApprovedBom(inactiveProduct);
        var driftBom = await AddApprovedBom(driftProduct);
        await AddApprovedBom(parentDriftProduct);
        var asOf = period.StartDate.AddDays(10);
        await AddApprovedBom(currentProduct);

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
        Assert.Equal("READY",
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

    private async Task<(Guid RevisionId, Guid ChildId)> AddApprovedBom(Guid productId)
    {
        var childId = await AddNonProduct();
        await using var db = await Db();
        var revision = new BomRevision($"BOM-{Random.Shared.NextInt64(1_000_000, 9_999_999)}",
            productId, _unit, 1, null);
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
    private sealed record ForecastEntryPeriodDto(string PeriodCode, int JalaliYear, int JalaliMonth,
        DateOnly StartDate, DateOnly EndDate, Guid? BusinessPeriodId, string Status, long? Version,
        bool Exists);
    private sealed record ForecastEntryRowDto(ProductDto Product, Guid? SeriesId,
        int? CurrentRevisionNumber, decimal? CurrentForecastQty, long? SeriesVersion);
    private sealed record ForecastEntryGridDto(ForecastEntryPeriodDto Period,
        ForecastEntryRowDto[] Items, int TotalCount);
    private sealed record ForecastBatchDto(PeriodDto Period, int CreatedCount, int RevisedCount,
        int UnchangedCount);
    private sealed record ForecastProductListDto(ProductDto[] Items, int Page, int PageSize,
        int TotalCount);
    private sealed record ForecastPlanMonthDto(string PeriodCode, int JalaliYear, int JalaliMonth,
        DateOnly StartDate, DateOnly EndDate, Guid? BusinessPeriodId, string Status);
    private sealed record ForecastPlanCellDto(Guid ProductId, string PeriodCode,
        decimal? CurrentForecastQty, int? CurrentRevisionNumber, long? SeriesVersion);
    private sealed record ForecastPlanPreviewDto(string Name, string StartJalaliDate,
        string EndJalaliDate, DateOnly StartDate, DateOnly EndDate,
        ForecastPlanMonthDto[] Months, ProductDto[] Products, ForecastPlanCellDto[] Cells);
    private sealed record ForecastPlanSaveDto(Guid PlanId, string Name, DateOnly StartDate,
        DateOnly EndDate, int CreatedCount, int RevisedCount, int UnchangedCount, int EntryCount);
    private sealed record ForecastPlanSummaryDto(Guid Id, string Name, string StartJalaliDate,
        string EndJalaliDate, DateOnly StartDate, DateOnly EndDate, int ProductCount,
        int MonthCount, int EntryCount, string? ActorIdentifier, DateTime CreatedAtUtc);
    private sealed record ForecastPlanListDto(ForecastPlanSummaryDto[] Items, int Page,
        int PageSize, int TotalCount);
    private sealed record ForecastPlanHistoryDto(int RevisionNumber, decimal ForecastQty,
        string? Reason, string? ActorIdentifier, DateTime CreatedAtUtc);
    private sealed record ForecastPlanDetailCellDto(Guid EntryId, Guid ProductId,
        string PeriodCode, decimal OriginalForecastQty, decimal ForecastQty,
        int PlanRevisionNumber, Guid ForecastRevisionId, int ForecastRevisionNumber,
        long SeriesVersion, decimal OfficialCurrentForecastQty,
        int OfficialCurrentRevisionNumber, bool IsOfficialCurrent, decimal MonthlyActualQty,
        int DailyActualEntryCount,
        ForecastPlanHistoryDto[] History);
    private sealed record ForecastPlanDetailDto(ForecastPlanSummaryDto Plan,
        ForecastPlanMonthDto[] Months, ProductDto[] Products, ForecastPlanDetailCellDto[] Cells,
        int Page, int PageSize, int TotalProducts);
    private sealed record ForecastPlanCorrectionDto(Guid PlanId, Guid EntryId,
        decimal ForecastQty, int PlanRevisionNumber, Guid ForecastRevisionId,
        int ForecastRevisionNumber, long SeriesVersion, bool ForecastRevisionCreated,
        ForecastPlanHistoryDto History);
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
    private sealed record ActualListDto(ActualDto[] Items, int Page, int PageSize, int TotalCount);
    private sealed record ActualHistoryDto(ActualDto Actual, ActualRevisionDto[] Revisions);

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
