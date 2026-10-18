using System.Net;
using System.Net.Http.Json;
using Hadron.Erp.Api.Infrastructure;
using Hadron.Erp.Api.Modules.Catalog.Domain;
using Hadron.Erp.Api.Modules.Mrp.Application;
using Hadron.Erp.Api.Modules.Mrp.Domain;
using Hadron.Erp.Api.Modules.ReferenceData.Domain;
using Hadron.Erp.Api.Modules.Suppliers.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Hadron.Erp.IntegrationTests;

[Collection("ReferenceData")]
public sealed class MaterialPlanningTests : IClassFixture<FoundationApiFactory>, IAsyncLifetime
{
    private readonly FoundationApiFactory _factory;
    private readonly HttpClient _client;
    private readonly List<Guid> _suppliers = [];
    private readonly List<Guid> _references = [];

    public MaterialPlanningTests(FoundationApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    public async Task InitializeAsync()
    {
        await using var db = await Db();
        await db.Database.MigrateAsync();
        TestDatabaseSafety.EnsureSafeDatabaseName(db.Database.GetDbConnection().Database);
        await Truncate(db);
    }

    public async Task DisposeAsync()
    {
        await using var db = await Db();
        await Truncate(db);
        await db.SupplierPaymentProfiles.Where(x => _suppliers.Contains(x.SupplierId)).ExecuteDeleteAsync();
        await db.Suppliers.Where(x => _suppliers.Contains(x.Id)).ExecuteDeleteAsync();
        await db.ReferenceValues.Where(x => _references.Contains(x.Id)).ExecuteDeleteAsync();
    }

    [Fact]
    public async Task Completed_run_selection_material_scope_risk_precedence_and_legacy_context_are_snapshot_only()
    {
        var older = await SeedRun("MRP-RUN-910001", DateTime.UtcNow.AddMinutes(-2), false,
            [RiskItem("PRT-9101", ItemTypes.Part, 2), RiskItem("CMP-9101", ItemTypes.Component, 10)]);
        _ = older;
        await SeedRunning("MRP-RUN-910002");
        await SeedFailed("MRP-RUN-910004");
        var rows = new[]
        {
            RiskItem("PRT-9201", ItemTypes.Part, 2),
            RiskItem("PRT-9202", ItemTypes.Part, 3),
            RiskItem("PRT-9203", ItemTypes.Part, 5),
            RiskItem("PRT-9204", ItemTypes.Part, 10),
            RiskItem("CMP-9201", ItemTypes.Component, null, MrpCoverageStates.NoFutureDemand),
            RiskItem("CMP-9202", ItemTypes.Component, null, MrpCoverageStates.BeyondHorizon),
            RiskItem("CMP-9203", ItemTypes.Component, 7, stockout: true),
            RiskItem("CMP-LEGACY", ItemTypes.Component, 7, legacy: true),
            RiskItem("PRD-9999", ItemTypes.Product, 7)
        };
        var latest = await SeedRun("MRP-RUN-910003", DateTime.UtcNow, true, rows);

        var runList = (await _client.GetFromJsonAsync<MaterialPlanningRunListResponse>(
            "/api/material-planning/runs?page=1&pageSize=25"))!;
        Assert.Equal(latest, runList.Items[0].Id);
        Assert.Equal(2, runList.TotalCount);
        Assert.DoesNotContain(runList.Items, x => x.RunNumber == "MRP-RUN-910002");
        Assert.DoesNotContain(runList.Items, x => x.RunNumber == "MRP-RUN-910004");
        Assert.All(runList.Items, x => Assert.Contains(x.Status,
            new[] { MrpRunStatuses.Completed, MrpRunStatuses.CompletedWithExceptions }));

        var header = (await _client.GetFromJsonAsync<MaterialPlanningRunHeader>(
            $"/api/material-planning/runs/{latest}"))!;
        Assert.Equal("MRP-RUN-910003", header.RunNumber);
        Assert.Equal("MRP-10-v1", header.EngineVersion);
        Assert.Equal(MrpRunStatuses.CompletedWithExceptions, header.Status);
        Assert.Equal(MrpCompleteness.Complete, header.Completeness);

        var facets = (await _client.GetFromJsonAsync<MaterialPlanningFacetsResponse>(
            $"/api/material-planning/runs/{latest}/facets"))!;
        Assert.Equal(7, facets.Categories.Count);
        Assert.All(facets.Categories, category =>
        {
            Assert.Equal("CAT", category.Code);
            Assert.Equal("گروه", category.NameFa);
            Assert.Equal("Category", category.NameEn);
        });
        Assert.Equal([SupplyMethods.Make], facets.SupplyMethods);

        var summary = (await _client.GetFromJsonAsync<MaterialPlanningSummaryListResponse>(
            $"/api/material-planning/runs/{latest}/summary?page=1&pageSize=25"))!;
        Assert.Equal(8, summary.TotalCount);
        Assert.DoesNotContain(summary.Items, x => x.Item.ItemType == ItemTypes.Product);
        Assert.Equal(["CMP-LEGACY", "CMP-9203", "PRT-9201"],
            summary.Items.Take(3).Select(x => x.Item.Code).ToArray());
        Assert.Equal(MaterialRiskBands.Critical, ByCode(summary, "PRT-9201").RiskBand);
        Assert.Equal(MaterialRiskBands.Low, ByCode(summary, "PRT-9202").RiskBand);
        Assert.Equal(MaterialRiskBands.Safe, ByCode(summary, "PRT-9203").RiskBand);
        Assert.Equal(MaterialRiskBands.Excess, ByCode(summary, "PRT-9204").RiskBand);
        Assert.Equal(MaterialRiskBands.NoFutureDemand, ByCode(summary, "CMP-9201").RiskBand);
        Assert.Equal(MaterialRiskBands.BeyondHorizon, ByCode(summary, "CMP-9202").RiskBand);
        Assert.Equal(MaterialRiskBands.Stockout, ByCode(summary, "CMP-9203").RiskBand);
        var legacy = ByCode(summary, "CMP-LEGACY");
        Assert.Equal(MaterialRiskBands.Incomplete, legacy.RiskBand);
        Assert.Equal("LEGACY_RUN_CONTEXT_INCOMPLETE", legacy.RiskReason);
        var stockout = ByCode(summary, "CMP-9203");
        Assert.Equal(10, stockout.StartingOnHand);
        Assert.Equal(5, stockout.TotalFirmIncoming);
        Assert.Equal(10, stockout.TotalNetRequirement);
        Assert.Equal(new DateOnly(2026, 8, 25), stockout.StockoutDate);
        Assert.Equal(MrpCompleteness.Complete, stockout.Completeness);

        var filtered = (await _client.GetFromJsonAsync<MaterialPlanningSummaryListResponse>(
            $"/api/material-planning/runs/{latest}/summary?riskBand=CRITICAL&page=1&pageSize=25"))!;
        Assert.Single(filtered.Items);
        Assert.Equal("PRT-9201", filtered.Items[0].Item.Code);

        var sourceFiltered = (await _client.GetFromJsonAsync<MaterialPlanningSummaryListResponse>(
            $"/api/material-planning/runs/{latest}/summary?supplyMethod=MAKE" +
            $"&categoryId={facets.Categories[0].Id}&page=1&pageSize=25"))!;
        Assert.Single(sourceFiltered.Items);
        Assert.Equal(facets.Categories[0].Id, sourceFiltered.Items[0].Item.CategoryId);
        Assert.Equal(SupplyMethods.Make, sourceFiltered.Items[0].Item.SupplyMethod);

        var stockoutWindow = (await _client.GetFromJsonAsync<MaterialPlanningSummaryListResponse>(
            $"/api/material-planning/runs/{latest}/summary?stockoutFrom=2026-08-25" +
            "&stockoutTo=2026-08-25&page=1&pageSize=25"))!;
        Assert.Single(stockoutWindow.Items);
        Assert.Equal("CMP-9203", stockoutWindow.Items[0].Item.Code);

        var codeDescending = (await _client.GetFromJsonAsync<MaterialPlanningSummaryListResponse>(
            $"/api/material-planning/runs/{latest}/summary?sortBy=code&sortDirection=desc&page=1&pageSize=25"))!;
        Assert.Equal(codeDescending.Items.Select(x => x.Item.Code).OrderDescending(),
            codeDescending.Items.Select(x => x.Item.Code));

        var emptyOptionalSummary = await _client.GetAsync(
            $"/api/material-planning/runs/{latest}/summary?page=1&pageSize=25&categoryId=&stockoutFrom=&stockoutTo=");
        Assert.Equal(HttpStatusCode.OK, emptyOptionalSummary.StatusCode);
        var emptyOptionalExceptions = await _client.GetAsync(
            $"/api/material-planning/runs/{latest}/exceptions?page=1&pageSize=25&itemId=&date=");
        Assert.Equal(HttpStatusCode.OK, emptyOptionalExceptions.StatusCode);
    }

    [Fact]
    public async Task Run_selector_returns_both_completed_states_and_excludes_running_and_failed_runs()
    {
        var completed = await SeedRun("MRP-RUN-915001", DateTime.UtcNow.AddMinutes(-2), false,
            [RiskItem("PRT-9151", ItemTypes.Part, 4)]);
        var completedWithExceptions = await SeedRun("MRP-RUN-915002", DateTime.UtcNow.AddMinutes(-1), true,
            [RiskItem("CMP-9152", ItemTypes.Component, 4)], exceptionCount: 1);
        await SeedRunning("MRP-RUN-915003");
        await SeedFailed("MRP-RUN-915004");

        var selector = (await _client.GetFromJsonAsync<MaterialPlanningRunListResponse>(
            "/api/material-planning/runs?page=1&pageSize=25"))!;

        Assert.Equal(2, selector.TotalCount);
        Assert.Contains(selector.Items, x => x.Id == completed && x.Status == MrpRunStatuses.Completed);
        Assert.Contains(selector.Items, x => x.Id == completedWithExceptions &&
            x.Status == MrpRunStatuses.CompletedWithExceptions);
        Assert.DoesNotContain(selector.Items, x => x.RunNumber == "MRP-RUN-915003" ||
            x.Status == MrpRunStatuses.Running);
        Assert.DoesNotContain(selector.Items, x => x.RunNumber == "MRP-RUN-915004" ||
            x.Status == MrpRunStatuses.Failed);
    }

    [Fact]
    public async Task Timeline_filters_dangerous_out_of_horizon_rows_before_daily_weekly_and_monthly_aggregation()
    {
        var item = RiskItem("PRT-9301", ItemTypes.Part, 4, stockout: true, safetyBreach: true);
        var runId = await SeedRun("MRP-RUN-930001", DateTime.UtcNow, false, [item],
            [new(new DateOnly(2026, 8, 22), 900, 800, 700, 999, 998),
             new(new DateOnly(2026, 8, 24), 2, 1, 1, 9, 10),
             new(new DateOnly(2026, 8, 25), 3, 2, 2, 8, 10),
             new(new DateOnly(2026, 8, 28), 5, 0, 5, 3, 10),
             new(new DateOnly(2026, 8, 29), 7, 4, 3, 0, 10),
             new(new DateOnly(2026, 9, 2), 11, 1, 10, -10, 10),
             new(new DateOnly(2026, 9, 4), 600, 500, 400, 997, 996)],
            asOfDate: new DateOnly(2026, 8, 24), horizonEndDate: new DateOnly(2026, 9, 2));

        var daily = (await _client.GetFromJsonAsync<MaterialPlanningTimelineResponse>(
            $"/api/material-planning/runs/{runId}/items/{item.Id}/timeline?grain=DAILY"))!;
        Assert.Equal(5, daily.Buckets.Count);
        Assert.DoesNotContain(daily.Buckets, x => x.StartDate == new DateOnly(2026, 8, 22));
        Assert.DoesNotContain(daily.Buckets, x => x.StartDate == new DateOnly(2026, 9, 4));
        Assert.Equal(new DateOnly(2026, 8, 24), daily.Buckets[0].StartDate);
        Assert.Equal(new DateOnly(2026, 9, 2), daily.Buckets[^1].EndDate);
        Assert.Equal([8m, 5m, 0m, -7m, -18m], daily.Buckets.Select(x => x.StockOnlyProjected).ToArray());
        Assert.Equal(new int?[] { 9, 8, 5, 4, 0 }, daily.Buckets.Select(x => x.CoverageDays).ToArray());
        Assert.Equal([MaterialRiskBands.Safe, MaterialRiskBands.Safe, MaterialRiskBands.Safe,
            MaterialRiskBands.Low, MaterialRiskBands.Critical], daily.Buckets.Select(x => x.Zone).ToArray());

        var weekly = (await _client.GetFromJsonAsync<MaterialPlanningTimelineResponse>(
            $"/api/material-planning/runs/{runId}/items/{item.Id}/timeline?grain=WEEKLY"))!;
        Assert.Equal(2, weekly.Buckets.Count);
        Assert.Equal(new DateOnly(2026, 8, 24), weekly.Buckets[0].StartDate);
        Assert.Equal(new DateOnly(2026, 8, 28), weekly.Buckets[0].EndDate);
        Assert.Equal(10, weekly.Buckets[0].GrossDemand);
        Assert.Equal(3, weekly.Buckets[0].FirmIncoming);
        Assert.Equal(8, weekly.Buckets[0].NetRequirement);
        Assert.Equal(3, weekly.Buckets[0].FirmProjected);
        Assert.Equal(10, weekly.Buckets[0].PlanningBalance);
        Assert.Equal(new DateOnly(2026, 8, 29), weekly.Buckets[^1].StartDate);
        Assert.Equal(new DateOnly(2026, 9, 2), weekly.Buckets[^1].EndDate);
        Assert.Equal(18, weekly.Buckets[^1].GrossDemand);
        Assert.Equal(5, weekly.Buckets[^1].FirmIncoming);
        Assert.Equal(13, weekly.Buckets[^1].NetRequirement);
        Assert.Equal(-10, weekly.Buckets[^1].FirmProjected);
        Assert.Equal(10, weekly.Buckets[^1].PlanningBalance);
        Assert.True(weekly.Buckets[0].IncludesStockoutEvent);
        Assert.True(weekly.Buckets[0].IncludesSafetyBreachEvent);
        Assert.Equal(1, weekly.SafetyStockQty);
        Assert.Equal(MaterialRiskBands.Safe, weekly.Buckets[0].Zone);
        Assert.Equal(MaterialRiskBands.Critical, weekly.Buckets[1].Zone);
        Assert.Equal(0, weekly.Buckets[0].StockOnlyProjected);
        Assert.Equal(-18, weekly.Buckets[1].StockOnlyProjected);

        var monthly = (await _client.GetFromJsonAsync<MaterialPlanningTimelineResponse>(
            $"/api/material-planning/runs/{runId}/items/{item.Id}/timeline?grain=MONTHLY"))!;
        var month = Assert.Single(monthly.Buckets);
        Assert.Equal(new DateOnly(2026, 8, 24), month.StartDate);
        Assert.Equal(new DateOnly(2026, 9, 2), month.EndDate);
        Assert.Equal(28, month.GrossDemand);
        Assert.Equal(8, month.FirmIncoming);
        Assert.Equal(21, month.NetRequirement);
        Assert.Equal(-10, month.FirmProjected);
        Assert.Equal(10, month.PlanningBalance);
        Assert.Equal(MrpCoverageStates.Date, monthly.FirmCoverageState);
        Assert.Equal(4, monthly.FirmCoverageDays);
        Assert.Equal(MaterialRiskBands.Critical, month.Zone);
    }

    [Fact]
    public async Task Supply_trace_uses_supplier_snapshot_and_exception_pages_beyond_one_hundred_are_reachable()
    {
        var item = RiskItem("CMP-9401", ItemTypes.Component, 4);
        var supplier = await AddSupplier();
        var materials = new List<SeedItem> { item };
        materials.AddRange(Enumerable.Range(2, 104).Select(index =>
            RiskItem($"CMP-{9400 + index}", ItemTypes.Component, 4)));
        var runId = await SeedRun("MRP-RUN-940001", DateTime.UtcNow, true, materials,
            [new(new DateOnly(2026, 8, 24), 5, 2, 3, -3, 10)], addFirmSupply: true,
            supplierId: supplier,
            exceptionCount: 105);

        await using (var mutate = await Db())
        {
            var live = await mutate.Suppliers.SingleAsync(x => x.Id == supplier);
            live.Edit("نام زنده تغییرکرده", "LIVE-CHANGED", live.CountryId, null, null, null, null);
            await mutate.SaveChangesAsync();
        }

        var trace = (await _client.GetFromJsonAsync<MrpRequirementTraceResponse>(
            $"/api/material-planning/runs/{runId}/items/{item.Id}/trace?demandDate=2026-08-24"))!;
        var firm = Assert.Single(trace.FirmSupply);
        Assert.Equal("SUP-9401", firm.SupplierNumber);
        Assert.Equal("تأمین‌کننده Snapshot", firm.SupplierNameFa);
        Assert.Equal("Snapshot Supplier", firm.SupplierNameEn);
        Assert.Equal("PO-9401", firm.PurchaseOrderNumber);
        Assert.NotEqual(Guid.Empty, firm.PurchaseOrderLineId);
        Assert.NotEqual(Guid.Empty, firm.DeliveryScheduleId);
        Assert.Equal(2, firm.OpenQty);
        Assert.Equal(new DateOnly(2026, 8, 24), firm.Eta);
        Assert.Equal("ON_TIME", firm.EtaStatus);
        Assert.True(firm.AppliedByDemandDate);

        var first = (await _client.GetFromJsonAsync<MaterialPlanningExceptionListResponse>(
            $"/api/material-planning/runs/{runId}/exceptions?page=1&pageSize=25"))!;
        var fifth = (await _client.GetFromJsonAsync<MaterialPlanningExceptionListResponse>(
            $"/api/material-planning/runs/{runId}/exceptions?page=5&pageSize=25"))!;
        Assert.Equal(105, first.TotalCount);
        Assert.Equal(5, fifth.Items.Count);
        Assert.DoesNotContain(fifth.Items.Select(x => x.Id), id => first.Items.Any(x => x.Id == id));

        var summaryFifth = (await _client.GetFromJsonAsync<MaterialPlanningSummaryListResponse>(
            $"/api/material-planning/runs/{runId}/summary?page=5&pageSize=25"))!;
        Assert.Equal(105, summaryFifth.TotalCount);
        Assert.Equal(5, summaryFifth.Items.Count);

        var filtered = (await _client.GetFromJsonAsync<MaterialPlanningExceptionListResponse>(
            $"/api/material-planning/runs/{runId}/exceptions?severity=WARNING&sourceModule=BOM" +
            "&affectsDemand=true&date=2026-08-24&code=BOM_TEST&page=1&pageSize=100"))!;
        Assert.Equal(53, filtered.TotalCount);
        Assert.All(filtered.Items, row =>
        {
            Assert.Equal("WARNING", row.Severity);
            Assert.Equal("BOM", row.SourceModule);
            Assert.True(row.AffectsDemand);
            Assert.Equal(new DateOnly(2026, 8, 24), row.Date);
        });

        var mutation = await _client.PostAsJsonAsync($"/api/material-planning/runs/{runId}/exceptions",
            new { code = "RESOLVE" });
        Assert.Equal(HttpStatusCode.MethodNotAllowed, mutation.StatusCode);
    }

    [Fact]
    public async Task Shared_material_is_globally_aggregated_and_lists_every_forecast_product()
    {
        var runId = Guid.Empty;
        var productA = RiskItem("PRD-9501", ItemTypes.Product, null);
        var productB = RiskItem("PRD-9502", ItemTypes.Product, null);
        var partA = RiskItem("PRT-9501", ItemTypes.Part, 4);
        var partB = RiskItem("PRT-9502", ItemTypes.Part, 4);
        var shared = RiskItem("CMP-9501", ItemTypes.Component, 2, stockout: true);
        await using (var db = await Db())
        {
            var completedAt = DateTime.UtcNow;
            var run = new MrpRun("MRP-RUN-950001", new DateOnly(2026, 8, 24),
                new DateOnly(2026, 9, 24), "MRP-10-v1", null, completedAt.AddSeconds(-1));
            runId = run.Id;
            db.MrpRuns.Add(run);
            var unitId = Guid.NewGuid();
            foreach (var item in new[] { productA, productB, partA, partB, shared })
                db.MrpRunItemInputs.Add(new MrpRunItemInput(run.Id, item.Id, item.Code, item.Type,
                    $"نام {item.Code}", $"Name {item.Code}", true, unitId, "EA", "عدد", "Each",
                    item.Type == ItemTypes.Product ? null : SupplyMethods.Make, 0, 0,
                    item.Type == ItemTypes.Product ? null : Guid.NewGuid(),
                    item.Type == ItemTypes.Product ? null : "CAT",
                    item.Type == ItemTypes.Product ? null : "گروه",
                    item.Type == ItemTypes.Product ? null : "Category",
                    item.Type == ItemTypes.Product ? null : 3,
                    item.Type == ItemTypes.Product ? null : 5,
                    item.Type == ItemTypes.Product ? null : 10));
            foreach (var item in new[] { partA, partB, shared })
                db.MrpRunItemSummaries.Add(new MrpRunItemSummary(run.Id, item.Id,
                    item.Id == shared.Id ? 5 : 20, item.Id == shared.Id ? 30 : 10, 0,
                    item.Id == shared.Id ? 25 : 0, 0, 0,
                    item.Id == shared.Id ? new DateOnly(2026, 8, 26) : null, null,
                    MrpCoverageStates.Date, new DateOnly(2026, 8, 26), 2,
                    MrpCoverageStates.Date, new DateOnly(2026, 8, 26), 2,
                    MrpCompleteness.Complete));
            var periodId = Guid.NewGuid();
            foreach (var pair in new[] { (Product: productA, Qty: 40m), (Product: productB, Qty: 60m) })
            {
                var seriesId = Guid.NewGuid();
                var revisionId = Guid.NewGuid();
                db.MrpRunForecastPeriodInputs.Add(new MrpRunForecastPeriodInput(run.Id, pair.Product.Id,
                    periodId, seriesId, revisionId, pair.Qty, 0, pair.Qty, unitId,
                    new DateOnly(2026, 8, 24), new DateOnly(2026, 9, 24),
                    new DateOnly(2026, 8, 24), "ALLOCATED", null));
                db.MrpRunForecastInputs.Add(new MrpRunForecastInput(run.Id, pair.Product.Id,
                    periodId, seriesId, revisionId, new DateOnly(2026, 8, 25), pair.Qty,
                    pair.Qty, 0, unitId));
            }
            AddBom(db, run.Id, productA.Id, partA.Id, unitId);
            AddBom(db, run.Id, productB.Id, partB.Id, unitId);
            AddBom(db, run.Id, partA.Id, shared.Id, unitId);
            AddBom(db, run.Id, partB.Id, shared.Id, unitId);
            db.MrpRunDailyResults.Add(new MrpRunDailyResult(run.Id, shared.Id,
                new DateOnly(2026, 8, 25), 30, 0, -25, 5, 25, 0, false,
                MrpCompleteness.Complete));
            await db.SaveChangesAsync();
            run.MarkSnapshotCaptured(completedAt.AddMilliseconds(-100));
            run.Complete(MrpCompleteness.Complete, false, completedAt);
            await db.SaveChangesAsync();
        }

        var products = (await _client.GetFromJsonAsync<MaterialPlanningProductListResponse>(
            $"/api/material-planning/runs/{runId}/products?page=1&pageSize=25"))!;
        Assert.Equal(2, products.TotalCount);
        Assert.Equal(40, Assert.Single(products.Items, x => x.Product.Id == productA.Id).PlannedProductionQty);
        Assert.Equal(60, Assert.Single(products.Items, x => x.Product.Id == productB.Id).PlannedProductionQty);
        Assert.All(products.Items, row => Assert.Equal(2, row.MaterialCount));

        var productAMaterials = (await _client.GetFromJsonAsync<MaterialPlanningSummaryListResponse>(
            $"/api/material-planning/runs/{runId}/products/{productA.Id}/materials?page=1&pageSize=25"))!;
        var productBMaterials = (await _client.GetFromJsonAsync<MaterialPlanningSummaryListResponse>(
            $"/api/material-planning/runs/{runId}/products/{productB.Id}/materials?page=1&pageSize=25"))!;
        Assert.Equal(30, Assert.Single(productAMaterials.Items, x => x.Item.Id == shared.Id).TotalGrossDemand);
        Assert.Equal(30, Assert.Single(productBMaterials.Items, x => x.Item.Id == shared.Id).TotalGrossDemand);

        var timeline = (await _client.GetFromJsonAsync<MaterialPlanningTimelineResponse>(
            $"/api/material-planning/runs/{runId}/items/{shared.Id}/timeline?grain=DAILY"))!;
        Assert.Equal(["PRD-9501", "PRD-9502"], timeline.UsedByProducts.Select(x => x.Code).ToArray());
        var day = Assert.Single(timeline.Buckets);
        Assert.Equal(30, day.GrossDemand);
        Assert.Equal(-25, day.StockOnlyProjected);
        Assert.Equal(MaterialRiskBands.Critical, day.Zone);
    }

    [Fact]
    public async Task Source_change_and_outbox_are_atomic_and_pending_changes_are_coalesced_into_one_run()
    {
        Guid referenceId;
        await using (var seed = await Db())
        {
            var type = await seed.ReferenceTypes.SingleAsync(x => x.Code == "COUNTRY");
            var reference = new ReferenceValue(type.Id, $"AUTO-{Guid.NewGuid():N}"[..20],
                $"AUTO-{Guid.NewGuid():N}"[..20], "کشور خودکار", "Automatic country", 0);
            referenceId = reference.Id;
            seed.ReferenceValues.Add(reference);
            await seed.SaveChangesAsync();
        }
        _references.Add(referenceId);

        await using (var mutate = await Db())
        await using (var transaction = await mutate.Database.BeginTransactionAsync())
        {
            var reference = await mutate.ReferenceValues.SingleAsync(x => x.Id == referenceId);
            reference.SetActive(false);
            await mutate.SaveChangesAsync();
            Assert.Single(await mutate.MrpRefreshOutboxMessages.Where(x => x.Source == "REFERENCE_DATA")
                .ToListAsync());
            await transaction.RollbackAsync();
        }
        await using (var verifyRollback = await Db())
        {
            Assert.True((await verifyRollback.ReferenceValues.SingleAsync(x => x.Id == referenceId)).IsActive);
            Assert.Empty(await verifyRollback.MrpRefreshOutboxMessages.ToListAsync());
            var now = DateTime.UtcNow.AddSeconds(-1);
            verifyRollback.MrpRefreshOutboxMessages.AddRange(
                new MrpRefreshOutboxMessage("FORECAST", "ForecastRevision", null, null, now, now),
                new MrpRefreshOutboxMessage("BOM", "BomRevision", null, null, now, now));
            await verifyRollback.SaveChangesAsync();
        }

        await using var scope = _factory.Services.CreateAsyncScope();
        var processor = scope.ServiceProvider.GetRequiredService<AutomaticMrpRefreshProcessor>();
        Assert.True(await processor.ProcessOnceAsync(CancellationToken.None));
        await using var verify = await Db();
        var messages = await verify.MrpRefreshOutboxMessages.OrderBy(x => x.Source).ToListAsync();
        Assert.Equal(2, messages.Count);
        Assert.All(messages, row => Assert.Equal(MrpRefreshOutboxStatuses.Completed, row.Status));
        Assert.Single(messages.Select(x => x.RunId).Distinct());
        Assert.NotNull(messages[0].RunId);
        Assert.Equal(1, await verify.MrpRuns.CountAsync());
    }

    [Fact]
    public async Task Current_slot_is_atomically_replaced_and_old_unpinned_automatic_run_is_pruned()
    {
        var first = await SeedTransientRun("MRP-RUN-990001", new DateOnly(2026, 8, 30));
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var lifecycle = scope.ServiceProvider.GetRequiredService<MrpPlanLifecycleService>();
            await lifecycle.ActivateCurrentAsync(first, CancellationToken.None);
        }
        await using (var db = await Db())
        {
            var message = new MrpRefreshOutboxMessage("TEST", "MrpRun", first, null,
                DateTime.UtcNow, DateTime.UtcNow);
            message.MarkCompleted(first, DateTime.UtcNow);
            db.MrpRefreshOutboxMessages.Add(message);
            await db.SaveChangesAsync();
        }

        var second = await SeedTransientRun("MRP-RUN-990002", new DateOnly(2026, 8, 31));
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var lifecycle = scope.ServiceProvider.GetRequiredService<MrpPlanLifecycleService>();
            await lifecycle.ActivateCurrentAsync(second, CancellationToken.None);
        }

        await using var verify = await Db();
        Assert.Equal(second, (await verify.MrpPlanSlots.SingleAsync(x =>
            x.SlotName == MrpPlanSlotNames.Current)).RunId);
        Assert.False(await verify.MrpRuns.AnyAsync(x => x.Id == first));
        Assert.False(await verify.MrpRunManufacturingOperationInputs.AnyAsync(x => x.RunId == first));
        Assert.True(await verify.MrpRuns.AnyAsync(x => x.Id == second));
        Assert.Null((await verify.MrpRefreshOutboxMessages.SingleAsync()).RunId);
    }

    [Fact]
    public async Task Daily_certification_is_immutable_hashed_and_replaces_only_unpinned_full_plan_data()
    {
        var firstDate = new DateOnly(2026, 8, 30);
        var first = await SeedTransientRun("MRP-RUN-990003", firstDate);
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var lifecycle = scope.ServiceProvider.GetRequiredService<MrpPlanLifecycleService>();
            await lifecycle.ActivateCurrentAsync(first, CancellationToken.None);
            await lifecycle.CertifyAsync(first, firstDate, CancellationToken.None);
        }

        var secondDate = firstDate.AddDays(1);
        var second = await SeedTransientRun("MRP-RUN-990004", secondDate);
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var lifecycle = scope.ServiceProvider.GetRequiredService<MrpPlanLifecycleService>();
            await lifecycle.ActivateCurrentAsync(second, CancellationToken.None);
            await lifecycle.CertifyAsync(second, secondDate, CancellationToken.None);
        }

        await using var verify = await Db();
        Assert.Equal(2, await verify.MrpPlanSlots.CountAsync());
        Assert.All(await verify.MrpPlanSlots.ToListAsync(), slot => Assert.Equal(second, slot.RunId));
        Assert.False(await verify.MrpRuns.AnyAsync(x => x.Id == first));
        Assert.Single(await verify.MrpRuns.ToListAsync());
        var certificates = await verify.MrpDailyCertificates.OrderBy(x => x.BusinessDate).ToListAsync();
        Assert.Equal(2, certificates.Count);
        Assert.All(certificates, certificate => Assert.Matches("^[0-9a-f]{64}$", certificate.ResultHash));
        var failure = await Assert.ThrowsAsync<PostgresException>(async () =>
        {
            await verify.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM planning.mrp_daily_certificates WHERE \"Id\" = {certificates[0].Id}");
        });
        Assert.Contains("MRP_DAILY_CERTIFICATE_IMMUTABLE", failure.Message);
    }

    [Fact]
    public async Task Failed_certification_keeps_active_slots_and_published_business_snapshot_is_never_pruned()
    {
        var businessDate = new DateOnly(2026, 8, 31);
        var published = await SeedTransientRun("MRP-RUN-990005", businessDate,
            MrpRunPurposes.Published);
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var lifecycle = scope.ServiceProvider.GetRequiredService<MrpPlanLifecycleService>();
            await lifecycle.ActivateCurrentAsync(published, CancellationToken.None);
            var failure = await Assert.ThrowsAsync<MrpConflictException>(() =>
                lifecycle.CertifyAsync(published, businessDate.AddDays(-1), CancellationToken.None));
            Assert.Equal("MRP_CERTIFICATE_DATE_MISMATCH", failure.Code);
        }

        var replacement = await SeedTransientRun("MRP-RUN-990006", businessDate);
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var lifecycle = scope.ServiceProvider.GetRequiredService<MrpPlanLifecycleService>();
            await lifecycle.ActivateCurrentAsync(replacement, CancellationToken.None);
        }

        await using var verify = await Db();
        Assert.Equal(replacement, (await verify.MrpPlanSlots.SingleAsync(x =>
            x.SlotName == MrpPlanSlotNames.Current)).RunId);
        Assert.False(await verify.MrpPlanSlots.AnyAsync(x =>
            x.SlotName == MrpPlanSlotNames.Certified));
        Assert.True(await verify.MrpRuns.AnyAsync(x => x.Id == published));
        Assert.True(await verify.MrpRuns.AnyAsync(x => x.Id == replacement));
        Assert.Empty(await verify.MrpDailyCertificates.ToListAsync());
    }

    [Fact]
    public async Task Failed_unpublished_candidate_can_be_discarded_without_leaving_full_snapshot_data()
    {
        var candidate = await SeedTransientRun("MRP-RUN-990007", new DateOnly(2026, 8, 31));

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var lifecycle = scope.ServiceProvider.GetRequiredService<MrpPlanLifecycleService>();
            await lifecycle.DiscardTransientRunAsync(candidate, CancellationToken.None);
        }

        await using var verify = await Db();
        Assert.False(await verify.MrpRuns.AnyAsync(x => x.Id == candidate));
        Assert.False(await verify.MrpRunResolvedRouteResults.AnyAsync(x => x.RunId == candidate));
        Assert.False(await verify.MrpRunManufacturingOperationInputs.AnyAsync(x => x.RunId == candidate));
        Assert.Empty(await verify.MrpPlanSlots.ToListAsync());
    }

    [Fact]
    public async Task Twenty_daily_plan_refreshes_keep_only_current_and_previous_certified_full_snapshots()
    {
        var firstDate = new DateOnly(2026, 8, 1);
        var runIds = new List<Guid>();
        Guid? currentRunId = null;

        for (var day = 0; day < 20; day++)
        {
            if (currentRunId.HasValue)
            {
                await using var certificationScope = _factory.Services.CreateAsyncScope();
                var lifecycle = certificationScope.ServiceProvider.GetRequiredService<MrpPlanLifecycleService>();
                await lifecycle.CertifyAsync(currentRunId.Value, firstDate.AddDays(day - 1),
                    CancellationToken.None);
            }

            currentRunId = await SeedTransientRun($"MRP-RUN-{991001 + day}",
                firstDate.AddDays(day));
            runIds.Add(currentRunId.Value);
            await using (var activationScope = _factory.Services.CreateAsyncScope())
            {
                var lifecycle = activationScope.ServiceProvider.GetRequiredService<MrpPlanLifecycleService>();
                await lifecycle.ActivateCurrentAsync(currentRunId.Value, CancellationToken.None);
            }

            await using var iterationAudit = await Db();
            Assert.InRange(await iterationAudit.MrpRuns.CountAsync(), 1, 2);
            Assert.Equal(currentRunId.Value, (await iterationAudit.MrpPlanSlots.SingleAsync(x =>
                x.SlotName == MrpPlanSlotNames.Current)).RunId);
        }

        await using var verify = await Db();
        var currentSlot = await verify.MrpPlanSlots.SingleAsync(x =>
            x.SlotName == MrpPlanSlotNames.Current);
        var certifiedSlot = await verify.MrpPlanSlots.SingleAsync(x =>
            x.SlotName == MrpPlanSlotNames.Certified);
        Assert.Equal(runIds[^1], currentSlot.RunId);
        Assert.Equal(runIds[^2], certifiedSlot.RunId);
        Assert.Equal(2, await verify.MrpRuns.CountAsync());
        Assert.Equal(2, await verify.MrpRunResolvedRouteResults.CountAsync());
        Assert.Equal(2, await verify.MrpRunManufacturingOperationInputs.CountAsync());
        Assert.Equal(19, await verify.MrpDailyCertificates.CountAsync());
        Assert.Equal(19, await verify.MrpDailyCertificates.Select(x => x.BusinessDate)
            .Distinct().CountAsync());
        Assert.Equal(19, await verify.MrpDailyCertificates.Select(x => x.ResultHash)
            .Distinct().CountAsync());
        Assert.False(await verify.MrpRuns.AnyAsync(x => runIds.Take(18).Contains(x.Id)));
    }

    private async Task<Guid> SeedTransientRun(string number, DateOnly asOfDate,
        string purpose = MrpRunPurposes.Current)
    {
        await using var db = await Db();
        var run = new MrpRun(number, asOfDate, asOfDate.AddDays(30), "MRP-10-v1", null,
            DateTime.UtcNow.AddSeconds(-1), purpose);
        db.MrpRuns.Add(run);
        await db.SaveChangesAsync();
        db.MrpRunResolvedRouteResults.Add(new MrpRunResolvedRouteResult(run.Id, Guid.NewGuid(),
            asOfDate, MrpRoutes.External, MrpRouteOrigins.Buy, null));
        db.MrpRunManufacturingOperationInputs.Add(new MrpRunManufacturingOperationInput(run.Id,
            Guid.NewGuid(), "RTE-990001", Guid.NewGuid(), Guid.NewGuid(), 10, Guid.NewGuid(),
            "WC-990001", 63, 1, 2));
        await db.SaveChangesAsync();
        run.MarkSnapshotCaptured(DateTime.UtcNow.AddMilliseconds(-100));
        run.Complete(MrpCompleteness.Complete, false, DateTime.UtcNow);
        await db.SaveChangesAsync();
        return run.Id;
    }

    private async Task<Guid> SeedRun(string number, DateTime completedAt, bool withExceptions,
        IReadOnlyList<SeedItem> items, IReadOnlyList<SeedDay>? days = null,
        bool addFirmSupply = false, Guid? supplierId = null, int exceptionCount = 0,
        DateOnly? asOfDate = null, DateOnly? horizonEndDate = null)
    {
        await using var db = await Db();
        var run = new MrpRun(number, asOfDate ?? new DateOnly(2026, 8, 22),
            horizonEndDate ?? new DateOnly(2026, 9, 25),
            "MRP-10-v1", null, completedAt.AddSeconds(-1));
        db.MrpRuns.Add(run);
        foreach (var item in items)
        {
            db.MrpRunItemInputs.Add(new MrpRunItemInput(run.Id, item.Id, item.Code, item.Type,
                $"نام {item.Code}", $"Name {item.Code}", true, Guid.NewGuid(), "EA", "عدد", "Each",
                item.Type == ItemTypes.Product ? null : SupplyMethods.Make, 1, 0,
                item.Legacy ? null : Guid.NewGuid(), item.Legacy ? null : "CAT",
                item.Legacy ? null : "گروه", item.Legacy ? null : "Category",
                item.Legacy ? null : 3, item.Legacy ? null : 5, item.Legacy ? null : 10));
            db.MrpRunItemSummaries.Add(new MrpRunItemSummary(run.Id, item.Id, 10, 20, 5, 10,
                0, 0, item.Stockout ? new DateOnly(2026, 8, 25) : null,
                item.SafetyBreach ? new DateOnly(2026, 8, 26) : null,
                MrpCoverageStates.Date, new DateOnly(2026, 8, 26), 4,
                item.CoverageState, item.CoverageDays.HasValue ? new DateOnly(2026, 8, 22).AddDays(item.CoverageDays.Value) : null,
                item.CoverageDays, MrpCompleteness.Complete));
        }
        if (days is not null)
            foreach (var day in days)
                db.MrpRunDailyResults.Add(new MrpRunDailyResult(run.Id, items[0].Id, day.Date,
                    day.Gross, day.Firm, day.FirmProjected, day.FirmProjected - day.Firm + day.Gross,
                    day.Net, day.Planning, false, MrpCompleteness.Complete));
        if (addFirmSupply)
            db.MrpRunFirmSupplyInputs.Add(new MrpRunFirmSupplyInput(run.Id, items[0].Id,
                Guid.NewGuid(), "PO-9401", Guid.NewGuid(), Guid.NewGuid(), 2,
                new DateOnly(2026, 8, 24), "ORIGINAL", "ON_TIME", "ORDERED", supplierId ?? Guid.NewGuid(),
                "SUP-9401", "تأمین‌کننده Snapshot", "Snapshot Supplier"));
        for (var index = 0; index < exceptionCount; index++)
            db.MrpRunExceptions.Add(new MrpRunException(run.Id, items[0].Id,
                new DateOnly(2026, 8, 24), $"BOM_TEST_{index:000}", MrpExceptionSeverities.Warning,
                index % 2 == 0, $"Snapshot exception {index}", $"SRC-{index:000}"));
        await db.SaveChangesAsync();
        run.MarkSnapshotCaptured(completedAt.AddMilliseconds(-100));
        run.Complete(MrpCompleteness.Complete, withExceptions, completedAt);
        await db.SaveChangesAsync();
        return run.Id;
    }

    private async Task SeedRunning(string number)
    {
        await using var db = await Db();
        db.MrpRuns.Add(new MrpRun(number, new DateOnly(2026, 8, 22),
            new DateOnly(2026, 9, 25), "MRP-10-v1", null, DateTime.UtcNow));
        await db.SaveChangesAsync();
    }

    private async Task SeedFailed(string number)
    {
        await using var db = await Db();
        var run = new MrpRun(number, new DateOnly(2026, 8, 22),
            new DateOnly(2026, 9, 25), "MRP-10-v1", null, DateTime.UtcNow);
        db.MrpRuns.Add(run);
        await db.SaveChangesAsync();
        run.Fail("TEST_FAILURE", "Expected failed run", DateTime.UtcNow);
        await db.SaveChangesAsync();
    }

    private async Task<Guid> AddSupplier()
    {
        await using var db = await Db();
        var type = await db.ReferenceTypes.SingleAsync(x => x.Code == "COUNTRY");
        var country = new ReferenceValue(type.Id, $"MP-{Guid.NewGuid():N}"[..20],
            $"MP-{Guid.NewGuid():N}"[..20], "کشور تست", "Test country", 0);
        var supplier = new Supplier("SUP-9401", "تأمین‌کننده Snapshot", "SNAPSHOT SUPPLIER",
            country.Id, null, null, null, null);
        db.AddRange(country, supplier);
        await db.SaveChangesAsync();
        _references.Add(country.Id); _suppliers.Add(supplier.Id);
        return supplier.Id;
    }

    private static SeedItem RiskItem(string code, string type, int? coverageDays,
        string coverageState = MrpCoverageStates.Date, bool stockout = false, bool legacy = false,
        bool safetyBreach = false) =>
        new(Guid.NewGuid(), code, type, coverageDays, coverageState, stockout, legacy, safetyBreach);

    private static MaterialPlanningSummaryRow ByCode(MaterialPlanningSummaryListResponse page, string code) =>
        Assert.Single(page.Items, x => x.Item.Code == code);

    private static void AddBom(HadronDbContext db, Guid runId, Guid parentId, Guid childId, Guid unitId) =>
        db.MrpRunBomInputs.Add(new MrpRunBomInput(runId, Guid.NewGuid(),
            $"BOM-{Guid.NewGuid():N}"[..16], Guid.NewGuid(), parentId, childId, unitId, unitId,
            1, 1, null, new DateOnly(2026, 8, 24), null));

    private async Task<HadronDbContext> Db()
    {
        var scope = _factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IDbContextFactory<HadronDbContext>>()
            .CreateDbContextAsync();
    }

    private static Task Truncate(HadronDbContext db) => db.Database.ExecuteSqlRawAsync("""
        TRUNCATE planning.mrp_daily_certificates, planning.mrp_plan_slots,
          planning.mrp_refresh_outbox, planning.mrp_run_demand_traces, planning.mrp_run_daily_results,
          planning.mrp_run_item_summaries, planning.mrp_run_exceptions,
          planning.mrp_run_external_source_inputs, planning.mrp_run_forecast_period_inputs,
          planning.mrp_run_route_decisions, planning.mrp_run_route_availability_inputs,
          planning.mrp_run_firm_supply_inputs, planning.mrp_run_inventory_inputs,
          planning.mrp_run_bom_inputs, planning.mrp_run_forecast_inputs,
          planning.mrp_run_item_inputs, planning.mrp_runs RESTART IDENTITY CASCADE
        """);

    private sealed record SeedItem(Guid Id, string Code, string Type, int? CoverageDays,
        string CoverageState, bool Stockout, bool Legacy, bool SafetyBreach);
    private sealed record SeedDay(DateOnly Date, decimal Gross, decimal Firm, decimal Net,
        decimal FirmProjected, decimal Planning);
}
