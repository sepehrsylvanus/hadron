using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Hadron.Erp.Api.Infrastructure;
using Hadron.Erp.Api.Modules.Catalog.Domain;
using Hadron.Erp.Api.Modules.Mrp.Domain;
using Hadron.Erp.Api.Modules.OrderPlanning.Application;
using Hadron.Erp.Api.Modules.OrderPlanning.Domain;
using Hadron.Erp.Api.Modules.ReferenceData.Domain;
using Hadron.Erp.Api.Modules.Sourcing.Application;
using Hadron.Erp.Api.Modules.Sourcing.Domain;
using Hadron.Erp.Api.Modules.Suppliers.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Hadron.Erp.IntegrationTests;

[Collection("ReferenceData")]
public sealed class OrderPlanningTests : IClassFixture<FoundationApiFactory>, IAsyncLifetime
{
    private readonly FoundationApiFactory _factory;
    private readonly HttpClient _client;

    public OrderPlanningTests(FoundationApiFactory factory)
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
            TRUNCATE planning.order_recommendation_decisions,
              planning.order_recommendation_candidates, planning.order_recommendations,
              planning.order_planning_generations, planning.mrp_run_resolved_route_results,
              planning.mrp_run_daily_results, planning.mrp_run_item_inputs,
              planning.mrp_runs RESTART IDENTITY CASCADE
            """);
        await db.Database.ExecuteSqlRawAsync("""
            TRUNCATE procurement.sourcing_prices, procurement.sourcing_options,
              procurement.supplier_contacts, procurement.supplier_payment_profiles,
              procurement.suppliers, organization.internal_people,
              catalog.material_item_profiles, catalog.products, catalog.items,
              reference_data.reference_values, reference_data.currencies,
              reference_data.units RESTART IDENTITY CASCADE
            """);
        _factory.AuditSink.Clear();
        _factory.PlanningCalendar.TodayOverride = null;
        _factory.OrderPlanningFaultInjector.Reset();
    }

    public async Task DisposeAsync()
    {
        await using var db = await Db();
        await db.Database.ExecuteSqlRawAsync("""
            TRUNCATE planning.order_recommendation_decisions,
              planning.order_recommendation_candidates, planning.order_recommendations,
              planning.order_planning_generations, planning.mrp_run_resolved_route_results,
              planning.mrp_run_daily_results, planning.mrp_run_item_inputs,
              planning.mrp_runs RESTART IDENTITY CASCADE
            """);
    }

    [Fact]
    public async Task Legacy_route_context_blocks_generation_without_live_inference_and_refresh_is_immutable()
    {
        var fixture = await CompletedRun(includeRoute: false);

        using var firstResponse = await _client.PostAsJsonAsync("/api/order-planning/generations",
            new GenerateOrderPlanningRequest(fixture.RunId));
        Assert.Equal(HttpStatusCode.Created, firstResponse.StatusCode);
        var first = await firstResponse.Content.ReadFromJsonAsync<OrderPlanningGenerationResponse>();
        Assert.NotNull(first);
        Assert.Equal(1, first.GenerationSequence);
        Assert.True(first.IsCurrent);
        Assert.Equal(OrderPlanningGenerationStatuses.Completed, first.Status);

        var firstRecommendation = await GetOnlyRecommendation(fixture.RunId, first.Id);
        Assert.Equal(RecommendationStates.Blocked, firstRecommendation.State);
        Assert.Contains("LEGACY_RUN_ROUTE_CONTEXT_INCOMPLETE", firstRecommendation.ReasonCodes);
        Assert.Equal("LEGACY", firstRecommendation.RouteOrigin);

        using var secondResponse = await _client.PostAsJsonAsync("/api/order-planning/generations",
            new GenerateOrderPlanningRequest(fixture.RunId));
        Assert.Equal(HttpStatusCode.Created, secondResponse.StatusCode);
        var second = await secondResponse.Content.ReadFromJsonAsync<OrderPlanningGenerationResponse>();
        Assert.NotNull(second);
        Assert.Equal(2, second.GenerationSequence);
        Assert.True(second.IsCurrent);

        await using var db = await Db();
        var generations = await db.OrderPlanningGenerations.AsNoTracking()
            .Where(x => x.RunId == fixture.RunId).OrderBy(x => x.GenerationSequence).ToListAsync();
        Assert.False(generations[0].IsCurrent);
        Assert.True(generations[1].IsCurrent);
        Assert.Equal(2, await db.OrderRecommendations.CountAsync());
        Assert.Equal(0, await db.PurchaseOrders.CountAsync());
        Assert.Equal(0, await db.InventoryTransactions.CountAsync());

        await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE planning.order_recommendations SET \"ItemCode\" = 'CMP-TAMPER' WHERE \"Id\" = {firstRecommendation.Id}"));
    }

    [Fact]
    public async Task Incomplete_route_is_explicitly_blocked_and_route_and_machine_snapshots_are_db_immutable()
    {
        var fixture = await CompletedRun(includeRoute: true, resolvedRoute: MrpResolvedPlanningRoutes.Incomplete,
            routeOrigin: null, reason: "HYBRID_ROUTE_DECISION_REQUIRED");

        using var response = await _client.PostAsJsonAsync("/api/order-planning/generations",
            new GenerateOrderPlanningRequest(fixture.RunId));
        response.EnsureSuccessStatusCode();
        var generation = await response.Content.ReadFromJsonAsync<OrderPlanningGenerationResponse>();
        var recommendation = await GetOnlyRecommendation(fixture.RunId, generation!.Id);
        Assert.Equal(RecommendationStates.Blocked, recommendation.State);
        Assert.Equal("INCOMPLETE", recommendation.RouteOrigin);
        Assert.Contains("HYBRID_ROUTE_DECISION_REQUIRED", recommendation.ReasonCodes);

        await using var db = await Db();
        await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE planning.mrp_run_resolved_route_results SET \"RouteOrigin\" = 'BUY' WHERE \"RunId\" = {fixture.RunId}"));
        await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM planning.order_recommendations WHERE \"Id\" = {recommendation.Id}"));
    }

    [Fact]
    public async Task Candidate_lineage_and_decision_sequence_are_enforced_by_postgresql()
    {
        var fixture = await CompletedRun(includeDailyNeed: false);
        await using var db = await Db();
        var generation = new OrderPlanningGeneration(fixture.RunId, 1, fixture.AsOfDate, DateTime.UtcNow, "db-attack");
        db.Add(generation); await db.SaveChangesAsync();

        var nonexistent = ReadySnapshot(generation.Id, fixture.ItemId, "CMP-MISSING", Guid.NewGuid(), 1, 1);
        db.Add(nonexistent);
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        db.ChangeTracker.Clear();

        var wrongItemCandidate = CandidateSnapshot(generation.Id, Guid.NewGuid(), "SRC-WRONG", "CNY", 1);
        db.Add(wrongItemCandidate); await db.SaveChangesAsync();
        var bad = ReadySnapshot(generation.Id, fixture.ItemId, "CMP-LINEAGE", wrongItemCandidate.Id, 1, 1);
        db.Add(bad);
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        db.ChangeTracker.Clear();

        var otherGeneration = new OrderPlanningGeneration(fixture.RunId, 2, fixture.AsOfDate, DateTime.UtcNow, "db-attack");
        db.Add(otherGeneration); await db.SaveChangesAsync();
        var otherGenerationCandidate = CandidateSnapshot(otherGeneration.Id, fixture.ItemId, "SRC-OTHER-GEN", "CNY", 1);
        db.Add(otherGenerationCandidate); await db.SaveChangesAsync();
        var crossGeneration = ReadySnapshot(generation.Id, fixture.ItemId, "CMP-CROSS-GEN", otherGenerationCandidate.Id, 1, 1);
        db.Add(crossGeneration);
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        db.ChangeTracker.Clear();

        var validCandidate = CandidateSnapshot(generation.Id, fixture.ItemId, "SRC-VALID", "CNY", 1);
        db.Add(validCandidate); await db.SaveChangesAsync();
        var valid = ReadySnapshot(generation.Id, fixture.ItemId, "CMP-VALID", validCandidate.Id, 1, 1);
        db.Add(valid); await db.SaveChangesAsync();

        await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO planning.order_recommendation_decisions
            ("Id","RecommendationId","DecisionVersion","DecisionType","Reason","DecisionDate","DecidedAtUtc","WarningCodes")
            VALUES ({Guid.NewGuid()},{valid.Id},{2L},'REJECT','db-attack',{fixture.AsOfDate},{DateTime.UtcNow},'')
            """));
        await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO planning.order_recommendation_decisions
            ("Id","RecommendationId","DecisionVersion","DecisionType","Reason","DecisionDate","DecidedAtUtc","WarningCodes")
            VALUES ({Guid.NewGuid()},{valid.Id},{999L},'REJECT','db-attack',{fixture.AsOfDate},{DateTime.UtcNow},'')
            """));

        var decisionId = Guid.NewGuid();
        await using (var tx = await db.Database.BeginTransactionAsync())
        {
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE planning.order_recommendations SET \"DecisionVersion\" = 1 WHERE \"Id\" = {valid.Id}");
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO planning.order_recommendation_decisions
                ("Id","RecommendationId","DecisionVersion","DecisionType","Reason","DecisionDate","DecidedAtUtc","WarningCodes")
                VALUES ({decisionId},{valid.Id},{1L},'REJECT','db-valid',{fixture.AsOfDate},{DateTime.UtcNow},'')
                """);
            await tx.CommitAsync();
        }
        Assert.Equal(1, await db.OrderRecommendationDecisions.CountAsync(x => x.RecommendationId == valid.Id));
        Assert.Equal(1, await db.OrderRecommendations.Where(x => x.Id == valid.Id).Select(x => x.DecisionVersion).SingleAsync());
        await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO planning.order_recommendation_decisions
            ("Id","RecommendationId","DecisionVersion","DecisionType","Reason","DecisionDate","DecidedAtUtc","WarningCodes")
            VALUES ({Guid.NewGuid()},{valid.Id},{3L},'REJECT','db-attack',{fixture.AsOfDate},{DateTime.UtcNow},'')
            """));
        await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE planning.order_recommendation_decisions SET \"Reason\" = 'tamper' WHERE \"Id\" = {decisionId}"));
        await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM planning.order_recommendation_decisions WHERE \"Id\" = {decisionId}"));
    }

    [Fact]
    public async Task Concurrent_generation_and_decision_commands_are_serialized_without_partial_rows()
    {
        var fixture = await CompletedRun(includeRoute: false);
        var clients = Enumerable.Range(0, 5).Select(_ => _factory.CreateClient()).ToArray();
        var generations = await Task.WhenAll(clients.Select(client => client.PostAsJsonAsync(
            "/api/order-planning/generations", new GenerateOrderPlanningRequest(fixture.RunId))));
        Assert.All(generations, response => Assert.Equal(HttpStatusCode.Created, response.StatusCode));
        var created = await Task.WhenAll(generations.Select(x => x.Content.ReadFromJsonAsync<OrderPlanningGenerationResponse>()));
        Assert.Equal([1, 2, 3, 4, 5], created.Select(x => x!.GenerationSequence).Order().ToArray());
        await using var stateDb = await Db();
        var currentId = await stateDb.OrderPlanningGenerations.Where(x => x.RunId == fixture.RunId && x.IsCurrent)
            .Select(x => x.Id).SingleAsync();
        var current = created.Single(x => x!.Id == currentId)!;
        Assert.Equal(5, current.GenerationSequence);
        var recommendation = await GetOnlyRecommendation(fixture.RunId, current.Id);
        var decisions = await Task.WhenAll(
            _client.PostAsJsonAsync($"/api/order-planning/recommendations/{recommendation.Id}/decisions", new SaveExpertDecisionRequest("REJECT", 0, "A", null, null, null, null)),
            _client.PostAsJsonAsync($"/api/order-planning/recommendations/{recommendation.Id}/decisions", new SaveExpertDecisionRequest("REJECT", 0, "B", null, null, null, null)));
        Assert.Equal(1, decisions.Count(x => x.StatusCode == HttpStatusCode.OK));
        Assert.Equal(1, decisions.Count(x => x.StatusCode == HttpStatusCode.Conflict));
        await using var db = await Db();
        Assert.Equal(1, await db.OrderRecommendationDecisions.CountAsync(x => x.RecommendationId == recommendation.Id));
        foreach (var response in generations) response.Dispose();
        foreach (var client in clients) client.Dispose();
    }

    [Fact]
    public async Task Completion_order_cannot_replace_the_higher_completed_generation()
    {
        var fixture = await CompletedRun(includeRoute: false);
        var paused = _factory.OrderPlanningFaultInjector.PauseNext();
        using var firstClient = _factory.CreateClient();
        using var secondClient = _factory.CreateClient();
        var firstTask = firstClient.PostAsJsonAsync("/api/order-planning/generations",
            new GenerateOrderPlanningRequest(fixture.RunId));
        await paused.WaitAsync(TimeSpan.FromSeconds(15));

        using var secondResponse = await secondClient.PostAsJsonAsync("/api/order-planning/generations",
            new GenerateOrderPlanningRequest(fixture.RunId));
        Assert.Equal(HttpStatusCode.Created, secondResponse.StatusCode);
        var second = await secondResponse.Content.ReadFromJsonAsync<OrderPlanningGenerationResponse>();
        Assert.Equal(2, second!.GenerationSequence);
        Assert.True(second.IsCurrent);

        _factory.OrderPlanningFaultInjector.ReleasePaused();
        using var firstResponse = await firstTask;
        Assert.Equal(HttpStatusCode.Created, firstResponse.StatusCode);
        var first = await firstResponse.Content.ReadFromJsonAsync<OrderPlanningGenerationResponse>();
        Assert.Equal(1, first!.GenerationSequence);
        Assert.False(first.IsCurrent);

        await using var db = await Db();
        var history = await db.OrderPlanningGenerations.AsNoTracking().Where(x => x.RunId == fixture.RunId)
            .OrderBy(x => x.GenerationSequence).ToListAsync();
        Assert.Equal([1, 2], history.Select(x => x.GenerationSequence).ToArray());
        Assert.False(history[0].IsCurrent);
        Assert.True(history[1].IsCurrent);
        Assert.Equal(OrderPlanningGenerationStatuses.Completed, history[0].Status);
        Assert.Equal(OrderPlanningGenerationStatuses.Completed, history[1].Status);
    }

    [Fact]
    public async Task Generation_sequence_unique_constraint_rejects_direct_database_duplicates()
    {
        var fixture = await CompletedRun(includeRoute: false);
        using var response = await _client.PostAsJsonAsync("/api/order-planning/generations",
            new GenerateOrderPlanningRequest(fixture.RunId));
        response.EnsureSuccessStatusCode();
        var generation = await response.Content.ReadFromJsonAsync<OrderPlanningGenerationResponse>();

        await using var db = await Db();
        var exception = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO planning.order_planning_generations
              ("Id","RunId","GenerationSequence","EvaluationDate","GeneratedAtUtc","Status","IsCurrent")
            VALUES ({Guid.NewGuid()},{fixture.RunId},{generation!.GenerationSequence},{fixture.AsOfDate},
              {DateTime.UtcNow},'RUNNING',false)
            """));
        Assert.Equal(PostgresErrorCodes.UniqueViolation, exception.SqlState);
    }

    [Fact]
    public async Task Failed_middle_generation_has_no_official_rows_and_does_not_replace_current()
    {
        var fixture = await CompletedRun(includeRoute: false);
        var firstResponse = await _client.PostAsJsonAsync("/api/order-planning/generations", new GenerateOrderPlanningRequest(fixture.RunId));
        var first = await firstResponse.Content.ReadFromJsonAsync<OrderPlanningGenerationResponse>();
        _factory.OrderPlanningFaultInjector.FailAfterCandidateCapture = true;
        var failedResponse = await _client.PostAsJsonAsync("/api/order-planning/generations", new GenerateOrderPlanningRequest(fixture.RunId));
        Assert.Equal(HttpStatusCode.InternalServerError, failedResponse.StatusCode);
        _factory.OrderPlanningFaultInjector.FailAfterCandidateCapture = false;
        var thirdResponse = await _client.PostAsJsonAsync("/api/order-planning/generations", new GenerateOrderPlanningRequest(fixture.RunId));
        var third = await thirdResponse.Content.ReadFromJsonAsync<OrderPlanningGenerationResponse>();
        await using var db = await Db();
        var history = await db.OrderPlanningGenerations.OrderBy(x => x.GenerationSequence).ToListAsync();
        Assert.Equal([1, 2, 3], history.Select(x => x.GenerationSequence).ToArray());
        Assert.Equal(["COMPLETED", "FAILED", "COMPLETED"], history.Select(x => x.Status).ToArray());
        Assert.True(history[2].IsCurrent); Assert.False(history[0].IsCurrent); Assert.False(history[1].IsCurrent);
        Assert.Equal(0, await db.OrderRecommendations.CountAsync(x => x.GenerationId == history[1].Id));
        Assert.True(await db.OrderRecommendations.AnyAsync(x => x.GenerationId == history[0].Id));
        Assert.True(await db.OrderRecommendations.AnyAsync(x => x.GenerationId == history[2].Id));
        Assert.Equal(first!.RecommendationCount + third!.RecommendationCount, await db.OrderRecommendations.CountAsync());
    }

    [Fact]
    public async Task Reject_and_defer_are_append_only_and_stale_decisions_return_structured_conflict()
    {
        var fixture = await CompletedRun(includeRoute: false);
        using var generationResponse = await _client.PostAsJsonAsync("/api/order-planning/generations",
            new GenerateOrderPlanningRequest(fixture.RunId));
        var generation = await generationResponse.Content.ReadFromJsonAsync<OrderPlanningGenerationResponse>();
        var recommendation = await GetOnlyRecommendation(fixture.RunId, generation!.Id);

        using var rejected = await _client.PostAsJsonAsync($"/api/order-planning/recommendations/{recommendation.Id}/decisions",
            new SaveExpertDecisionRequest(ExpertDecisionTypes.Reject, 0, "No business approval", null, null, null, null));
        Assert.Equal(HttpStatusCode.OK, rejected.StatusCode);
        var afterReject = await rejected.Content.ReadFromJsonAsync<OrderRecommendationResponse>();
        Assert.Equal(1, afterReject!.DecisionVersion);
        Assert.Equal(ExpertDecisionTypes.Reject, afterReject.Decisions.Single().DecisionType);

        using var stale = await _client.PostAsJsonAsync($"/api/order-planning/recommendations/{recommendation.Id}/decisions",
            new SaveExpertDecisionRequest(ExpertDecisionTypes.Defer, 0, "Review later", fixture.NeedByDate, null, null, null));
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        var problem = await stale.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("STALE_VERSION", problem.GetProperty("code").GetString());

        using var deferred = await _client.PostAsJsonAsync($"/api/order-planning/recommendations/{recommendation.Id}/decisions",
            new SaveExpertDecisionRequest(ExpertDecisionTypes.Defer, 1, "Review later", fixture.NeedByDate, null, null, null));
        deferred.EnsureSuccessStatusCode();
        var afterDefer = await deferred.Content.ReadFromJsonAsync<OrderRecommendationResponse>();
        Assert.Equal(2, afterDefer!.DecisionVersion);
        Assert.Equal([2L, 1L], afterDefer.Decisions.Select(x => x.DecisionVersion).ToArray());

        await using var db = await Db();
        Assert.Equal(2, await db.OrderRecommendationDecisions.CountAsync());
        await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE planning.order_recommendation_decisions SET \"Reason\" = 'tamper' WHERE \"RecommendationId\" = {recommendation.Id}"));
    }

    [Fact]
    public async Task Recommendation_worklist_uses_real_server_paging_beyond_one_hundred_without_operational_side_effects()
    {
        var fixture = await CompletedRun(includeDailyNeed: false);
        await using (var db = await Db())
        {
            var generation = new OrderPlanningGeneration(fixture.RunId, 1, fixture.AsOfDate,
                DateTime.UtcNow, "integration-test");
            db.OrderPlanningGenerations.Add(generation);
            await db.SaveChangesAsync();
            db.OrderRecommendations.AddRange(Enumerable.Range(1, 125).Select(index =>
                new OrderRecommendation(generation.Id, Guid.NewGuid(), $"CMP-{index:0000}", "COMPONENT",
                    $"ماده {index}", $"Material {index}", fixture.UnitId, "KG", fixture.NeedByDate,
                    MrpRouteOrigins.Buy, 1m, 0m, 1, 2, 3, RecommendationStates.Blocked,
                    RecommendationTimingStates.OnTime, null, null, null, null, null, null, null,
                    null, null, RecommendationNextActions.ResolveData, null, null,
                    "NO_READY_SOURCE", "NO_ELIGIBLE_MACHINE_SELECTION", "{}", "MRP-12-v1")));
            await db.SaveChangesAsync();
            generation.Complete(DateTime.UtcNow);
            await db.SaveChangesAsync();
        }

        using var firstPageResponse = await _client.GetAsync(
            $"/api/order-planning/recommendations?runId={fixture.RunId}&page=1&pageSize=100");
        firstPageResponse.EnsureSuccessStatusCode();
        var firstPage = await firstPageResponse.Content.ReadFromJsonAsync<OrderRecommendationListResponse>();
        Assert.Equal(125, firstPage!.TotalCount);
        Assert.Equal(100, firstPage.Items.Count);

        using var secondPageResponse = await _client.GetAsync(
            $"/api/order-planning/recommendations?runId={fixture.RunId}&page=2&pageSize=100");
        secondPageResponse.EnsureSuccessStatusCode();
        var secondPage = await secondPageResponse.Content.ReadFromJsonAsync<OrderRecommendationListResponse>();
        Assert.Equal(25, secondPage!.Items.Count);
        Assert.DoesNotContain(secondPage.Items, item => firstPage.Items.Any(first => first.Id == item.Id));

        await using var verify = await Db();
        Assert.Equal(0, await verify.PurchaseOrders.CountAsync());
        Assert.Equal(0, await verify.InventoryTransactions.CountAsync());
    }

    [Fact]
    public async Task Zero_external_need_creates_a_completed_generation_without_a_recommendation_row()
    {
        var fixture = await CompletedRun(includeRoute: true, includeDailyNeed: false);
        using var response = await _client.PostAsJsonAsync("/api/order-planning/generations",
            new GenerateOrderPlanningRequest(fixture.RunId));
        response.EnsureSuccessStatusCode();
        var generation = await response.Content.ReadFromJsonAsync<OrderPlanningGenerationResponse>();
        Assert.Equal(OrderPlanningGenerationStatuses.Completed, generation!.Status);
        Assert.Equal(0, generation.RecommendationCount);

        await using var db = await Db();
        Assert.Equal(0, await db.OrderRecommendations.CountAsync());
        Assert.Equal(0, await db.PurchaseOrders.CountAsync());
        Assert.Equal(0, await db.InventoryTransactions.CountAsync());
    }

    [Theory]
    [InlineData(MrpResolvedPlanningRoutes.External, MrpRouteOrigins.Buy, 1, false)]
    [InlineData(MrpResolvedPlanningRoutes.External, MrpRouteOrigins.OutsourceExternal, 1, false)]
    [InlineData(MrpResolvedPlanningRoutes.External, MrpRouteOrigins.HybridExternal, 1, false)]
    [InlineData(MrpResolvedPlanningRoutes.InternalBom, MrpRouteOrigins.Make, 0, false)]
    [InlineData(MrpResolvedPlanningRoutes.InternalBom, MrpRouteOrigins.OutsourceBom, 0, false)]
    [InlineData(MrpResolvedPlanningRoutes.InternalBom, MrpRouteOrigins.HybridInternal, 0, false)]
    [InlineData(MrpResolvedPlanningRoutes.Incomplete, null, 1, true)]
    public async Task Order_planning_consumes_every_persisted_route_without_live_reinference(
        string resolvedRoute, string? routeOrigin, int expectedRecommendations, bool expectedBlocked)
    {
        var fixture = await CompletedRun(includeRoute: true, resolvedRoute: resolvedRoute,
            routeOrigin: routeOrigin, reason: expectedBlocked ? "ROUTE_INCOMPLETE_TEST" : null);
        using var response = await _client.PostAsJsonAsync("/api/order-planning/generations",
            new GenerateOrderPlanningRequest(fixture.RunId));
        response.EnsureSuccessStatusCode();
        var generation = await response.Content.ReadFromJsonAsync<OrderPlanningGenerationResponse>();
        var list = await _client.GetFromJsonAsync<OrderRecommendationListResponse>(
            $"/api/order-planning/recommendations?runId={fixture.RunId}&generationId={generation!.Id}&page=1&pageSize=25");
        Assert.Equal(expectedRecommendations, list!.TotalCount);
        if (expectedRecommendations == 1)
        {
            var listItem = Assert.Single(list.Items);
            var recommendation = await _client.GetFromJsonAsync<OrderRecommendationResponse>(
                $"/api/order-planning/recommendations/{listItem.Id}");
            if (expectedBlocked) Assert.Equal(RecommendationStates.Blocked, recommendation!.State);
            Assert.Equal(routeOrigin ?? "INCOMPLETE", recommendation!.RouteOrigin);
        }
    }

    [Fact]
    public async Task Ready_generation_preserves_golden_commercial_snapshot_and_expert_revalidation_without_operational_side_effects()
    {
        var today = TehranToday();
        var source = await ReadySource(today);
        var fixture = await CompletedRunWithReadySource(source, today);

        using var firstGenerationResponse = await _client.PostAsJsonAsync("/api/order-planning/generations",
            new GenerateOrderPlanningRequest(fixture.RunId));
        firstGenerationResponse.EnsureSuccessStatusCode();
        var firstGeneration = await firstGenerationResponse.Content.ReadFromJsonAsync<OrderPlanningGenerationResponse>();
        var recommendation = await GetOnlyRecommendation(fixture.RunId, firstGeneration!.Id);

        Assert.Equal(today, recommendation.EvaluationDate);
        Assert.Equal(fixture.NeedByDate, recommendation.NeedByDate);
        Assert.Equal(fixture.NeedByDate.AddDays(-3), recommendation.OrderByDate);
        Assert.Equal(fixture.NeedByDate.AddDays(2), recommendation.DecisionHorizonDate);
        Assert.Equal(-55m, recommendation.MinimumBaseline);
        Assert.Equal(55m, recommendation.BaseRecommendedQty);
        Assert.Equal(80m, recommendation.SystemRecommendedQty);
        Assert.Equal(560m, recommendation.EstimatedValue);
        Assert.Equal("CNY", recommendation.SelectedCandidate!.CurrencyCode);
        Assert.Equal(7m, recommendation.SelectedCandidate.UnitPrice);
        Assert.Equal(RecommendationPriorities.Upcoming, recommendation.Priority);
        Assert.Equal(RecommendationNextActions.OrderByDate, recommendation.NextAction);
        Assert.Equal("PREFERRED_ELIGIBLE_ON_TIME", recommendation.SourceSelectionReason);
        Assert.Contains("MOQ_APPLIED", recommendation.ReasonCodes);
        Assert.Contains("RouteOrigin", recommendation.CalculationTrace);

        using var acceptedResponse = await _client.PostAsJsonAsync(
            $"/api/order-planning/recommendations/{recommendation.Id}/decisions",
            new SaveExpertDecisionRequest(ExpertDecisionTypes.Accept, 0, null, null, null, null, null));
        acceptedResponse.EnsureSuccessStatusCode();
        var accepted = await acceptedResponse.Content.ReadFromJsonAsync<OrderRecommendationResponse>();
        Assert.Equal(1, accepted!.DecisionVersion);
        Assert.Equal(7m, accepted.Decisions.Single().FinalUnitPrice);
        var acceptedSource = accepted.Decisions.Single().FinalSourceSnapshot;
        Assert.NotNull(acceptedSource);
        using (var snapshot = JsonDocument.Parse(acceptedSource))
        {
            var root = snapshot.RootElement;
            Assert.Equal(source.OptionId, root.GetProperty("SourcingOptionId").GetGuid());
            Assert.Equal("SUP-9001", root.GetProperty("SupplierNumber").GetString());
            Assert.Equal("SEA", root.GetProperty("ShippingModeCode").GetString());
            Assert.Equal(1, root.GetProperty("SupplierLeadTimeDays").GetInt32());
            Assert.Equal(0, root.GetProperty("ForwarderToShipmentDays").GetInt32());
            Assert.Equal(1, root.GetProperty("ShippingPeriodDays").GetInt32());
            Assert.Equal(1, root.GetProperty("DestinationCustomsDays").GetInt32());
            Assert.Equal(3, root.GetProperty("TotalLeadTimeDays").GetInt32());
            Assert.Equal(7m, root.GetProperty("UnitPrice").GetDecimal());
            Assert.Equal("CNY", root.GetProperty("CurrencyCode").GetString());
            Assert.Equal("CASH", root.GetProperty("PaymentTermCode").GetString());
            Assert.Equal(100m, root.GetProperty("AdvancePercent").GetDecimal());
        }

        await using (var change = await Db())
        {
            var priorPrice = await change.SourcingPrices.SingleAsync(x => x.Id == source.PriceId);
            priorPrice.Close(today.AddDays(-1));
            change.SourcingPrices.Add(new SourcingPrice(source.OptionId, 8, priorPrice.CurrencyId, today, null));
            await change.SaveChangesAsync();
        }

        using var staleSource = await _client.PostAsJsonAsync(
            $"/api/order-planning/recommendations/{recommendation.Id}/decisions",
            new SaveExpertDecisionRequest(ExpertDecisionTypes.Accept, 1, null, null, null, null, null));
        Assert.Equal(HttpStatusCode.Conflict, staleSource.StatusCode);
        var sourceProblem = await staleSource.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("SOURCE_REVALIDATION_REQUIRED", sourceProblem.GetProperty("code").GetString());

        var historical = await _client.GetFromJsonAsync<OrderRecommendationResponse>(
            $"/api/order-planning/recommendations/{recommendation.Id}");
        Assert.Equal(7m, historical!.SelectedCandidate!.UnitPrice);
        Assert.Equal(560m, historical.EstimatedValue);
        Assert.Equal(1, historical.DecisionVersion);

        using var refreshResponse = await _client.PostAsJsonAsync("/api/order-planning/generations",
            new GenerateOrderPlanningRequest(fixture.RunId));
        refreshResponse.EnsureSuccessStatusCode();
        var refresh = await refreshResponse.Content.ReadFromJsonAsync<OrderPlanningGenerationResponse>();
        var refreshedRecommendation = await GetOnlyRecommendation(fixture.RunId, refresh!.Id);
        Assert.Equal(2, refresh.GenerationSequence);
        Assert.Equal(8m, refreshedRecommendation.SelectedCandidate!.UnitPrice);
        Assert.Equal(640m, refreshedRecommendation.EstimatedValue);

        Guid alternateOptionId;
        await using (var alternateDb = await Db())
        {
            var original = await alternateDb.SourcingOptions.AsNoTracking().SingleAsync(x => x.Id == source.OptionId);
            var currencyId = await alternateDb.Currencies.Where(x => x.Code == "CNY").Select(x => x.Id).SingleAsync();
            var alternate = new SourcingOption("SRC-9002", original.ItemId, original.SupplierId,
                original.ItemBaseUnitId, original.MinimumOrderQuantity, original.ShippingModeId, original.IncotermId,
                original.SupplierLeadTimeDays, original.ForwarderToShipmentDays, original.ShippingPeriodDays,
                original.DestinationCustomsDays);
            alternateDb.Add(alternate);
            await alternateDb.SaveChangesAsync();
            alternateDb.Add(new SourcingPrice(alternate.Id, 8, currencyId, today, null));
            await alternateDb.SaveChangesAsync();
            alternateOptionId = alternate.Id;
        }

        using var outdatedModify = await _client.PostAsJsonAsync(
            $"/api/order-planning/recommendations/{recommendation.Id}/decisions",
            new SaveExpertDecisionRequest(ExpertDecisionTypes.Modify, 1, "Use explicit current source",
                null, source.OptionId, 80, fixture.NeedByDate));
        Assert.Equal(HttpStatusCode.Conflict, outdatedModify.StatusCode);
        var outdatedProblem = await outdatedModify.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("OUTDATED_RUN", outdatedProblem.GetProperty("code").GetString());

        foreach (var invalidQty in new[] { 0m, 79m })
        {
            using var invalidModify = await _client.PostAsJsonAsync(
                $"/api/order-planning/recommendations/{refreshedRecommendation.Id}/decisions",
                new SaveExpertDecisionRequest(ExpertDecisionTypes.Modify, 0, "Invalid quantity",
                    null, alternateOptionId, invalidQty, today.AddDays(3)));
            Assert.Equal(HttpStatusCode.BadRequest, invalidModify.StatusCode);
        }

        using var modifiedResponse = await _client.PostAsJsonAsync(
            $"/api/order-planning/recommendations/{refreshedRecommendation.Id}/decisions",
            new SaveExpertDecisionRequest(ExpertDecisionTypes.Modify, 0, "Expedite despite feasibility",
                null, alternateOptionId, 80, today.AddDays(1)));
        modifiedResponse.EnsureSuccessStatusCode();
        var modified = await modifiedResponse.Content.ReadFromJsonAsync<OrderRecommendationResponse>();
        Assert.Equal(8m, modified!.Decisions.Single().FinalUnitPrice);
        Assert.Contains("EXPERT_TARGET_INFEASIBLE", modified.Decisions.Single().WarningCodes);
        var immutableExpertSnapshot = modified.Decisions.Single().FinalSourceSnapshot;
        Assert.NotNull(immutableExpertSnapshot);

        await using (var drift = await Db())
        {
            var option = await drift.SourcingOptions.SingleAsync(x => x.Id == alternateOptionId);
            option.Edit(120, option.ShippingModeId, option.IncotermId, 2, 1, 2, 1);
            var supplier = await drift.Suppliers.Include(x => x.PaymentProfile).SingleAsync(x => x.Id == option.SupplierId);
            supplier.PaymentProfile.Set(supplier.PaymentProfile.PaymentTermId, 50, 50, 0, null, null);
            await drift.SaveChangesAsync();
        }
        var historicalDecision = await _client.GetFromJsonAsync<OrderRecommendationResponse>(
            $"/api/order-planning/recommendations/{refreshedRecommendation.Id}");
        Assert.Equal(immutableExpertSnapshot, historicalDecision!.Decisions.Single().FinalSourceSnapshot);

        await using var verify = await Db();
        Assert.Equal(0, await verify.PurchaseOrders.CountAsync());
        Assert.Equal(0, await verify.InventoryTransactions.CountAsync());
        Assert.Equal(2, await verify.OrderPlanningGenerations.CountAsync());
        Assert.Equal(2, await verify.OrderRecommendations.CountAsync());
    }

    [Fact]
    public async Task Generation_uses_controlled_server_Tehran_date_and_client_cannot_override_it()
    {
        var serverDay = new DateOnly(2026, 8, 25);
        _factory.PlanningCalendar.TodayOverride = serverDay;
        var source = await ReadySource(serverDay);
        var fixture = await CompletedRunWithReadySource(source, serverDay);

        using var response = await _client.PostAsJsonAsync("/api/order-planning/generations",
            new { runId = fixture.RunId, evaluationDate = serverDay.AddDays(-20) });
        response.EnsureSuccessStatusCode();
        var generation = await response.Content.ReadFromJsonAsync<OrderPlanningGenerationResponse>();
        Assert.Equal(serverDay, generation!.EvaluationDate);
        Assert.NotEqual(fixture.AsOfDate, generation.EvaluationDate);
        var recommendation = await GetOnlyRecommendation(fixture.RunId, generation.Id);
        Assert.Equal(serverDay, recommendation.EvaluationDate);
        Assert.Equal(fixture.NeedByDate, recommendation.NeedByDate);
        Assert.True(recommendation.SelectedCandidate!.IsEligible);
        Assert.Equal(7m, recommendation.SelectedCandidate.UnitPrice);
        Assert.Equal(serverDay.AddDays(-1), recommendation.SelectedCandidate.PriceEffectiveFrom);
    }

    [Fact]
    public async Task Newer_completed_run_blocks_accept_and_modify_without_mutating_historical_machine_truth()
    {
        var today = new DateOnly(2026, 8, 25);
        _factory.PlanningCalendar.TodayOverride = today;
        var source = await ReadySource(today);
        var oldRun = await CompletedRunWithReadySource(source, today);
        var generationResponse = await _client.PostAsJsonAsync("/api/order-planning/generations",
            new GenerateOrderPlanningRequest(oldRun.RunId));
        var generation = await generationResponse.Content.ReadFromJsonAsync<OrderPlanningGenerationResponse>();
        var recommendation = await GetOnlyRecommendation(oldRun.RunId, generation!.Id);
        var originalTrace = recommendation.CalculationTrace;
        var newerRun = await CompletedRun(includeDailyNeed: false);

        foreach (var request in new[]
        {
            new SaveExpertDecisionRequest(ExpertDecisionTypes.Accept, 0, null, null, null, null, null),
            new SaveExpertDecisionRequest(ExpertDecisionTypes.Modify, 0, "Different source", null,
                source.OptionId, 80, oldRun.NeedByDate)
        })
        {
            using var blocked = await _client.PostAsJsonAsync(
                $"/api/order-planning/recommendations/{recommendation.Id}/decisions", request);
            Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);
            var problem = await blocked.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal("OUTDATED_RUN", problem.GetProperty("code").GetString());
        }

        var historical = await _client.GetFromJsonAsync<OrderRecommendationResponse>(
            $"/api/order-planning/recommendations/{recommendation.Id}");
        Assert.True(historical!.IsOutdatedRun);
        Assert.Equal(originalTrace, historical.CalculationTrace);
        Assert.Equal(0, historical.DecisionVersion);
        Assert.Empty(historical.Decisions);
        Assert.NotEqual(oldRun.RunId, newerRun.RunId);
    }

    [Theory]
    [InlineData("SOURCE_ACTIVE")]
    [InlineData("READINESS")]
    [InlineData("PRICE")]
    [InlineData("PRICE_EXPIRY")]
    [InlineData("MOQ")]
    [InlineData("LEAD_TIME")]
    [InlineData("BASE_UNIT")]
    public async Task Accept_revalidates_each_material_source_drift_dimension(string driftKind)
    {
        var today = new DateOnly(2026, 8, 25);
        _factory.PlanningCalendar.TodayOverride = today;
        var source = await ReadySource(today);
        var fixture = await CompletedRunWithReadySource(source, today);
        var generationResponse = await _client.PostAsJsonAsync("/api/order-planning/generations",
            new GenerateOrderPlanningRequest(fixture.RunId));
        var generation = await generationResponse.Content.ReadFromJsonAsync<OrderPlanningGenerationResponse>();
        var recommendation = await GetOnlyRecommendation(fixture.RunId, generation!.Id);
        var originalCandidate = recommendation.SelectedCandidate!;

        await using (var db = await Db())
        {
            var option = await db.SourcingOptions.SingleAsync(x => x.Id == source.OptionId);
            switch (driftKind)
            {
                case "SOURCE_ACTIVE":
                    option.ChangeState(false);
                    break;
                case "READINESS":
                    var shipping = await db.ReferenceValues.SingleAsync(x => x.Id == option.ShippingModeId);
                    shipping.SetActive(false); shipping.AdvanceVersion(); shipping.Touch();
                    break;
                case "PRICE":
                    var currentPrice = await db.SourcingPrices.SingleAsync(x => x.Id == source.PriceId);
                    currentPrice.Close(today.AddDays(-1));
                    db.SourcingPrices.Add(new SourcingPrice(source.OptionId, 9, currentPrice.CurrencyId, today, null));
                    break;
                case "PRICE_EXPIRY":
                    (await db.SourcingPrices.SingleAsync(x => x.Id == source.PriceId)).Close(today.AddDays(-1));
                    break;
                case "MOQ":
                    option.Edit(81, option.ShippingModeId, option.IncotermId, option.SupplierLeadTimeDays,
                        option.ForwarderToShipmentDays, option.ShippingPeriodDays, option.DestinationCustomsDays);
                    break;
                case "LEAD_TIME":
                    option.Edit(option.MinimumOrderQuantity, option.ShippingModeId, option.IncotermId,
                        2, 0, 0, 1);
                    break;
                case "BASE_UNIT":
                    var newUnit = new Unit("BOX", "BOX", "جعبه", "Box", null, 2);
                    db.Units.Add(newUnit); await db.SaveChangesAsync();
                    var item = await db.Items.SingleAsync(x => x.Id == source.ItemId);
                    item.Edit(item.NameFa, item.NameEn, newUnit.Id, item.Notes);
                    break;
            }
            await db.SaveChangesAsync();
        }

        var auditCount = _factory.AuditSink.Snapshot().Count;
        using var response = await _client.PostAsJsonAsync(
            $"/api/order-planning/recommendations/{recommendation.Id}/decisions",
            new SaveExpertDecisionRequest(ExpertDecisionTypes.Accept, 0, null, null, null, null, null));
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("SOURCE_REVALIDATION_REQUIRED", problem.GetProperty("code").GetString());
        var after = await _client.GetFromJsonAsync<OrderRecommendationResponse>(
            $"/api/order-planning/recommendations/{recommendation.Id}");
        Assert.Equal(0, after!.DecisionVersion);
        Assert.Empty(after.Decisions);
        Assert.Equal(originalCandidate.UnitPrice, after.SelectedCandidate!.UnitPrice);
        Assert.Equal(originalCandidate.MinimumOrderQuantity, after.SelectedCandidate.MinimumOrderQuantity);
        Assert.Equal(originalCandidate.TotalLeadTimeDays, after.SelectedCandidate.TotalLeadTimeDays);
        Assert.Equal(auditCount, _factory.AuditSink.Snapshot().Count);
    }

    [Fact]
    public async Task Candidate_capture_reuses_MRP05_readiness_state_and_exact_reason_codes()
    {
        var today = new DateOnly(2026, 8, 25);
        _factory.PlanningCalendar.TodayOverride = today;
        var source = await ReadySource(today);
        var fixture = await CompletedRunWithReadySource(source, today);
        SourcingReadinessEvaluation canonical;
        await using (var db = await Db())
        {
            var option = await db.SourcingOptions
                .Include(x => x.Item).Include(x => x.Supplier)
                .Include(x => x.ShippingMode).Include(x => x.Incoterm)
                .Include(x => x.Prices).ThenInclude(x => x.Currency)
                .SingleAsync(x => x.Id == source.OptionId);
            option.Edit(option.MinimumOrderQuantity, option.ShippingModeId, option.IncotermId,
                null, option.ForwarderToShipmentDays, option.ShippingPeriodDays, option.DestinationCustomsDays);
            await db.SaveChangesAsync();
            canonical = SourcingReadinessEvaluator.Evaluate(option, SupplyMethods.Buy,
                option.Prices.Single(), source.UnitId);
        }
        Assert.Equal(SourcingReadiness.IncompleteData, canonical.State);
        Assert.Equal(["SUPPLIER_LEAD_TIME_MISSING"], canonical.ReasonCodes);

        var generationResponse = await _client.PostAsJsonAsync("/api/order-planning/generations",
            new GenerateOrderPlanningRequest(fixture.RunId));
        var generation = await generationResponse.Content.ReadFromJsonAsync<OrderPlanningGenerationResponse>();
        var recommendation = await GetOnlyRecommendation(fixture.RunId, generation!.Id);
        var candidate = Assert.Single(recommendation.Candidates);
        Assert.False(candidate.IsEligible);
        Assert.Equal(canonical.ReasonCodes, candidate.ReasonCodes);
    }

    [Fact]
    public async Task Run_selector_search_and_server_sort_reach_records_beyond_first_page()
    {
        var fixture = await CompletedRun(includeDailyNeed: false);
        await using (var db = await Db())
        {
            var createdRuns = new List<MrpRun>();
            for (var index = 1; index <= 101; index++)
            {
                var run = new MrpRun($"MRP-RUN-{900000 + index:000000}", fixture.AsOfDate,
                    fixture.AsOfDate.AddDays(1), "MRP-10-v1", "selector-test", DateTime.UtcNow.AddSeconds(index));
                db.MrpRuns.Add(run);
                createdRuns.Add(run);
            }
            await db.SaveChangesAsync();
            foreach (var run in createdRuns)
            {
                run.MarkSnapshotCaptured(DateTime.UtcNow);
                run.Complete(MrpCompleteness.Complete, false, DateTime.UtcNow);
            }
            await db.SaveChangesAsync();
        }

        var runs = await _client.GetFromJsonAsync<OrderPlanningRunListResponse>(
            "/api/order-planning/runs?search=900101&page=1&pageSize=25");
        var selected = Assert.Single(runs!.Items);
        Assert.Equal("MRP-RUN-900101", selected.RunNumber);
        Assert.True(runs.TotalCount >= 1);

        await using (var db = await Db())
        {
            var generation = new OrderPlanningGeneration(fixture.RunId, 1, fixture.AsOfDate, DateTime.UtcNow, "sort-test");
            db.OrderPlanningGenerations.Add(generation);
            await db.SaveChangesAsync();
            db.OrderRecommendations.AddRange(Enumerable.Range(1, 125).Select(index =>
                new OrderRecommendation(generation.Id, Guid.NewGuid(), $"CMP-{index:0000}", "COMPONENT",
                    $"ماده {index}", $"Material {index}", fixture.UnitId, "KG", fixture.NeedByDate,
                    MrpRouteOrigins.Buy, 1m, 0m, 1, 2, 3, RecommendationStates.Blocked,
                    RecommendationTimingStates.OnTime, null, null, null, null, null, null, null,
                    null, null, RecommendationNextActions.ResolveData, null, null,
                    "NO_READY_SOURCE", "NO_ELIGIBLE_MACHINE_SELECTION", "{}", "MRP-12-v1")));
            await db.SaveChangesAsync();
            generation.Complete(DateTime.UtcNow);
            await db.SaveChangesAsync();
        }
        var sorted = await _client.GetFromJsonAsync<OrderRecommendationListResponse>(
            $"/api/order-planning/recommendations?runId={fixture.RunId}&sortBy=code&sortDirection=desc&page=1&pageSize=25");
        Assert.Equal("CMP-0125", sorted!.Items[0].ItemCode);
        Assert.Equal("CMP-0101", sorted.Items[^1].ItemCode);
    }

    [Fact]
    public async Task Gd10_preserves_original_currencies_per_recommendation_without_a_cross_currency_total()
    {
        var fixture = await CompletedRun(includeDailyNeed: false);
        await using (var db = await Db())
        {
            var generation = new OrderPlanningGeneration(fixture.RunId, 1, fixture.AsOfDate,
                DateTime.UtcNow, "integration-test");
            db.OrderPlanningGenerations.Add(generation);
            await db.SaveChangesAsync();
            var cnyItem = Guid.NewGuid(); var eurItem = Guid.NewGuid();
            var cny = CandidateSnapshot(generation.Id, cnyItem, "SRC-CNY", "CNY", 7);
            var eur = CandidateSnapshot(generation.Id, eurItem, "SRC-EUR", "EUR", 100);
            db.OrderRecommendationCandidates.AddRange(cny, eur);
            db.OrderRecommendations.AddRange(
                ReadySnapshot(generation.Id, cnyItem, "CMP-CNY", cny.Id, 80, 560),
                ReadySnapshot(generation.Id, eurItem, "CMP-EUR", eur.Id, 10, 1000));
            await db.SaveChangesAsync();
            generation.Complete(DateTime.UtcNow);
            await db.SaveChangesAsync();
        }

        var list = await _client.GetFromJsonAsync<OrderRecommendationListResponse>(
            $"/api/order-planning/recommendations?runId={fixture.RunId}&page=1&pageSize=25");
        Assert.Equal(2, list!.TotalCount);
        Assert.Contains(list.Items, x => x.ItemCode == "CMP-CNY" && x.CurrencyCode == "CNY" && x.EstimatedValue == 560);
        Assert.Contains(list.Items, x => x.ItemCode == "CMP-EUR" && x.CurrencyCode == "EUR" && x.EstimatedValue == 1000);
        Assert.Null(typeof(OrderRecommendationListResponse).GetProperty("GrandTotal"));
        Assert.Null(typeof(OrderRecommendationListResponse).GetProperty("TotalEstimatedValue"));
    }

    private async Task<RunFixture> CompletedRun(bool includeRoute = false,
        string resolvedRoute = MrpResolvedPlanningRoutes.External, string? routeOrigin = MrpRouteOrigins.Buy,
        string? reason = null, bool includeDailyNeed = true)
    {
        var asOf = new DateOnly(2026, 8, 25);
        var needBy = asOf.AddDays(5);
        var horizon = asOf.AddDays(30);
        var unitId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var run = new MrpRun($"MRP-RUN-{Random.Shared.Next(800000, 999999)}", asOf, horizon,
            "MRP-10-v1", "integration-test", DateTime.UtcNow);
        await using var db = await Db();
        db.MrpRuns.Add(run);
        await db.SaveChangesAsync();
        db.MrpRunItemInputs.Add(new MrpRunItemInput(run.Id, itemId, "CMP-9001", "COMPONENT",
            "ماده آزمون", "Test material", true, unitId, "KG", "کیلوگرم", "Kilogram", "BUY",
            10m, 0m, Guid.NewGuid(), "COMPONENT_CATEGORY", "قطعه", "Component", 2, 2, 10));
        if (includeDailyNeed)
            db.MrpRunDailyResults.Add(new MrpRunDailyResult(run.Id, itemId, needBy, 20m, 0m,
                -20m, -20m, 20m, 0m, false, MrpCompleteness.Complete));
        if (includeRoute)
            db.MrpRunResolvedRouteResults.Add(new MrpRunResolvedRouteResult(run.Id, itemId, needBy,
                resolvedRoute, routeOrigin, reason));
        await db.SaveChangesAsync();
        run.MarkSnapshotCaptured(DateTime.UtcNow);
        run.Complete(MrpCompleteness.Complete, false, DateTime.UtcNow);
        await db.SaveChangesAsync();
        return new(run.Id, itemId, unitId, asOf, needBy);
    }

    private async Task<RunFixture> CompletedRunWithReadySource(SourceFixture source, DateOnly today)
    {
        var needBy = today.AddDays(4);
        var horizon = needBy.AddDays(2);
        var run = new MrpRun($"MRP-RUN-{Random.Shared.Next(800000, 999999)}", today.AddDays(-10), horizon,
            "MRP-10-v1", "integration-test", DateTime.UtcNow);
        await using var db = await Db();
        db.MrpRuns.Add(run);
        await db.SaveChangesAsync();
        db.MrpRunItemInputs.Add(new MrpRunItemInput(run.Id, source.ItemId, "PRT-9001", "PART",
            "قطعه برنامه‌ریزی", "Planning part", true, source.UnitId, "PCS", "عدد", "Piece", "BUY",
            0m, 0m, source.CategoryId, "PART_TEST", "گروه", "Category", 2, 2, 30));
        db.MrpRunDailyResults.AddRange(
            new MrpRunDailyResult(run.Id, source.ItemId, needBy, 7m, 0m, -15m, -15m, 7m, -8m, false, MrpCompleteness.Complete),
            new MrpRunDailyResult(run.Id, source.ItemId, needBy.AddDays(1), 20m, 0m, -35m, -35m, 0m, -35m, false, MrpCompleteness.Complete),
            new MrpRunDailyResult(run.Id, source.ItemId, needBy.AddDays(2), 20m, 0m, -55m, -55m, 0m, -55m, false, MrpCompleteness.Complete));
        db.MrpRunResolvedRouteResults.Add(new MrpRunResolvedRouteResult(run.Id, source.ItemId, needBy,
            MrpResolvedPlanningRoutes.External, MrpRouteOrigins.Buy, null));
        await db.SaveChangesAsync();
        run.MarkSnapshotCaptured(DateTime.UtcNow);
        run.Complete(MrpCompleteness.Complete, false, DateTime.UtcNow);
        await db.SaveChangesAsync();
        return new(run.Id, source.ItemId, source.UnitId, today.AddDays(-10), needBy);
    }

    private static OrderRecommendationCandidate CandidateSnapshot(Guid generationId, Guid itemId,
        string sourcingNumber, string currencyCode, decimal price) =>
        new(generationId, itemId, Guid.NewGuid(), sourcingNumber, Guid.NewGuid(), "SUP-TEST",
            "تأمین‌کننده", "Supplier", false, true, string.Empty, Guid.NewGuid(), 0,
            Guid.NewGuid(), "SEA", "دریایی", "Sea", null, null, null, null, 1, 0, 0, 0, 1,
            new DateOnly(2026, 8, 26), true, Guid.NewGuid(), price, Guid.NewGuid(), currencyCode,
            currencyCode, currencyCode, new DateOnly(2026, 8, 1), null,
            null, null, null, null, null, null, null, null, null);

    private static OrderRecommendation ReadySnapshot(Guid generationId, Guid itemId, string itemCode,
        Guid candidateId, decimal qty, decimal value) =>
        new(generationId, itemId, itemCode, "COMPONENT", itemCode, itemCode, Guid.NewGuid(), "PCS",
            new DateOnly(2026, 8, 30), MrpRouteOrigins.Buy, 1, 0, 1, 2, 3,
            RecommendationStates.Ready, RecommendationTimingStates.OnTime, new DateOnly(2026, 8, 26),
            new DateOnly(2026, 8, 30), new DateOnly(2026, 8, 29), new DateOnly(2026, 9, 1),
            0, -qty, qty, qty, RecommendationPriorities.Upcoming, RecommendationNextActions.OrderByDate,
            candidateId, value, string.Empty, "FASTEST_ELIGIBLE_ON_TIME", "{}", "ORDER-PLANNING-V1");

    private async Task<SourceFixture> ReadySource(DateOnly today)
    {
        await using var db = await Db();
        var types = await db.ReferenceTypes.ToDictionaryAsync(x => x.Code, x => x.Id);
        var unit = new Unit("PCS", "PCS", "عدد", "Piece", null, 1);
        var currency = new Currency("CNY", "CNY", "یوان", "Chinese Yuan", "¥", 2);
        var category = new ReferenceValue(types["PART_CATEGORY"], "PART_TEST", "PART_TEST", "گروه", "Category", 1);
        var country = new ReferenceValue(types["COUNTRY"], "CN", "CN", "چین", "China", 1);
        var paymentTerm = new ReferenceValue(types["PAYMENT_TERM"], "CASH", "CASH", "نقدی", "Cash", 1);
        var shipping = new ReferenceValue(types["SHIPPING_MODE"], "SEA", "SEA", "دریایی", "Sea", 1);
        db.AddRange(unit, currency, category, country, paymentTerm, shipping);
        await db.SaveChangesAsync();

        var person = new InternalPerson("مالک تأمین", "Sourcing owner");
        var supplier = new Supplier("SUP-9001", "تأمین‌کننده آزمون", "تامینکننده ازمون", country.Id,
            person.Id, null, null, null);
        supplier.PaymentProfile.Set(paymentTerm.Id, 100, 0, 0, null, null);
        supplier.ChangeState(true);
        var item = new Item(ItemTypes.Part, "PRT-9001", "قطعه برنامه‌ریزی", "Planning part", unit.Id, null);
        var profile = new MaterialItemProfile(item.Id, category.Id, SupplyMethods.Buy, null, 0, 0, 2, 2, 30);
        db.AddRange(person, supplier, item, profile);
        await db.SaveChangesAsync();

        var option = new SourcingOption("SRC-9001", item.Id, supplier.Id, unit.Id, 80,
            shipping.Id, null, 1, 0, 1, 1);
        option.SetPreferred(true);
        var price = new SourcingPrice(option.Id, 7, currency.Id, today.AddDays(-1), null);
        db.AddRange(option, price);
        await db.SaveChangesAsync();
        return new(item.Id, unit.Id, category.Id, option.Id, price.Id);
    }

    private static DateOnly TehranToday()
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Tehran");
        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, zone).DateTime);
    }

    private async Task<OrderRecommendationResponse> GetOnlyRecommendation(Guid runId, Guid generationId)
    {
        using var listResponse = await _client.GetAsync(
            $"/api/order-planning/recommendations?runId={runId}&generationId={generationId}&page=1&pageSize=25");
        listResponse.EnsureSuccessStatusCode();
        var list = await listResponse.Content.ReadFromJsonAsync<OrderRecommendationListResponse>();
        var id = Assert.Single(list!.Items).Id;
        return (await _client.GetFromJsonAsync<OrderRecommendationResponse>(
            $"/api/order-planning/recommendations/{id}"))!;
    }

    private Task<HadronDbContext> Db() =>
        _factory.Services.GetRequiredService<IDbContextFactory<HadronDbContext>>().CreateDbContextAsync();

    private sealed record RunFixture(Guid RunId, Guid ItemId, Guid UnitId,
        DateOnly AsOfDate, DateOnly NeedByDate);
    private sealed record SourceFixture(Guid ItemId, Guid UnitId, Guid CategoryId, Guid OptionId, Guid PriceId);
}
