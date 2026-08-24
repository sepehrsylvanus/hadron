using System.Data;
using Hadron.Erp.Api.Infrastructure;
using Hadron.Erp.Api.Modules.Bom.Domain;
using Hadron.Erp.Api.Modules.Catalog.Domain;
using Hadron.Erp.Api.Modules.Mrp.Domain;
using Hadron.Erp.Api.Modules.Planning.Application;
using Hadron.Erp.Api.Modules.PurchaseSupply.Domain;
using Hadron.Erp.Api.Platform.Audit;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Hadron.Erp.Api.Modules.Mrp.Application;

public sealed class MrpService(
    IDbContextFactory<HadronDbContext> factory,
    IPlanningCalendar calendar,
    MrpCalculator calculator,
    IAuditEventSink auditSink,
    IHttpContextAccessor accessor,
    ILogger<MrpService> logger)
{
    public const string EngineVersion = "MRP-10.1.0";

    public async Task<MrpRunResponse> Execute(CreateMrpRunRequest request, CancellationToken ct)
    {
        var horizon = request.HorizonEndDate ?? DefaultHorizon(request.AsOfDate);
        if (horizon < request.AsOfDate)
            throw MrpValidationException.Single("horizonEndDate", "Horizon End must be on or after As-Of.");
        ValidateDecisions(request.RouteDecisions);

        MrpRun run;
        await using (var db = await factory.CreateDbContextAsync(ct))
        {
            if (request.RouteDecisions is { Count: > 0 })
            {
                var decisionIds = request.RouteDecisions.Select(x => x.ItemId).Distinct().ToList();
                var validCount = await db.MaterialItemProfiles.CountAsync(x =>
                    decisionIds.Contains(x.ItemId) && x.SupplyMethod == SupplyMethods.Hybrid, ct);
                if (validCount != decisionIds.Count)
                    throw MrpValidationException.Single("routeDecisions",
                        "Route decisions may reference HYBRID Items only.");
            }
            run = new(await NextRunNumber(db, ct), request.AsOfDate, horizon, EngineVersion,
                Actor(), DateTime.UtcNow);
            db.MrpRuns.Add(run);
            await db.SaveChangesAsync(ct);
        }

        try
        {
            await CaptureSnapshot(run, request.RouteDecisions ?? [], ct);
            var input = await LoadCalculationInput(run.Id, ct);
            var output = calculator.Calculate(input);
            await PersistOfficialResults(run.Id, output, ct);
            var response = (await Get(run.Id, ct))!;
            await Emit(run.Id, "EXECUTE", new Dictionary<string, object?>
            {
                ["RunNumber"] = response.RunNumber,
                ["AsOfDate"] = response.AsOfDate,
                ["HorizonEndDate"] = response.HorizonEndDate,
                ["Status"] = response.Status,
                ["Completeness"] = response.Completeness
            }, ct);
            return response;
        }
        catch (Exception exception)
        {
            await MarkFailed(run.Id, exception, CancellationToken.None);
            throw;
        }
    }

    public async Task<MrpRunListResponse> List(int page, int pageSize, CancellationToken ct)
    {
        ValidatePage(page, pageSize);
        await using var db = await factory.CreateDbContextAsync(ct);
        var total = await db.MrpRuns.CountAsync(ct);
        var rows = await db.MrpRuns.AsNoTracking().OrderByDescending(x => x.StartedAtUtc)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new MrpRunListItem(x.Id, x.RunNumber, x.AsOfDate, x.HorizonEndDate,
                x.EngineVersion, x.Status, x.Completeness, x.StartedAtUtc, x.CompletedAtUtc,
                db.MrpRunExceptions.Count(e => e.RunId == x.Id))).ToListAsync(ct);
        return new(rows, page, pageSize, total);
    }

    public async Task<MrpRunResponse?> Get(Guid id, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.MrpRuns.AsNoTracking().Where(x => x.Id == id)
            .Select(x => new MrpRunResponse(x.Id, x.RunNumber, x.AsOfDate, x.HorizonEndDate,
                x.EngineVersion, x.Status, x.Completeness, x.Assumptions, x.ActorIdentifier,
                x.StartedAtUtc, x.SnapshotCapturedAtUtc, x.CompletedAtUtc, x.FailureCode, x.FailureMessage,
                db.MrpRunItemInputs.Count(i => i.RunId == x.Id), db.MrpRunDailyResults.Count(r => r.RunId == x.Id),
                db.MrpRunExceptions.Count(e => e.RunId == x.Id))).SingleOrDefaultAsync(ct);
    }

    public async Task<MrpRouteCandidateListResponse> RouteCandidates(string? search,
        int page, int pageSize, CancellationToken ct)
    {
        ValidatePage(page, pageSize);
        await using var db = await factory.CreateDbContextAsync(ct);
        var query = from item in db.Items.AsNoTracking()
                    join profile in db.MaterialItemProfiles.AsNoTracking() on item.Id equals profile.ItemId
                    where item.IsActive && profile.SupplyMethod == SupplyMethods.Hybrid
                    select item;
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(x => EF.Functions.ILike(x.BusinessNumber, $"%{term}%") ||
                EF.Functions.ILike(x.NameFa, $"%{term}%") ||
                (x.NameEn != null && EF.Functions.ILike(x.NameEn, $"%{term}%")));
        }
        var total = await query.CountAsync(ct);
        var rows = await query.OrderBy(x => x.BusinessNumber).Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new MrpRouteCandidate(x.Id, x.BusinessNumber, x.NameFa, x.NameEn)).ToListAsync(ct);
        return new(rows, page, pageSize, total);
    }

    public async Task<MrpItemSummaryListResponse> ListItems(Guid runId, string? search,
        int page, int pageSize, CancellationToken ct)
    {
        ValidatePage(page, pageSize);
        await using var db = await factory.CreateDbContextAsync(ct);
        var query = from summary in db.MrpRunItemSummaries.AsNoTracking()
                    join item in db.MrpRunItemInputs.AsNoTracking()
                        on new { summary.RunId, summary.ItemId } equals new { item.RunId, item.ItemId }
                    where summary.RunId == runId
                    select new { summary, item };
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(x => EF.Functions.ILike(x.item.ItemCode, $"%{term}%") ||
                EF.Functions.ILike(x.item.NameFa, $"%{term}%") ||
                (x.item.NameEn != null && EF.Functions.ILike(x.item.NameEn, $"%{term}%")));
        }
        var total = await query.CountAsync(ct);
        var rows = await query.OrderBy(x => x.item.ItemCode).Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new MrpItemSummaryResponse(
                new MrpItemRef(x.item.ItemId, x.item.ItemCode, x.item.NameFa, x.item.NameEn,
                    x.item.ItemType, x.item.UnitCode, x.item.UnitNameFa, x.item.UnitNameEn),
                x.summary.StartingOnHand, x.summary.TotalGrossDemand, x.summary.TotalFirmIncoming,
                x.summary.TotalNetRequirement, x.summary.OverdueFirmQty, x.summary.MissingEtaFirmQty,
                x.summary.StockoutDate, x.summary.SafetyBreachDate, x.summary.StockCoverageState,
                x.summary.StockCoverageDate, x.summary.StockCoverageDays, x.summary.FirmCoverageState,
                x.summary.FirmCoverageDate, x.summary.FirmCoverageDays, x.summary.Completeness)).ToListAsync(ct);
        return new(rows, page, pageSize, total);
    }

    public async Task<MrpItemTimelineResponse?> Timeline(Guid runId, Guid itemId, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var item = await db.MrpRunItemInputs.AsNoTracking().SingleOrDefaultAsync(x => x.RunId == runId && x.ItemId == itemId, ct);
        if (item is null) return null;
        var rows = await db.MrpRunDailyResults.AsNoTracking().Where(x => x.RunId == runId && x.ItemId == itemId)
            .OrderBy(x => x.Date).Select(x => new MrpDailyResultResponse(x.Date, x.GrossDemand, x.FirmIncoming,
                x.FirmProjected, x.PlanningBeforeNeed, x.NetRequirement, x.PlanningBalance,
                x.IsRootProduct, x.Completeness)).ToListAsync(ct);
        var trace = await db.MrpRunDemandTraces.AsNoTracking().Where(x => x.RunId == runId &&
                (x.ParentItemId == itemId || x.ChildItemId == itemId)).OrderBy(x => x.Date)
            .Select(x => new MrpTraceResponse(x.Date, x.ParentItemId, x.ChildItemId, x.BomRevisionId,
                x.BomLineId, x.ParentNetRequirement, x.BaseRequirement, x.ScrapPercent,
                x.ScrapSource, x.ChildGrossRequirement)).ToListAsync(ct);
        return new(new(item.ItemId, item.ItemCode, item.NameFa, item.NameEn, item.ItemType,
            item.UnitCode, item.UnitNameFa, item.UnitNameEn), rows, trace);
    }

    public async Task<MrpRequirementTraceResponse?> Trace(Guid runId, Guid itemId, DateOnly demandDate,
        CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var run = await db.MrpRuns.AsNoTracking().SingleOrDefaultAsync(x => x.Id == runId, ct);
        if (run is null) return null;
        var itemRows = await db.MrpRunItemInputs.AsNoTracking().Where(x => x.RunId == runId).ToListAsync(ct);
        var itemById = itemRows.ToDictionary(x => x.ItemId);
        if (!itemById.TryGetValue(itemId, out var item)) return null;
        var result = await db.MrpRunDailyResults.AsNoTracking().SingleOrDefaultAsync(x =>
            x.RunId == runId && x.ItemId == itemId && x.Date == demandDate, ct);
        var traceRows = await db.MrpRunDemandTraces.AsNoTracking()
            .Where(x => x.RunId == runId && x.Date == demandDate).ToListAsync(ct);
        var ancestry = new HashSet<Guid> { itemId };
        bool changed;
        do
        {
            changed = false;
            foreach (var edge in traceRows.Where(x => ancestry.Contains(x.ChildItemId)))
                changed |= ancestry.Add(edge.ParentItemId);
        } while (changed);

        var forecastRows = await db.MrpRunForecastInputs.AsNoTracking().Where(x => x.RunId == runId &&
            x.DemandDate == demandDate && ancestry.Contains(x.ProductId)).ToListAsync(ct);
        var forecastPeriods = await db.MrpRunForecastPeriodInputs.AsNoTracking()
            .Where(x => x.RunId == runId).ToListAsync(ct);
        var origins = forecastRows.Select(forecast =>
        {
            var period = forecastPeriods.Single(x => x.ProductId == forecast.ProductId &&
                x.BusinessPeriodId == forecast.BusinessPeriodId);
            return new MrpTraceOriginResponse(forecast.ProductId, itemById[forecast.ProductId].ItemCode,
                forecast.BusinessPeriodId, period.ForecastSeriesId, period.ForecastRevisionId,
                period.PeriodStartDate, period.PeriodEndDate, forecast.DemandDate, forecast.DemandQty,
                period.OfficialForecastQty, period.ActualMtdQty, period.RemainingForecastQty,
                period.AllocationState);
        }).ToList();
        var bomInputs = await db.MrpRunBomInputs.AsNoTracking().Where(x => x.RunId == runId).ToListAsync(ct);
        var bom = traceRows.Where(x => ancestry.Contains(x.ParentItemId) && ancestry.Contains(x.ChildItemId))
            .Select(trace =>
            {
                var snapshot = bomInputs.Single(x => x.RevisionId == trace.BomRevisionId && x.LineId == trace.BomLineId);
                return new MrpTraceBomResponse(snapshot.RevisionId, snapshot.RevisionCode, snapshot.LineId,
                    snapshot.ParentItemId, itemById[snapshot.ParentItemId].ItemCode, snapshot.ChildItemId,
                    itemById[snapshot.ChildItemId].ItemCode, snapshot.UsageQty, snapshot.BaseOutputQty,
                    trace.ScrapSource, trace.ScrapPercent, trace.ChildGrossRequirement);
            }).ToList();
        var inventoryRows = await db.MrpRunInventoryInputs.AsNoTracking()
            .Where(x => x.RunId == runId && x.ItemId == itemId).ToListAsync(ct);
        var inventory = inventoryRows.Select(x => new MrpTraceInventoryResponse(x.WarehouseId,
            x.WarehouseCode, x.WarehouseIsActive, x.UnitId, x.OnHandQty)).ToList();
        var firmRows = await db.MrpRunFirmSupplyInputs.AsNoTracking()
            .Where(x => x.RunId == runId && x.ItemId == itemId).ToListAsync(ct);
        var firm = firmRows.Select(x => new MrpTraceFirmSupplyResponse(x.DeliveryScheduleId,
            x.PurchaseOrderNumber, x.OpenQty, x.Eta, x.EtaSource, x.EtaStatus,
            x.EtaStatus != "OVERDUE" && x.Eta.HasValue && x.Eta >= run.AsOfDate && x.Eta <= demandDate)).ToList();
        var decision = await db.MrpRunRouteDecisions.AsNoTracking()
            .SingleOrDefaultAsync(x => x.RunId == runId && x.ItemId == itemId, ct);
        var exceptionCodes = await db.MrpRunExceptions.AsNoTracking().Where(x => x.RunId == runId &&
                x.ItemId == itemId && (!x.Date.HasValue || x.Date == demandDate))
            .Select(x => x.Code).Distinct().Order().ToListAsync(ct);
        var chosenRoute = decision?.Route ?? (item.ItemType == ItemTypes.Product ? MrpRoutes.Bom :
            item.SupplyMethod == SupplyMethods.Buy ? MrpRoutes.External :
            item.SupplyMethod == SupplyMethods.Make ? MrpRoutes.Bom :
            item.SupplyMethod == SupplyMethods.Outsource
                ? (bomInputs.Any(x => x.ParentItemId == itemId && x.EffectiveFrom <= demandDate &&
                    (!x.EffectiveTo.HasValue || x.EffectiveTo >= demandDate)) ? MrpRoutes.Bom : MrpRoutes.External)
                : null);
        var netting = result is null ? null : new MrpTraceNettingResponse(result.GrossDemand,
            result.FirmIncoming, result.FirmProjected - result.FirmIncoming + result.GrossDemand,
            result.FirmProjected, item.SafetyStockQty, result.PlanningBeforeNeed,
            result.NetRequirement, result.PlanningBalance);
        return new(runId, new(item.ItemId, item.ItemCode, item.NameFa, item.NameEn, item.ItemType,
                item.UnitCode, item.UnitNameFa, item.UnitNameEn), demandDate, origins, bom, inventory,
            inventoryRows.Sum(x => x.OnHandQty), inventoryRows.Any(x => !x.WarehouseIsActive && x.OnHandQty != 0),
            firm, netting, new(item.SupplyMethod, chosenRoute, decision?.Source, exceptionCodes));
    }

    public async Task<MrpExceptionListResponse> Exceptions(Guid runId, string? severity,
        int page, int pageSize, CancellationToken ct)
    {
        ValidatePage(page, pageSize);
        await using var db = await factory.CreateDbContextAsync(ct);
        var query = db.MrpRunExceptions.AsNoTracking().Where(x => x.RunId == runId);
        if (!string.IsNullOrWhiteSpace(severity)) query = query.Where(x => x.Severity == severity.Trim().ToUpperInvariant());
        var total = await query.CountAsync(ct);
        var rows = await (from exception in query
                          join item in db.MrpRunItemInputs.AsNoTracking().Where(x => x.RunId == runId)
                              on exception.ItemId equals (Guid?)item.ItemId into itemJoin
                          from item in itemJoin.DefaultIfEmpty()
                          orderby exception.Severity, exception.Date, exception.Code
                          select new MrpExceptionResponse(exception.Id, exception.ItemId,
                              item == null ? null : item.ItemCode, exception.Date, exception.Code,
                              exception.Severity, exception.AffectsDemand, exception.Message,
                              exception.SourceReference)).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return new(rows, page, pageSize, total);
    }

    private async Task CaptureSnapshot(MrpRun run, IReadOnlyList<MrpRouteDecisionInput> requestedDecisions,
        CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);

        var periodIds = await db.BusinessPeriods.AsNoTracking().Where(x => x.EndDate >= run.AsOfDate &&
                x.StartDate <= run.HorizonEndDate).Select(x => x.Id).ToListAsync(ct);
        var forecastRootIds = await db.ForecastSeries.AsNoTracking().Where(x => periodIds.Contains(x.BusinessPeriodId) &&
                x.CurrentRevisionId != null).Select(x => x.ProductId).Distinct().ToListAsync(ct);
        if (periodIds.Count == 0)
            forecastRootIds = await db.ForecastSeries.AsNoTracking().Where(x => x.CurrentRevisionId != null)
                .Select(x => x.ProductId).Distinct().ToListAsync(ct);
        var actualRootIds = await db.DailyActuals.AsNoTracking().Where(x => periodIds.Contains(x.BusinessPeriodId) &&
                x.CurrentRevisionId != null).Select(x => x.ProductId).Distinct().ToListAsync(ct);
        var rootIds = forecastRootIds.Concat(actualRootIds).Distinct().ToList();
        var productRows = await db.Items.AsNoTracking().Where(x => rootIds.Contains(x.Id) &&
                x.ItemType == ItemTypes.Product)
            .Include(x => x.BaseUnit).ToListAsync(ct);
        var initialExceptions = new List<MrpCalculationException>();
        var forecastCapture = await CaptureForecast(db, run, productRows, initialExceptions, ct);
        var bomInputs = await CaptureReachableBomInputs(db, run, productRows.Select(x => x.Id).ToList(), ct);
        var relevantItemIds = productRows.Select(x => x.Id).Concat(bomInputs.Select(x => x.ChildItemId))
            .Concat(bomInputs.Select(x => x.ParentItemId)).Distinct().ToList();
        var itemRows = await db.Items.AsNoTracking().Where(x => relevantItemIds.Contains(x.Id))
            .Include(x => x.BaseUnit).ToListAsync(ct);
        var profiles = await db.MaterialItemProfiles.AsNoTracking().Where(x => relevantItemIds.Contains(x.ItemId))
            .ToDictionaryAsync(x => x.ItemId, ct);
        var itemInputs = itemRows.Select(item =>
        {
            profiles.TryGetValue(item.Id, out var profile);
            return new MrpRunItemInput(run.Id, item.Id, item.BusinessNumber, item.ItemType, item.NameFa,
                item.NameEn, item.IsActive, item.BaseUnitId, item.BaseUnit.Code, item.BaseUnit.NameFa,
                item.BaseUnit.NameEn, profile?.SupplyMethod, profile?.SafetyStockQty ?? 0,
                profile?.DefaultPlanningScrapPercent ?? 0);
        }).ToList();
        db.MrpRunItemInputs.AddRange(itemInputs);
        db.MrpRunForecastPeriodInputs.AddRange(forecastCapture.Periods);
        db.MrpRunForecastInputs.AddRange(forecastCapture.Daily.Select(x => new MrpRunForecastInput(run.Id,
            x.ProductId, x.BusinessPeriodId, x.ForecastSeriesId, x.ForecastRevisionId, x.Date,
            x.Quantity, x.PeriodForecastQty, x.ActualMtd, x.UnitId)));
        db.MrpRunBomInputs.AddRange(bomInputs);

        var effects = await db.InventoryLedgerEffects.AsNoTracking().Where(x => relevantItemIds.Contains(x.ItemId) &&
                x.EffectiveDate <= run.AsOfDate)
            .GroupBy(x => new { x.ItemId, x.WarehouseId, x.UnitId })
            .Select(x => new { x.Key.ItemId, x.Key.WarehouseId, x.Key.UnitId, Qty = x.Sum(y => y.QuantityEffect) })
            .ToListAsync(ct);
        var warehouses = await db.Warehouses.AsNoTracking().ToDictionaryAsync(x => x.Id, ct);
        db.MrpRunInventoryInputs.AddRange(effects.Select(x => new MrpRunInventoryInput(run.Id, x.ItemId,
            x.WarehouseId, warehouses[x.WarehouseId].BusinessNumber, warehouses[x.WarehouseId].IsActive,
            x.UnitId, x.Qty)));

        var firmInputs = await CaptureFirmSupply(db, run, relevantItemIds, ct);
        db.MrpRunFirmSupplyInputs.AddRange(firmInputs.Select(x => new MrpRunFirmSupplyInput(run.Id,
            x.ItemId, x.PurchaseOrderId, x.PurchaseOrderNumber, x.PurchaseOrderLineId,
            x.DeliveryScheduleId, x.OpenQty, x.Eta, x.EtaSource, x.EtaStatus, x.PurchaseOrderStatus)));

        db.MrpRunExternalSourceInputs.AddRange(await CaptureExternalSourceInputs(db, run.Id, relevantItemIds, ct));
        foreach (var decision in requestedDecisions)
            db.MrpRunRouteDecisions.Add(new(run.Id, decision.ItemId, decision.Route.Trim().ToUpperInvariant(), "USER"));
        db.MrpRunExceptions.AddRange(initialExceptions.Select(x => Exception(run.Id, x)));
        var attachedRun = await db.MrpRuns.SingleAsync(x => x.Id == run.Id, ct);
        attachedRun.MarkSnapshotCaptured(DateTime.UtcNow);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    private async Task<ForecastCapture> CaptureForecast(HadronDbContext db, MrpRun run,
        IReadOnlyList<Item> itemRows, ICollection<MrpCalculationException> exceptions, CancellationToken ct)
    {
        var periods = await db.BusinessPeriods.AsNoTracking().Where(x => x.EndDate >= run.AsOfDate &&
            x.StartDate <= run.HorizonEndDate).OrderBy(x => x.StartDate).ToListAsync(ct);
        var series = await db.ForecastSeries.AsNoTracking().Where(x =>
                periods.Select(p => p.Id).Contains(x.BusinessPeriodId) && x.CurrentRevisionId != null)
            .Include(x => x.CurrentRevision).ToListAsync(ct);
        var actuals = await db.DailyActuals.AsNoTracking().Where(x => x.ProductionDate <= run.AsOfDate &&
                periods.Select(p => p.Id).Contains(x.BusinessPeriodId) && x.CurrentRevisionId != null)
            .Include(x => x.CurrentRevision).ToListAsync(ct);
        var products = itemRows.Where(x => x.ItemType == ItemTypes.Product).ToList();
        var result = new List<MrpProductDemand>();
        var periodSnapshots = new List<MrpRunForecastPeriodInput>();
        var expectedStart = run.AsOfDate;
        while (expectedStart <= run.HorizonEndDate)
        {
            var (jalaliYear, jalaliMonth) = calendar.GetJalaliPeriod(expectedStart);
            var expected = calendar.GetPeriodBounds(jalaliYear, jalaliMonth);
            if (products.Count > 0 && !periods.Any(x => x.JalaliYear == jalaliYear && x.JalaliMonth == jalaliMonth))
                exceptions.Add(new(null, expected.StartDate, "MISSING_BUSINESS_PERIOD",
                    MrpExceptionSeverities.Error, true,
                    $"Business Period {jalaliYear:D4}/{jalaliMonth:D2} does not exist."));
            expectedStart = expected.EndDate.AddDays(1);
        }
        foreach (var period in periods)
            foreach (var product in products)
            {
                var current = series.SingleOrDefault(x => x.ProductId == product.Id && x.BusinessPeriodId == period.Id);
                var actualMtd = period.StartDate <= run.AsOfDate && period.EndDate >= run.AsOfDate
                    ? actuals.Where(x => x.ProductId == product.Id && x.BusinessPeriodId == period.Id &&
                        x.ProductionDate <= run.AsOfDate).Sum(x => x.CurrentRevision!.ActualQty) : 0;
                var first = period.StartDate <= run.AsOfDate ? run.AsOfDate.AddDays(1) : period.StartDate;
                if (current?.CurrentRevision is null)
                {
                    exceptions.Add(new(product.Id, period.StartDate, "MISSING_FORECAST", MrpExceptionSeverities.Error,
                        true, "No official Forecast exists for Product and planning Period."));
                    periodSnapshots.Add(new(run.Id, product.Id, period.Id, null, null, null, actualMtd, null,
                        product.BaseUnitId, period.StartDate, period.EndDate, first,
                        "MISSING_FORECAST", "MISSING_FORECAST"));
                    continue;
                }
                if (current.ProductBaseUnitId != product.BaseUnitId)
                {
                    exceptions.Add(new(product.Id, period.StartDate, "BASE_UNIT_CHANGED", MrpExceptionSeverities.Error,
                        true, "Forecast unit snapshot no longer matches Product base unit."));
                    periodSnapshots.Add(new(run.Id, product.Id, period.Id, current.Id, current.CurrentRevision.Id,
                        current.CurrentRevision.ForecastQty, actualMtd, null, current.ProductBaseUnitId,
                        period.StartDate, period.EndDate, first, "BLOCKED", "BASE_UNIT_CHANGED"));
                    continue;
                }
                if (!product.IsActive)
                    exceptions.Add(new(product.Id, period.StartDate, "INACTIVE_PRODUCT_WITH_FORECAST",
                        MrpExceptionSeverities.Warning, false, "Inactive Product has an official future Forecast."));
                var quantity = period.StartDate <= run.AsOfDate && period.EndDate >= run.AsOfDate
                    ? Math.Max(current.CurrentRevision.ForecastQty - actualMtd, 0) : current.CurrentRevision.ForecastQty;
                if (first > period.EndDate)
                {
                    if (quantity > 0)
                    {
                        exceptions.Add(new(product.Id, run.AsOfDate, "CURRENT_PERIOD_REMAINDER_UNALLOCATABLE",
                            MrpExceptionSeverities.Error, true, "Current-period remainder has no eligible future day."));
                        periodSnapshots.Add(new(run.Id, product.Id, period.Id, current.Id, current.CurrentRevision.Id,
                            current.CurrentRevision.ForecastQty, actualMtd, quantity, current.ProductBaseUnitId,
                            period.StartDate, period.EndDate, first, "UNALLOCATABLE",
                            "CURRENT_PERIOD_REMAINDER_UNALLOCATABLE"));
                    }
                    else
                        periodSnapshots.Add(new(run.Id, product.Id, period.Id, current.Id, current.CurrentRevision.Id,
                            current.CurrentRevision.ForecastQty, actualMtd, quantity, current.ProductBaseUnitId,
                            period.StartDate, period.EndDate, first, "NO_ALLOCATION_REQUIRED", null));
                    continue;
                }
                periodSnapshots.Add(new(run.Id, product.Id, period.Id, current.Id, current.CurrentRevision.Id,
                    current.CurrentRevision.ForecastQty, actualMtd, quantity, current.ProductBaseUnitId,
                    period.StartDate, period.EndDate, first, quantity == 0 ? "NO_ALLOCATION_REQUIRED" : "ALLOCATED", null));
                var allocations = MrpCalculator.Distribute(quantity, first, period.EndDate);
                foreach (var allocation in allocations.Where(x => x.Date <= run.HorizonEndDate && x.Quantity != 0))
                    result.Add(new(product.Id, period.Id, current.Id, current.CurrentRevision.Id, allocation.Date,
                        allocation.Quantity, current.CurrentRevision.ForecastQty, actualMtd, current.ProductBaseUnitId));
            }
        return new(result, periodSnapshots);
    }

    private static async Task<List<MrpFirmSupply>> CaptureFirmSupply(HadronDbContext db, MrpRun run,
        IReadOnlyList<Guid> relevantItemIds, CancellationToken ct)
    {
        var orders = await db.PurchaseOrders.AsNoTracking().Where(x =>
                (x.Status == PurchaseOrderStatuses.Ordered || x.Status == PurchaseOrderStatuses.Shipped ||
                 x.Status == PurchaseOrderStatuses.Customs || x.Status == PurchaseOrderStatuses.PartiallyReceived) &&
                x.Lines.Any(line => relevantItemIds.Contains(line.ItemId)))
            .Include(x => x.Lines).ThenInclude(x => x.Schedules).ThenInclude(x => x.PlanRevisions)
            .AsSplitQuery().ToListAsync(ct);
        var result = new List<MrpFirmSupply>();
        foreach (var order in orders)
            foreach (var line in order.Lines.Where(x => relevantItemIds.Contains(x.ItemId)))
                foreach (var schedule in line.Schedules)
                {
                    var revised = schedule.PlanRevisions.Where(x => x.PlanType == PurchasePlanTypes.Revised)
                        .OrderByDescending(x => x.RevisionNumber).Select(x => x.WarehouseReceiptDate).FirstOrDefault();
                    var original = schedule.PlanRevisions.Where(x => x.PlanType == PurchasePlanTypes.Original)
                        .Select(x => x.WarehouseReceiptDate).SingleOrDefault();
                    var eta = revised ?? original;
                    var source = revised.HasValue ? "REVISED_PLANNED_WAREHOUSE_RECEIPT" :
                        original.HasValue ? "ORIGINAL_PLANNED_WAREHOUSE_RECEIPT" : "MISSING";
                    var etaStatus = !eta.HasValue ? "MISSING" : eta.Value < run.AsOfDate ? "OVERDUE" : "ON_TIME";
                    result.Add(new(line.ItemId, order.Id, order.PurchaseOrderNumber, line.Id, schedule.Id,
                        schedule.AllocatedQuantity, eta, source, etaStatus, order.Status));
                }
        return result;
    }

    private static async Task<List<MrpRunBomInput>> CaptureReachableBomInputs(HadronDbContext db,
        MrpRun run, IReadOnlyList<Guid> productRoots, CancellationToken ct)
    {
        var result = new List<MrpRunBomInput>();
        var visited = new HashSet<Guid>();
        var frontier = productRoots.Distinct().ToList();
        while (frontier.Count > 0)
        {
            var batch = frontier.Where(visited.Add).ToList();
            if (batch.Count == 0) break;
            var revisions = await db.BomRevisions.AsNoTracking().Where(x => batch.Contains(x.ParentItemId) &&
                    x.Status == BomStatuses.Approved && x.EffectiveFrom <= run.HorizonEndDate &&
                    (!x.EffectiveTo.HasValue || x.EffectiveTo >= run.AsOfDate))
                .Include(x => x.Lines).AsSplitQuery().ToListAsync(ct);
            var captured = revisions.SelectMany(revision => revision.Lines.Select(line => new MrpRunBomInput(run.Id,
                revision.Id, revision.RevisionCode, line.Id, revision.ParentItemId, line.ChildItemId,
                revision.ParentBaseUnitId, line.ChildBaseUnitId, revision.BaseOutputQty, line.UsageQty,
                line.ScrapOverridePercent, revision.EffectiveFrom!.Value, revision.EffectiveTo))).ToList();
            result.AddRange(captured);
            frontier = captured.Select(x => x.ChildItemId).Where(x => !visited.Contains(x)).Distinct().ToList();
        }
        return result;
    }

    private static async Task<List<MrpRunExternalSourceInput>> CaptureExternalSourceInputs(HadronDbContext db,
        Guid runId, IReadOnlyList<Guid> relevantItemIds, CancellationToken ct)
    {
        var options = await db.SourcingOptions.AsNoTracking().Where(x => relevantItemIds.Contains(x.ItemId))
            .Include(x => x.Supplier).Include(x => x.Item).Include(x => x.ShippingMode).Include(x => x.Incoterm)
            .Include(x => x.Prices).ThenInclude(x => x.Currency).AsSplitQuery().ToListAsync(ct);
        var result = new List<MrpRunExternalSourceInput>();
        foreach (var option in options)
        {
            var common = new
            {
                OptionActive = option.IsActive,
                SupplierActive = option.Supplier.IsActive,
                ItemActive = option.Item.IsActive,
                MoqSet = option.MinimumOrderQuantity.HasValue,
                ShippingActive = option.ShippingMode is { IsActive: true },
                IncotermValid = option.Incoterm is null || option.Incoterm.IsActive,
                LeadsSet = option.SupplierLeadTimeDays.HasValue && option.ForwarderToShipmentDays.HasValue &&
                    option.ShippingPeriodDays.HasValue && option.DestinationCustomsDays.HasValue
            };
            if (option.Prices.Count == 0)
                result.Add(new(runId, option.Id, option.ItemId, common.OptionActive, common.SupplierActive,
                    common.ItemActive, option.ItemBaseUnitId, common.MoqSet, common.ShippingActive,
                    common.IncotermValid, common.LeadsSet, null, null, null, null));
            else
                result.AddRange(option.Prices.Select(price => new MrpRunExternalSourceInput(runId, option.Id,
                    option.ItemId, common.OptionActive, common.SupplierActive, common.ItemActive,
                    option.ItemBaseUnitId, common.MoqSet, common.ShippingActive, common.IncotermValid,
                    common.LeadsSet, price.Id, price.Currency.IsActive, price.EffectiveFrom, price.EffectiveTo)));
        }
        return result;
    }

    private async Task<MrpCalculationInput> LoadCalculationInput(Guid runId, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var run = await db.MrpRuns.AsNoTracking().SingleAsync(x => x.Id == runId, ct);
        var itemRows = await db.MrpRunItemInputs.AsNoTracking().Where(x => x.RunId == runId).ToListAsync(ct);
        var forecast = await db.MrpRunForecastInputs.AsNoTracking().Where(x => x.RunId == runId).ToListAsync(ct);
        var bom = await db.MrpRunBomInputs.AsNoTracking().Where(x => x.RunId == runId).ToListAsync(ct);
        var inventory = await db.MrpRunInventoryInputs.AsNoTracking().Where(x => x.RunId == runId).ToListAsync(ct);
        var firm = await db.MrpRunFirmSupplyInputs.AsNoTracking().Where(x => x.RunId == runId).ToListAsync(ct);
        var availability = await db.MrpRunRouteAvailabilityInputs.AsNoTracking().Where(x => x.RunId == runId).ToListAsync(ct);
        var externalSources = await db.MrpRunExternalSourceInputs.AsNoTracking().Where(x => x.RunId == runId).ToListAsync(ct);
        var decisions = await db.MrpRunRouteDecisions.AsNoTracking().Where(x => x.RunId == runId).ToListAsync(ct);
        var issues = await db.MrpRunExceptions.AsNoTracking().Where(x => x.RunId == runId).ToListAsync(ct);
        return new(run.AsOfDate, run.HorizonEndDate,
            itemRows.Select(x => new MrpItemSnapshot(x.ItemId, x.ItemCode, x.ItemType, x.NameFa, x.NameEn,
                x.IsActive, x.BaseUnitId, x.SupplyMethod, x.SafetyStockQty, x.DefaultScrapPercent)).ToList(),
            forecast.Select(x => new MrpProductDemand(x.ProductId, x.BusinessPeriodId, x.ForecastSeriesId,
                x.ForecastRevisionId, x.DemandDate, x.DemandQty, x.PeriodForecastQty, x.ActualMtd, x.UnitId)).ToList(),
            bom.Select(x => new MrpBomEdge(x.RevisionId, x.RevisionCode, x.LineId, x.ParentItemId,
                x.ChildItemId, x.ParentBaseUnitId, x.ChildBaseUnitId, x.BaseOutputQty, x.UsageQty,
                x.ScrapOverridePercent, x.EffectiveFrom, x.EffectiveTo)).ToList(),
            inventory.Select(x => new MrpInventoryPosition(x.ItemId, x.WarehouseId, x.WarehouseCode,
                x.WarehouseIsActive, x.UnitId, x.OnHandQty)).ToList(),
            firm.Select(x => new MrpFirmSupply(x.ItemId, x.PurchaseOrderId, x.PurchaseOrderNumber,
                x.PurchaseOrderLineId, x.DeliveryScheduleId, x.OpenQty, x.Eta, x.EtaSource,
                x.EtaStatus, x.PurchaseOrderStatus)).ToList(),
            externalSources.Count > 0
                ? BuildRouteAvailability(itemRows, bom, externalSources, forecast.Select(x => x.DemandDate).Distinct())
                : availability.Select(x => new MrpRouteAvailability(x.ItemId, x.DemandDate, x.BomReady, x.ExternalReady)).ToList(),
            decisions.Select(x => new MrpRouteChoice(x.ItemId, x.Route, x.Source)).ToList(),
            issues.Select(x => new MrpCalculationException(x.ItemId, x.Date, x.Code, x.Severity,
                x.AffectsDemand, x.Message, x.SourceReference)).ToList());
    }

    private static List<MrpRouteAvailability> BuildRouteAvailability(
        IReadOnlyList<MrpRunItemInput> items, IReadOnlyList<MrpRunBomInput> boms,
        IReadOnlyList<MrpRunExternalSourceInput> externalSources, IEnumerable<DateOnly> dates)
    {
        var itemById = items.ToDictionary(x => x.ItemId);
        var result = new List<MrpRouteAvailability>();
        foreach (var item in items.Where(x => x.ItemType != ItemTypes.Product))
            foreach (var date in dates.Distinct())
            {
                var effective = boms.Where(x => x.ParentItemId == item.ItemId && x.EffectiveFrom <= date &&
                    (!x.EffectiveTo.HasValue || x.EffectiveTo.Value >= date)).ToList();
                var bomReady = effective.Count > 0 && effective.Select(x => x.RevisionId).Distinct().Count() == 1 &&
                    effective.All(x => x.ParentBaseUnitId == item.BaseUnitId &&
                        itemById.TryGetValue(x.ChildItemId, out var child) && child.IsActive &&
                        child.BaseUnitId == x.ChildBaseUnitId);
                var externalReady = externalSources.Where(x => x.ItemId == item.ItemId).Any(x =>
                    x.OptionIsActive && x.SupplierIsActive && x.ItemIsActive && x.ItemBaseUnitId == item.BaseUnitId &&
                    x.MinimumOrderQuantityIsSet && x.ShippingModeIsActive && x.IncotermIsValid &&
                    x.AllLeadStagesAreSet && x.PriceId.HasValue && x.CurrencyIsActive == true &&
                    x.EffectiveFrom <= date && (!x.EffectiveTo.HasValue || x.EffectiveTo.Value >= date));
                result.Add(new(item.ItemId, date, bomReady, externalReady));
            }
        return result;
    }

    private async Task PersistOfficialResults(Guid runId, MrpCalculationOutput output, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var run = await db.MrpRuns.SingleAsync(x => x.Id == runId, ct);
        if (run.Status != MrpRunStatuses.Running) throw new MrpConflictException("MRP_RUN_STATE_CHANGED", "Run is not RUNNING.");
        db.MrpRunDailyResults.AddRange(output.Daily.Select(x => new MrpRunDailyResult(runId, x.ItemId, x.Date,
            x.GrossDemand, x.FirmIncoming, x.FirmProjected, x.PlanningBeforeNeed, x.NetRequirement,
            x.PlanningBalance, x.IsRootProduct, x.Completeness)));
        db.MrpRunItemSummaries.AddRange(output.Summaries.Select(x => new MrpRunItemSummary(runId, x.ItemId,
            x.StartingOnHand, x.TotalGrossDemand, x.TotalFirmIncoming, x.TotalNetRequirement,
            x.OverdueFirmQty, x.MissingEtaFirmQty, x.StockoutDate, x.SafetyBreachDate,
            x.StockCoverageState, x.StockCoverageDate, x.StockCoverageDays,
            x.FirmCoverageState, x.FirmCoverageDate, x.FirmCoverageDays, x.Completeness)));
        db.MrpRunDemandTraces.AddRange(output.Traces.Select(x => new MrpRunDemandTrace(runId, x.Date,
            x.ParentItemId, x.ChildItemId, x.BomRevisionId, x.BomLineId, x.ParentNetRequirement,
            x.BaseRequirement, x.ScrapPercent, x.ScrapSource, x.ChildGrossRequirement)));
        var existingIssues = await db.MrpRunExceptions.Where(x => x.RunId == runId).Select(x => new
        { x.ItemId, x.Date, x.Code, x.SourceReference }).ToListAsync(ct);
        db.MrpRunExceptions.AddRange(output.Exceptions.Where(x => !existingIssues.Any(y => y.ItemId == x.ItemId &&
            y.Date == x.Date && y.Code == x.Code && y.SourceReference == x.SourceReference)).Select(x => Exception(runId, x)));
        var existingDecisions = await db.MrpRunRouteDecisions.Where(x => x.RunId == runId).Select(x => x.ItemId).ToListAsync(ct);
        db.MrpRunRouteDecisions.AddRange(output.EffectiveRouteDecisions.Where(x => !existingDecisions.Contains(x.ItemId))
            .Select(x => new MrpRunRouteDecision(runId, x.ItemId, x.Route, x.Source)));
        // Official result rows must be inserted while the run is still RUNNING. The database
        // immutability trigger intentionally rejects child writes after terminal finalization.
        await db.SaveChangesAsync(ct);
        run.Complete(output.Completeness, output.Exceptions.Count > 0, DateTime.UtcNow);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    private async Task MarkFailed(Guid runId, Exception exception, CancellationToken ct)
    {
        try
        {
            await using var db = await factory.CreateDbContextAsync(ct);
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            var run = await db.MrpRuns.SingleAsync(x => x.Id == runId, ct);
            run.Fail("MRP_EXECUTION_FAILED", exception.Message.Length > 1900 ? exception.Message[..1900] : exception.Message,
                DateTime.UtcNow);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        catch (Exception failure) { logger.LogError(failure, "Failed to mark MRP Run {RunId} as failed", runId); }
    }

    private DateOnly DefaultHorizon(DateOnly asOf)
    {
        var period = calendar.GetJalaliPeriod(asOf);
        var year = period.JalaliYear;
        var month = period.JalaliMonth;
        for (var index = 0; index < 11; index++)
        {
            month++;
            if (month <= 12) continue;
            month = 1; year++;
        }
        return calendar.GetPeriodBounds(year, month).EndDate;
    }

    private static void ValidateDecisions(IReadOnlyList<MrpRouteDecisionInput>? decisions)
    {
        if (decisions is null) return;
        if (decisions.GroupBy(x => x.ItemId).Any(x => x.Count() > 1))
            throw MrpValidationException.Single("routeDecisions", "Only one route decision per Item+Run is allowed.");
        if (decisions.Any(x => x.Route.Trim().ToUpperInvariant() is not (MrpRoutes.Bom or MrpRoutes.External)))
            throw MrpValidationException.Single("routeDecisions", "Route must be BOM or EXTERNAL.");
    }

    private static void ValidatePage(int page, int pageSize)
    {
        if (page < 1 || pageSize is < 1 or > 100)
            throw MrpValidationException.Single("page", "Page must be positive and pageSize between 1 and 100.");
    }

    private static MrpRunException Exception(Guid runId, MrpCalculationException x) =>
        new(runId, x.ItemId, x.Date, x.Code, x.Severity, x.AffectsDemand, x.Message, x.SourceReference);

    private static async Task<string> NextRunNumber(HadronDbContext db, CancellationToken ct)
    {
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open) await connection.OpenAsync(ct);
        await using var command = new NpgsqlCommand("SELECT nextval('planning.mrp_run_number_seq')", connection);
        var value = (long)(await command.ExecuteScalarAsync(ct))!;
        return $"MRP-RUN-{value:D6}";
    }

    private string? Actor() => AuditEventContext.ActorId(accessor.HttpContext);
    private async Task Emit(Guid id, string action, IReadOnlyDictionary<string, object?> values, CancellationToken ct)
    {
        try
        {
            await auditSink.WriteAsync(new("MrpRun", id, action, DateTime.UtcNow, Actor(),
                AuditEventContext.CorrelationId(accessor.HttpContext), null, values, null), ct);
        }
        catch (Exception exception) { logger.LogWarning(exception, "MRP audit sink failed after commit for {RunId}", id); }
    }

    private sealed record ForecastCapture(IReadOnlyList<MrpProductDemand> Daily,
        IReadOnlyList<MrpRunForecastPeriodInput> Periods);
}
