using System.Net;
using System.Net.Http.Json;
using System.IO.Compression;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using Hadron.Erp.Api.Infrastructure;
using Hadron.Erp.Api.Modules.Analytics.Application;
using Hadron.Erp.Api.Modules.Catalog.Domain;
using Hadron.Erp.Api.Modules.Inventory.Domain;
using Hadron.Erp.Api.Modules.Planning.Domain;
using Hadron.Erp.Api.Modules.Products.Domain;
using Hadron.Erp.Api.Modules.PurchaseSupply.Domain;
using Hadron.Erp.Api.Modules.ReferenceData.Domain;
using Hadron.Erp.Api.Modules.Sourcing.Domain;
using Hadron.Erp.Api.Modules.Suppliers.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Hadron.Erp.IntegrationTests;

public sealed class AnalyticsGoldenTests
{
    [Fact]
    public void GD06_uses_literal_continuous_percentile_expectations()
    {
        int[] observations = [10, 12, 14, 16, 21];

        var statistics = AnalyticsMath.Statistics(observations, "12M");

        Assert.NotNull(statistics);
        Assert.Equal(5, statistics.SampleCount);
        Assert.Equal(14.6m, statistics.Mean);
        Assert.Equal(14m, statistics.P50);
        Assert.Equal(17m, statistics.P80);
        Assert.Equal(19m, statistics.P90);
        Assert.Equal(17, statistics.RecommendedDays);
        var onTimeCount = observations.Count(x => x <= 14);
        Assert.Equal(3, onTimeCount);
        Assert.Equal(60m, onTimeCount * 100m / observations.Length);
    }

    [Fact]
    public void GD07_uses_literal_forecast_accuracy_expectations()
    {
        (decimal Forecast, decimal Actual)[] baseline = [(100m, 105m), (200m, 150m)];
        (decimal Forecast, decimal Actual)[] final = [(110m, 105m), (180m, 150m)];

        Assert.Equal(21.5686274510m, decimal.Round(AnalyticsMath.Wape(baseline)!.Value, 10));
        Assert.Equal(17.6470588235m, decimal.Round(AnalyticsMath.Bias(baseline)!.Value, 10));
        Assert.Equal(13.7254901961m, decimal.Round(AnalyticsMath.Wape(final)!.Value, 10));
        Assert.Equal(13.7254901961m, decimal.Round(AnalyticsMath.Bias(final)!.Value, 10));
        Assert.Null(AnalyticsMath.Ape(0m, 0m));
        Assert.Equal(100m, AnalyticsMath.Ape(10m, 5m));
    }
}

public sealed class AnalyticsEndpointTests : IClassFixture<FoundationApiFactory>, IAsyncLifetime
{
    private readonly FoundationApiFactory _factory;
    private readonly HttpClient _client;

    public AnalyticsEndpointTests(FoundationApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    public async Task InitializeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<HadronDbContext>>();
        await using var db = await dbFactory.CreateDbContextAsync();
        await db.Database.MigrateAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Theory]
    [InlineData("/api/v1/analytics/lead-time?evaluationDate=2026-08-25&page=1&pageSize=25")]
    [InlineData("/api/v1/analytics/forecast-accuracy?page=1&pageSize=25")]
    [InlineData("/api/v1/analytics/commercial-commitments?evaluationDate=2026-08-25&page=1&pageSize=25")]
    [InlineData("/api/v1/analytics/inventory/positions?asOfDate=2026-08-25&page=1&pageSize=25")]
    [InlineData("/api/v1/analytics/inventory/movements?fromDate=2026-08-01&toDate=2026-08-25&page=1&pageSize=25")]
    public async Task Versioned_read_contracts_are_bounded_and_read_only(string path)
    {
        using var response = await _client.GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("\"totalCount\":0", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Excel_export_is_server_generated_and_contains_xlsx_package()
    {
        using var response = await _client.GetAsync(
            "/api/v1/analytics/exports/forecast-accuracy/xlsx?evaluationDate=2026-08-25&language=en&maximumRows=100");
        var bytes = await response.Content.ReadAsByteArrayAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            response.Content.Headers.ContentType?.MediaType);
        Assert.True(bytes.Length > 100);
        Assert.Equal((byte)'P', bytes[0]);
        Assert.Equal((byte)'K', bytes[1]);
    }

    [Fact]
    public async Task Mutation_surface_is_limited_to_explicit_sourcing_apply_command()
    {
        using var response = await _client.PostAsJsonAsync("/api/v1/analytics/forecast-accuracy", new { });

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
    }

    [Fact]
    public async Task Applied_lead_time_history_is_database_immutable()
    {
        using var scope = _factory.Services.CreateScope();
        var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<HadronDbContext>>();
        await using var db = await dbFactory.CreateDbContextAsync();

        var triggerCount = await db.Database.SqlQueryRaw<int>(
            "SELECT count(*)::integer AS \"Value\" FROM pg_trigger WHERE tgname = 'tr_lead_time_application_immutable' AND NOT tgisinternal")
            .SingleAsync();

        Assert.Equal(1, triggerCount);
    }
}

[Collection("ReferenceData")]
public sealed class AnalyticsRemediationTests : IClassFixture<FoundationApiFactory>, IAsyncLifetime
{
    private readonly FoundationApiFactory _factory;
    private readonly HttpClient _client;
    private MasterData _master = null!;

    public AnalyticsRemediationTests(FoundationApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    public async Task InitializeAsync()
    {
        await using var db = await Db();
        await db.Database.MigrateAsync();
        await db.Database.ExecuteSqlRawAsync("TRUNCATE procurement.purchase_orders RESTART IDENTITY CASCADE");
        await db.Database.ExecuteSqlRawAsync("TRUNCATE planning.business_periods RESTART IDENTITY CASCADE");
        await db.Database.ExecuteSqlRawAsync("TRUNCATE inventory.transactions RESTART IDENTITY CASCADE");
        _master = await AddMasterData(db);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task GD09_historical_forecast_cutoff_remains_F1_after_F2_and_reclose()
    {
        await using var db = await Db();
        var product = await AddProduct(db, "GD09");
        var start = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(5);
        var period = new BusinessPeriod(1406, 1, start, start.AddDays(30));
        db.BusinessPeriods.Add(period);
        await db.SaveChangesAsync();
        var series = new ForecastSeries(product.ItemId, period.Id, _master.Unit.Id);
        db.ForecastSeries.Add(series);
        await db.SaveChangesAsync();
        var f1 = new ForecastRevision(series.Id, 1, 40m, null, "GD09");
        db.ForecastRevisions.Add(f1);
        series.SetCurrent(f1);
        await db.SaveChangesAsync();
        period.Close();
        db.PeriodLifecycleEvents.Add(new(period.Id, BusinessPeriodStatuses.Open,
            BusinessPeriodStatuses.Closed, "GD09 F1 close", "GD09"));
        await db.SaveChangesAsync();
        await Task.Delay(5);
        var cutoff = await DatabaseUtcNow();

        var historicalBefore = await Forecast(product.Item.BusinessNumber, cutoff);
        var exportBefore = await ExportData(
            $"/api/v1/analytics/exports/forecast-accuracy/xlsx?evaluationDate={period.EndDate:yyyy-MM-dd}" +
            $"&language=en&maximumRows=100&search={product.Item.BusinessNumber}{Cutoff(cutoff)}");
        Assert.Equal(40m, historicalBefore.GetProperty("finalForecast").GetDecimal());
        Assert.Equal(0m, historicalBefore.GetProperty("actual").GetDecimal());

        var f2 = new ForecastRevision(series.Id, 2, 99m, "GD09 F2", "GD09");
        db.ForecastRevisions.Add(f2);
        series.Advance(f2);
        period.Reopen();
        db.PeriodLifecycleEvents.Add(new(period.Id, BusinessPeriodStatuses.Closed,
            BusinessPeriodStatuses.Open, "GD09 reopen", "GD09"));
        await db.SaveChangesAsync();
        await Task.Delay(5);
        period.Close();
        db.PeriodLifecycleEvents.Add(new(period.Id, BusinessPeriodStatuses.Open,
            BusinessPeriodStatuses.Closed, "GD09 reclose", "GD09"));
        await db.SaveChangesAsync();

        var historicalAfter = await Forecast(product.Item.BusinessNumber, cutoff);
        var exportAfter = await ExportData(
            $"/api/v1/analytics/exports/forecast-accuracy/xlsx?evaluationDate={period.EndDate:yyyy-MM-dd}" +
            $"&language=en&maximumRows=100&search={product.Item.BusinessNumber}{Cutoff(cutoff)}");
        var current = await Forecast(product.Item.BusinessNumber, null);
        Assert.Equal(40m, historicalAfter.GetProperty("finalForecast").GetDecimal());
        Assert.Equal(historicalBefore.GetRawText(), historicalAfter.GetRawText());
        Assert.Equal(exportBefore, exportAfter);
        Assert.Equal(99m, current.GetProperty("finalForecast").GetDecimal());
    }

    [Fact]
    public async Task GD09_all_analytics_context_and_schedule_allocation_remain_frozen_at_cutoff()
    {
        var order = await AddOrder(new DateOnly(2026, 9, 10), new DateOnly(2026, 9, 12));
        await using var db = await Db();
        var warehouse = new Warehouse($"WH-{Random.Shared.Next(100000, 999999)}", "انبار تاریخچه",
            "Historical warehouse", null);
        db.Warehouses.Add(warehouse);
        await db.SaveChangesAsync();
        db.InventoryTransactions.Add(new InventoryTransaction(InventoryTransactionTypes.Opening, _master.Item.Id,
            warehouse.Id, null, 5m, _master.Unit.Id, new DateOnly(2026, 8, 1), "opening", null, null, null, "GD09"));
        await db.SaveChangesAsync();
        await Task.Delay(10);
        var cutoff = await DatabaseUtcNow();

        var leadBefore = await LeadTime(_master.Option.SourcingNumber, cutoff);
        var commercialBefore = await Commercial(order.Number, cutoff);
        var inventoryBefore = await InventoryPosition(_master.Item.BusinessNumber, new DateOnly(2026, 8, 1), cutoff);
        var leadExportBefore = await ExportData(
            $"/api/v1/analytics/exports/lead-time/xlsx?evaluationDate=2026-12-01&language=en" +
            $"&maximumRows=100&search={_master.Option.SourcingNumber}{Cutoff(cutoff)}");
        var commercialExportBefore = await ExportData(
            $"/api/v1/analytics/exports/commercial-commitments/xlsx?evaluationDate=2026-12-01&language=en" +
            $"&maximumRows=100&search={order.Number}{Cutoff(cutoff)}");
        var inventoryExportBefore = await ExportData(
            $"/api/v1/analytics/exports/inventory-position/xlsx?evaluationDate=2026-08-01&language=en" +
            $"&maximumRows=100&search={_master.Item.BusinessNumber}{Cutoff(cutoff)}");

        var option = await db.SourcingOptions.SingleAsync(x => x.Id == _master.Option.Id);
        option.Edit(option.MinimumOrderQuantity, option.ShippingModeId, option.IncotermId, 9, 8, 7, 6);
        var item = await db.Items.SingleAsync(x => x.Id == _master.Item.Id);
        item.Edit("قطعه تحلیل جدید", "Renamed analytics part", item.BaseUnitId, item.Notes);
        var supplier = await db.Suppliers.SingleAsync(x => x.Id == _master.Supplier.Id);
        supplier.Edit("تأمین‌کننده تحلیل جدید", "تأمین‌کننده تحلیل جدید", supplier.CountryId, supplier.OwnerId,
            supplier.Notes, supplier.ExpertLeadTimeScore, supplier.ExpertQualityScore);
        await db.SaveChangesAsync();

        await using (var tx = await db.Database.BeginTransactionAsync())
        {
            var current = await db.DeliverySchedules.Where(x => x.PurchaseOrderLine.PurchaseOrderId == order.Id)
                .OrderBy(x => x.ScheduleNumber).ToListAsync();
            var before = JsonSerializer.Serialize(current.Select(x => new
            {
                x.Id,
                x.ScheduleNumber,
                x.AllocatedQuantity,
                x.RequestedReceiptDate
            }));
            current[0].Reallocate(20m, current[0].RequestedReceiptDate);
            current[1].Reallocate(40m, current[1].RequestedReceiptDate);
            db.DeliverySchedules.Add(new(current[0].PurchaseOrderLineId, 3, 20m, new DateOnly(2026, 9, 15)));
            db.DeliveryScheduleAllocationEvents.Add(new(order.Id, current[0].PurchaseOrderLineId, before,
                "[]", "GD09 later reallocation", "GD09", DateTime.UtcNow));
            await db.SaveChangesAsync();
            await tx.CommitAsync();
        }

        db.InventoryTransactions.Add(new InventoryTransaction(InventoryTransactionTypes.AdjustmentOut,
            _master.Item.Id, warehouse.Id, null, 5m, _master.Unit.Id, new DateOnly(2026, 8, 2),
            "later zeroing", null, null, null, "GD09"));
        await db.SaveChangesAsync();
        warehouse.ChangeState(false);
        await db.SaveChangesAsync();

        var leadAfter = await LeadTime(_master.Option.SourcingNumber, cutoff);
        var commercialAfter = await Commercial(order.Number, cutoff);
        var inventoryAfter = await InventoryPosition(_master.Item.BusinessNumber, new DateOnly(2026, 8, 1), cutoff);
        var currentLead = await LeadTime(_master.Option.SourcingNumber, null);
        var currentCommercial = await Commercial(order.Number, null);
        var currentInventory = await InventoryPosition(_master.Item.BusinessNumber, new DateOnly(2026, 8, 1), null);
        var leadExportAfter = await ExportData(
            $"/api/v1/analytics/exports/lead-time/xlsx?evaluationDate=2026-12-01&language=en" +
            $"&maximumRows=100&search={_master.Option.SourcingNumber}{Cutoff(cutoff)}");
        var commercialExportAfter = await ExportData(
            $"/api/v1/analytics/exports/commercial-commitments/xlsx?evaluationDate=2026-12-01&language=en" +
            $"&maximumRows=100&search={order.Number}{Cutoff(cutoff)}");
        var inventoryExportAfter = await ExportData(
            $"/api/v1/analytics/exports/inventory-position/xlsx?evaluationDate=2026-08-01&language=en" +
            $"&maximumRows=100&search={_master.Item.BusinessNumber}{Cutoff(cutoff)}");

        Assert.Equal(leadBefore.GetRawText(), leadAfter.GetRawText());
        Assert.Equal(commercialBefore.GetRawText(), commercialAfter.GetRawText());
        Assert.Equal(inventoryBefore.GetRawText(), inventoryAfter.GetRawText());
        Assert.Equal(leadExportBefore, leadExportAfter);
        Assert.Equal(commercialExportBefore, commercialExportAfter);
        Assert.Equal(inventoryExportBefore, inventoryExportAfter);
        Assert.Equal("قطعه تحلیل", leadAfter.GetProperty("itemNameFa").GetString());
        Assert.Equal(2, commercialAfter.GetProperty("requiredScheduleCount").GetInt32());
        Assert.True(inventoryAfter.GetProperty("warehouseIsActive").GetBoolean());
        Assert.Equal(9, currentLead.GetProperty("stages")[0].GetProperty("configuredDays").GetInt32());
        Assert.Equal("تأمین‌کننده تحلیل جدید", currentCommercial.GetProperty("supplierNameFa").GetString());
        Assert.Equal(3, currentCommercial.GetProperty("requiredScheduleCount").GetInt32());
        Assert.False(currentInventory.GetProperty("warehouseIsActive").GetBoolean());
    }

    [Fact]
    public async Task GD06_persisted_delivery_facts_match_literal_API_and_Excel_statistics()
    {
        await using var db = await Db();
        var warehouse = new Warehouse($"WH-{Random.Shared.Next(100000, 999999)}", "انبار GD06", "GD06 warehouse", null);
        db.Warehouses.Add(warehouse);
        await db.SaveChangesAsync();
        var completion = new DateOnly(2026, 8, 25);
        int[] literalLeadDays = [10, 12, 14, 16, 21];
        foreach (var days in literalLeadDays)
        {
            var fixture = await AddLeadTimeOrder(completion.AddDays(-days), completion.AddDays(-days).AddDays(14));
            await PostReceipt(fixture, warehouse.Id, 1m, completion);
        }
        var cancelled = await AddLeadTimeOrder(completion.AddDays(-5), completion.AddDays(14));
        var cancelledPo = await PurchaseOrder(cancelled.Id);
        var cancellationResponse = await _client.PostAsJsonAsync(
            $"/api/purchase-orders/{cancelled.Id}/schedules/{cancelled.FirstScheduleId}/cancel", new
            {
                expectedPurchaseOrderVersion = cancelledPo.GetProperty("version").GetInt64(),
                quantity = 1m,
                effectiveDate = completion.ToString("yyyy-MM-dd"),
                reason = "GD06 excluded cancellation"
            });
        cancellationResponse.EnsureSuccessStatusCode();

        var row = await LeadTime(_master.Option.SourcingNumber, null, completion);
        var total = row.GetProperty("total");
        Assert.Equal(5, total.GetProperty("sampleCount").GetInt32());
        Assert.Equal(14.6m, total.GetProperty("mean").GetDecimal());
        Assert.Equal(14m, total.GetProperty("p50").GetDecimal());
        Assert.Equal(17m, total.GetProperty("p80").GetDecimal());
        Assert.Equal(19m, total.GetProperty("p90").GetDecimal());
        Assert.Equal(60m, row.GetProperty("onTimePercent").GetDecimal());
        Assert.Equal(1, row.GetProperty("cancelledScheduleCount").GetInt32());

        var export = await _client.GetAsync($"/api/v1/analytics/exports/lead-time/xlsx?evaluationDate={completion:yyyy-MM-dd}" +
            $"&language=en&maximumRows=100&search={_master.Option.SourcingNumber}");
        export.EnsureSuccessStatusCode();
        var xml = await Sheet(export);
        Assert.Contains(_master.Option.SourcingNumber, xml, StringComparison.Ordinal);
        var excelNumbers = SheetDecimals(xml);
        Assert.Contains(14.6m, excelNumbers);
        Assert.Contains(14m, excelNumbers);
        Assert.Contains(17m, excelNumbers);
        Assert.Contains(19m, excelNumbers);
    }

    [Fact]
    public async Task GD07_persisted_forecast_history_matches_literal_API_and_Excel_accuracy()
    {
        await using var db = await Db();
        var period = new BusinessPeriod(1405, 10, new DateOnly(2026, 1, 10), new DateOnly(2026, 2, 8));
        db.BusinessPeriods.Add(period);
        await db.SaveChangesAsync();
        var baselineAt = new DateTime(2026, 1, 9, 8, 0, 0, DateTimeKind.Utc);
        var finalAt = new DateTime(2026, 1, 11, 8, 0, 0, DateTimeKind.Utc);
        var actualAt = new DateTime(2026, 1, 15, 8, 0, 0, DateTimeKind.Utc);
        await AddForecastAccuracyProduct(db, period, "GD07-METRIC-F1", 100m, 110m, 105m,
            baselineAt, finalAt, actualAt);
        await AddForecastAccuracyProduct(db, period, "GD07-METRIC-F2", 200m, 180m, 150m,
            baselineAt.AddMinutes(1), finalAt.AddMinutes(1), actualAt.AddMinutes(1));
        await AddForecastAccuracyProduct(db, period, "GD07-EDGE-ZERO", 10m, 10m, 0m,
            baselineAt.AddMinutes(2), finalAt.AddMinutes(2), actualAt.AddMinutes(2));
        await AddForecastAccuracyProduct(db, period, "GD07-EDGE-MISSING", null, null, 0m,
            baselineAt.AddMinutes(3), finalAt.AddMinutes(3), actualAt.AddMinutes(3));

        period.Close();
        var close1 = new PeriodLifecycleEvent(period.Id, BusinessPeriodStatuses.Open,
            BusinessPeriodStatuses.Closed, "GD07 first close", "GD07");
        db.PeriodLifecycleEvents.Add(close1);
        db.Entry(close1).Property(x => x.OccurredAtUtc).CurrentValue =
            new DateTime(2026, 1, 20, 8, 0, 0, DateTimeKind.Utc);
        await db.SaveChangesAsync();
        period.Reopen();
        var reopen = new PeriodLifecycleEvent(period.Id, BusinessPeriodStatuses.Closed,
            BusinessPeriodStatuses.Open, "GD07 reopen", "GD07");
        db.PeriodLifecycleEvents.Add(reopen);
        db.Entry(reopen).Property(x => x.OccurredAtUtc).CurrentValue =
            new DateTime(2026, 1, 21, 8, 0, 0, DateTimeKind.Utc);
        await db.SaveChangesAsync();
        period.Close();
        var close2 = new PeriodLifecycleEvent(period.Id, BusinessPeriodStatuses.Open,
            BusinessPeriodStatuses.Closed, "GD07 effective reclose", "GD07");
        db.PeriodLifecycleEvents.Add(close2);
        db.Entry(close2).Property(x => x.OccurredAtUtc).CurrentValue =
            new DateTime(2026, 1, 22, 8, 0, 0, DateTimeKind.Utc);
        await db.SaveChangesAsync();

        var response = await _client.GetAsync(
            "/api/v1/analytics/forecast-accuracy?search=GD07-METRIC&page=1&pageSize=25");
        response.EnsureSuccessStatusCode();
        var payload = await Read(response);
        var summary = payload.GetProperty("summary");
        Assert.Equal(21.5686274510m, decimal.Round(summary.GetProperty("baselineWape").GetDecimal(), 10));
        Assert.Equal(17.6470588235m, decimal.Round(summary.GetProperty("baselineBias").GetDecimal(), 10));
        Assert.Equal(13.7254901961m, decimal.Round(summary.GetProperty("finalWape").GetDecimal(), 10));
        Assert.Equal(13.7254901961m, decimal.Round(summary.GetProperty("finalBias").GetDecimal(), 10));
        Assert.Equal([100m, 200m], payload.GetProperty("page").GetProperty("items").EnumerateArray()
            .Select(x => x.GetProperty("baselineForecast").GetDecimal()).Order().ToArray());
        Assert.Equal([110m, 180m], payload.GetProperty("page").GetProperty("items").EnumerateArray()
            .Select(x => x.GetProperty("finalForecast").GetDecimal()).Order().ToArray());

        var edgeResponse = await _client.GetAsync(
            "/api/v1/analytics/forecast-accuracy?search=GD07-EDGE&page=1&pageSize=25");
        edgeResponse.EnsureSuccessStatusCode();
        var edgeRows = (await Read(edgeResponse)).GetProperty("page").GetProperty("items").EnumerateArray().ToArray();
        var zero = Assert.Single(edgeRows, x => x.GetProperty("productNameEn").GetString()!.Contains("ZERO"));
        Assert.Equal(0m, zero.GetProperty("actual").GetDecimal());
        Assert.Equal(JsonValueKind.Null, zero.GetProperty("baselineApe").ValueKind);
        Assert.Equal(JsonValueKind.Null, zero.GetProperty("finalApe").ValueKind);
        var missing = Assert.Single(edgeRows, x => x.GetProperty("productNameEn").GetString()!.Contains("MISSING"));
        Assert.Equal("MISSING_FORECAST", missing.GetProperty("baselineState").GetString());
        Assert.Equal("MISSING_FORECAST", missing.GetProperty("finalState").GetString());

        var export = await _client.GetAsync(
            "/api/v1/analytics/exports/forecast-accuracy/xlsx?evaluationDate=2026-02-08&language=en" +
            "&maximumRows=100&search=GD07-METRIC");
        export.EnsureSuccessStatusCode();
        var xml = await Sheet(export);
        var excelNumbers = SheetDecimals(xml);
        Assert.Contains(100m, excelNumbers);
        Assert.Contains(110m, excelNumbers);
        Assert.Contains(105m, excelNumbers);
        Assert.Contains(200m, excelNumbers);
        Assert.Contains(180m, excelNumbers);
        Assert.Contains(150m, excelNumbers);
        Assert.Contains("search=GD07-METRIC", xml, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Commercial_cutoff_preserves_D1_and_canonical_multi_schedule_anchor()
    {
        var d1a = new DateOnly(2026, 9, 10);
        var d1b = new DateOnly(2026, 9, 12);
        var order = await AddOrder(d1a, d1b);
        var cutoff = DateTime.UtcNow;
        var historicalBefore = await Commercial(order.Number, cutoff);
        var exportBefore = await ExportData(
            $"/api/v1/analytics/exports/commercial-commitments/xlsx?evaluationDate=2026-12-01&language=en" +
            $"&maximumRows=100&search={order.Number}{Cutoff(cutoff)}");

        await AddRevisedPlans(order, new DateOnly(2026, 9, 20), new DateOnly(2026, 9, 22),
            cutoff.AddMilliseconds(1));
        var laterOrder = await AddOrder(new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 2));

        var historical = await Commercial(order.Number, cutoff);
        var exportAfter = await ExportData(
            $"/api/v1/analytics/exports/commercial-commitments/xlsx?evaluationDate=2026-12-01&language=en" +
            $"&maximumRows=100&search={order.Number}{Cutoff(cutoff)}");
        var newerCutoff = DateTime.UtcNow;
        var current = await Commercial(order.Number, newerCutoff);
        var cutoffAll = await CommercialPage(null, cutoff);

        Assert.Equal(historicalBefore.GetRawText(), historical.GetRawText());
        Assert.Equal(exportBefore, exportAfter);
        Assert.Equal(80m, historical.GetProperty("orderedQuantity").GetDecimal());
        Assert.Equal(560m, historical.GetProperty("originalAmount").GetDecimal());
        Assert.Equal(d1b.ToString("yyyy-MM-dd"), historical.GetProperty("creditAnchorDate").GetString());
        Assert.Equal(new DateOnly(2026, 9, 22).ToString("yyyy-MM-dd"),
            current.GetProperty("creditAnchorDate").GetString());
        Assert.Equal("ORDERED", historical.GetProperty("status").GetString());
        Assert.DoesNotContain(cutoffAll.GetProperty("items").EnumerateArray(),
            x => x.GetProperty("purchaseOrderNumber").GetString() == laterOrder.Number);
        Assert.Single(cutoffAll.GetProperty("items").EnumerateArray(),
            x => x.GetProperty("purchaseOrderNumber").GetString() == order.Number);

        var unresolved = await AddOrder(new DateOnly(2026, 11, 1), null);
        var unresolvedRow = await Commercial(unresolved.Number, null);
        Assert.Equal(JsonValueKind.Null, unresolvedRow.GetProperty("creditAnchorDate").ValueKind);
        Assert.Equal("UNRESOLVED", unresolvedRow.GetProperty("anchorState").GetString());
    }

    [Fact]
    public async Task Inventory_position_has_warehouse_and_company_truth_at_explicit_cutoff()
    {
        await using var db = await Db();
        var first = new Warehouse($"WH-{Random.Shared.Next(100000, 999999)}", "انبار یک", "Warehouse one", null);
        var second = new Warehouse($"WH-{Random.Shared.Next(100000, 999999)}", "انبار دو", "Warehouse two", null);
        db.Warehouses.AddRange(first, second);
        await db.SaveChangesAsync();
        db.InventoryTransactions.AddRange(
            new InventoryTransaction(InventoryTransactionTypes.Opening, _master.Item.Id, first.Id, null, 3m,
                _master.Unit.Id, new DateOnly(2026, 8, 1), "opening", null, null, null, "MRP14"),
            new InventoryTransaction(InventoryTransactionTypes.Opening, _master.Item.Id, second.Id, null, 7m,
                _master.Unit.Id, new DateOnly(2026, 8, 1), "opening", null, null, null, "MRP14"));
        await db.SaveChangesAsync();
        await Task.Delay(5);
        var cutoff = await DatabaseUtcNow();
        db.InventoryTransactions.Add(new InventoryTransaction(InventoryTransactionTypes.AdjustmentIn,
            _master.Item.Id, first.Id, null, 5m, _master.Unit.Id, new DateOnly(2026, 8, 2),
            "later correction", null, null, null, "MRP14"));
        await db.SaveChangesAsync();

        var url = $"/api/v1/analytics/inventory/positions?asOfDate=2026-08-25&search={_master.Item.BusinessNumber}" +
            $"&dataCutoffUtc={Uri.EscapeDataString(cutoff.ToString("O"))}&page=1&pageSize=25";
        var response = await _client.GetAsync(url);
        response.EnsureSuccessStatusCode();
        var page = await Read(response);
        var rows = page.GetProperty("items").EnumerateArray().ToArray();

        Assert.Equal(2, rows.Length);
        Assert.Equal([3m, 7m], rows.Select(x => x.GetProperty("onHand").GetDecimal()).Order().ToArray());
        Assert.All(rows, x => Assert.Equal(10m, x.GetProperty("companyOnHand").GetDecimal()));
        Assert.DoesNotContain(rows, x => x.GetProperty("companyOnHand").GetDecimal() == 15m);
    }

    [Fact]
    public async Task Excel_uses_identical_search_cutoff_anchor_and_bounded_dataset()
    {
        var order = await AddOrder(new DateOnly(2026, 9, 10), new DateOnly(2026, 9, 12));
        await AddOrder(new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 2));
        await Task.Delay(5);
        var cutoff = await DatabaseUtcNow();
        await AddRevisedPlans(order, new DateOnly(2026, 9, 20), new DateOnly(2026, 9, 22));
        var row = await Commercial(order.Number, cutoff);
        var query = $"evaluationDate=2026-12-01&language=en&maximumRows=100&search={order.Number}" +
            $"&dataCutoffUtc={Uri.EscapeDataString(cutoff.ToString("O"))}";
        var response = await _client.GetAsync($"/api/v1/analytics/exports/commercial-commitments/xlsx?{query}");
        var xml = await Sheet(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(order.Number, xml, StringComparison.Ordinal);
        Assert.Contains(row.GetProperty("creditAnchorDate").GetString()!, xml, StringComparison.Ordinal);
        Assert.Contains($"search={order.Number}", xml, StringComparison.Ordinal);
        Assert.Contains($"dataCutoffUtc={cutoff:O}", xml, StringComparison.Ordinal);

        var limited = await _client.GetAsync(
            "/api/v1/analytics/exports/commercial-commitments/xlsx?evaluationDate=2026-12-01&language=en&maximumRows=1");
        Assert.Equal(HttpStatusCode.Conflict, limited.StatusCode);
        Assert.Equal("EXPORT_LIMIT_EXCEEDED", (await Read(limited)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Forecast_accuracy_server_paging_reaches_rows_beyond_100()
    {
        await using var db = await Db();
        var product = await AddProduct(db, "PAGE");
        for (var index = 0; index < 101; index++)
        {
            var start = new DateOnly(2010, 1, 1).AddMonths(index);
            var period = new BusinessPeriod(1300 + index / 12, index % 12 + 1, start, start.AddDays(27));
            period.Close();
            db.BusinessPeriods.Add(period);
            db.PeriodLifecycleEvents.Add(new(period.Id, BusinessPeriodStatuses.Open,
                BusinessPeriodStatuses.Closed, "paging fixture", "MRP14"));
            db.ForecastSeries.Add(new ForecastSeries(product.ItemId, period.Id, _master.Unit.Id));
        }
        await db.SaveChangesAsync();
        var response = await _client.GetAsync($"/api/v1/analytics/forecast-accuracy?search={product.Item.BusinessNumber}" +
            "&page=6&pageSize=20");
        response.EnsureSuccessStatusCode();
        var payload = await Read(response);

        Assert.Equal(101, payload.GetProperty("page").GetProperty("totalCount").GetInt32());
        Assert.Single(payload.GetProperty("page").GetProperty("items").EnumerateArray());
        Assert.Equal(product.Item.BusinessNumber,
            payload.GetProperty("page").GetProperty("items")[0].GetProperty("productCode").GetString());
    }

    private async Task<Product> AddProduct(HadronDbContext db, string suffix)
    {
        var category = await AddReference(db, "PRODUCT_CATEGORY", $"{suffix}-PRODUCT");
        var item = new Item(ItemTypes.Product, $"PRD-{Random.Shared.Next(100000, 999999)}",
            $"محصول {suffix}", $"Product {suffix}", _master.Unit.Id, null);
        var product = new Product(item.Id, category.Id, null, 0, 3, 7, 14);
        db.Items.Add(item);
        db.Products.Add(product);
        await db.SaveChangesAsync();
        return product;
    }

    private async Task AddForecastAccuracyProduct(HadronDbContext db, BusinessPeriod period, string suffix,
        decimal? baselineQty, decimal? finalQty, decimal actualQty, DateTime baselineAt, DateTime finalAt,
        DateTime actualAt)
    {
        var product = await AddProduct(db, suffix);
        ForecastSeries? series = null;
        if (baselineQty.HasValue)
        {
            series = new ForecastSeries(product.ItemId, period.Id, _master.Unit.Id);
            db.ForecastSeries.Add(series);
            db.Entry(series).Property(x => x.CreatedAtUtc).CurrentValue = baselineAt.AddSeconds(-1);
            await db.SaveChangesAsync();
            var baseline = new ForecastRevision(series.Id, 1, baselineQty.Value, null, "GD07");
            db.ForecastRevisions.Add(baseline);
            db.Entry(baseline).Property(x => x.CreatedAtUtc).CurrentValue = baselineAt;
            await db.SaveChangesAsync();
            series.SetCurrent(baseline);
            if (finalQty.HasValue)
            {
                var final = new ForecastRevision(series.Id, 2, finalQty.Value, "GD07 final", "GD07");
                db.ForecastRevisions.Add(final);
                db.Entry(final).Property(x => x.CreatedAtUtc).CurrentValue = finalAt;
                series.Advance(final);
            }
            await db.SaveChangesAsync();
        }

        var actual = new DailyActual(product.ItemId, period.Id, period.StartDate.AddDays(5), _master.Unit.Id);
        db.DailyActuals.Add(actual);
        db.Entry(actual).Property(x => x.CreatedAtUtc).CurrentValue = actualAt.AddSeconds(-1);
        await db.SaveChangesAsync();
        var revision = new DailyActualRevision(actual.Id, 1, actualQty, null, "GD07");
        db.DailyActualRevisions.Add(revision);
        db.Entry(revision).Property(x => x.CreatedAtUtc).CurrentValue = actualAt;
        actual.SetCurrent(revision);
        await db.SaveChangesAsync();
    }

    private async Task<OrderFixture> AddOrder(DateOnly firstWarehouseDate, DateOnly? secondWarehouseDate)
    {
        await using var db = await Db();
        var po = new PurchaseOrder($"PO-{Random.Shared.Next(100000, 999999)}", _master.Supplier.Id,
            _master.Currency.Id, _master.Payment.Id, 20m, 50m, 30m, 60,
            PurchaseMilestoneCodes.WarehouseReceipt, _master.Shipping.Id, _master.Incoterm.Id, null);
        db.PurchaseOrders.Add(po);
        await db.SaveChangesAsync();
        var line = new PurchaseOrderLine(po.Id, 1, _master.Item.Id, _master.Option.Id, _master.Price.Id,
            _master.Unit.Id, 80m, 7m, 0m, 2, 1, 3, 1, null);
        var first = new DeliverySchedule(line.Id, 1, 30m, firstWarehouseDate);
        var second = new DeliverySchedule(line.Id, 2, 50m, secondWarehouseDate);
        db.PurchaseOrderLines.Add(line);
        db.DeliverySchedules.AddRange(first, second);
        await db.SaveChangesAsync();
        var beforeApprove = po.Status;
        po.Approve(DateTime.UtcNow);
        db.PurchaseOrderLifecycleEvents.Add(new(po.Id, beforeApprove, po.Status, null, "MRP14", DateTime.UtcNow));
        await db.SaveChangesAsync();
        var beforeOrder = po.Status;
        po.MarkOrdered(new DateOnly(2026, 8, 25), DateTime.UtcNow);
        db.PurchaseOrderLifecycleEvents.Add(new(po.Id, beforeOrder, po.Status, null, "MRP14", DateTime.UtcNow));
        await db.SaveChangesAsync();
        var created = DateTime.UtcNow;
        db.DeliverySchedulePlanRevisions.AddRange(
            Plan(first.Id, 1, firstWarehouseDate, created),
            Plan(second.Id, 1, secondWarehouseDate, created));
        await db.SaveChangesAsync();
        return new(po.Id, po.PurchaseOrderNumber, first.Id, second.Id);
    }

    private async Task AddRevisedPlans(OrderFixture order, DateOnly first, DateOnly second,
        DateTime? createdAtUtc = null)
    {
        await using var db = await Db();
        var created = createdAtUtc ?? DateTime.UtcNow;
        db.DeliverySchedulePlanRevisions.AddRange(Plan(order.FirstScheduleId, 2, first, created, true),
            Plan(order.SecondScheduleId, 2, second, created, true));
        await db.SaveChangesAsync();
    }

    private static DeliverySchedulePlanRevision Plan(Guid scheduleId, int revision, DateOnly? warehouseDate,
        DateTime created, bool revised = false) => new(scheduleId,
        revised ? PurchasePlanTypes.Revised : PurchasePlanTypes.Original, revision,
        warehouseDate?.AddDays(-7), null, warehouseDate?.AddDays(-5), warehouseDate?.AddDays(-2), null,
        warehouseDate, revised ? "revised D2" : null, "MRP14", created);

    private async Task<JsonElement> Forecast(string code, DateTime? cutoff)
    {
        var url = $"/api/v1/analytics/forecast-accuracy?search={code}&page=1&pageSize=25" + Cutoff(cutoff);
        var response = await _client.GetAsync(url);
        response.EnsureSuccessStatusCode();
        return Assert.Single((await Read(response)).GetProperty("page").GetProperty("items").EnumerateArray());
    }

    private Task<JsonElement> LeadTime(string search, DateTime? cutoff) =>
        LeadTime(search, cutoff, new DateOnly(2026, 12, 1));

    private async Task<JsonElement> LeadTime(string search, DateTime? cutoff, DateOnly evaluationDate)
    {
        var url = $"/api/v1/analytics/lead-time?evaluationDate={evaluationDate:yyyy-MM-dd}&search={search}&page=1&pageSize=25" +
            Cutoff(cutoff);
        var response = await _client.GetAsync(url);
        response.EnsureSuccessStatusCode();
        return Assert.Single((await Read(response)).GetProperty("items").EnumerateArray());
    }

    private async Task<OrderFixture> AddLeadTimeOrder(DateOnly orderedDate, DateOnly originalReceiptDate)
    {
        await using var db = await Db();
        var po = new PurchaseOrder($"PO-{Random.Shared.Next(100000, 999999)}", _master.Supplier.Id,
            _master.Currency.Id, _master.Payment.Id, 20m, 50m, 30m, 60,
            PurchaseMilestoneCodes.WarehouseReceipt, _master.Shipping.Id, _master.Incoterm.Id, null);
        db.PurchaseOrders.Add(po);
        await db.SaveChangesAsync();
        var line = new PurchaseOrderLine(po.Id, 1, _master.Item.Id, _master.Option.Id, _master.Price.Id,
            _master.Unit.Id, 1m, 7m, 0m, 2, 1, 3, 1, null);
        var schedule = new DeliverySchedule(line.Id, 1, 1m, originalReceiptDate);
        db.AddRange(line, schedule);
        await db.SaveChangesAsync();
        var from = po.Status;
        po.Approve(DateTime.UtcNow);
        db.PurchaseOrderLifecycleEvents.Add(new(po.Id, from, po.Status, null, "GD06", DateTime.UtcNow));
        await db.SaveChangesAsync();
        from = po.Status;
        po.MarkOrdered(orderedDate, DateTime.UtcNow);
        db.PurchaseOrderLifecycleEvents.Add(new(po.Id, from, po.Status, null, "GD06", DateTime.UtcNow));
        await db.SaveChangesAsync();
        db.DeliverySchedulePlanRevisions.Add(Plan(schedule.Id, 1, originalReceiptDate, DateTime.UtcNow));
        await db.SaveChangesAsync();
        return new(po.Id, po.PurchaseOrderNumber, schedule.Id, schedule.Id);
    }

    private async Task PostReceipt(OrderFixture order, Guid warehouseId, decimal quantity, DateOnly effectiveDate)
    {
        var po = await PurchaseOrder(order.Id);
        var line = po.GetProperty("lines")[0];
        var create = await _client.PostAsJsonAsync("/api/purchase-receipts", new
        {
            purchaseOrderId = order.Id,
            warehouseId,
            effectiveDate = effectiveDate.ToString("yyyy-MM-dd"),
            notes = "GD06 persisted completion",
            lines = new[] { new
            {
                purchaseOrderLineId = line.GetProperty("id").GetGuid(),
                deliveryScheduleId = order.FirstScheduleId,
                quantity
            } }
        });
        create.EnsureSuccessStatusCode();
        var receipt = await Read(create);
        var post = await _client.PostAsJsonAsync($"/api/purchase-receipts/{receipt.GetProperty("id").GetGuid()}/post", new
        {
            expectedReceiptVersion = receipt.GetProperty("version").GetInt64(),
            expectedPurchaseOrderVersion = po.GetProperty("version").GetInt64()
        });
        post.EnsureSuccessStatusCode();
    }

    private async Task<JsonElement> PurchaseOrder(Guid id)
    {
        var response = await _client.GetAsync($"/api/purchase-orders/{id}");
        response.EnsureSuccessStatusCode();
        return await Read(response);
    }

    private async Task<JsonElement> InventoryPosition(string search, DateOnly asOf, DateTime? cutoff)
    {
        var url = $"/api/v1/analytics/inventory/positions?asOfDate={asOf:yyyy-MM-dd}&search={search}" +
            $"&page=1&pageSize=25{Cutoff(cutoff)}";
        var response = await _client.GetAsync(url);
        response.EnsureSuccessStatusCode();
        return Assert.Single((await Read(response)).GetProperty("items").EnumerateArray());
    }

    private async Task<JsonElement> Commercial(string number, DateTime? cutoff)
    {
        var page = await CommercialPage(number, cutoff);
        return Assert.Single(page.GetProperty("items").EnumerateArray());
    }

    private async Task<JsonElement> CommercialPage(string? search, DateTime? cutoff)
    {
        var url = "/api/v1/analytics/commercial-commitments?evaluationDate=2026-12-01&page=1&pageSize=200" +
            (search is null ? string.Empty : $"&search={search}") + Cutoff(cutoff);
        var response = await _client.GetAsync(url);
        response.EnsureSuccessStatusCode();
        return (await Read(response)).GetProperty("page");
    }

    private static string Cutoff(DateTime? cutoff) => cutoff.HasValue
        ? $"&dataCutoffUtc={Uri.EscapeDataString(cutoff.Value.ToString("O"))}" : string.Empty;

    private static async Task<string> Sheet(HttpResponseMessage response)
    {
        var bytes = await response.Content.ReadAsByteArrayAsync();
        using var archive = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
        await using var stream = archive.GetEntry("xl/worksheets/sheet1.xml")!.Open();
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return await reader.ReadToEndAsync();
    }

    private static decimal[] SheetDecimals(string xml) => XDocument.Parse(xml).Descendants()
        .Where(x => x.Name.LocalName == "t")
        .Select(x => x.Value)
        .Select(x => decimal.TryParse(x, NumberStyles.Number, CultureInfo.InvariantCulture, out var value)
            ? (decimal?)value : null)
        .Where(x => x.HasValue)
        .Select(x => x!.Value)
        .ToArray();

    private async Task<string> ExportData(string url)
    {
        var response = await _client.GetAsync(url);
        response.EnsureSuccessStatusCode();
        return string.Join('\n', XDocument.Parse(await Sheet(response)).Descendants()
            .Where(x => x.Name.LocalName == "row")
            .Skip(1)
            .Select(row => string.Join('|', row.Descendants()
                .Where(x => x.Name.LocalName == "t")
                .Select(x => x.Value))));
    }

    private static async Task<JsonElement> Read(HttpResponseMessage response) =>
        JsonSerializer.Deserialize<JsonElement>(await response.Content.ReadAsStringAsync(),
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

    private async Task<HadronDbContext> Db() =>
        await _factory.Services.GetRequiredService<IDbContextFactory<HadronDbContext>>().CreateDbContextAsync();

    private async Task<DateTime> DatabaseUtcNow()
    {
        await using var db = await Db();
        return await db.Database.SqlQueryRaw<DateTime>("SELECT clock_timestamp() AS \"Value\"").SingleAsync();
    }

    private static async Task<MasterData> AddMasterData(HadronDbContext db)
    {
        var unitCode = $"A{Random.Shared.Next(100000, 999999)}";
        var unit = new Unit(unitCode, unitCode, "واحد تحلیل", "Analytics unit", null, 1);
        var existingCurrencyCodes = (await db.Currencies.AsNoTracking().Select(x => x.Code).ToListAsync())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var currencyCode = (from first in Enumerable.Range('A', 26)
                            from second in Enumerable.Range('A', 26)
                            from third in Enumerable.Range('A', 26)
                            select $"{(char)first}{(char)second}{(char)third}")
            .First(code => !existingCurrencyCodes.Contains(code));
        var currency = new Currency(currencyCode, currencyCode, "ارز تحلیل", "Analytics currency", null, 2);
        db.Units.Add(unit);
        db.Currencies.Add(currency);
        await db.SaveChangesAsync();
        var country = await AddReference(db, "COUNTRY", "AN-COUNTRY");
        var payment = await AddReference(db, "PAYMENT_TERM", "AN-PAYMENT");
        var shipping = await AddReference(db, "SHIPPING_MODE", "AN-SHIPPING");
        var incoterm = await AddReference(db, "INCOTERM", "AN-INCOTERM");
        var category = await AddReference(db, "PART_CATEGORY", "AN-PART");
        var supplier = new Supplier($"SUP-{Random.Shared.Next(100000, 999999)}", "تأمین‌کننده تحلیل",
            "تأمین‌کننده تحلیل", country.Id, null, null, null, null);
        supplier.PaymentProfile.Set(payment.Id, 20m, 50m, 30m, 60, PurchaseMilestoneCodes.WarehouseReceipt);
        supplier.ChangeState(true);
        var item = new Item(ItemTypes.Part, $"PRT-{Random.Shared.Next(100000, 999999)}", "قطعه تحلیل",
            "Analytics part", unit.Id, null);
        var profile = new MaterialItemProfile(item.Id, category.Id, SupplyMethods.Buy, null, 0, 0, 0, 0, 0);
        var option = new SourcingOption($"SRC-{Random.Shared.Next(100000, 999999)}", item.Id, supplier.Id,
            unit.Id, 0m, shipping.Id, incoterm.Id, 2, 1, 3, 1);
        var price = new SourcingPrice(option.Id, 7m, currency.Id, new DateOnly(2026, 1, 1), null);
        db.Suppliers.Add(supplier);
        db.Items.Add(item);
        db.MaterialItemProfiles.Add(profile);
        db.SourcingOptions.Add(option);
        db.SourcingPrices.Add(price);
        await db.SaveChangesAsync();
        return new(unit, currency, country, payment, shipping, incoterm, supplier, item, option, price);
    }

    private static async Task<ReferenceValue> AddReference(HadronDbContext db, string typeCode, string prefix)
    {
        var typeId = await db.ReferenceTypes.Where(x => x.Code == typeCode).Select(x => x.Id).SingleAsync();
        var code = $"{prefix}-{Guid.NewGuid():N}"[..Math.Min(30, prefix.Length + 9)].ToUpperInvariant();
        var value = new ReferenceValue(typeId, code, code, $"فارسی {prefix}", $"English {prefix}", 1);
        db.ReferenceValues.Add(value);
        await db.SaveChangesAsync();
        return value;
    }

    private sealed record MasterData(Unit Unit, Currency Currency, ReferenceValue Country,
        ReferenceValue Payment, ReferenceValue Shipping, ReferenceValue Incoterm, Supplier Supplier,
        Item Item, SourcingOption Option, SourcingPrice Price);
    private sealed record OrderFixture(Guid Id, string Number, Guid FirstScheduleId, Guid SecondScheduleId);
}
