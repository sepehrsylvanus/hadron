using Hadron.Erp.Api.Infrastructure;
using Hadron.Erp.Api.Modules.Mrp.Domain;
using Hadron.Erp.Api.Modules.OrderPlanning.Domain;
using Hadron.Erp.Api.Modules.Planning.Application;
using Hadron.Erp.Api.Modules.ReferenceData.Domain;
using Hadron.Erp.Api.Modules.Sourcing.Application;
using Hadron.Erp.Api.Modules.Sourcing.Domain;
using Hadron.Erp.Api.Platform.Audit;
using Microsoft.EntityFrameworkCore;
using System.Data;
using System.Text.Json;

namespace Hadron.Erp.Api.Modules.OrderPlanning.Application;

public sealed class OrderPlanningService(
    IDbContextFactory<HadronDbContext> factory,
    OrderPlanningCalculator calculator,
    TimeProvider timeProvider,
    IPlanningCalendar planningCalendar,
    IOrderPlanningGenerationFaultInjector faultInjector,
    IAuditEventSink auditSink,
    IHttpContextAccessor accessor,
    ILogger<OrderPlanningService> logger)
{
    private DateOnly TehranToday => planningCalendar.TehranToday;

    public async Task<OrderPlanningGenerationResponse> Generate(GenerateOrderPlanningRequest request, CancellationToken ct)
    {
        Guid generationId;
        await using (var db = await factory.CreateDbContextAsync(ct))
        await using (var tx = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct))
        {
            await LockRun(db, request.RunId, ct);
            var run = await db.MrpRuns.SingleOrDefaultAsync(x => x.Id == request.RunId, ct)
                ?? throw new KeyNotFoundException();
            if (run.Status is not (MrpRunStatuses.Completed or MrpRunStatuses.CompletedWithExceptions))
                throw new OrderPlanningConflictException("MRP_RUN_NOT_COMPLETED", "Only completed MRP Runs can generate order recommendations.");
            var sequence = (await db.OrderPlanningGenerations.Where(x => x.RunId == request.RunId)
                .MaxAsync(x => (int?)x.GenerationSequence, ct) ?? 0) + 1;
            var now = timeProvider.GetUtcNow().UtcDateTime;
            var generation = new OrderPlanningGeneration(request.RunId, sequence, TehranToday, now, Actor());
            db.OrderPlanningGenerations.Add(generation);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            generationId = generation.Id;
        }

        try
        {
            await BuildGeneration(generationId, ct);
            await Emit("OrderPlanningGeneration", generationId, "GENERATE", new { request.RunId }, null, ct);
            return (await GetGeneration(generationId, ct))!;
        }
        catch (Exception exception)
        {
            await MarkFailed(generationId, exception, ct);
            throw;
        }
    }

    public async Task<IReadOnlyList<OrderPlanningGenerationResponse>> Generations(Guid runId, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await (from generation in db.OrderPlanningGenerations.AsNoTracking()
                      join run in db.MrpRuns.AsNoTracking() on generation.RunId equals run.Id
                      where generation.RunId == runId
                      orderby generation.GenerationSequence descending
                      select new OrderPlanningGenerationResponse(generation.Id, runId, run.RunNumber,
                          generation.GenerationSequence, generation.EvaluationDate, generation.GeneratedAtUtc,
                          generation.Status, generation.IsCurrent, generation.CompletedAtUtc, generation.FailureCode,
                          db.OrderRecommendations.Count(x => x.GenerationId == generation.Id))).ToListAsync(ct);
    }

    public async Task<OrderPlanningRunListResponse> Runs(string? search, int page, int pageSize, CancellationToken ct)
    {
        ValidatePage(page, pageSize);
        await using var db = await factory.CreateDbContextAsync(ct);
        var query = db.MrpRuns.AsNoTracking().Where(x => x.Status == MrpRunStatuses.Completed ||
            x.Status == MrpRunStatuses.CompletedWithExceptions);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(x => x.RunNumber.Contains(term));
        }
        var total = await query.CountAsync(ct);
        var rows = await query.OrderByDescending(x => x.CompletedAtUtc).ThenByDescending(x => x.StartedAtUtc)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new OrderPlanningRunListItem(x.Id, x.RunNumber, x.AsOfDate, x.CompletedAtUtc, x.Status))
            .ToListAsync(ct);
        return new(rows, page, pageSize, total);
    }

    public async Task<OrderRecommendationListResponse> List(Guid? runId, Guid? generationId, string? search,
        string? state, string? priority, string? decisionType, DateOnly? needByFrom, DateOnly? needByTo,
        string? sortBy, string? sortDirection, int page, int pageSize, CancellationToken ct)
    {
        ValidatePage(page, pageSize);
        await using var db = await factory.CreateDbContextAsync(ct);
        if (generationId.HasValue)
        {
            var context = await db.OrderPlanningGenerations.AsNoTracking()
                .Where(x => x.Id == generationId.Value)
                .Select(x => new { x.RunId, x.Status }).SingleOrDefaultAsync(ct);
            if (context is null || (runId.HasValue && context.RunId != runId.Value) ||
                context.Status != OrderPlanningGenerationStatuses.Completed)
                throw new KeyNotFoundException("GENERATION_CONTEXT_UNAVAILABLE");
        }
        var latestRunId = await LatestCompletedRunId(db, ct);
        var query = from recommendation in db.OrderRecommendations.AsNoTracking()
                    join generation in db.OrderPlanningGenerations.AsNoTracking() on recommendation.GenerationId equals generation.Id
                    join run in db.MrpRuns.AsNoTracking() on generation.RunId equals run.Id
                    where generation.Status == OrderPlanningGenerationStatuses.Completed
                    select new { recommendation, generation, run };
        if (runId.HasValue) query = query.Where(x => x.run.Id == runId);
        if (generationId.HasValue) query = query.Where(x => x.generation.Id == generationId);
        else
        {
            query = query.Where(x => x.generation.IsCurrent);
            if (!runId.HasValue && latestRunId.HasValue) query = query.Where(x => x.run.Id == latestRunId.Value);
        }
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim(); query = query.Where(x => x.recommendation.ItemCode.Contains(term) ||
                x.recommendation.ItemNameFa.Contains(term) || (x.recommendation.ItemNameEn != null && x.recommendation.ItemNameEn.Contains(term)));
        }
        if (!string.IsNullOrWhiteSpace(state)) query = query.Where(x => x.recommendation.State == state.Trim().ToUpperInvariant());
        if (!string.IsNullOrWhiteSpace(priority)) query = query.Where(x => x.recommendation.Priority == priority.Trim().ToUpperInvariant());
        if (needByFrom.HasValue) query = query.Where(x => x.recommendation.NeedByDate >= needByFrom);
        if (needByTo.HasValue) query = query.Where(x => x.recommendation.NeedByDate <= needByTo);
        if (!string.IsNullOrWhiteSpace(decisionType))
            query = query.Where(x => db.OrderRecommendationDecisions.Where(d => d.RecommendationId == x.recommendation.Id)
                .OrderByDescending(d => d.DecisionVersion).Select(d => d.DecisionType).FirstOrDefault() == decisionType.Trim().ToUpperInvariant());
        var total = await query.CountAsync(ct);
        var descending = string.Equals(sortDirection, "desc", StringComparison.OrdinalIgnoreCase);
        query = (sortBy ?? "priority").Trim().ToLowerInvariant() switch
        {
            "code" => descending ? query.OrderByDescending(x => x.recommendation.ItemCode) : query.OrderBy(x => x.recommendation.ItemCode),
            "needby" => descending ? query.OrderByDescending(x => x.recommendation.NeedByDate).ThenBy(x => x.recommendation.ItemCode) : query.OrderBy(x => x.recommendation.NeedByDate).ThenBy(x => x.recommendation.ItemCode),
            "quantity" => descending ? query.OrderByDescending(x => x.recommendation.SystemRecommendedQty).ThenBy(x => x.recommendation.ItemCode) : query.OrderBy(x => x.recommendation.SystemRecommendedQty).ThenBy(x => x.recommendation.ItemCode),
            _ => query.OrderBy(x => x.recommendation.State == RecommendationStates.Blocked ? 0 : 1)
                .ThenBy(x => x.recommendation.Priority == RecommendationPriorities.Emergency ? 0 :
                    x.recommendation.Priority == RecommendationPriorities.DueNow ? 1 :
                    x.recommendation.Priority == RecommendationPriorities.Upcoming ? 2 : 3)
                .ThenBy(x => x.recommendation.NeedByDate).ThenBy(x => x.recommendation.ItemCode)
        };
        var rows = await query
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        var recIds = rows.Select(x => x.recommendation.Id).ToList();
        // Candidate rows are keyed by Generation+Item; both dimensions are bounded by this page.
        var generationIds = rows.Select(x => x.generation.Id).Distinct().ToList();
        var itemIds = rows.Select(x => x.recommendation.ItemId).Distinct().ToList();
        var candidates = await db.OrderRecommendationCandidates.AsNoTracking()
            .Where(x => generationIds.Contains(x.GenerationId) && itemIds.Contains(x.ItemId)).ToListAsync(ct);
        var decisions = await db.OrderRecommendationDecisions.AsNoTracking().Where(x => recIds.Contains(x.RecommendationId))
            .OrderByDescending(x => x.DecisionVersion).ToListAsync(ct);
        var items = rows.Select(x =>
        {
            var candidate = candidates.SingleOrDefault(c => c.Id == x.recommendation.SelectedCandidateId);
            var decision = decisions.FirstOrDefault(d => d.RecommendationId == x.recommendation.Id);
            return new OrderRecommendationListItem(x.recommendation.Id, x.generation.Id, x.run.Id, x.run.RunNumber,
                x.generation.GenerationSequence, x.run.AsOfDate, x.generation.EvaluationDate, x.generation.IsCurrent,
                latestRunId.HasValue && x.run.Id != latestRunId, x.recommendation.ItemId, x.recommendation.ItemCode,
                x.recommendation.ItemNameFa, x.recommendation.ItemNameEn, x.recommendation.ItemType,
                x.recommendation.NeedByDate, x.recommendation.OrderByDate, x.recommendation.TargetReceiptDate,
                x.recommendation.SystemRecommendedQty, x.recommendation.UnitCode, x.recommendation.State,
                x.recommendation.TimingState, x.recommendation.Priority, x.recommendation.NextAction,
                candidate?.SourcingNumber, candidate?.SupplierNumber, candidate?.SupplierNameFa,
                candidate?.SupplierNameEn, candidate?.CurrencyCode, x.recommendation.EstimatedValue,
                x.recommendation.DecisionVersion, decision?.DecisionType, decision?.ReviewDate,
                Split(x.recommendation.ReasonCodes));
        }).ToList();
        return new(items, page, pageSize, total);
    }

    public async Task<OrderRecommendationResponse?> Get(Guid id, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var row = await (from recommendation in db.OrderRecommendations.AsNoTracking()
                         join generation in db.OrderPlanningGenerations.AsNoTracking() on recommendation.GenerationId equals generation.Id
                         join run in db.MrpRuns.AsNoTracking() on generation.RunId equals run.Id
                         where recommendation.Id == id
                         select new { recommendation, generation, run }).SingleOrDefaultAsync(ct);
        if (row is null) return null;
        var candidates = await db.OrderRecommendationCandidates.AsNoTracking()
            .Where(x => x.GenerationId == row.generation.Id && x.ItemId == row.recommendation.ItemId)
            .OrderByDescending(x => x.IsEligible).ThenByDescending(x => x.IsPreferred).ThenBy(x => x.TotalLeadTimeDays)
            .ThenBy(x => x.SourcingNumber).ToListAsync(ct);
        var decisions = await db.OrderRecommendationDecisions.AsNoTracking().Where(x => x.RecommendationId == id)
            .OrderByDescending(x => x.DecisionVersion).ToListAsync(ct);
        var latest = await LatestCompletedRunId(db, ct);
        var candidateDtos = candidates.Select(ToCandidate).ToList();
        return new(row.recommendation.Id, row.generation.Id, row.run.Id, row.run.RunNumber,
            row.generation.GenerationSequence, row.run.AsOfDate, row.generation.EvaluationDate,
            row.generation.GeneratedAtUtc, row.generation.IsCurrent,
            latest.HasValue && latest != row.run.Id, row.recommendation.ItemId, row.recommendation.ItemCode,
            row.recommendation.ItemNameFa, row.recommendation.ItemNameEn, row.recommendation.ItemType,
            row.recommendation.UnitCode, row.recommendation.NeedByDate, row.recommendation.RouteOrigin,
            row.recommendation.NetRequirementQty, row.recommendation.SafetyStockQty, row.recommendation.State,
            row.recommendation.TimingState, row.recommendation.EarliestFeasibleReceiptDate,
            row.recommendation.TargetReceiptDate, row.recommendation.OrderByDate,
            row.recommendation.DecisionHorizonDate, row.recommendation.LateDays,
            row.recommendation.MinimumBaseline, row.recommendation.BaseRecommendedQty,
            row.recommendation.SystemRecommendedQty, row.recommendation.Priority,
            row.recommendation.NextAction, row.recommendation.EstimatedValue, Split(row.recommendation.ReasonCodes),
            row.recommendation.SourceSelectionReason, row.recommendation.CalculationTrace,
            row.recommendation.AlgorithmVersion, row.recommendation.DecisionVersion,
            candidateDtos.SingleOrDefault(x => x.Id == row.recommendation.SelectedCandidateId), candidateDtos,
            decisions.Select(ToDecision).ToList());
    }

    public async Task<OrderRecommendationResponse> Decide(Guid recommendationId,
        SaveExpertDecisionRequest request, CancellationToken ct)
    {
        var type = request.DecisionType.Trim().ToUpperInvariant();
        ValidateDecision(type, request);
        await using var db = await factory.CreateDbContextAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var recommendation = await db.OrderRecommendations.FromSqlInterpolated(
            $"SELECT * FROM planning.order_recommendations WHERE \"Id\" = {recommendationId} FOR UPDATE")
            .SingleOrDefaultAsync(ct) ?? throw new KeyNotFoundException();
        if (recommendation.DecisionVersion != request.ExpectedVersion)
            throw new OrderPlanningConflictException("STALE_VERSION", "Recommendation decision state changed.");
        var generation = await db.OrderPlanningGenerations.SingleAsync(x => x.Id == recommendation.GenerationId, ct);
        var latestRunId = await LatestCompletedRunId(db, ct);
        if (type is (ExpertDecisionTypes.Accept or ExpertDecisionTypes.Modify) &&
            (latestRunId != generation.RunId || !generation.IsCurrent || recommendation.State == RecommendationStates.Blocked))
            throw new OrderPlanningConflictException("OUTDATED_RUN", "Only a ready current Generation of the latest completed Run can be accepted or modified.");
        var decisionDate = TehranToday;
        OrderRecommendationCandidate? source = null;
        decimal? finalQty = null;
        DateOnly? target = null;
        DateOnly? orderBy = null;
        Currency? currency = null;
        var warnings = new List<string>();
        if (type == ExpertDecisionTypes.Accept)
        {
            source = await db.OrderRecommendationCandidates.SingleAsync(x => x.Id == recommendation.SelectedCandidateId, ct);
            await RevalidateSource(db, source, decisionDate, true, ct);
            finalQty = recommendation.SystemRecommendedQty; target = recommendation.TargetReceiptDate;
            orderBy = recommendation.OrderByDate;
        }
        else if (type == ExpertDecisionTypes.Modify)
        {
            source = await CurrentSourceSnapshot(db, generation.Id, recommendation.ItemId,
                request.SourcingOptionId!.Value, recommendation.NeedByDate, decisionDate, ct);
            if (!source.IsEligible) throw new OrderPlanningConflictException("SOURCE_REVALIDATION_REQUIRED", "Selected source is not currently planning-ready.");
            finalQty = request.FinalQty; target = request.TargetReceiptDate;
            if (finalQty <= 0) throw OrderPlanningValidationException.Single("finalQty", "Final quantity must be positive.");
            if (source.MinimumOrderQuantity.HasValue && finalQty < source.MinimumOrderQuantity)
                throw OrderPlanningValidationException.Single("finalQty", "Final quantity cannot be below current MOQ.");
            orderBy = target!.Value.AddDays(-source.TotalLeadTimeDays!.Value);
            if (decisionDate.AddDays(source.TotalLeadTimeDays.Value) > target)
                warnings.Add("EXPERT_TARGET_INFEASIBLE");
        }
        if (type is ExpertDecisionTypes.Accept or ExpertDecisionTypes.Modify)
        {
            if (!request.UnitPrice.HasValue || request.UnitPrice < 0)
                throw OrderPlanningValidationException.Single("unitPrice", "Unit Price greater than or equal to zero is required at the commercial decision boundary.");
            if (!request.CurrencyId.HasValue)
                throw OrderPlanningValidationException.Single("currencyId", "An active Currency is required at the commercial decision boundary.");
            currency = await db.Currencies.AsNoTracking().SingleOrDefaultAsync(x => x.Id == request.CurrencyId && x.IsActive, ct)
                ?? throw OrderPlanningValidationException.Single("currencyId", "An active Currency is required at the commercial decision boundary.");
        }
        recommendation.AdvanceDecisionVersion();
        var finalSourceSnapshot = source is null ? null : JsonSerializer.Serialize(new
        {
            EvaluationDate = decisionDate,
            source.SourcingOptionId,
            source.SourcingNumber,
            source.SupplierId,
            source.SupplierNumber,
            source.SupplierNameFa,
            source.SupplierNameEn,
            ReadinessState = source.IsEligible ? SourcingReadiness.Ready : SourcingReadiness.IncompleteData,
            ReasonCodes = Split(source.ReasonCodes),
            source.ItemBaseUnitId,
            source.MinimumOrderQuantity,
            source.ShippingModeId,
            source.ShippingModeCode,
            source.ShippingModeNameFa,
            source.ShippingModeNameEn,
            source.IncotermId,
            source.IncotermCode,
            source.IncotermNameFa,
            source.IncotermNameEn,
            source.SupplierLeadTimeDays,
            source.ForwarderToShipmentDays,
            source.ShippingPeriodDays,
            source.DestinationCustomsDays,
            source.TotalLeadTimeDays,
            CommercialInputSource = "EXPERT_DECISION",
            UnitPrice = request.UnitPrice,
            CurrencyId = currency?.Id,
            CurrencyCode = currency?.Code,
            CurrencyNameFa = currency?.NameFa,
            CurrencyNameEn = currency?.NameEn,
            source.PaymentTermId,
            source.PaymentTermCode,
            source.PaymentTermNameFa,
            source.PaymentTermNameEn,
            source.AdvancePercent,
            source.BeforeShipmentPercent,
            source.CreditPercent,
            source.CreditDays,
            source.CreditAnchor
        });
        var decision = new OrderRecommendationDecision(recommendation.Id, recommendation.DecisionVersion, type,
            Clean(request.Reason), decisionDate, timeProvider.GetUtcNow().UtcDateTime, Actor(), request.ReviewDate,
            source?.SourcingOptionId, source?.SourcingNumber, source?.SupplierId, source?.SupplierNumber,
            source?.SupplierNameFa, finalQty, target, orderBy, null, request.UnitPrice,
            currency?.Id, currency?.Code, source?.MinimumOrderQuantity, source?.TotalLeadTimeDays,
            source?.ItemBaseUnitId, string.Join(';', warnings), finalSourceSnapshot);
        db.OrderRecommendationDecisions.Add(decision);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        await Emit("OrderRecommendation", recommendation.Id, type, new { decision.Id, decision.DecisionVersion }, request.Reason, ct);
        return (await Get(recommendation.Id, ct))!;
    }

    private async Task BuildGeneration(Guid generationId, CancellationToken ct)
    {
        var candidates = new List<OrderRecommendationCandidate>();
        var recommendations = new List<OrderRecommendation>();
        Guid runId;
        int generationSequence;
        await using var db = await factory.CreateDbContextAsync(ct);
        await using (var snapshotTx = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct))
        {
            var generation = await db.OrderPlanningGenerations.SingleAsync(x => x.Id == generationId, ct);
            var run = await db.MrpRuns.AsNoTracking().SingleAsync(x => x.Id == generation.RunId, ct);
            runId = run.Id;
            generationSequence = generation.GenerationSequence;
            var items = await db.MrpRunItemInputs.AsNoTracking().Where(x => x.RunId == run.Id && x.ItemType != "PRODUCT")
                .ToDictionaryAsync(x => x.ItemId, ct);
            var daily = await db.MrpRunDailyResults.AsNoTracking().Where(x => x.RunId == run.Id && x.NetRequirement > 0 && !x.IsRootProduct)
                .OrderBy(x => x.Date).ToListAsync(ct);
            var routes = await db.MrpRunResolvedRouteResults.AsNoTracking().Where(x => x.RunId == run.Id).ToListAsync(ct);
            var routeMap = routes.ToDictionary(x => (x.ItemId, x.DemandDate));
            var applicable = daily.GroupBy(x => x.ItemId).Select(grouping =>
            {
                var rows = grouping.OrderBy(x => x.Date).ToList();
                var resolved = rows.Select(row => routeMap.GetValueOrDefault((row.ItemId, row.Date))).ToList();
                if (resolved.Any(x => x is null))
                    return new ApplicableNeed(grouping.Key, rows[0].Date, "LEGACY", rows.Sum(x => x.NetRequirement),
                        true, "LEGACY_RUN_ROUTE_CONTEXT_INCOMPLETE");
                var incomplete = resolved.FirstOrDefault(x => x!.ResolvedPlanningRoute == MrpResolvedPlanningRoutes.Incomplete);
                if (incomplete is not null)
                    return new ApplicableNeed(grouping.Key, rows[0].Date, incomplete.RouteOrigin ?? "INCOMPLETE",
                        rows.Sum(x => x.NetRequirement), true, incomplete.ReasonCode ?? "PLANNING_ROUTE_INCOMPLETE");
                var external = rows.Zip(resolved).Where(x => x.Second!.ResolvedPlanningRoute == MrpResolvedPlanningRoutes.External)
                    .ToList();
                return external.Count == 0
                    ? null
                    : new ApplicableNeed(grouping.Key, external[0].First.Date,
                        external[0].Second!.RouteOrigin ?? "UNKNOWN", external.Sum(x => x.First.NetRequirement), false, null);
            }).Where(x => x is not null).Select(x => x!).ToList();
            var itemIds = applicable.Select(x => x.ItemId).Distinct().ToList();
            var options = await db.SourcingOptions.AsNoTracking().Where(x => itemIds.Contains(x.ItemId))
                .Include(x => x.Item).Include(x => x.Supplier).ThenInclude(x => x.PaymentProfile).ThenInclude(x => x.PaymentTerm)
                .Include(x => x.ShippingMode).Include(x => x.Incoterm).ToListAsync(ct);
            var currentSupplyMethods = await db.MaterialItemProfiles.AsNoTracking().Where(x => itemIds.Contains(x.ItemId))
                .ToDictionaryAsync(x => x.ItemId, x => x.SupplyMethod, ct);
            var timelineRows = await db.MrpRunDailyResults.AsNoTracking()
                .Where(x => x.RunId == run.Id && itemIds.Contains(x.ItemId) && x.Date <= run.HorizonEndDate)
                .OrderBy(x => x.ItemId).ThenBy(x => x.Date)
                .Select(x => new { x.ItemId, x.Date, x.FirmProjected }).ToListAsync(ct);
            var timelines = timelineRows.GroupBy(x => x.ItemId).ToDictionary(x => x.Key,
                x => (IReadOnlyList<(DateOnly Date, decimal FirmProjected)>)x.Select(y => (y.Date, y.FirmProjected)).ToList());
            foreach (var need in applicable)
            {
                var (itemId, needBy, routeOrigin, netQty, blockedByRoute, routeReason) = need;
                if (!items.TryGetValue(itemId, out var item)) continue;
                if (blockedByRoute)
                {
                    recommendations.Add(Blocked(generation.Id, item, needBy, routeOrigin, netQty,
                        routeReason!));
                    continue;
                }
                var itemCandidates = options.Where(x => x.ItemId == itemId)
                    .Select(x => SnapshotCandidate(generation.Id, x, generation.EvaluationDate, needBy,
                        item.BaseUnitId, currentSupplyMethods.GetValueOrDefault(itemId))).ToList();
                candidates.AddRange(itemCandidates);
                var selectedEval = calculator.SelectCandidate(itemCandidates.Select(x => new CandidateEvaluation(
                    x.Id, x.SourcingNumber, x.IsPreferred, x.IsEligible, x.TotalLeadTimeDays)).ToList(),
                    generation.EvaluationDate, needBy);
                var selected = selectedEval is null ? null : itemCandidates.Single(x => x.Id == selectedEval.SourcingOptionId);
                if (selected is null)
                {
                    recommendations.Add(Blocked(generation.Id, item, needBy, routeOrigin, netQty, "NO_READY_SOURCE"));
                    continue;
                }
                var earliest = generation.EvaluationDate.AddDays(selected.TotalLeadTimeDays!.Value);
                var canMeet = earliest <= needBy;
                var target = canMeet ? needBy : earliest;
                var timing = canMeet ? RecommendationTimingStates.OnTime : RecommendationTimingStates.Late;
                var orderBy = needBy.AddDays(-selected.TotalLeadTimeDays.Value);
                var timeline = timelines.GetValueOrDefault(itemId) ?? [];
                var size = calculator.Size(target, item.SafeStartDays ?? 0, run.HorizonEndDate,
                    item.SafetyStockQty, selected.MinimumOrderQuantity, timeline);
                if (!size.HorizonSufficient)
                {
                    recommendations.Add(Blocked(generation.Id, item, needBy, routeOrigin, netQty, "HORIZON_INSUFFICIENT",
                        timing, earliest, target, orderBy, size.DecisionHorizonDate, selected.Id));
                    continue;
                }
                var (priority, next) = OrderPlanningCalculator.Priority(generation.EvaluationDate, needBy, orderBy,
                    item.CriticalDays ?? 0, canMeet);
                var reasons = new List<string>();
                if (selected.MinimumOrderQuantity.HasValue && size.SystemRecommendedQty > size.BaseRecommendedQty)
                {
                    reasons.Add("MOQ_APPLIED");
                    if (OrderPlanningCalculator.MoqCausesExcess(target, item.ExcessStartDays ?? 0,
                        run.HorizonEndDate, size.SystemRecommendedQty, timeline))
                        reasons.Add("MOQ_CAUSES_EXCESS");
                }
                var selectionReason = selected.IsPreferred && selected.CanMeetNeedBy
                    ? "PREFERRED_ELIGIBLE_ON_TIME"
                    : selected.CanMeetNeedBy ? "FASTEST_ELIGIBLE_ON_TIME" : "FASTEST_ELIGIBLE_LATE";
                var trace = JsonSerializer.Serialize(new
                {
                    RouteOrigin = routeOrigin,
                    NeedByDate = needBy,
                    NetRequirementQty = netQty,
                    SafetyStockQty = item.SafetyStockQty,
                    SelectedCandidateId = selected.Id,
                    SelectionReason = selectionReason,
                    TargetReceiptDate = target,
                    DecisionHorizonDate = size.DecisionHorizonDate,
                    MinimumBaseline = size.MinimumBaseline,
                    BaseRecommendedQty = size.BaseRecommendedQty,
                    MinimumOrderQuantity = selected.MinimumOrderQuantity,
                    SystemRecommendedQty = size.SystemRecommendedQty
                });
                recommendations.Add(new(generation.Id, itemId, item.ItemCode, item.ItemType, item.NameFa, item.NameEn,
                    item.BaseUnitId, item.UnitCode, needBy, routeOrigin, netQty, item.SafetyStockQty,
                    item.CriticalDays ?? 0, item.SafeStartDays ?? 0, item.ExcessStartDays ?? 0,
                    RecommendationStates.Ready, timing, earliest, target, orderBy, size.DecisionHorizonDate,
                    Math.Max(earliest.DayNumber - needBy.DayNumber, 0), size.MinimumBaseline, size.BaseRecommendedQty,
                    size.SystemRecommendedQty, priority, next, selected.Id,
                    null, string.Join(';', reasons), selectionReason, trace,
                    OrderPlanningCalculator.AlgorithmVersion));
            }
            await faultInjector.AfterCandidateCapture(generation.Id, ct);
            await snapshotTx.CommitAsync(ct);
        }

        db.ChangeTracker.Clear();
        await using var finalTx = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        await LockRun(db, runId, ct);
        var generationToComplete = await db.OrderPlanningGenerations.SingleAsync(x => x.Id == generationId, ct);
        db.OrderRecommendationCandidates.AddRange(candidates);
        db.OrderRecommendations.AddRange(recommendations);
        // Official rows and current-generation promotion are one short, serialized transaction.
        await db.SaveChangesAsync(ct);
        var hasHigherCompleted = await db.OrderPlanningGenerations.AnyAsync(x => x.RunId == runId &&
            x.GenerationSequence > generationSequence && x.Status == OrderPlanningGenerationStatuses.Completed, ct);
        if (!hasHigherCompleted)
        {
            var previous = await db.OrderPlanningGenerations
                .Where(x => x.RunId == runId && x.IsCurrent && x.Id != generationId).ToListAsync(ct);
            foreach (var old in previous) old.Supersede();
            if (previous.Count > 0) await db.SaveChangesAsync(ct);
        }
        generationToComplete.Complete(timeProvider.GetUtcNow().UtcDateTime, !hasHigherCompleted);
        await db.SaveChangesAsync(ct);
        await finalTx.CommitAsync(ct);
    }

    private static OrderRecommendationCandidate SnapshotCandidate(Guid generationId, SourcingOption option,
        DateOnly evaluationDate, DateOnly needBy, Guid runBaseUnitId, string? currentSupplyMethod)
    {
        var readiness = SourcingReadinessEvaluator.Evaluate(option, currentSupplyMethod, runBaseUnitId);
        var lead = readiness.TotalLeadTimeDays;
        DateOnly? earliest = lead.HasValue ? evaluationDate.AddDays(lead.Value) : null;
        var payment = option.Supplier.PaymentProfile;
        return new(generationId, option.ItemId, option.Id, option.SourcingNumber, option.SupplierId,
            option.Supplier.SupplierNumber, option.Supplier.Name, null, option.IsPreferred,
            readiness.State == SourcingReadiness.Ready, string.Join(';', readiness.ReasonCodes),
            option.ItemBaseUnitId, option.MinimumOrderQuantity,
            option.ShippingModeId, option.ShippingMode?.Code, option.ShippingMode?.NameFa, option.ShippingMode?.NameEn,
            option.IncotermId, option.Incoterm?.Code, option.Incoterm?.NameFa, option.Incoterm?.NameEn,
            option.SupplierLeadTimeDays, option.ForwarderToShipmentDays, option.ShippingPeriodDays,
            option.DestinationCustomsDays, lead, earliest, earliest.HasValue && earliest <= needBy,
            null, null, null, null, null, null, null, null, payment.PaymentTermId, payment.PaymentTerm?.Code,
            payment.PaymentTerm?.NameFa, payment.PaymentTerm?.NameEn, payment.AdvancePercent,
            payment.BeforeShipmentPercent, payment.CreditPercent, payment.CreditDays, payment.CreditAnchor);
    }

    private static OrderRecommendation Blocked(Guid generationId, MrpRunItemInput item, DateOnly needBy,
        string routeOrigin, decimal netQty, string code, string timing = RecommendationTimingStates.OnTime,
        DateOnly? earliest = null, DateOnly? target = null, DateOnly? orderBy = null,
        DateOnly? horizon = null, Guid? selectedCandidateId = null) =>
        new(generationId, item.ItemId, item.ItemCode, item.ItemType, item.NameFa, item.NameEn,
            item.BaseUnitId, item.UnitCode, needBy, routeOrigin, netQty, item.SafetyStockQty,
            item.CriticalDays ?? 0, item.SafeStartDays ?? 0, item.ExcessStartDays ?? 0,
            RecommendationStates.Blocked, timing, earliest, target, orderBy, horizon,
            earliest.HasValue ? Math.Max(earliest.Value.DayNumber - needBy.DayNumber, 0) : null,
            null, null, null, null, RecommendationNextActions.ResolveData, selectedCandidateId,
            null, code, "NO_ELIGIBLE_MACHINE_SELECTION",
            JsonSerializer.Serialize(new
            {
                RouteOrigin = routeOrigin,
                NeedByDate = needBy,
                NetRequirementQty = netQty,
                SafetyStockQty = item.SafetyStockQty,
                BlockReason = code
            }),
            OrderPlanningCalculator.AlgorithmVersion);

    private sealed record ApplicableNeed(Guid ItemId, DateOnly NeedByDate, string RouteOrigin,
        decimal NetRequirementQty, bool BlockedByRoute, string? RouteReason);

    private async Task<OrderRecommendationCandidate> CurrentSourceSnapshot(HadronDbContext db, Guid generationId,
        Guid itemId, Guid sourcingOptionId, DateOnly needBy, DateOnly evaluationDate, CancellationToken ct)
    {
        var item = await db.MrpRunItemInputs.AsNoTracking().Where(x => x.ItemId == itemId &&
            db.OrderPlanningGenerations.Where(g => g.Id == generationId).Select(g => g.RunId).Contains(x.RunId))
            .SingleAsync(ct);
        var option = await db.SourcingOptions.AsNoTracking().Where(x => x.Id == sourcingOptionId && x.ItemId == itemId)
            .Include(x => x.Item).Include(x => x.Supplier).ThenInclude(x => x.PaymentProfile).ThenInclude(x => x.PaymentTerm)
            .Include(x => x.ShippingMode).Include(x => x.Incoterm)
            .SingleOrDefaultAsync(ct) ?? throw new OrderPlanningConflictException("SOURCE_REVALIDATION_REQUIRED", "Source no longer exists for this Item.");
        var currentSupplyMethod = await db.MaterialItemProfiles.AsNoTracking().Where(x => x.ItemId == itemId)
            .Select(x => x.SupplyMethod).SingleOrDefaultAsync(ct);
        return SnapshotCandidate(generationId, option, evaluationDate, needBy, item.BaseUnitId, currentSupplyMethod);
    }

    private async Task RevalidateSource(HadronDbContext db, OrderRecommendationCandidate snapshot,
        DateOnly date, bool compareSnapshot, CancellationToken ct)
    {
        var current = await CurrentSourceSnapshot(db, snapshot.GenerationId, snapshot.ItemId,
            snapshot.SourcingOptionId, date, date, ct);
        if (!current.IsEligible || (compareSnapshot && (current.MinimumOrderQuantity != snapshot.MinimumOrderQuantity ||
            current.SupplierLeadTimeDays != snapshot.SupplierLeadTimeDays ||
            current.ForwarderToShipmentDays != snapshot.ForwarderToShipmentDays ||
            current.ShippingPeriodDays != snapshot.ShippingPeriodDays ||
            current.DestinationCustomsDays != snapshot.DestinationCustomsDays ||
            current.TotalLeadTimeDays != snapshot.TotalLeadTimeDays || current.ItemBaseUnitId != snapshot.ItemBaseUnitId ||
            current.ShippingModeId != snapshot.ShippingModeId || current.IncotermId != snapshot.IncotermId)))
            throw new OrderPlanningConflictException("SOURCE_REVALIDATION_REQUIRED", "Machine source commercial facts changed; refresh or modify explicitly.");
    }

    private async Task<OrderPlanningGenerationResponse?> GetGeneration(Guid id, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await (from generation in db.OrderPlanningGenerations.AsNoTracking()
                      join run in db.MrpRuns.AsNoTracking() on generation.RunId equals run.Id
                      where generation.Id == id
                      select new OrderPlanningGenerationResponse(generation.Id, run.Id, run.RunNumber,
                          generation.GenerationSequence, generation.EvaluationDate, generation.GeneratedAtUtc,
                          generation.Status, generation.IsCurrent, generation.CompletedAtUtc, generation.FailureCode,
                          db.OrderRecommendations.Count(x => x.GenerationId == generation.Id))).SingleOrDefaultAsync(ct);
    }

    private async Task MarkFailed(Guid id, Exception exception, CancellationToken ct)
    {
        try
        {
            await using var db = await factory.CreateDbContextAsync(ct);
            var generation = await db.OrderPlanningGenerations.SingleAsync(x => x.Id == id, ct);
            generation.Fail("ORDER_PLANNING_GENERATION_FAILED", exception.Message.Length > 1900 ? exception.Message[..1900] : exception.Message,
                timeProvider.GetUtcNow().UtcDateTime);
            await db.SaveChangesAsync(ct);
        }
        catch (Exception failure) { logger.LogError(failure, "Unable to mark Order Planning Generation {Id} failed", id); }
    }

    private static async Task<Guid?> LatestCompletedRunId(HadronDbContext db, CancellationToken ct) =>
        await db.MrpPlanSlots.AsNoTracking().Where(x => x.SlotName == MrpPlanSlotNames.Current)
            .Select(x => (Guid?)x.RunId).SingleOrDefaultAsync(ct) ??
        await db.MrpRuns.AsNoTracking().Where(x => x.Status == MrpRunStatuses.Completed ||
            x.Status == MrpRunStatuses.CompletedWithExceptions).OrderByDescending(x => x.CompletedAtUtc)
            .ThenByDescending(x => x.StartedAtUtc).Select(x => (Guid?)x.Id).FirstOrDefaultAsync(ct);

    private static async Task LockRun(HadronDbContext db, Guid runId, CancellationToken ct) =>
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({runId.ToString()}, 12012))", ct);

    private static void ValidateDecision(string type, SaveExpertDecisionRequest request)
    {
        if (type is not (ExpertDecisionTypes.Accept or ExpertDecisionTypes.Modify or ExpertDecisionTypes.Reject or ExpertDecisionTypes.Defer))
            throw OrderPlanningValidationException.Single("decisionType", "Decision must be ACCEPT, MODIFY, REJECT or DEFER.");
        if (type != ExpertDecisionTypes.Accept && string.IsNullOrWhiteSpace(request.Reason))
            throw OrderPlanningValidationException.Single("reason", "Reason is required.");
        if (type == ExpertDecisionTypes.Defer && !request.ReviewDate.HasValue)
            throw OrderPlanningValidationException.Single("reviewDate", "Review date is required for DEFER.");
        if (type == ExpertDecisionTypes.Modify && (!request.SourcingOptionId.HasValue || !request.FinalQty.HasValue || !request.TargetReceiptDate.HasValue))
            throw OrderPlanningValidationException.Single("modify", "Source, final quantity and target receipt date are required for MODIFY.");
    }

    private static void ValidatePage(int page, int pageSize)
    { if (page < 1 || pageSize is < 1 or > 100) throw OrderPlanningValidationException.Single("page", "pageSize must be between 1 and 100."); }
    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private string? Actor() => AuditEventContext.ActorId(accessor.HttpContext);
    private static IReadOnlyList<string> Split(string value) => string.IsNullOrWhiteSpace(value) ? [] : value.Split(';', StringSplitOptions.RemoveEmptyEntries);
    private static OrderPlanningCandidateResponse ToCandidate(OrderRecommendationCandidate x) =>
        new(x.Id, x.SourcingOptionId, x.SourcingNumber, x.SupplierId, x.SupplierNumber, x.SupplierNameFa,
            x.SupplierNameEn, x.IsPreferred, x.IsEligible, Split(x.ReasonCodes), x.MinimumOrderQuantity,
            x.TotalLeadTimeDays, x.EarliestFeasibleReceiptDate, x.CanMeetNeedBy, x.PriceId, x.UnitPrice,
            x.CurrencyId, x.CurrencyCode, x.PriceEffectiveFrom, x.PriceEffectiveTo, x.ShippingModeCode,
            x.IncotermCode, x.CreditAnchor, x.SupplierLeadTimeDays, x.ForwarderToShipmentDays,
            x.ShippingPeriodDays, x.DestinationCustomsDays);
    private static OrderPlanningDecisionResponse ToDecision(OrderRecommendationDecision x) =>
        new(x.Id, x.DecisionVersion, x.DecisionType, x.Reason, x.DecisionDate, x.DecidedAtUtc,
            x.ActorIdentifier, x.ReviewDate, x.FinalSourcingOptionId, x.FinalSourcingNumber,
            x.FinalSupplierNumber, x.FinalSupplierNameFa, x.FinalQty, x.FinalTargetReceiptDate,
            x.FinalOrderByDate, x.FinalUnitPrice, x.FinalCurrencyCode, x.WarningCodes, x.FinalSourceSnapshot);
    private async Task Emit(string entityType, Guid id, string action, object values, string? reason, CancellationToken ct)
    {
        try
        {
            await auditSink.WriteAsync(new(entityType, id, action, timeProvider.GetUtcNow().UtcDateTime, Actor(),
            AuditEventContext.CorrelationId(accessor.HttpContext), null,
            new Dictionary<string, object?> { ["Value"] = values }, reason), ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception exception) { logger.LogError(exception, "Post-commit Order Planning audit failed for {Id} {Action}", id, action); }
    }
}
