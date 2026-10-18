using System.IO.Compression;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using Hadron.Erp.Api.Infrastructure;
using Hadron.Erp.Api.Modules.Analytics.Application;
using Hadron.Erp.Api.Modules.Bom.Domain;
using Hadron.Erp.Api.Modules.Catalog.Domain;
using Hadron.Erp.Api.Modules.Inventory.Application;
using Hadron.Erp.Api.Modules.Inventory.Domain;
using Hadron.Erp.Api.Modules.Mrp.Application;
using Hadron.Erp.Api.Modules.OrderPlanning.Application;
using Hadron.Erp.Api.Modules.OrderPlanning.Domain;
using Hadron.Erp.Api.Modules.Planning.Application;
using Hadron.Erp.Api.Modules.Planning.Domain;
using Hadron.Erp.Api.Modules.Products.Domain;
using Hadron.Erp.Api.Modules.PurchaseSupply.Domain;
using Hadron.Erp.Api.Modules.ReferenceData.Domain;
using Hadron.Erp.Api.Modules.Sourcing.Domain;
using Hadron.Erp.Api.Modules.Suppliers.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Hadron.Erp.IntegrationTests;

[Collection("ReferenceData")]
public sealed class FullGoldenE2ETests : IClassFixture<FoundationApiFactory>
{
    private readonly FoundationApiFactory _factory;
    private readonly HttpClient _client;

    public FullGoldenE2ETests(FoundationApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task GD08_one_fresh_database_runs_the_complete_public_operational_chain()
    {
        await using var db = await Db();
        await db.Database.MigrateAsync();
        var warehouseResponsibleId = await IntegrationTestIdentity
            .EnsureActiveWarehouseResponsible(db, "gd08");
        var calendar = _factory.Services.GetRequiredService<IPlanningCalendar>();
        var bounds = calendar.GetPeriodBounds(1405, 5);
        var asOfR1 = bounds.StartDate.AddDays(26);
        var day28 = bounds.StartDate.AddDays(27);
        var day29 = bounds.StartDate.AddDays(28);
        var day30 = bounds.StartDate.AddDays(29);
        var day31 = bounds.StartDate.AddDays(30);
        _factory.PlanningCalendar.TodayOverride = asOfR1;

        var unit = new Unit($"G8U-{Guid.NewGuid():N}"[..20], $"G8U-{Guid.NewGuid():N}"[..20].ToUpperInvariant(),
            "واحد GD08", "GD08 unit", null, 1);
        var currency = new Currency("CNY", "CNY", "یوان چین", "Chinese Yuan", "¥", 2);
        db.AddRange(unit, currency);
        await db.SaveChangesAsync();
        var country = await Reference(db, "COUNTRY", "GD08-COUNTRY");
        var payment = await Reference(db, "PAYMENT_TERM", "GD08-PAYMENT");
        var shipping = await Reference(db, "SHIPPING_MODE", "GD08-SHIPPING");
        var incoterm = await Reference(db, "INCOTERM", "GD08-INCOTERM");
        var productCategory = await Reference(db, "PRODUCT_CATEGORY", "GD08-PRODUCT");
        var partCategory = await Reference(db, "PART_CATEGORY", "GD08-PART");
        var componentCategory = await Reference(db, "COMPONENT_CATEGORY", "GD08-COMPONENT");

        var supplier = new Supplier($"SUP-{Random.Shared.Next(100000, 999999)}", "تأمین‌کننده GD08",
            "تأمین‌کننده GD08", country.Id, null, null, null, null);
        supplier.PaymentProfile.Set(payment.Id, 20m, 50m, 30m, 60, PurchaseMilestoneCodes.WarehouseReceipt);
        supplier.ChangeState(true);
        var productItem = new Item(ItemTypes.Product, $"PRD-{Random.Shared.NextInt64(100000, 999999)}",
            "محصول GD08", "GD08 product", unit.Id, null);
        var product = new Product(productItem.Id, productCategory.Id, null, 0, 1, 2, 3);
        var part = new Item(ItemTypes.Part, $"PRT-{Random.Shared.NextInt64(100000, 999999)}",
            "قطعه ساخت GD08", "GD08 make part", unit.Id, null);
        var partProfile = new MaterialItemProfile(part.Id, partCategory.Id, SupplyMethods.Make, null, 0, 0, 1, 1, 3);
        var component = new Item(ItemTypes.Component, $"CMP-{Random.Shared.NextInt64(100000, 999999)}",
            "ماده خرید GD08", "GD08 purchased component", unit.Id, null);
        var componentProfile = new MaterialItemProfile(component.Id, componentCategory.Id, SupplyMethods.Buy,
            null, 0, 0, 1, 1, 3);
        var source = new SourcingOption($"SRC-{Random.Shared.NextInt64(100000, 999999)}", component.Id,
            supplier.Id, unit.Id, 50m, shipping.Id, incoterm.Id, 1, 0, 0, 0);
        var period = new BusinessPeriod(1405, 5, bounds.StartDate, bounds.EndDate);
        db.AddRange(supplier, productItem, product, part, partProfile, component, componentProfile, source, period);
        await db.SaveChangesAsync();

        var productBom = new BomRevision($"BOM-GD08-P-{Guid.NewGuid():N}"[..28], productItem.Id, unit.Id, 1,
            "GD08 product BOM");
        var productEdge = new BomLine(productBom.Id, part.Id, unit.Id, 1m, 0m, 1);
        var partBom = new BomRevision($"BOM-GD08-A-{Guid.NewGuid():N}"[..28], part.Id, unit.Id, 1,
            "GD08 part BOM");
        var componentEdge = new BomLine(partBom.Id, component.Id, unit.Id, 2m, 0m, 1);
        db.AddRange(productBom, productEdge, partBom, componentEdge);
        await db.SaveChangesAsync();
        productBom.Approve(null);
        partBom.Approve(null);
        await db.SaveChangesAsync();

        var warehouseCreate = await _client.PostAsJsonAsync("/api/inventory/warehouses",
            new { nameFa = "انبار GD08", nameEn = "GD08 warehouse", addressFa = "موقعیت GD08", addressEn = "GD08 location", primaryResponsibleUserId = warehouseResponsibleId, notes = "Golden" });
        warehouseCreate.EnsureSuccessStatusCode();
        var warehouse = await Read(warehouseCreate);
        var warehouseId = warehouse.GetProperty("id").GetGuid();

        var forecastResponse = await _client.PostAsJsonAsync("/api/planning/forecast",
            new CreateForecastRequest(productItem.Id, period.Id, 40m, null));
        Assert.Equal(HttpStatusCode.Created, forecastResponse.StatusCode);
        var opening = await _client.PostAsJsonAsync("/api/inventory/openings",
            new PostInventoryRequest(component.Id, warehouseId, 30m, asOfR1, "GD08 opening"));
        Assert.Equal(HttpStatusCode.Created, opening.StatusCode);

        async Task<MrpRunResponse> Run(DateOnly asOf)
        {
            _factory.PlanningCalendar.TodayOverride = asOf;
            var response = await _client.PostAsJsonAsync("/api/mrp-runs",
                new CreateMrpRunRequest(asOf, day31, []));
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            return (await response.Content.ReadFromJsonAsync<MrpRunResponse>())!;
        }

        async Task<MrpItemSummaryResponse> Summary(Guid runId)
        {
            var page = await _client.GetFromJsonAsync<MrpItemSummaryListResponse>(
                $"/api/mrp-runs/{runId}/items?search={component.BusinessNumber}&page=1&pageSize=25");
            return Assert.Single(page!.Items);
        }

        async Task ActualAndIssue(DateOnly date)
        {
            _factory.PlanningCalendar.TodayOverride = date;
            var actual = await _client.PostAsJsonAsync("/api/planning/actuals",
                new CreateDailyActualRequest(productItem.Id, date, 10m));
            Assert.Equal(HttpStatusCode.Created, actual.StatusCode);
            var issue = await _client.PostAsJsonAsync("/api/inventory/issues",
                new PostInventoryRequest(component.Id, warehouseId, 20m, date, "GD08 production issue"));
            Assert.Equal(HttpStatusCode.Created, issue.StatusCode);
        }

        var r1 = await Run(asOfR1);
        var r1Summary = await Summary(r1.Id);
        Assert.Equal(day29, r1Summary.StockoutDate);
        Assert.Equal(80m, r1Summary.TotalGrossDemand);
        Assert.Equal(50m, r1Summary.TotalNetRequirement);
        var material = await _client.GetFromJsonAsync<MaterialPlanningSummaryListResponse>(
            $"/api/material-planning/runs/{r1.Id}/summary?search={component.BusinessNumber}&page=1&pageSize=25");
        Assert.Equal(50m, Assert.Single(material!.Items).TotalNetRequirement);

        var generationResponse = await _client.PostAsJsonAsync("/api/order-planning/generations",
            new GenerateOrderPlanningRequest(r1.Id));
        Assert.Equal(HttpStatusCode.Created, generationResponse.StatusCode);
        var generation = (await generationResponse.Content.ReadFromJsonAsync<OrderPlanningGenerationResponse>())!;
        var recommendationPage = await _client.GetFromJsonAsync<OrderRecommendationListResponse>(
            $"/api/order-planning/recommendations?runId={r1.Id}&generationId={generation.Id}&page=1&pageSize=25");
        var recommendationItem = Assert.Single(recommendationPage!.Items);
        var recommendation = await _client.GetFromJsonAsync<OrderRecommendationResponse>(
            $"/api/order-planning/recommendations/{recommendationItem.Id}");
        Assert.Equal(day29, recommendation!.NeedByDate);
        Assert.Equal(day28, recommendation.OrderByDate);
        Assert.Equal(day30, recommendation.DecisionHorizonDate);
        Assert.Equal(30m, recommendation.BaseRecommendedQty);
        Assert.Equal(50m, recommendation.SystemRecommendedQty);
        Assert.Equal("P2_UPCOMING", recommendation.Priority);

        var decisionResponse = await _client.PostAsJsonAsync(
            $"/api/order-planning/recommendations/{recommendation.Id}/decisions",
            new SaveExpertDecisionRequest(ExpertDecisionTypes.Accept, recommendation.DecisionVersion,
                null, null, null, null, null, 7m, currency.Id));
        decisionResponse.EnsureSuccessStatusCode();
        var decided = (await decisionResponse.Content.ReadFromJsonAsync<OrderRecommendationResponse>())!;
        var decision = Assert.Single(decided.Decisions);
        Assert.Equal(50m, decision.FinalQty);

        var conversionResponse = await _client.PostAsJsonAsync("/api/purchase-orders/convert-decisions",
            new { decisionIds = new[] { decision.Id } });
        conversionResponse.EnsureSuccessStatusCode();
        var conversion = await Read(conversionResponse);
        var poId = conversion.GetProperty("groups")[0].GetProperty("purchaseOrderId").GetGuid();
        var draft = await PurchaseOrder(poId);
        var line = draft.GetProperty("lines")[0];
        Assert.Equal(r1.Id, line.GetProperty("mrpRunId").GetGuid());
        Assert.Equal(generation.Id, line.GetProperty("generationId").GetGuid());
        Assert.Equal(recommendation.Id, line.GetProperty("recommendationId").GetGuid());
        Assert.Equal(decision.Id, line.GetProperty("expertDecisionId").GetGuid());
        var approved = await Post($"/api/purchase-orders/{poId}/approve",
            new { expectedVersion = draft.GetProperty("version").GetInt64() });
        var ordered = await Post($"/api/purchase-orders/{poId}/mark-ordered", new
        {
            expectedVersion = approved.GetProperty("version").GetInt64(),
            orderedDate = day28.ToString("yyyy-MM-dd")
        });
        var orderedLine = ordered.GetProperty("lines")[0];
        var schedule = orderedLine.GetProperty("schedules")[0];
        var lineId = orderedLine.GetProperty("id").GetGuid();
        var scheduleId = schedule.GetProperty("id").GetGuid();
        Assert.Equal(50m, (await Firm(day28)).Sum(x => x.GetProperty("openQuantity").GetDecimal()));

        await ActualAndIssue(day28);
        var r2 = await Run(day28);
        var r2Summary = await Summary(r2.Id);
        Assert.Equal(50m, r2Summary.TotalFirmIncoming);
        Assert.Null(r2Summary.StockoutDate);

        var partial = await Receipt(poId, warehouseId, lineId, scheduleId, 20m, day29);
        var postedPartial = await Post($"/api/purchase-receipts/{partial.GetProperty("id").GetGuid()}/post", new
        {
            expectedReceiptVersion = partial.GetProperty("version").GetInt64(),
            expectedPurchaseOrderVersion = ordered.GetProperty("version").GetInt64()
        });
        Assert.Equal(30m, await Balance(component.Id, warehouseId, day29));
        Assert.Equal(30m, (await Firm(day29)).Sum(x => x.GetProperty("openQuantity").GetDecimal()));
        var afterPartial = await PurchaseOrder(poId);
        await Post($"/api/purchase-orders/{poId}/schedules/{scheduleId}/revise-plan", new
        {
            expectedVersion = afterPartial.GetProperty("version").GetInt64(),
            reason = "GD08 remaining schedule ETA",
            supplierReadyDate = (string?)null,
            handoverToForwarderDate = (string?)null,
            shippedDate = (string?)null,
            arrivedDestinationDate = (string?)null,
            customsClearedDate = (string?)null,
            warehouseReceiptDate = day30.ToString("yyyy-MM-dd")
        });
        await ActualAndIssue(day29);
        var r3 = await Run(day29);
        Assert.Equal(30m, (await Summary(r3.Id)).TotalFirmIncoming);

        var beforeFinal = await PurchaseOrder(poId);
        var final = await Receipt(poId, warehouseId, lineId, scheduleId, 30m, day30);
        var postedFinal = await Post($"/api/purchase-receipts/{final.GetProperty("id").GetGuid()}/post", new
        {
            expectedReceiptVersion = final.GetProperty("version").GetInt64(),
            expectedPurchaseOrderVersion = beforeFinal.GetProperty("version").GetInt64()
        });
        Assert.Empty(await Firm(day30));
        Assert.Equal(40m, await Balance(component.Id, warehouseId, day30));
        var coveredRun = await Run(day30);
        Assert.Equal(0m, (await Summary(coveredRun.Id)).TotalNetRequirement);
        var coveredGenerationResponse = await _client.PostAsJsonAsync("/api/order-planning/generations",
            new GenerateOrderPlanningRequest(coveredRun.Id));
        coveredGenerationResponse.EnsureSuccessStatusCode();
        Assert.Equal(0, (await coveredGenerationResponse.Content
            .ReadFromJsonAsync<OrderPlanningGenerationResponse>())!.RecommendationCount);

        var beforeCorrection = await PurchaseOrder(poId);
        var reversed = await Post($"/api/purchase-receipts/{final.GetProperty("id").GetGuid()}/reverse", new
        {
            expectedReceiptVersion = postedFinal.GetProperty("version").GetInt64(),
            expectedPurchaseOrderVersion = beforeCorrection.GetProperty("version").GetInt64(),
            reason = "GD08 controlled correction"
        });
        Assert.Equal("REVERSED", reversed.GetProperty("status").GetString());
        Assert.Equal(30m, (await Firm(day30)).Sum(x => x.GetProperty("openQuantity").GetDecimal()));
        Assert.Equal(10m, await Balance(component.Id, warehouseId, day30));
        var afterReversal = await PurchaseOrder(poId);
        var replacement = await Receipt(poId, warehouseId, lineId, scheduleId, 30m, day30);
        await Post($"/api/purchase-receipts/{replacement.GetProperty("id").GetGuid()}/post", new
        {
            expectedReceiptVersion = replacement.GetProperty("version").GetInt64(),
            expectedPurchaseOrderVersion = afterReversal.GetProperty("version").GetInt64()
        });
        Assert.Empty(await Firm(day30));
        Assert.Equal(40m, await Balance(component.Id, warehouseId, day30));
        await ActualAndIssue(day30);
        var r4 = await Run(day30);
        var r4Summary = await Summary(r4.Id);
        Assert.Equal(0m, r4Summary.TotalFirmIncoming);
        Assert.Equal(0m, r4Summary.TotalNetRequirement);
        var r4GenerationResponse = await _client.PostAsJsonAsync("/api/order-planning/generations",
            new GenerateOrderPlanningRequest(r4.Id));
        r4GenerationResponse.EnsureSuccessStatusCode();
        Assert.Equal(0, (await r4GenerationResponse.Content
            .ReadFromJsonAsync<OrderPlanningGenerationResponse>())!.RecommendationCount);
        await ActualAndIssue(day31);
        Assert.Equal(0m, await Balance(component.Id, warehouseId, day31));
        var r5 = await Run(day31);
        var r5GenerationResponse = await _client.PostAsJsonAsync("/api/order-planning/generations",
            new GenerateOrderPlanningRequest(r5.Id));
        r5GenerationResponse.EnsureSuccessStatusCode();
        Assert.Equal(0, (await r5GenerationResponse.Content.ReadFromJsonAsync<OrderPlanningGenerationResponse>())!.RecommendationCount);

        await using (var immutable = await Db())
        {
            Assert.Equal(40m, await immutable.MrpRunInventoryInputs.Where(x => x.RunId == coveredRun.Id &&
                x.ItemId == component.Id).SumAsync(x => x.OnHandQty));
            Assert.False(await immutable.MrpRunFirmSupplyInputs.AnyAsync(x => x.RunId == coveredRun.Id &&
                x.DeliveryScheduleId == scheduleId));
            Assert.Equal(20m, await immutable.MrpRunInventoryInputs.Where(x => x.RunId == r4.Id &&
                x.ItemId == component.Id).SumAsync(x => x.OnHandQty));
            Assert.False(await immutable.MrpRunFirmSupplyInputs.AnyAsync(x => x.RunId == r4.Id &&
                x.DeliveryScheduleId == scheduleId));
            Assert.Equal(50m, await immutable.OrderRecommendations.Where(x => x.Id == recommendation.Id)
                .Select(x => x.SystemRecommendedQty!.Value).SingleAsync());
            Assert.Equal(50m, await immutable.PurchaseOrderLines.Where(x => x.PurchaseOrderId == poId)
                .Select(x => x.OrderedQuantity).SingleAsync());
        }

        using var analyticsScope = _factory.Services.CreateScope();
        var analyticsService = analyticsScope.ServiceProvider.GetRequiredService<AnalyticsService>();
        var analytics = await analyticsService.Commercial(day31, null,
            draft.GetProperty("purchaseOrderNumber").GetString(), 1, 25, CancellationToken.None);
        var commercial = Assert.Single(analytics.Page.Items);
        Assert.Equal(50m, commercial.ReceivedQuantity);
        Assert.Equal(0m, commercial.OpenQuantity);
        Assert.Equal("RECEIVED", commercial.CompletionOutcome);
        var poNumber = commercial.PurchaseOrderNumber;
        var export = await _client.GetAsync(
            $"/api/v1/analytics/exports/purchase-order-receipt/xlsx?evaluationDate={day31:yyyy-MM-dd}" +
            $"&language=en&maximumRows=100&search={poNumber}");
        export.EnsureSuccessStatusCode();
        var xml = await Sheet(export);
        Assert.Contains(poNumber, xml, StringComparison.Ordinal);
        Assert.Contains(50m, SheetDecimals(xml));
        Assert.Contains(">RECEIVED<", xml, StringComparison.Ordinal);
    }

    private async Task<ReferenceValue> Reference(HadronDbContext db, string typeCode, string prefix)
    {
        var type = await db.ReferenceTypes.SingleAsync(x => x.Code == typeCode);
        var code = $"{prefix}-{Guid.NewGuid():N}"[..Math.Min(30, prefix.Length + 9)].ToUpperInvariant();
        var value = new ReferenceValue(type.Id, code, code, $"فارسی {prefix}", $"English {prefix}", 1);
        db.ReferenceValues.Add(value);
        await db.SaveChangesAsync();
        return value;
    }

    private async Task<JsonElement> PurchaseOrder(Guid id)
    {
        var response = await _client.GetAsync($"/api/purchase-orders/{id}");
        response.EnsureSuccessStatusCode();
        return await Read(response);
    }

    private async Task<JsonElement> Receipt(Guid poId, Guid warehouseId, Guid lineId, Guid scheduleId,
        decimal quantity, DateOnly effectiveDate)
    {
        _factory.PlanningCalendar.TodayOverride = effectiveDate;
        var response = await _client.PostAsJsonAsync("/api/purchase-receipts", new
        {
            purchaseOrderId = poId,
            warehouseId,
            effectiveDate = effectiveDate.ToString("yyyy-MM-dd"),
            notes = "GD08",
            lines = new[] { new { purchaseOrderLineId = lineId, deliveryScheduleId = scheduleId, quantity } }
        });
        response.EnsureSuccessStatusCode();
        return await Read(response);
    }

    private async Task<JsonElement> Post(string url, object value)
    {
        var response = await _client.PostAsJsonAsync(url, value);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"{response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        return await Read(response);
    }

    private async Task<IReadOnlyList<JsonElement>> Firm(DateOnly evaluationDate)
    {
        var response = await _client.GetAsync(
            $"/api/purchase-orders/firm-incoming?evaluationDate={evaluationDate:yyyy-MM-dd}");
        response.EnsureSuccessStatusCode();
        return JsonSerializer.Deserialize<List<JsonElement>>(await response.Content.ReadAsStringAsync(), Json())!;
    }

    private async Task<decimal> Balance(Guid itemId, Guid warehouseId, DateOnly asOf)
    {
        var response = await _client.GetAsync(
            $"/api/inventory/balances?asOfDate={asOf:yyyy-MM-dd}&itemId={itemId}&warehouseId={warehouseId}&page=1&pageSize=25");
        response.EnsureSuccessStatusCode();
        var payload = await Read(response);
        var rows = payload.GetProperty("items").EnumerateArray().ToArray();
        return rows.Length == 0 ? 0m : Assert.Single(rows).GetProperty("actualOnHand").GetDecimal();
    }

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

    private static async Task<JsonElement> Read(HttpResponseMessage response) =>
        JsonSerializer.Deserialize<JsonElement>(await response.Content.ReadAsStringAsync(), Json());

    private static JsonSerializerOptions Json() => new(JsonSerializerDefaults.Web);

    private async Task<HadronDbContext> Db() =>
        await _factory.Services.GetRequiredService<IDbContextFactory<HadronDbContext>>().CreateDbContextAsync();
}
