using System.Net;
using System.Net.Http.Json;
using Hadron.Erp.Api.Infrastructure;
using Hadron.Erp.Api.Modules.Catalog.Domain;
using Hadron.Erp.Api.Modules.Mrp.Application;
using Hadron.Erp.Api.Modules.Mrp.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Hadron.Erp.IntegrationTests;

[Collection("ReferenceData")]
public sealed class PlanningCollaborationTests : IClassFixture<FoundationApiFactory>, IAsyncLifetime
{
    private readonly FoundationApiFactory _factory;
    private readonly HttpClient _client;

    public PlanningCollaborationTests(FoundationApiFactory factory)
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
        _factory.AuditSink.Clear();
    }

    public async Task DisposeAsync()
    {
        await using var db = await Db();
        await Truncate(db);
        _factory.AuditSink.Clear();
    }

    [Fact]
    public async Task Planner_work_item_tracks_an_immutable_run_exception_with_history_concurrency_and_private_view()
    {
        var fixture = await SeedCompletedRun(includeException: true);

        var emptyAssigneeFilter = await _client.GetAsync(
            $"/api/material-planning/planner-inbox?runId={fixture.RunId}&assignedUserId=&page=1&pageSize=25");
        Assert.Equal(HttpStatusCode.OK, emptyAssigneeFilter.StatusCode);

        Guid exceptionId;
        await using (var db = await Db())
            exceptionId = (await db.MrpRunExceptions.SingleAsync()).Id;

        var create = await _client.PostAsJsonAsync("/api/material-planning/planner-work-items",
            new CreatePlannerWorkItemRequest(exceptionId, null));
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var item = (await create.Content.ReadFromJsonAsync<PlannerWorkItemResponse>())!;
        Assert.Equal(PlannerWorkItemStatuses.Open, item.Status);
        Assert.Equal(1, item.Version);

        var duplicate = await _client.PostAsJsonAsync("/api/material-planning/planner-work-items",
            new CreatePlannerWorkItemRequest(exceptionId, null));
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);

        var acknowledge = await _client.PostAsJsonAsync(
            $"/api/material-planning/planner-work-items/{item.Id}/acknowledge",
            new AcknowledgePlannerWorkItemRequest(item.Version));
        Assert.Equal(HttpStatusCode.OK, acknowledge.StatusCode);
        var acknowledged = (await acknowledge.Content.ReadFromJsonAsync<PlannerWorkItemResponse>())!;
        Assert.Equal(PlannerWorkItemStatuses.Acknowledged, acknowledged.Status);
        Assert.Equal(2, acknowledged.Version);

        var stale = await _client.PostAsJsonAsync(
            $"/api/material-planning/planner-work-items/{item.Id}/acknowledge",
            new AcknowledgePlannerWorkItemRequest(item.Version));
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);

        var saved = await _client.PostAsJsonAsync("/api/material-planning/saved-views",
            new CreatePlannerSavedViewRequest("بحرانی‌های من", "{\"status\":\"OPEN\",\"page\":2}"));
        Assert.Equal(HttpStatusCode.Created, saved.StatusCode);
        var views = (await _client.GetFromJsonAsync<IReadOnlyList<PlannerSavedViewResponse>>(
            "/api/material-planning/saved-views"))!;
        Assert.Single(views);
        Assert.Contains("OPEN", views[0].FiltersJson);

        await using var verify = await Db();
        var source = await verify.MrpRunExceptions.SingleAsync(x => x.Id == exceptionId);
        Assert.Equal("TEST_PLANNER_EXCEPTION", source.Code);
        Assert.Equal("Immutable calculation exception", source.Message);
        Assert.Equal(["CREATED", "ACKNOWLEDGED"], await verify.PlannerWorkItemEvents
            .OrderBy(x => x.OccurredAtUtc).Select(x => x.EventType).ToArrayAsync());
        var eventId = await verify.PlannerWorkItemEvents.Select(x => x.Id).FirstAsync();
        var directMutation = await Assert.ThrowsAsync<PostgresException>(() => verify.Database.ExecuteSqlAsync(
            $"UPDATE planning.planner_work_item_events SET \"EventType\" = 'TAMPERED' WHERE \"Id\" = {eventId}"));
        Assert.Equal("23514", directMutation.SqlState);
        Assert.Contains(_factory.AuditSink.Snapshot(), x => x.EntityType == "PlannerWorkItem" && x.Action == "Created");
        Assert.Contains(_factory.AuditSink.Snapshot(), x => x.EntityType == "PlannerWorkItem" && x.Action == "Acknowledged");
    }

    [Fact]
    public async Task What_if_uses_only_the_immutable_run_snapshot_and_keeps_official_truth_unchanged()
    {
        var fixture = await SeedCompletedRun();
        var request = new CreateWhatIfScenarioRequest("افزایش برنامه و تغییر موجودی/تأمین", fixture.RunId,
            [new(fixture.ProductId, fixture.PeriodId, 20m)],
            [new(fixture.PartId, 5m)],
            [new(fixture.ScheduleId, 4m, fixture.DemandDate)]);

        var response = await _client.PostAsJsonAsync("/api/material-planning/what-if-scenarios", request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var scenario = (await response.Content.ReadFromJsonAsync<WhatIfScenarioResponse>())!;
        Assert.Equal(WhatIfScenarioStatuses.Calculated, scenario.Status);
        Assert.Equal(3, scenario.OverrideCount);

        var results = (await _client.GetFromJsonAsync<WhatIfResultListResponse>(
            $"/api/material-planning/what-if-scenarios/{scenario.Id}/results?page=1&pageSize=25"))!;
        var part = Assert.Single(results.Items, x => x.ItemId == fixture.PartId);
        Assert.Equal(0m, part.OfficialStartingOnHand);
        Assert.Equal(5m, part.ScenarioStartingOnHand);
        Assert.Equal(8m, part.OfficialNetRequirement);
        Assert.Equal(11m, part.ScenarioNetRequirement);
        Assert.Equal(3m, part.NetRequirementDelta);

        var candidates = (await _client.GetFromJsonAsync<WhatIfCandidateListResponse>(
            $"/api/material-planning/runs/{fixture.RunId}/what-if/items?search=PRT-9801&page=1&pageSize=1"))!;
        Assert.Equal(1, candidates.TotalCount);
        Assert.Equal(fixture.PartId, candidates.Items[0].Id);
        var supplies = (await _client.GetFromJsonAsync<WhatIfFirmSupplyCandidateListResponse>(
            $"/api/material-planning/runs/{fixture.RunId}/what-if/firm-supplies?search=PO-9801&page=1&pageSize=1"))!;
        Assert.Equal(fixture.ScheduleId, Assert.Single(supplies.Items).DeliveryScheduleId);

        await using var verify = await Db();
        Assert.Single(await verify.MrpRuns.ToListAsync());
        Assert.Equal(8m, (await verify.MrpRunItemSummaries.SingleAsync(x =>
            x.RunId == fixture.RunId && x.ItemId == fixture.PartId)).TotalNetRequirement);
        Assert.Single(await verify.WhatIfForecastOverrides.ToListAsync());
        Assert.Single(await verify.WhatIfInventoryOverrides.ToListAsync());
        Assert.Single(await verify.WhatIfFirmSupplyOverrides.ToListAsync());
        Assert.Empty(await verify.PurchaseOrders.ToListAsync());
        Assert.Empty(await verify.InventoryTransactions.ToListAsync());
        var directMutation = await Assert.ThrowsAsync<PostgresException>(() => verify.Database.ExecuteSqlAsync(
            $"UPDATE planning.what_if_scenarios SET \"Name\" = 'TAMPERED' WHERE \"Id\" = {scenario.Id}"));
        Assert.Equal("23514", directMutation.SqlState);
        Assert.Contains(_factory.AuditSink.Snapshot(), x => x.EntityType == "WhatIfScenario" &&
            x.Action == "Calculated");
    }

    [Fact]
    public async Task Failed_collaboration_commands_leave_no_partial_state_or_success_audit()
    {
        var fixture = await SeedCompletedRun();
        _factory.AuditSink.Clear();
        var invalid = await _client.PostAsJsonAsync("/api/material-planning/what-if-scenarios",
            new CreateWhatIfScenarioRequest("سناریوی نامعتبر", fixture.RunId, [],
                [new(fixture.PartId, -1m)], []));
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);

        await using var verify = await Db();
        Assert.Empty(await verify.WhatIfScenarios.ToListAsync());
        Assert.Empty(await verify.WhatIfItemSummaries.ToListAsync());
        Assert.DoesNotContain(_factory.AuditSink.Snapshot(), x => x.EntityType == "WhatIfScenario");
    }

    private async Task<ScenarioFixture> SeedCompletedRun(bool includeException = false)
    {
        var asOf = new DateOnly(2026, 8, 24);
        var demandDate = new DateOnly(2026, 8, 25);
        var run = new MrpRun("MRP-RUN-980001", asOf, new DateOnly(2026, 8, 26),
            "MRP-10-v1", "test", DateTime.UtcNow.AddSeconds(-2));
        var productId = Guid.NewGuid(); var partId = Guid.NewGuid(); var unitId = Guid.NewGuid();
        var periodId = Guid.NewGuid(); var seriesId = Guid.NewGuid(); var revisionId = Guid.NewGuid();
        var scheduleId = Guid.NewGuid();

        await using var db = await Db();
        db.MrpRuns.Add(run);
        db.MrpRunItemInputs.AddRange(
            Item(run.Id, productId, "PRD-9801", ItemTypes.Product, unitId, null),
            Item(run.Id, partId, "PRT-9801", ItemTypes.Part, unitId, SupplyMethods.Buy));
        db.MrpRunForecastPeriodInputs.Add(new(run.Id, productId, periodId, seriesId, revisionId,
            10m, 0m, 10m, unitId, demandDate, demandDate, demandDate, "ALLOCATED", null));
        db.MrpRunForecastInputs.Add(new(run.Id, productId, periodId, seriesId, revisionId,
            demandDate, 10m, 10m, 0m, unitId));
        db.MrpRunBomInputs.Add(new(run.Id, Guid.NewGuid(), "BOM-9801", Guid.NewGuid(),
            productId, partId, unitId, unitId, 1m, 1m, null, demandDate, null));
        db.MrpRunInventoryInputs.Add(new(run.Id, partId, Guid.NewGuid(), "WH-9801", true, unitId, 0m));
        db.MrpRunFirmSupplyInputs.Add(new(run.Id, partId, Guid.NewGuid(), "PO-9801", Guid.NewGuid(),
            scheduleId, 2m, demandDate, "ORIGINAL", "ON_TIME", "ORDERED", Guid.NewGuid(),
            "SUP-9801", "تأمین‌کننده", "Supplier"));
        db.MrpRunRouteAvailabilityInputs.Add(new(run.Id, partId, demandDate, false, true));
        db.MrpRunItemSummaries.AddRange(
            Summary(run.Id, productId, 0m, 10m, 0m, 10m),
            Summary(run.Id, partId, 0m, 10m, 2m, 8m));
        if (includeException)
            db.MrpRunExceptions.Add(new MrpRunException(run.Id, partId, demandDate,
                "TEST_PLANNER_EXCEPTION", MrpExceptionSeverities.Warning, true,
                "Immutable calculation exception", "BOM-TEST"));
        await db.SaveChangesAsync();
        run.MarkSnapshotCaptured(DateTime.UtcNow.AddSeconds(-1));
        run.Complete(MrpCompleteness.Complete, false, DateTime.UtcNow);
        await db.SaveChangesAsync();
        return new(run.Id, productId, partId, periodId, scheduleId, demandDate);
    }

    private static MrpRunItemInput Item(Guid runId, Guid itemId, string code, string type,
        Guid unitId, string? supplyMethod) => new(runId, itemId, code, type, $"نام {code}",
        $"Name {code}", true, unitId, "EA", "عدد", "Each", supplyMethod, 0m, 0m,
        Guid.NewGuid(), "CAT", "گروه", "Category", 3, 5, 10);

    private static MrpRunItemSummary Summary(Guid runId, Guid itemId, decimal onHand,
        decimal gross, decimal firm, decimal net) => new(runId, itemId, onHand, gross, firm, net,
        0m, 0m, null, null, MrpCoverageStates.NoFutureDemand, null, null,
        MrpCoverageStates.NoFutureDemand, null, null, MrpCompleteness.Complete);

    private async Task<HadronDbContext> Db()
    {
        var scope = _factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IDbContextFactory<HadronDbContext>>()
            .CreateDbContextAsync();
    }

    private static Task Truncate(HadronDbContext db) => db.Database.ExecuteSqlRawAsync("""
        TRUNCATE planning.what_if_exceptions, planning.what_if_item_summaries,
          planning.what_if_daily_results, planning.what_if_firm_supply_overrides,
          planning.what_if_inventory_overrides, planning.what_if_forecast_overrides,
          planning.what_if_scenarios, planning.planner_work_item_events,
          planning.planner_work_items, planning.planner_saved_views,
          planning.mrp_run_demand_traces, planning.mrp_run_daily_results,
          planning.mrp_run_item_summaries, planning.mrp_run_exceptions,
          planning.mrp_run_external_source_inputs, planning.mrp_run_forecast_period_inputs,
          planning.mrp_run_route_decisions, planning.mrp_run_route_availability_inputs,
          planning.mrp_run_firm_supply_inputs, planning.mrp_run_inventory_inputs,
          planning.mrp_run_bom_inputs, planning.mrp_run_forecast_inputs,
          planning.mrp_run_item_inputs, planning.mrp_runs RESTART IDENTITY CASCADE
        """);

    private sealed record ScenarioFixture(Guid RunId, Guid ProductId, Guid PartId,
        Guid PeriodId, Guid ScheduleId, DateOnly DemandDate);
}
