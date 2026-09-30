using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Hadron.Erp.Api.Infrastructure;
using Hadron.Erp.Api.Modules.Bom.Domain;
using Hadron.Erp.Api.Modules.Catalog.Domain;
using Hadron.Erp.Api.Modules.Inventory.Application;
using Hadron.Erp.Api.Modules.Planning.Application;
using Hadron.Erp.Api.Modules.Planning.Domain;
using Hadron.Erp.Api.Modules.Products.Domain;
using Hadron.Erp.Api.Modules.ReferenceData.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Hadron.Erp.IntegrationTests;

[Collection("ReferenceData")]
public sealed class ProductionTests : IClassFixture<FoundationApiFactory>, IAsyncLifetime
{
    private readonly FoundationApiFactory _factory;
    private readonly HttpClient _client;

    public ProductionTests(FoundationApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync()
    {
        _factory.PlanningCalendar.TodayOverride = null;
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Twenty_production_reports_stage_material_backflush_inventory_and_actual_atomically()
    {
        await using var db = await Db();
        await db.Database.MigrateAsync();
        var responsible = await IntegrationTestIdentity.EnsureActiveWarehouseResponsible(db, "production");
        var calendar = _factory.Services.GetRequiredService<IPlanningCalendar>();
        var bounds = calendar.GetPeriodBounds(1399, 1);
        _factory.PlanningCalendar.TodayOverride = bounds.EndDate;

        var unit = new Unit($"PROD-{Guid.NewGuid():N}"[..20], $"PROD-{Guid.NewGuid():N}"[..20],
            "عدد تولید", "Production piece", "pcs", 1);
        db.Units.Add(unit);
        await db.SaveChangesAsync();
        var productCategory = await Reference(db, "PRODUCT_CATEGORY", "PROD-PRODUCT");
        var partCategory = await Reference(db, "PART_CATEGORY", "PROD-PART");
        var productItem = new Item(ItemTypes.Product,
            $"PRD-{Random.Shared.NextInt64(1_000_000, 9_999_999)}", "محصول اجرای تولید",
            "Production execution product", unit.Id, null);
        var product = new Product(productItem.Id, productCategory.Id, null, 0, 1, 2, 3);
        var partItem = new Item(ItemTypes.Part,
            $"PRT-{Random.Shared.NextInt64(1_000_000, 9_999_999)}", "قطعه اجرای تولید",
            "Production execution part", unit.Id, null);
        var part = new MaterialItemProfile(partItem.Id, partCategory.Id, SupplyMethods.Make,
            null, 0, 0, 1, 2, 3);
        var period = new BusinessPeriod(1399, 1, bounds.StartDate, bounds.EndDate);
        db.AddRange(productItem, product, partItem, part, period);
        await db.SaveChangesAsync();

        var forecastResponse = await _client.PostAsJsonAsync("/api/planning/forecast",
            new CreateForecastRequest(productItem.Id, period.Id, 20m, null));
        Assert.Equal(HttpStatusCode.Created, forecastResponse.StatusCode);

        var bom = new BomRevision($"BOM-PROD-{Guid.NewGuid():N}"[..28], productItem.Id,
            unit.Id, 1m, "Production integration BOM");
        var line = new BomLine(bom.Id, partItem.Id, unit.Id, 2m, 0m, 1);
        db.AddRange(bom, line);
        await db.SaveChangesAsync();
        bom.Approve("integration-test");
        await db.SaveChangesAsync();

        var source = await Warehouse("انبار مواد تولید", "Production material storage",
            "STORAGE", responsible);
        var staging = await Warehouse("پای خط تولید تست", "Production staging test",
            "PRODUCTION_STAGING", responsible);
        var finished = await Warehouse("انبار محصول تولید", "Production finished goods",
            "FINISHED_GOODS", responsible);
        var opening = await _client.PostAsJsonAsync("/api/inventory/openings",
            new PostInventoryRequest(partItem.Id, Id(source), 40m, bounds.StartDate,
                "Production test opening"));
        opening.EnsureSuccessStatusCode();

        var create = await _client.PostAsJsonAsync("/api/production-orders", new
        {
            productId = productItem.Id,
            plannedQuantity = 20m,
            plannedStartDate = bounds.StartDate,
            plannedEndDate = bounds.StartDate.AddDays(19),
            destinationWarehouseId = Id(finished),
            notes = "20-sample production simulation"
        });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var order = await Json(create);
        Assert.Matches("^MO-[0-9]{6,}$", order.GetProperty("code").GetString());
        Assert.Equal("DRAFT", order.GetProperty("status").GetString());
        Assert.Equal(20m, order.GetProperty("forecast").GetProperty("forecastQuantity").GetDecimal());

        order = await PostOrder(order, "approve", new
        {
            expectedVersion = order.GetProperty("version").GetInt64(),
            reason = "approved for simulation"
        });
        Assert.Equal("APPROVED", order.GetProperty("status").GetString());
        order = await PostOrder(order, "release", new
        {
            expectedVersion = order.GetProperty("version").GetInt64(),
            reason = "released with approved BOM"
        });
        Assert.Equal("RELEASED", order.GetProperty("status").GetString());
        Assert.Equal(bom.Id, order.GetProperty("bomRevisionId").GetGuid());
        var material = Assert.Single(order.GetProperty("materials").EnumerateArray().ToArray());
        Assert.Equal(partItem.Id, material.GetProperty("item").GetProperty("id").GetGuid());
        Assert.Equal(40m, material.GetProperty("requiredQuantity").GetDecimal());
        var materialId = material.GetProperty("id").GetGuid();

        var overStage = await _client.PostAsJsonAsync(
            $"/api/production-orders/{order.GetProperty("id").GetGuid()}/materials/{materialId}/stage", new
            {
                sourceWarehouseId = Id(source),
                stagingWarehouseId = Id(staging),
                quantity = 41m,
                reason = "too much",
                expectedOrderVersion = order.GetProperty("version").GetInt64()
            });
        Assert.Equal(HttpStatusCode.Conflict, overStage.StatusCode);
        Assert.Equal("MATERIAL_STAGE_OVER_REQUIRED", await ProblemCode(overStage));

        var stage = await _client.PostAsJsonAsync(
            $"/api/production-orders/{order.GetProperty("id").GetGuid()}/materials/{materialId}/stage", new
            {
                sourceWarehouseId = Id(source),
                stagingWarehouseId = Id(staging),
                quantity = 40m,
                reason = "full material staging",
                expectedOrderVersion = order.GetProperty("version").GetInt64()
            });
        stage.EnsureSuccessStatusCode();
        order = await Json(stage);
        material = Assert.Single(order.GetProperty("materials").EnumerateArray().ToArray());
        var stagingLink = Assert.Single(material.GetProperty("stagings").EnumerateArray().ToArray());
        var transferId = stagingLink.GetProperty("inventoryTransferId").GetGuid();

        var transfer = await Get($"/api/inventory/transfers/{transferId}");
        var dispatch = await _client.PostAsJsonAsync($"/api/inventory/transfers/{transferId}/dispatch", new
        {
            effectiveDate = bounds.StartDate,
            expectedVersion = transfer.GetProperty("version").GetInt64(),
            reason = "production dispatch"
        });
        dispatch.EnsureSuccessStatusCode();
        transfer = await Json(dispatch);
        var receive = await _client.PostAsJsonAsync($"/api/inventory/transfers/{transferId}/receive", new
        {
            quantity = 40m,
            effectiveDate = bounds.StartDate,
            expectedVersion = transfer.GetProperty("version").GetInt64(),
            reason = "production staging receipt"
        });
        receive.EnsureSuccessStatusCode();
        order = await Get($"/api/production-orders/{order.GetProperty("id").GetGuid()}");
        material = Assert.Single(order.GetProperty("materials").EnumerateArray().ToArray());
        Assert.Equal(40m, material.GetProperty("stagedQuantity").GetDecimal());

        var stagedPosition = await Get(
            $"/api/inventory/items/{partItem.Id}/position?asOfDate={bounds.StartDate:yyyy-MM-dd}");
        Assert.Equal(0m, stagedPosition.GetProperty("usablePhysicalTotal").GetDecimal());
        Assert.Equal(40m, stagedPosition.GetProperty("productionStagingTotal").GetDecimal());
        Assert.Equal(40m, stagedPosition.GetProperty("companyOwnedTotal").GetDecimal());

        var failedReport = await _client.PostAsJsonAsync(
            $"/api/production-orders/{order.GetProperty("id").GetGuid()}/reports", new
            {
                productionDate = bounds.StartDate,
                stagingWarehouseId = Id(staging),
                goodQuantity = 1m,
                rejectedQuantity = 0m,
                notes = "missing variance reason",
                expectedVersion = order.GetProperty("version").GetInt64(),
                materials = new[] { new { productionOrderMaterialId = materialId,
                    actualQuantity = 2.5m, varianceReason = (string?)null } }
            });
        Assert.Equal(HttpStatusCode.BadRequest, failedReport.StatusCode);
        await using (var verifyFailure = await Db())
        {
            Assert.False(await verifyFailure.ProductionReports.AnyAsync(x =>
                x.ProductionOrderId == order.GetProperty("id").GetGuid()));
            Assert.False(await verifyFailure.DailyActuals.AnyAsync(x => x.ProductId == productItem.Id));
            Assert.False(await verifyFailure.InventoryTransactions.AnyAsync(x =>
                x.SourceDocumentType == "PRODUCTION_RECEIPT"));
        }

        for (var sample = 0; sample < 20; sample++)
        {
            decimal? actual = sample == 5 ? 2.5m : sample == 6 ? 1.5m : null;
            string? variance = sample is 5 or 6 ? $"controlled variance sample {sample + 1}" : null;
            var response = await _client.PostAsJsonAsync(
                $"/api/production-orders/{order.GetProperty("id").GetGuid()}/reports", new
                {
                    productionDate = bounds.StartDate.AddDays(sample),
                    stagingWarehouseId = Id(staging),
                    goodQuantity = 1m,
                    rejectedQuantity = 0m,
                    notes = $"sample {sample + 1}",
                    expectedVersion = order.GetProperty("version").GetInt64(),
                    materials = actual.HasValue
                        ? new[] { new { productionOrderMaterialId = materialId,
                            actualQuantity = actual, varianceReason = variance } }
                        : null
                });
            Assert.True(response.StatusCode == HttpStatusCode.Created,
                $"sample {sample + 1}: {(int)response.StatusCode} {await response.Content.ReadAsStringAsync()}");
            order = await Json(response);
            Assert.Equal(sample + 1m, order.GetProperty("reportedGoodQuantity").GetDecimal());
        }

        Assert.Equal("COMPLETED", order.GetProperty("status").GetString());
        Assert.Equal(20, order.GetProperty("reports").GetArrayLength());
        material = Assert.Single(order.GetProperty("materials").EnumerateArray().ToArray());
        Assert.Equal(40m, material.GetProperty("consumedQuantity").GetDecimal());

        var productPosition = await Get(
            $"/api/inventory/items/{productItem.Id}/position?asOfDate={bounds.EndDate:yyyy-MM-dd}");
        Assert.Equal(20m, productPosition.GetProperty("companyOwnedTotal").GetDecimal());
        var partPosition = await Get(
            $"/api/inventory/items/{partItem.Id}/position?asOfDate={bounds.EndDate:yyyy-MM-dd}");
        Assert.Equal(0m, partPosition.GetProperty("companyOwnedTotal").GetDecimal());
        Assert.Equal(0m, partPosition.GetProperty("productionStagingTotal").GetDecimal());

        await using var finalDb = await Db();
        Assert.Equal(20, await finalDb.ProductionReports.CountAsync(x =>
            x.ProductionOrderId == order.GetProperty("id").GetGuid()));
        Assert.Equal(20, await finalDb.DailyActuals.CountAsync(x => x.ProductId == productItem.Id));
        Assert.Equal(20m, await finalDb.DailyActualRevisions.Where(x =>
            x.DailyActual.ProductId == productItem.Id).SumAsync(x => x.ActualQty));
        Assert.Equal(20, await finalDb.InventoryTransactions.CountAsync(x =>
            x.SourceDocumentType == "PRODUCTION_ISSUE"));
        Assert.Equal(20, await finalDb.InventoryTransactions.CountAsync(x =>
            x.SourceDocumentType == "PRODUCTION_RECEIPT"));

        var reportId = order.GetProperty("reports")[0].GetProperty("id").GetGuid();
        var immutable = await Assert.ThrowsAsync<PostgresException>(() =>
            finalDb.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE production.reports SET \"GoodQuantity\" = {99m} WHERE \"Id\" = {reportId}"));
        Assert.Equal("ck_production_posted_immutable", immutable.ConstraintName);
        _factory.PlanningCalendar.TodayOverride = null;
    }

    private async Task<JsonElement> PostOrder(JsonElement order, string action, object body)
    {
        var response = await _client.PostAsJsonAsync(
            $"/api/production-orders/{order.GetProperty("id").GetGuid()}/{action}", body);
        response.EnsureSuccessStatusCode();
        return await Json(response);
    }

    private async Task<JsonElement> Warehouse(string nameFa, string nameEn, string type,
        Guid responsible)
    {
        var response = await _client.PostAsJsonAsync("/api/inventory/warehouses", new
        {
            nameFa,
            nameEn,
            addressFa = $"موقعیت {nameFa}",
            addressEn = $"Location {nameEn}",
            primaryResponsibleUserId = responsible,
            warehouseType = type,
            notes = "Production integration"
        });
        response.EnsureSuccessStatusCode();
        return await Json(response);
    }

    private async Task<ReferenceValue> Reference(HadronDbContext db, string typeCode, string prefix)
    {
        var type = await db.ReferenceTypes.SingleAsync(x => x.Code == typeCode);
        var code = $"{prefix}-{Guid.NewGuid():N}"[..Math.Min(30, prefix.Length + 9)].ToUpperInvariant();
        var value = new ReferenceValue(type.Id, code, code, $"فارسی {prefix}",
            $"English {prefix}", 1);
        db.ReferenceValues.Add(value);
        await db.SaveChangesAsync();
        return value;
    }

    private async Task<JsonElement> Get(string url)
    {
        var response = await _client.GetAsync(url);
        response.EnsureSuccessStatusCode();
        return await Json(response);
    }

    private static async Task<JsonElement> Json(HttpResponseMessage response) =>
        JsonSerializer.Deserialize<JsonElement>(await response.Content.ReadAsStringAsync(),
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

    private static async Task<string?> ProblemCode(HttpResponseMessage response)
    {
        var json = await Json(response);
        return json.TryGetProperty("code", out var code) ? code.GetString() : null;
    }

    private static Guid Id(JsonElement value) => value.GetProperty("id").GetGuid();

    private async Task<HadronDbContext> Db() =>
        await _factory.Services.GetRequiredService<IDbContextFactory<HadronDbContext>>()
            .CreateDbContextAsync();
}
