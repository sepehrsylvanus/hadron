using System.Net;
using System.Net.Http.Json;
using Hadron.Erp.Api.Infrastructure;
using Hadron.Erp.Api.Modules.Bom.Domain;
using Hadron.Erp.Api.Modules.Catalog.Domain;
using Hadron.Erp.Api.Modules.Identity.Domain;
using Hadron.Erp.Api.Modules.Mrp.Application;
using Hadron.Erp.Api.Modules.Mrp.Domain;
using Hadron.Erp.Api.Modules.Planning.Domain;
using Hadron.Erp.Api.Modules.Production.Application;
using Hadron.Erp.Api.Modules.Production.Domain;
using Hadron.Erp.Api.Modules.Products.Domain;
using Hadron.Erp.Api.Modules.ReferenceData.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Hadron.Erp.IntegrationTests;

[Collection("ReferenceData")]
public sealed class ProductionRoutingTests : IClassFixture<FoundationApiFactory>
{
    private readonly FoundationApiFactory _factory;
    private readonly HttpClient _client;

    public ProductionRoutingTests(FoundationApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Approved_route_schedules_internal_preparation_and_protects_used_work_center()
    {
        await using var db = await Db();
        await db.Database.MigrateAsync();
        var unit = new Unit($"RTE-{Guid.NewGuid():N}"[..20], $"RTE-{Guid.NewGuid():N}"[..20],
            "عدد مسیر", "Route piece", "pcs", 1);
        var organization = new OrganizationalUnit("واحد آماده‌سازی تست", "Test preparation department");
        var categoryType = await db.ReferenceTypes.SingleAsync(x => x.Code == "PRODUCT_CATEGORY");
        var category = new ReferenceValue(categoryType.Id, $"RTE-{Guid.NewGuid():N}"[..20].ToUpperInvariant(),
            $"RTE-{Guid.NewGuid():N}"[..20].ToUpperInvariant(), "محصول مسیر تست", "Route test product", 1);
        var item = new Item(ItemTypes.Product, $"PRD-{Random.Shared.NextInt64(1_000_000, 9_999_999)}",
            "محصول زمان‌بندی داخلی", "Internal scheduling product", unit.Id, null);
        var product = new Product(item.Id, category.Id, null, 0, 0, 0, 0);
        db.AddRange(unit, organization, category, item, product);
        await db.SaveChangesAsync();

        var centerResponse = await _client.PostAsJsonAsync("/api/production-work-centers", new
        {
            nameFa = "آماده‌سازی پلیمر",
            nameEn = "Polymer preparation",
            organizationalUnitId = organization.Id,
            workingWeekMask = 95
        });
        Assert.Equal(HttpStatusCode.Created, centerResponse.StatusCode);
        var center = (await centerResponse.Content.ReadFromJsonAsync<ProductionWorkCenterResponse>())!;
        Assert.Matches("^WC-[0-9]{4,}$", center.Code);

        var candidatePage = await _client.GetFromJsonAsync<ManufacturingRouteItemCandidateListResponse>(
            $"/api/manufacturing-routes/item-candidates?search={item.BusinessNumber}&page=1&pageSize=25");
        Assert.Contains(candidatePage!.Items, x => x.Id == item.Id);

        var draftResponse = await _client.PostAsJsonAsync("/api/manufacturing-routes", new
        {
            itemId = item.Id,
            notes = "four-day preparation plus one execution day",
            operations = new[]
            {
                new { sequence = 10, workCenterId = center.Id,
                    preparationWorkingDays = 4, executionWorkingDays = 1,
                    notes = "test route" }
            }
        });
        Assert.Equal(HttpStatusCode.Created, draftResponse.StatusCode);
        var draft = (await draftResponse.Content.ReadFromJsonAsync<ManufacturingRouteResponse>())!;
        Assert.Equal("DRAFT", draft.Status);
        Assert.Equal(5, draft.TotalWorkingDays);

        var approvedResponse = await _client.PostAsJsonAsync($"/api/manufacturing-routes/{draft.Id}/approve",
            new { expectedVersion = draft.Version, reason = "approved integration scenario" });
        approvedResponse.EnsureSuccessStatusCode();
        var approved = (await approvedResponse.Content.ReadFromJsonAsync<ManufacturingRouteResponse>())!;
        Assert.Equal("APPROVED", approved.Status);
        await using (var verifyRefresh = await Db())
        {
            var refresh = await verifyRefresh.MrpRefreshOutboxMessages.AsNoTracking()
                .SingleAsync(x => x.Source == "MANUFACTURING_ROUTE");
            Assert.Equal(nameof(ManufacturingRoute), refresh.SourceEntityTypes);
            Assert.Equal(approved.Id, refresh.SourceEntityId);
        }

        var completion = new DateOnly(2026, 9, 10);
        var preview = await _client.GetFromJsonAsync<ManufacturingRouteScheduleResponse>(
            $"/api/manufacturing-routes/{approved.Id}/schedule-preview?completionDate={completion:yyyy-MM-dd}");
        Assert.NotNull(preview);
        Assert.Equal(new DateOnly(2026, 9, 5), preview.MaterialRequiredDate);
        Assert.Single(preview.Operations);

        var approvedEdit = await _client.PutAsJsonAsync($"/api/manufacturing-routes/{approved.Id}", new
        {
            notes = "illegal edit",
            operations = new[] { new { sequence = 10, workCenterId = center.Id,
                preparationWorkingDays = 4, executionWorkingDays = 1, notes = "illegal" } },
            expectedVersion = approved.Version
        });
        Assert.Equal(HttpStatusCode.Conflict, approvedEdit.StatusCode);

        var deactivate = await _client.PostAsJsonAsync($"/api/production-work-centers/{center.Id}/deactivate",
            new { expectedVersion = center.Version, reason = "must be blocked" });
        Assert.Equal(HttpStatusCode.Conflict, deactivate.StatusCode);
        Assert.Contains(_factory.AuditSink.Snapshot(), x => x.EntityType == "ManufacturingRoute" &&
            x.Action == "APPROVE");
    }

    [Fact]
    public async Task Mrp_snapshots_approved_routes_and_moves_internal_material_need_to_department_start()
    {
        await using var db = await Db();
        await db.Database.MigrateAsync();
        var unit = new Unit($"MR-{Guid.NewGuid():N}"[..20], $"MR-{Guid.NewGuid():N}"[..20],
            "عدد مسیر MRP", "MRP route piece", "pcs", 1);
        var organization = new OrganizationalUnit("واحد آماده‌سازی MRP", "MRP preparation department");
        var productCategoryType = await db.ReferenceTypes.SingleAsync(x => x.Code == "PRODUCT_CATEGORY");
        var partCategoryType = await db.ReferenceTypes.SingleAsync(x => x.Code == "PART_CATEGORY");
        var productCategory = new ReferenceValue(productCategoryType.Id, $"MRP-{Guid.NewGuid():N}"[..20],
            $"MRP-{Guid.NewGuid():N}"[..20], "گروه محصول مسیر", "Route product category", 1);
        var partCategory = new ReferenceValue(partCategoryType.Id, $"MRP-{Guid.NewGuid():N}"[..20],
            $"MRP-{Guid.NewGuid():N}"[..20], "گروه مواد مسیر", "Route material category", 1);
        var productItem = new Item(ItemTypes.Product, $"PRD-{Random.Shared.NextInt64(1_000_000, 9_999_999)}",
            "محصول مسیر کامل", "Full route product", unit.Id, null);
        var bodyItem = new Item(ItemTypes.Part, $"PRT-{Random.Shared.NextInt64(1_000_000, 9_999_999)}",
            "بدنه ساخت داخل", "Internally made body", unit.Id, null);
        var polymerItem = new Item(ItemTypes.Component, $"CMP-{Random.Shared.NextInt64(1_000_000, 9_999_999)}",
            "پلیمر مصرفی", "Consumed polymer", unit.Id, null);
        db.AddRange(unit, organization, productCategory, partCategory, productItem, bodyItem, polymerItem,
            new Product(productItem.Id, productCategory.Id, null, 0, 0, 0, 0),
            new MaterialItemProfile(bodyItem.Id, partCategory.Id, SupplyMethods.Make, null, 0, 0, 0, 0, 0),
            new MaterialItemProfile(polymerItem.Id, partCategory.Id, SupplyMethods.Buy, null, 0, 0, 0, 0, 0));
        await db.SaveChangesAsync();

        var calendar = _factory.Services.GetRequiredService<Hadron.Erp.Api.Modules.Planning.Application.IPlanningCalendar>();
        var bounds = calendar.GetPeriodBounds(1420, 1);
        var period = new BusinessPeriod(1420, 1, bounds.StartDate, bounds.EndDate);
        var series = new ForecastSeries(productItem.Id, period.Id, unit.Id);
        db.AddRange(period, series);
        await db.SaveChangesAsync();
        var days = bounds.EndDate.DayNumber - bounds.StartDate.DayNumber + 1;
        var forecast = new ForecastRevision(series.Id, 1, days * 1_000m, "routing integration", null);
        db.ForecastRevisions.Add(forecast);
        await db.SaveChangesAsync();
        series.SetCurrent(forecast);

        var productBom = new BomRevision($"BOM-MR-{Guid.NewGuid():N}"[..28], productItem.Id, unit.Id, 1, null);
        var bodyBom = new BomRevision($"BOM-MR-{Guid.NewGuid():N}"[..28], bodyItem.Id, unit.Id, 1, null);
        db.AddRange(productBom, bodyBom,
            new BomLine(productBom.Id, bodyItem.Id, unit.Id, 1, null, 1),
            new BomLine(bodyBom.Id, polymerItem.Id, unit.Id, 0.014m, null, 1));
        await db.SaveChangesAsync();
        productBom.Approve(null);
        bodyBom.Approve(null);

        var center = new ProductionWorkCenter($"WC-{Random.Shared.Next(100000, 999999)}",
            "آماده‌سازی پلیمر", "Polymer preparation", organization.Id, 95);
        var productRoute = new ManufacturingRoute($"RTE-{Random.Shared.Next(100000, 999999)}",
            productItem.Id, unit.Id, 1, null);
        var bodyRoute = new ManufacturingRoute($"RTE-{Random.Shared.Next(100000, 999999)}",
            bodyItem.Id, unit.Id, 1, null);
        db.AddRange(center, productRoute, bodyRoute,
            new ManufacturingRouteOperation(productRoute.Id, 10, center.Id, 0, 1, null),
            new ManufacturingRouteOperation(bodyRoute.Id, 10, center.Id, 4, 0, null));
        await db.SaveChangesAsync();
        productRoute.Approve();
        bodyRoute.Approve();
        await db.SaveChangesAsync();

        var response = await _client.PostAsJsonAsync("/api/mrp-runs", new CreateMrpRunRequest(
            bounds.StartDate.AddDays(-1), bounds.StartDate.AddDays(10), []));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var run = (await response.Content.ReadFromJsonAsync<MrpRunResponse>())!;

        Assert.Equal(2, await db.MrpRunManufacturingOperationInputs.AsNoTracking()
            .CountAsync(x => x.RunId == run.Id));
        Assert.Contains(await db.MrpRunDemandTraces.AsNoTracking().Where(x => x.RunId == run.Id &&
                x.ParentItemId == bodyItem.Id && x.ChildItemId == polymerItem.Id).ToListAsync(),
            trace => trace.Date < trace.ParentDemandDate);
        Assert.DoesNotContain(await db.MrpRunExceptions.AsNoTracking().Where(x => x.RunId == run.Id)
            .ToListAsync(), exception => exception.Code == "MISSING_APPROVED_MANUFACTURING_ROUTE" &&
                (exception.ItemId == productItem.Id || exception.ItemId == bodyItem.Id));
    }

    private async Task<HadronDbContext> Db() =>
        await _factory.Services.GetRequiredService<IDbContextFactory<HadronDbContext>>()
            .CreateDbContextAsync();
}
