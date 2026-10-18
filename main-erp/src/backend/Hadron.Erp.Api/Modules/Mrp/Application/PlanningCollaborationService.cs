using System.Security.Claims;
using System.Text.Json;
using Hadron.Erp.Api.Infrastructure;
using Hadron.Erp.Api.Modules.Catalog.Domain;
using Hadron.Erp.Api.Modules.Mrp.Domain;
using Hadron.Erp.Api.Platform.Audit;
using Hadron.Erp.Api.Platform.Security;
using Microsoft.EntityFrameworkCore;

namespace Hadron.Erp.Api.Modules.Mrp.Application;

public sealed class PlanningCollaborationService(
    IDbContextFactory<HadronDbContext> factory,
    MrpCalculator calculator,
    IHttpContextAccessor accessor,
    IAuditEventSink auditSink,
    ILogger<PlanningCollaborationService> logger)
{
    private static readonly HashSet<string> RecalculatedExceptionCodes =
    [
        "INACTIVE_WAREHOUSE_STOCK_INCLUDED", "FIRM_SUPPLY_MISSING_ETA", "OVERDUE_FIRM_SUPPLY",
        "NO_PLANNING_READY_EXTERNAL_SOURCE", "AMBIGUOUS_EFFECTIVE_BOM", "MISSING_EFFECTIVE_BOM",
        "INACTIVE_BOM_ITEM", "BASE_UNIT_CHANGED", "HYBRID_ROUTE_NOT_VALID_FOR_ALL_DATES",
        "HYBRID_ROUTE_DECISION_REQUIRED", "HYBRID_NO_VALID_ROUTE", "BOM_CYCLE_DETECTED"
    ];

    public async Task<PlannerWorkItemListResponse> WorkItems(Guid? runId, string? status,
        Guid? assignedUserId, string? search, int page, int pageSize, CancellationToken ct)
    {
        ValidatePage(page, pageSize);
        await using var db = await factory.CreateDbContextAsync(ct);
        var query = from workItem in db.PlannerWorkItems.AsNoTracking()
                    join issue in db.MrpRunExceptions.AsNoTracking() on workItem.ExceptionId equals issue.Id
                    join run in db.MrpRuns.AsNoTracking() on workItem.RunId equals run.Id
                    join item in db.MrpRunItemInputs.AsNoTracking()
                        on new { workItem.RunId, ItemId = issue.ItemId ?? Guid.Empty }
                        equals new { item.RunId, item.ItemId } into items
                    from item in items.DefaultIfEmpty()
                    join assigned in db.Users.AsNoTracking() on workItem.AssignedUserId equals assigned.Id into assignees
                    from assigned in assignees.DefaultIfEmpty()
                    select new { workItem, issue, run, item, assigned };
        if (runId.HasValue) query = query.Where(x => x.workItem.RunId == runId.Value);
        if (!string.IsNullOrWhiteSpace(status)) query = query.Where(x => x.workItem.Status == status.Trim().ToUpper());
        if (assignedUserId.HasValue) query = query.Where(x => x.workItem.AssignedUserId == assignedUserId.Value);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = $"%{search.Trim()}%";
            query = query.Where(x => EF.Functions.ILike(x.issue.Code, pattern) ||
                EF.Functions.ILike(x.issue.Message, pattern) ||
                (x.item != null && (EF.Functions.ILike(x.item.ItemCode, pattern) ||
                    EF.Functions.ILike(x.item.NameFa, pattern) ||
                    (x.item.NameEn != null && EF.Functions.ILike(x.item.NameEn, pattern)))));
        }
        var total = await query.CountAsync(ct);
        var rows = await query.OrderBy(x => x.workItem.Status).ThenByDescending(x => x.issue.AffectsDemand)
            .ThenByDescending(x => x.workItem.UpdatedAtUtc).Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new PlannerWorkItemResponse(x.workItem.Id, x.workItem.RunId, x.workItem.ExceptionId,
                x.run.RunNumber, x.issue.ItemId, x.item == null ? null : x.item.ItemCode, x.issue.Date,
                x.issue.Code, x.issue.Severity, x.issue.AffectsDemand, x.issue.Message,
                x.workItem.AssignedUserId, x.assigned == null ? null : x.assigned.UserName,
                x.assigned == null ? null : (x.assigned.FirstName + " " + x.assigned.LastName).Trim(),
                x.workItem.Status, x.workItem.AcknowledgedAtUtc, x.workItem.UpdatedAtUtc,
                x.workItem.Version)).ToListAsync(ct);
        return new(rows, page, pageSize, total);
    }

    public async Task<PlannerWorkItemResponse> CreateWorkItem(CreatePlannerWorkItemRequest request, CancellationToken ct)
    {
        var actor = ActorIdentifier(); var now = DateTime.UtcNow;
        await using var db = await factory.CreateDbContextAsync(ct);
        var issue = await db.MrpRunExceptions.AsNoTracking().SingleOrDefaultAsync(x => x.Id == request.ExceptionId, ct)
            ?? throw new KeyNotFoundException();
        if (request.AssignedUserId.HasValue) await RequireActiveAssignee(db, request.AssignedUserId.Value, ct);
        if (await db.PlannerWorkItems.AnyAsync(x => x.ExceptionId == request.ExceptionId, ct))
            throw new MrpConflictException("PLANNER_WORK_ITEM_EXISTS", "This exception already has a planner work item.");
        var entity = new PlannerWorkItem(issue.RunId, issue.Id, request.AssignedUserId, actor, now);
        db.PlannerWorkItems.Add(entity);
        db.PlannerWorkItemEvents.Add(new(entity.Id, "CREATED", actor, request.AssignedUserId, now));
        await db.SaveChangesAsync(ct);
        await Emit("PlannerWorkItem", entity.Id, "Created", null,
            new Dictionary<string, object?> { ["ExceptionId"] = issue.Id, ["AssignedUserId"] = request.AssignedUserId }, ct);
        return (await WorkItem(entity.Id, ct))!;
    }

    public async Task<PlannerWorkItemResponse> Assign(Guid id, AssignPlannerWorkItemRequest request, CancellationToken ct)
    {
        var actor = ActorIdentifier(); var now = DateTime.UtcNow;
        await using var db = await factory.CreateDbContextAsync(ct);
        var entity = await db.PlannerWorkItems.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new KeyNotFoundException();
        EnsureVersion(entity.Version, request.ExpectedVersion);
        if (request.AssignedUserId.HasValue) await RequireActiveAssignee(db, request.AssignedUserId.Value, ct);
        var previous = entity.AssignedUserId; entity.Assign(request.AssignedUserId, now);
        db.PlannerWorkItemEvents.Add(new(entity.Id, "ASSIGNED", actor, request.AssignedUserId, now));
        await db.SaveChangesAsync(ct);
        await Emit("PlannerWorkItem", entity.Id, "Assigned",
            new Dictionary<string, object?> { ["AssignedUserId"] = previous },
            new Dictionary<string, object?> { ["AssignedUserId"] = request.AssignedUserId }, ct);
        return (await WorkItem(entity.Id, ct))!;
    }

    public async Task<PlannerWorkItemResponse> Acknowledge(Guid id, AcknowledgePlannerWorkItemRequest request,
        CancellationToken ct)
    {
        var actor = ActorIdentifier(); var actorUserId = ActorUserId(); var now = DateTime.UtcNow;
        await using var db = await factory.CreateDbContextAsync(ct);
        var entity = await db.PlannerWorkItems.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new KeyNotFoundException();
        EnsureVersion(entity.Version, request.ExpectedVersion);
        entity.Acknowledge(actorUserId, now);
        db.PlannerWorkItemEvents.Add(new(entity.Id, "ACKNOWLEDGED", actor, entity.AssignedUserId, now));
        await db.SaveChangesAsync(ct);
        await Emit("PlannerWorkItem", entity.Id, "Acknowledged", null,
            new Dictionary<string, object?> { ["Status"] = entity.Status }, ct);
        return (await WorkItem(entity.Id, ct))!;
    }

    public async Task<PlannerAssigneeListResponse> Assignees(string? search, int page, int pageSize, CancellationToken ct)
    {
        ValidatePage(page, pageSize); await using var db = await factory.CreateDbContextAsync(ct);
        var query = db.Users.AsNoTracking().Where(x => x.IsActive);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = $"%{search.Trim()}%";
            query = query.Where(x => EF.Functions.ILike(x.UserName!, pattern) ||
                EF.Functions.ILike(x.FirstName, pattern) || EF.Functions.ILike(x.LastName, pattern));
        }
        var total = await query.CountAsync(ct);
        var items = await query.OrderBy(x => x.FirstName).ThenBy(x => x.LastName).ThenBy(x => x.UserName)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new PlannerAssigneeResponse(x.Id, x.UserName!, (x.FirstName + " " + x.LastName).Trim()))
            .ToListAsync(ct);
        return new(items, page, pageSize, total);
    }

    public async Task<IReadOnlyList<PlannerSavedViewResponse>> SavedViews(CancellationToken ct)
    {
        var owner = ActorIdentifier(); await using var db = await factory.CreateDbContextAsync(ct);
        return await db.PlannerSavedViews.AsNoTracking().Where(x => x.OwnerIdentifier == owner)
            .OrderBy(x => x.Name).Select(x => new PlannerSavedViewResponse(x.Id, x.Name, x.FiltersJson,
                x.UpdatedAtUtc, x.Version)).ToListAsync(ct);
    }

    public async Task<PlannerSavedViewResponse> CreateSavedView(CreatePlannerSavedViewRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Name)) throw MrpValidationException.Single("name", "Name is required.");
        try { using var _ = JsonDocument.Parse(request.FiltersJson); }
        catch (JsonException) { throw MrpValidationException.Single("filtersJson", "Filters must be valid JSON."); }
        var owner = ActorIdentifier(); var now = DateTime.UtcNow;
        await using var db = await factory.CreateDbContextAsync(ct);
        if (await db.PlannerSavedViews.AnyAsync(x => x.OwnerIdentifier == owner &&
            x.NormalizedName == request.Name.Trim().ToUpper(), ct))
            throw new MrpConflictException("SAVED_VIEW_NAME_EXISTS", "A saved view with this name already exists.");
        var entity = new PlannerSavedView(owner, request.Name, request.FiltersJson, now);
        db.PlannerSavedViews.Add(entity); await db.SaveChangesAsync(ct);
        await Emit("PlannerSavedView", entity.Id, "Created", null,
            new Dictionary<string, object?> { ["Name"] = entity.Name }, ct);
        return new(entity.Id, entity.Name, entity.FiltersJson, entity.UpdatedAtUtc, entity.Version);
    }

    public async Task DeleteSavedView(Guid id, CancellationToken ct)
    {
        var owner = ActorIdentifier(); await using var db = await factory.CreateDbContextAsync(ct);
        var entity = await db.PlannerSavedViews.SingleOrDefaultAsync(x => x.Id == id && x.OwnerIdentifier == owner, ct)
            ?? throw new KeyNotFoundException();
        db.PlannerSavedViews.Remove(entity); await db.SaveChangesAsync(ct);
        await Emit("PlannerSavedView", id, "Deleted", null, null, ct);
    }

    public async Task<WhatIfScenarioListResponse> Scenarios(int page, int pageSize, CancellationToken ct)
    {
        ValidatePage(page, pageSize); var owner = ActorIdentifier();
        await using var db = await factory.CreateDbContextAsync(ct);
        var query = db.WhatIfScenarios.AsNoTracking().Where(x => x.OwnerIdentifier == owner);
        var total = await query.CountAsync(ct);
        var rows = await (from scenario in query
                          join run in db.MrpRuns.AsNoTracking() on scenario.BaseRunId equals run.Id
                          let overrideCount = db.WhatIfForecastOverrides.Count(x => x.ScenarioId == scenario.Id) +
                              db.WhatIfInventoryOverrides.Count(x => x.ScenarioId == scenario.Id) +
                              db.WhatIfFirmSupplyOverrides.Count(x => x.ScenarioId == scenario.Id)
                          orderby scenario.CreatedAtUtc descending
                          select new WhatIfScenarioResponse(scenario.Id, scenario.BaseRunId, run.RunNumber,
                              scenario.Name, scenario.Status, scenario.Completeness, scenario.CreatedAtUtc,
                              overrideCount)).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return new(rows, page, pageSize, total);
    }

    public async Task<WhatIfScenarioResponse> CreateScenario(CreateWhatIfScenarioRequest request, CancellationToken ct)
    {
        ValidateScenarioRequest(request);
        var owner = ActorIdentifier(); var now = DateTime.UtcNow;
        await using var db = await factory.CreateDbContextAsync(ct);
        var run = await db.MrpRuns.AsNoTracking().SingleOrDefaultAsync(x => x.Id == request.BaseRunId, ct)
            ?? throw new KeyNotFoundException();
        if (run.Status is not (MrpRunStatuses.Completed or MrpRunStatuses.CompletedWithExceptions))
            throw new MrpConflictException("WHAT_IF_BASE_RUN_NOT_COMPLETED", "The base MRP Run must be completed.");
        var input = await LoadWhatIfInput(db, run, request, ct);
        var output = calculator.Calculate(input);
        var scenario = new WhatIfScenario(run.Id, request.Name, owner, output.Completeness, now);
        db.WhatIfScenarios.Add(scenario);
        db.WhatIfForecastOverrides.AddRange((request.ForecastOverrides ?? []).Select(x =>
            new WhatIfForecastOverride(scenario.Id, x.ProductId, x.BusinessPeriodId, x.Quantity)));
        db.WhatIfInventoryOverrides.AddRange((request.InventoryOverrides ?? []).Select(x =>
            new WhatIfInventoryOverride(scenario.Id, x.ItemId, x.OnHandQty)));
        db.WhatIfFirmSupplyOverrides.AddRange((request.FirmSupplyOverrides ?? []).Select(x =>
            new WhatIfFirmSupplyOverride(scenario.Id, x.DeliveryScheduleId, x.OpenQty, x.Eta)));
        db.WhatIfDailyResults.AddRange(output.Daily.Select(x => new WhatIfDailyResult(scenario.Id, x.ItemId,
            x.Date, x.GrossDemand, x.FirmIncoming, x.FirmProjected, x.PlanningBeforeNeed,
            x.NetRequirement, x.PlanningBalance, x.IsRootProduct, x.Completeness)));
        db.WhatIfItemSummaries.AddRange(output.Summaries.Select(x => new WhatIfItemSummary(scenario.Id,
            x.ItemId, x.StartingOnHand, x.TotalGrossDemand, x.TotalFirmIncoming, x.TotalNetRequirement,
            x.StockoutDate, x.SafetyBreachDate, x.Completeness)));
        db.WhatIfExceptions.AddRange(output.Exceptions.Select(x => new WhatIfException(scenario.Id,
            x.ItemId, x.Date, x.Code, x.Severity, x.AffectsDemand, x.Message, x.SourceReference)));
        await db.SaveChangesAsync(ct);
        await Emit("WhatIfScenario", scenario.Id, "Calculated", null,
            new Dictionary<string, object?> { ["BaseRunId"] = run.Id, ["Completeness"] = output.Completeness }, ct);
        var count = (request.ForecastOverrides?.Count ?? 0) + (request.InventoryOverrides?.Count ?? 0) +
            (request.FirmSupplyOverrides?.Count ?? 0);
        return new(scenario.Id, run.Id, run.RunNumber, scenario.Name, scenario.Status,
            scenario.Completeness, scenario.CreatedAtUtc, count);
    }

    public async Task<WhatIfResultListResponse> ScenarioResults(Guid scenarioId, string? search,
        int page, int pageSize, CancellationToken ct)
    {
        ValidatePage(page, pageSize); var owner = ActorIdentifier();
        await using var db = await factory.CreateDbContextAsync(ct);
        var scenario = await db.WhatIfScenarios.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == scenarioId && x.OwnerIdentifier == owner, ct)
            ?? throw new KeyNotFoundException();
        var query = from result in db.WhatIfItemSummaries.AsNoTracking()
                    join item in db.MrpRunItemInputs.AsNoTracking()
                        on new { RunId = scenario.BaseRunId, result.ItemId } equals new { item.RunId, item.ItemId }
                    join official in db.MrpRunItemSummaries.AsNoTracking()
                        on new { RunId = scenario.BaseRunId, result.ItemId } equals new { official.RunId, official.ItemId }
                        into officials
                    from official in officials.DefaultIfEmpty()
                    where result.ScenarioId == scenario.Id
                    select new { result, item, official };
        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = $"%{search.Trim()}%";
            query = query.Where(x => EF.Functions.ILike(x.item.ItemCode, pattern) ||
                EF.Functions.ILike(x.item.NameFa, pattern) ||
                (x.item.NameEn != null && EF.Functions.ILike(x.item.NameEn, pattern)));
        }
        var total = await query.CountAsync(ct);
        var rows = await query.OrderByDescending(x => x.result.TotalNetRequirement -
                (x.official == null ? 0 : x.official.TotalNetRequirement)).ThenBy(x => x.item.ItemCode)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new WhatIfResultResponse(x.item.ItemId, x.item.ItemCode, x.item.NameFa, x.item.NameEn,
                x.item.ItemType, x.official == null ? 0 : x.official.StartingOnHand, x.result.StartingOnHand,
                x.official == null ? 0 : x.official.TotalNetRequirement, x.result.TotalNetRequirement,
                x.result.TotalNetRequirement - (x.official == null ? 0 : x.official.TotalNetRequirement),
                x.official == null ? null : x.official.StockoutDate, x.result.StockoutDate,
                x.result.Completeness)).ToListAsync(ct);
        return new(rows, page, pageSize, total);
    }

    public async Task<WhatIfCandidateListResponse> Candidates(Guid runId, string? search, string? itemType,
        int page, int pageSize, CancellationToken ct)
    {
        ValidatePage(page, pageSize); await using var db = await factory.CreateDbContextAsync(ct);
        var query = db.MrpRunItemInputs.AsNoTracking().Where(x => x.RunId == runId);
        if (!string.IsNullOrWhiteSpace(itemType)) query = query.Where(x => x.ItemType == itemType.Trim().ToUpper());
        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = $"%{search.Trim()}%";
            query = query.Where(x => EF.Functions.ILike(x.ItemCode, pattern) || EF.Functions.ILike(x.NameFa, pattern) ||
                (x.NameEn != null && EF.Functions.ILike(x.NameEn, pattern)));
        }
        var total = await query.CountAsync(ct);
        var rows = await query.OrderBy(x => x.ItemCode).Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new WhatIfCandidateResponse(x.ItemId, x.ItemCode, x.NameFa, x.NameEn, x.ItemType)).ToListAsync(ct);
        return new(rows, page, pageSize, total);
    }

    public async Task<IReadOnlyList<WhatIfForecastPeriodResponse>> ForecastPeriods(Guid runId, Guid productId,
        CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.MrpRunForecastPeriodInputs.AsNoTracking().Where(x => x.RunId == runId && x.ProductId == productId)
            .OrderBy(x => x.PeriodStartDate).Select(x => new WhatIfForecastPeriodResponse(x.ProductId,
                x.BusinessPeriodId, x.PeriodStartDate, x.PeriodEndDate, x.OfficialForecastQty,
                x.ActualMtdQty, x.AllocationState)).ToListAsync(ct);
    }

    public async Task<WhatIfFirmSupplyCandidateListResponse> FirmSupplyCandidates(Guid runId, string? search,
        int page, int pageSize, CancellationToken ct)
    {
        ValidatePage(page, pageSize); await using var db = await factory.CreateDbContextAsync(ct);
        var query = from supply in db.MrpRunFirmSupplyInputs.AsNoTracking()
                    join item in db.MrpRunItemInputs.AsNoTracking()
                        on new { supply.RunId, supply.ItemId } equals new { item.RunId, item.ItemId }
                    where supply.RunId == runId
                    select new { supply, item };
        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = $"%{search.Trim()}%";
            query = query.Where(x => EF.Functions.ILike(x.supply.PurchaseOrderNumber, pattern) ||
                EF.Functions.ILike(x.item.ItemCode, pattern) || EF.Functions.ILike(x.item.NameFa, pattern) ||
                (x.item.NameEn != null && EF.Functions.ILike(x.item.NameEn, pattern)));
        }
        var total = await query.CountAsync(ct);
        var rows = await query.OrderBy(x => x.supply.PurchaseOrderNumber).ThenBy(x => x.item.ItemCode)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new WhatIfFirmSupplyCandidateResponse(x.supply.DeliveryScheduleId, x.item.ItemId,
                x.item.ItemCode, x.item.NameFa, x.item.NameEn, x.supply.PurchaseOrderNumber,
                x.supply.OpenQty, x.supply.Eta, x.supply.EtaStatus)).ToListAsync(ct);
        return new(rows, page, pageSize, total);
    }

    private async Task<PlannerWorkItemResponse?> WorkItem(Guid id, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await (from workItem in db.PlannerWorkItems.AsNoTracking()
                      join issue in db.MrpRunExceptions.AsNoTracking() on workItem.ExceptionId equals issue.Id
                      join run in db.MrpRuns.AsNoTracking() on workItem.RunId equals run.Id
                      join item in db.MrpRunItemInputs.AsNoTracking()
                          on new { workItem.RunId, ItemId = issue.ItemId ?? Guid.Empty }
                          equals new { item.RunId, item.ItemId } into items
                      from item in items.DefaultIfEmpty()
                      join assigned in db.Users.AsNoTracking()
                          on workItem.AssignedUserId equals assigned.Id into assignees
                      from assigned in assignees.DefaultIfEmpty()
                      where workItem.Id == id
                      select new PlannerWorkItemResponse(workItem.Id, workItem.RunId, workItem.ExceptionId,
                          run.RunNumber, issue.ItemId, item == null ? null : item.ItemCode, issue.Date,
                          issue.Code, issue.Severity, issue.AffectsDemand, issue.Message,
                          workItem.AssignedUserId, assigned == null ? null : assigned.UserName,
                          assigned == null ? null : (assigned.FirstName + " " + assigned.LastName).Trim(),
                          workItem.Status, workItem.AcknowledgedAtUtc, workItem.UpdatedAtUtc,
                          workItem.Version)).SingleOrDefaultAsync(ct);
    }

    private static async Task RequireActiveAssignee(HadronDbContext db, Guid id, CancellationToken ct)
    {
        if (!await db.Users.AsNoTracking().AnyAsync(x => x.Id == id && x.IsActive, ct))
            throw MrpValidationException.Single("assignedUserId", "Assignee must be an active user.");
    }

    private async Task<MrpCalculationInput> LoadWhatIfInput(HadronDbContext db, MrpRun run,
        CreateWhatIfScenarioRequest request, CancellationToken ct)
    {
        var items = await db.MrpRunItemInputs.AsNoTracking().Where(x => x.RunId == run.Id).ToListAsync(ct);
        var forecastRows = await db.MrpRunForecastInputs.AsNoTracking().Where(x => x.RunId == run.Id).ToListAsync(ct);
        var periods = await db.MrpRunForecastPeriodInputs.AsNoTracking().Where(x => x.RunId == run.Id).ToListAsync(ct);
        var boms = await db.MrpRunBomInputs.AsNoTracking().Where(x => x.RunId == run.Id).ToListAsync(ct);
        var inventoryRows = await db.MrpRunInventoryInputs.AsNoTracking().Where(x => x.RunId == run.Id).ToListAsync(ct);
        var firmRows = await db.MrpRunFirmSupplyInputs.AsNoTracking().Where(x => x.RunId == run.Id).ToListAsync(ct);
        var sourceRows = await db.MrpRunExternalSourceInputs.AsNoTracking().Where(x => x.RunId == run.Id).ToListAsync(ct);
        var availabilityRows = await db.MrpRunRouteAvailabilityInputs.AsNoTracking().Where(x => x.RunId == run.Id).ToListAsync(ct);
        var decisionRows = await db.MrpRunRouteDecisions.AsNoTracking().Where(x => x.RunId == run.Id).ToListAsync(ct);
        var issueRows = await db.MrpRunExceptions.AsNoTracking().Where(x => x.RunId == run.Id).ToListAsync(ct);

        var demands = forecastRows.Select(x => new MrpProductDemand(x.ProductId, x.BusinessPeriodId,
            x.ForecastSeriesId, x.ForecastRevisionId, x.DemandDate, x.DemandQty,
            x.PeriodForecastQty, x.ActualMtd, x.UnitId)).ToList();
        var overriddenPeriods = new HashSet<(Guid, Guid)>();
        foreach (var change in request.ForecastOverrides ?? [])
        {
            var period = periods.SingleOrDefault(x => x.ProductId == change.ProductId &&
                x.BusinessPeriodId == change.BusinessPeriodId)
                ?? throw MrpValidationException.Single("forecastOverrides", "Forecast period is not present in the base Run snapshot.");
            overriddenPeriods.Add((change.ProductId, change.BusinessPeriodId));
            demands.RemoveAll(x => x.ProductId == change.ProductId && x.BusinessPeriodId == change.BusinessPeriodId);
            var planned = period.PeriodEndDate < run.AsOfDate ? 0m :
                period.PeriodStartDate <= run.AsOfDate ? Math.Max(change.Quantity - period.ActualMtdQty, 0m) : change.Quantity;
            var first = Max(period.PeriodStartDate, period.EligibleAllocationStart, run.AsOfDate.AddDays(1));
            var last = Min(period.PeriodEndDate, run.HorizonEndDate);
            if (planned > 0 && first <= last)
            {
                foreach (var allocation in MrpCalculator.Distribute(planned, first, last))
                    demands.Add(new(change.ProductId, change.BusinessPeriodId, period.ForecastSeriesId ?? Guid.Empty,
                        period.ForecastRevisionId ?? Guid.Empty, allocation.Date, allocation.Quantity,
                        change.Quantity, period.ActualMtdQty, period.ProductBaseUnitId));
            }
        }

        var inventory = inventoryRows.Select(x => new MrpInventoryPosition(x.ItemId, x.WarehouseId,
            x.WarehouseCode, x.WarehouseIsActive, x.UnitId, x.OnHandQty)).ToList();
        foreach (var change in request.InventoryOverrides ?? [])
        {
            var item = items.SingleOrDefault(x => x.ItemId == change.ItemId)
                ?? throw MrpValidationException.Single("inventoryOverrides", "Item is not present in the base Run snapshot.");
            inventory.RemoveAll(x => x.ItemId == change.ItemId);
            inventory.Add(new(change.ItemId, Guid.Empty, "WHAT-IF", true, item.BaseUnitId, change.OnHandQty));
        }

        var firm = firmRows.Select(x => new MrpFirmSupply(x.ItemId, x.PurchaseOrderId, x.PurchaseOrderNumber,
            x.PurchaseOrderLineId, x.DeliveryScheduleId, x.OpenQty, x.Eta, x.EtaSource, x.EtaStatus,
            x.PurchaseOrderStatus, x.SupplierId, x.SupplierNumber, x.SupplierNameFa, x.SupplierNameEn)).ToList();
        foreach (var change in request.FirmSupplyOverrides ?? [])
        {
            var index = firm.FindIndex(x => x.DeliveryScheduleId == change.DeliveryScheduleId);
            if (index < 0) throw MrpValidationException.Single("firmSupplyOverrides", "Firm schedule is not present in the base Run snapshot.");
            var old = firm[index]; var etaStatus = !change.Eta.HasValue ? "MISSING" :
                change.Eta.Value < run.AsOfDate ? "OVERDUE" : "ON_TIME";
            firm[index] = old with { OpenQty = change.OpenQty, Eta = change.Eta, EtaStatus = etaStatus, EtaSource = "WHAT_IF_OVERRIDE" };
        }

        var exceptions = issueRows.Where(x => !RecalculatedExceptionCodes.Contains(x.Code) &&
                !(overriddenPeriods.Contains((x.ItemId ?? Guid.Empty,
                    periods.FirstOrDefault(p => p.ProductId == x.ItemId && x.Date >= p.PeriodStartDate && x.Date <= p.PeriodEndDate)?.BusinessPeriodId ?? Guid.Empty)) &&
                  x.Code is "MISSING_OFFICIAL_FORECAST" or "CURRENT_PERIOD_REMAINDER_UNALLOCATABLE"))
            .Select(x => new MrpCalculationException(x.ItemId, x.Date, x.Code, x.Severity,
                x.AffectsDemand, x.Message, x.SourceReference)).ToList();
        foreach (var change in request.ForecastOverrides ?? [])
        {
            var period = periods.Single(x => x.ProductId == change.ProductId && x.BusinessPeriodId == change.BusinessPeriodId);
            var planned = period.PeriodStartDate <= run.AsOfDate && period.PeriodEndDate >= run.AsOfDate
                ? Math.Max(change.Quantity - period.ActualMtdQty, 0m) : change.Quantity;
            if (planned > 0 && Max(period.PeriodStartDate, period.EligibleAllocationStart, run.AsOfDate.AddDays(1)) >
                Min(period.PeriodEndDate, run.HorizonEndDate))
                exceptions.Add(new(change.ProductId, period.PeriodEndDate,
                    "CURRENT_PERIOD_REMAINDER_UNALLOCATABLE", MrpExceptionSeverities.Error, true,
                    "The scenario has remaining forecast but no eligible future day in the period."));
        }

        var demandDates = demands.Select(x => x.Date).Distinct().ToList();
        var availability = sourceRows.Count > 0
            ? BuildAvailability(items, boms, sourceRows, demandDates)
            : availabilityRows.Where(x => demandDates.Contains(x.DemandDate))
                .Select(x => new MrpRouteAvailability(x.ItemId, x.DemandDate, x.BomReady, x.ExternalReady)).ToList();
        return new(run.AsOfDate, run.HorizonEndDate,
            items.Select(x => new MrpItemSnapshot(x.ItemId, x.ItemCode, x.ItemType, x.NameFa, x.NameEn,
                x.IsActive, x.BaseUnitId, x.SupplyMethod, x.SafetyStockQty, x.DefaultScrapPercent)).ToList(),
            demands,
            boms.Select(x => new MrpBomEdge(x.RevisionId, x.RevisionCode, x.LineId, x.ParentItemId,
                x.ChildItemId, x.ParentBaseUnitId, x.ChildBaseUnitId, x.BaseOutputQty, x.UsageQty,
                x.ScrapOverridePercent, x.EffectiveFrom, x.EffectiveTo)).ToList(), inventory, firm, availability,
            decisionRows.Select(x => new MrpRouteChoice(x.ItemId, x.Route, x.Source)).ToList(), exceptions);
    }

    private static List<MrpRouteAvailability> BuildAvailability(IReadOnlyList<MrpRunItemInput> items,
        IReadOnlyList<MrpRunBomInput> boms, IReadOnlyList<MrpRunExternalSourceInput> sources,
        IEnumerable<DateOnly> dates)
    {
        var itemById = items.ToDictionary(x => x.ItemId); var result = new List<MrpRouteAvailability>();
        foreach (var item in items.Where(x => x.ItemType != ItemTypes.Product))
            foreach (var date in dates.Distinct())
            {
                var effective = boms.Where(x => x.ParentItemId == item.ItemId && x.EffectiveFrom <= date &&
                    (!x.EffectiveTo.HasValue || x.EffectiveTo.Value >= date)).ToList();
                var bomReady = effective.Count > 0 && effective.Select(x => x.RevisionId).Distinct().Count() == 1 &&
                    effective.All(x => x.ParentBaseUnitId == item.BaseUnitId &&
                        itemById.TryGetValue(x.ChildItemId, out var child) && child.IsActive &&
                        child.BaseUnitId == x.ChildBaseUnitId);
                var externalReady = sources.Where(x => x.ItemId == item.ItemId).Any(x => x.OptionIsActive &&
                    x.SupplierIsActive && x.ItemIsActive && x.ItemBaseUnitId == item.BaseUnitId &&
                    x.MinimumOrderQuantityIsSet && x.ShippingModeIsActive && x.IncotermIsValid &&
                    x.AllLeadStagesAreSet && x.PriceId.HasValue && x.CurrencyIsActive == true &&
                    x.EffectiveFrom <= date && (!x.EffectiveTo.HasValue || x.EffectiveTo.Value >= date));
                result.Add(new(item.ItemId, date, bomReady, externalReady));
            }
        return result;
    }

    private static void ValidateScenarioRequest(CreateWhatIfScenarioRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name)) throw MrpValidationException.Single("name", "Name is required.");
        if ((request.ForecastOverrides?.Count ?? 0) + (request.InventoryOverrides?.Count ?? 0) +
            (request.FirmSupplyOverrides?.Count ?? 0) == 0)
            throw MrpValidationException.Single("overrides", "At least one override is required.");
        if ((request.ForecastOverrides ?? []).Any(x => x.Quantity < 0) ||
            (request.InventoryOverrides ?? []).Any(x => x.OnHandQty < 0) ||
            (request.FirmSupplyOverrides ?? []).Any(x => x.OpenQty < 0))
            throw MrpValidationException.Single("overrides", "Override quantities cannot be negative.");
        if ((request.ForecastOverrides ?? []).GroupBy(x => new { x.ProductId, x.BusinessPeriodId }).Any(x => x.Count() > 1) ||
            (request.InventoryOverrides ?? []).GroupBy(x => x.ItemId).Any(x => x.Count() > 1) ||
            (request.FirmSupplyOverrides ?? []).GroupBy(x => x.DeliveryScheduleId).Any(x => x.Count() > 1))
            throw MrpValidationException.Single("overrides", "Duplicate override targets are not allowed.");
    }

    private static DateOnly Max(params DateOnly[] values) => values.Max();
    private static DateOnly Min(params DateOnly[] values) => values.Min();
    private static void EnsureVersion(long actual, long expected)
    { if (actual != expected) throw new MrpConflictException("STALE_VERSION", "Planner work item version is stale."); }
    private static void ValidatePage(int page, int pageSize)
    { if (page < 1 || pageSize is < 1 or > 100) throw MrpValidationException.Single("page", "Page must be positive and pageSize between 1 and 100."); }

    private string ActorIdentifier()
    {
        var user = accessor.HttpContext?.User;
        return user?.FindFirstValue(ClaimTypes.NameIdentifier) ?? user?.Identity?.Name ??
            (user?.HasClaim("hadron:test-authentication", "true") == true ? "integration-test" :
                throw new UnauthorizedAccessException("Authenticated actor identifier is required."));
    }
    private Guid? ActorUserId()
    {
        var user = accessor.HttpContext?.User;
        if (user?.HasClaim(SecurityClaimTypes.TestAuthentication, "true") == true) return null;
        return Guid.TryParse(user?.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;
    }
    private async Task Emit(string entityType, Guid id, string action,
        IReadOnlyDictionary<string, object?>? oldValues, IReadOnlyDictionary<string, object?>? newValues,
        CancellationToken ct)
    {
        try
        {
            await auditSink.WriteAsync(new(entityType, id, action, DateTime.UtcNow, ActorIdentifier(),
            AuditEventContext.CorrelationId(accessor.HttpContext), oldValues, newValues, null), ct);
        }
        catch (Exception exception) { logger.LogWarning(exception, "Planning collaboration audit sink failed after commit for {EntityId}", id); }
    }
}
