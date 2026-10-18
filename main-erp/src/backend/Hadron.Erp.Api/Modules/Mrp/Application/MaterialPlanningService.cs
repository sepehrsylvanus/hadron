using Hadron.Erp.Api.Infrastructure;
using Hadron.Erp.Api.Modules.Catalog.Domain;
using Hadron.Erp.Api.Modules.Mrp.Domain;
using Hadron.Erp.Api.Modules.Planning.Application;
using Microsoft.EntityFrameworkCore;

namespace Hadron.Erp.Api.Modules.Mrp.Application;

public sealed class MaterialPlanningService(
    IDbContextFactory<HadronDbContext> factory,
    IPlanningCalendar calendar,
    MrpService mrpService)
{
    private static readonly string[] EligibleStatuses =
        [MrpRunStatuses.Completed, MrpRunStatuses.CompletedWithExceptions];

    public async Task<MaterialPlanningRunListResponse> Runs(int page, int pageSize, CancellationToken ct)
    {
        ValidatePage(page, pageSize);
        await using var db = await factory.CreateDbContextAsync(ct);
        var query = db.MrpRuns.AsNoTracking().Where(x => EligibleStatuses.Contains(x.Status) &&
            x.CompletedAtUtc != null && (x.Purpose == MrpRunPurposes.Published ||
                db.MrpPlanSlots.Any(slot => slot.RunId == x.Id)));
        var total = await query.CountAsync(ct);
        var rows = await query.OrderByDescending(x => x.CompletedAtUtc).ThenByDescending(x => x.RunNumber)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new MaterialPlanningRunItem(x.Id, x.RunNumber, x.AsOfDate, x.HorizonEndDate,
                x.EngineVersion, x.Status, x.Completeness,
                db.MrpPlanSlots.Where(slot => slot.RunId == x.Id)
                    .OrderBy(slot => slot.SlotName == MrpPlanSlotNames.Current ? 0 : 1)
                    .Select(slot => slot.SlotName).FirstOrDefault() ?? x.Purpose,
                x.CompletedAtUtc!.Value,
                db.MrpRunExceptions.Count(e => e.RunId == x.Id))).ToListAsync(ct);
        return new(rows, page, pageSize, total);
    }

    public async Task<MaterialPlanningRunHeader?> Header(Guid runId, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var latestId = await db.MrpPlanSlots.AsNoTracking()
            .Where(x => x.SlotName == MrpPlanSlotNames.Current)
            .Select(x => (Guid?)x.RunId).SingleOrDefaultAsync(ct) ??
            await db.MrpRuns.AsNoTracking()
                .Where(x => EligibleStatuses.Contains(x.Status) && x.CompletedAtUtc != null)
                .OrderByDescending(x => x.CompletedAtUtc).ThenByDescending(x => x.RunNumber)
                .Select(x => (Guid?)x.Id).FirstOrDefaultAsync(ct);
        return await db.MrpRuns.AsNoTracking()
            .Where(x => x.Id == runId && EligibleStatuses.Contains(x.Status) && x.CompletedAtUtc != null)
            .Select(x => new MaterialPlanningRunHeader(x.Id, x.RunNumber, x.AsOfDate, x.HorizonEndDate,
                x.EngineVersion, x.Status, x.Completeness, x.StartedAtUtc, x.SnapshotCapturedAtUtc,
                x.CompletedAtUtc!.Value, latestId == x.Id,
                db.MrpPlanSlots.Where(slot => slot.RunId == x.Id)
                    .OrderBy(slot => slot.SlotName == MrpPlanSlotNames.Current ? 0 : 1)
                    .Select(slot => slot.SlotName).FirstOrDefault() ?? x.Purpose,
                db.MrpRunItemInputs.Count(i => i.RunId == x.Id &&
                    (i.ItemType == ItemTypes.Part || i.ItemType == ItemTypes.Component)),
            db.MrpRunExceptions.Count(e => e.RunId == x.Id))).SingleOrDefaultAsync(ct);
    }

    public async Task<MaterialPlanningRefreshStatusResponse> RefreshStatus(CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var latest = await (from slot in db.MrpPlanSlots.AsNoTracking()
                            join run in db.MrpRuns.AsNoTracking() on slot.RunId equals run.Id
                            where slot.SlotName == MrpPlanSlotNames.Current
                            select run)
            .Select(x => new
            {
                x.Id,
                x.RunNumber,
                x.AsOfDate,
                x.CompletedAtUtc,
                x.SnapshotCapturedAtUtc
            }).SingleOrDefaultAsync(ct);
        latest ??= await db.MrpRuns.AsNoTracking()
            .Where(x => EligibleStatuses.Contains(x.Status) && x.CompletedAtUtc != null)
            .OrderByDescending(x => x.CompletedAtUtc).ThenByDescending(x => x.RunNumber)
            .Select(x => new
            {
                x.Id,
                x.RunNumber,
                x.AsOfDate,
                x.CompletedAtUtc,
                x.SnapshotCapturedAtUtc
            }).FirstOrDefaultAsync(ct);
        var certified = await db.MrpDailyCertificates.AsNoTracking()
            .OrderByDescending(x => x.BusinessDate).ThenByDescending(x => x.CertifiedAtUtc)
            .Select(x => new { x.SourceRunId, x.RunNumber, x.BusinessDate, x.CertifiedAtUtc, x.ResultHash })
            .FirstOrDefaultAsync(ct);
        var lastChange = await db.MrpRefreshOutboxMessages.AsNoTracking()
            .OrderByDescending(x => x.OccurredAtUtc).Select(x => (DateTime?)x.OccurredAtUtc)
            .FirstOrDefaultAsync(ct);
        var pending = await db.MrpRefreshOutboxMessages.AsNoTracking().CountAsync(x =>
            x.Status == MrpRefreshOutboxStatuses.Pending, ct);
        var processing = await db.MrpRefreshOutboxMessages.AsNoTracking().CountAsync(x =>
            x.Status == MrpRefreshOutboxStatuses.Processing, ct);
        var lastFailure = await db.MrpRefreshOutboxMessages.AsNoTracking()
            .Where(x => x.Status == MrpRefreshOutboxStatuses.Failed)
            .OrderByDescending(x => x.CompletedAtUtc).Select(x => new
            {
                x.OccurredAtUtc,
                x.ErrorCode,
                x.ErrorMessage
            }).FirstOrDefaultAsync(ct);
        var latestSnapshot = latest?.SnapshotCapturedAtUtc;
        var failureIsCurrent = lastFailure is not null &&
            (!latestSnapshot.HasValue || lastFailure.OccurredAtUtc > latestSnapshot.Value);
        var status = processing > 0 ? "REFRESHING" : pending > 0 ? "PENDING" :
            failureIsCurrent ? "FAILED" : latest is null ? "NO_RESULT" : "UP_TO_DATE";
        return new(status, latest?.Id, latest?.RunNumber, latest?.AsOfDate, latest?.CompletedAtUtc,
            lastChange, pending + processing, failureIsCurrent ? lastFailure?.ErrorCode : null,
            failureIsCurrent ? lastFailure?.ErrorMessage : null, certified?.SourceRunId,
            certified?.RunNumber, certified?.BusinessDate, certified?.CertifiedAtUtc,
            certified?.ResultHash);
    }

    public async Task<MaterialPlanningProductListResponse> Products(Guid runId, string? search,
        int page, int pageSize, CancellationToken ct)
    {
        ValidatePage(page, pageSize);
        await EnsureEligibleRun(runId, ct);
        await using var db = await factory.CreateDbContextAsync(ct);
        var query = db.MrpRunItemInputs.AsNoTracking().Where(x => x.RunId == runId &&
            x.ItemType == ItemTypes.Product && db.MrpRunForecastPeriodInputs.Any(period =>
                period.RunId == runId && period.ProductId == x.ItemId));
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(x => EF.Functions.ILike(x.ItemCode, $"%{term}%") ||
                EF.Functions.ILike(x.NameFa, $"%{term}%") ||
                (x.NameEn != null && EF.Functions.ILike(x.NameEn, $"%{term}%")));
        }
        var total = await query.CountAsync(ct);
        var products = await query.OrderBy(x => x.ItemCode).Skip((page - 1) * pageSize)
            .Take(pageSize).ToListAsync(ct);
        var productIds = products.Select(x => x.ItemId).ToList();
        var reachable = await LoadReachableMaterials(db, runId, productIds, ct);
        var allMaterialIds = reachable.Values.SelectMany(x => x).Distinct().ToList();
        var summaries = allMaterialIds.Count == 0
            ? []
            : await SummaryQuery(db, runId).Where(x => allMaterialIds.Contains(x.ItemId)).ToListAsync(ct);
        var planned = await db.MrpRunForecastInputs.AsNoTracking().Where(x => x.RunId == runId &&
                productIds.Contains(x.ProductId)).GroupBy(x => x.ProductId)
            .Select(group => new { ProductId = group.Key, Qty = group.Sum(x => x.DemandQty) })
            .ToDictionaryAsync(x => x.ProductId, x => x.Qty, ct);
        var productExceptions = await db.MrpRunExceptions.AsNoTracking().Where(x => x.RunId == runId &&
                x.ItemId != null && productIds.Contains(x.ItemId.Value))
            .GroupBy(x => x.ItemId!.Value).Select(group => new
            {
                ProductId = group.Key,
                Code = group.OrderBy(x => x.Severity == MrpExceptionSeverities.Error ? 0 :
                    x.Severity == MrpExceptionSeverities.Warning ? 1 : 2).ThenBy(x => x.Code)
                    .Select(x => x.Code).FirstOrDefault()
            }).ToDictionaryAsync(x => x.ProductId, x => x.Code, ct);

        var rows = new List<MaterialPlanningProductRow>(products.Count);
        foreach (var product in products)
        {
            var materialRows = summaries.Where(x => reachable.GetValueOrDefault(product.ItemId, []).Contains(x.ItemId))
                .ToList();
            var ordered = materialRows.OrderBy(RiskRank).ThenBy(x => x.StockoutDate ?? DateOnly.MaxValue)
                .ThenBy(x => x.FirmCoverageDays ?? int.MaxValue).ThenBy(x => x.Code).ToList();
            var limiting = ordered.FirstOrDefault();
            var limitingRisk = limiting is null ? MaterialRiskBands.Incomplete : Risk(limiting).Risk;
            var stockCoverage = AggregateCoverage(materialRows, false);
            var firmCoverage = AggregateCoverage(materialRows, true);
            var attention = materialRows.Count(x => RiskRank(x) <= 3);
            rows.Add(new MaterialPlanningProductRow(Item(product), planned.GetValueOrDefault(product.ItemId),
                materialRows.Count, attention, stockCoverage.State, stockCoverage.Date, stockCoverage.Days,
                firmCoverage.State, firmCoverage.Date, firmCoverage.Days,
                limiting is null ? null : Item(limiting), limiting?.StockoutDate ?? limiting?.FirmCoverageDate,
                limitingRisk, limiting is null || materialRows.Any(x =>
                    x.Completeness == MrpCompleteness.Incomplete) ? MrpCompleteness.Incomplete :
                    materialRows.Any(x => x.Completeness == MrpCompleteness.CompleteWithWarnings)
                        ? MrpCompleteness.CompleteWithWarnings : MrpCompleteness.Complete,
                productExceptions.GetValueOrDefault(product.ItemId) ?? limiting?.PrimaryExceptionCode));
        }
        return new(rows, page, pageSize, total);
    }

    public async Task<MaterialPlanningSummaryListResponse> ProductMaterials(Guid runId, Guid productId,
        int page, int pageSize, CancellationToken ct)
    {
        ValidatePage(page, pageSize);
        await EnsureEligibleRun(runId, ct);
        await using var db = await factory.CreateDbContextAsync(ct);
        var productExists = await db.MrpRunItemInputs.AsNoTracking().AnyAsync(x => x.RunId == runId &&
            x.ItemId == productId && x.ItemType == ItemTypes.Product, ct);
        if (!productExists) throw new KeyNotFoundException();
        var reachable = await LoadReachableMaterials(db, runId, [productId], ct);
        var ids = reachable.GetValueOrDefault(productId, []).ToList();
        if (ids.Count == 0) return new([], page, pageSize, 0);
        var query = SummaryQuery(db, runId).Where(x => ids.Contains(x.ItemId));
        var total = await query.CountAsync(ct);
        var rows = await ApplyRiskSort(query, false).Skip((page - 1) * pageSize)
            .Take(pageSize).ToListAsync(ct);
        return new(rows.Select(ToResponse).ToList(), page, pageSize, total);
    }

    public async Task<MaterialPlanningFacetsResponse> Facets(Guid runId, CancellationToken ct)
    {
        await EnsureEligibleRun(runId, ct);
        await using var db = await factory.CreateDbContextAsync(ct);
        var material = db.MrpRunItemInputs.AsNoTracking().Where(x => x.RunId == runId &&
            (x.ItemType == ItemTypes.Part || x.ItemType == ItemTypes.Component));
        var categoryRows = await material.Where(x => x.CategoryId != null && x.CategoryCode != null &&
                x.CategoryNameFa != null)
            .Select(x => new
            {
                Id = x.CategoryId!.Value,
                Code = x.CategoryCode!,
                NameFa = x.CategoryNameFa!,
                x.CategoryNameEn
            })
            .Distinct().OrderBy(x => x.Code).ToListAsync(ct);
        var methods = await material.Where(x => x.SupplyMethod != null).Select(x => x.SupplyMethod!)
            .Distinct().OrderBy(x => x).ToListAsync(ct);
        return new(categoryRows.Select(x => new MaterialPlanningCategoryFacet(x.Id, x.Code,
            x.NameFa, x.CategoryNameEn)).ToList(), methods);
    }

    public async Task<MaterialPlanningSummaryListResponse> Summary(Guid runId, string? search,
        string? itemType, Guid? categoryId, string? supplyMethod, string? riskBand,
        string? completeness, bool? hasExceptions, DateOnly? stockoutFrom, DateOnly? stockoutTo,
        string? sortBy, string? sortDirection,
        int page, int pageSize, CancellationToken ct)
    {
        ValidatePage(page, pageSize);
        if (stockoutFrom.HasValue && stockoutTo.HasValue && stockoutFrom > stockoutTo)
            throw MrpValidationException.Single("stockoutFrom", "Stockout From must be on or before Stockout To.");
        await EnsureEligibleRun(runId, ct);
        await using var db = await factory.CreateDbContextAsync(ct);
        var query = SummaryQuery(db, runId);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(x => EF.Functions.ILike(x.Code, $"%{term}%") ||
                EF.Functions.ILike(x.NameFa, $"%{term}%") ||
                (x.NameEn != null && EF.Functions.ILike(x.NameEn, $"%{term}%")));
        }
        var normalizedType = itemType?.Trim().ToUpperInvariant();
        if (normalizedType is ItemTypes.Part or ItemTypes.Component)
            query = query.Where(x => x.ItemType == normalizedType);
        if (categoryId.HasValue) query = query.Where(x => x.CategoryId == categoryId);
        if (!string.IsNullOrWhiteSpace(supplyMethod))
        {
            var method = supplyMethod.Trim().ToUpperInvariant();
            query = query.Where(x => x.SupplyMethod == method);
        }
        if (!string.IsNullOrWhiteSpace(completeness))
        {
            var state = completeness.Trim().ToUpperInvariant();
            query = query.Where(x => x.Completeness == state);
        }
        if (hasExceptions.HasValue)
            query = hasExceptions.Value ? query.Where(x => x.ExceptionCount > 0) : query.Where(x => x.ExceptionCount == 0);
        if (stockoutFrom.HasValue) query = query.Where(x => x.StockoutDate >= stockoutFrom);
        if (stockoutTo.HasValue) query = query.Where(x => x.StockoutDate <= stockoutTo);
        if (!string.IsNullOrWhiteSpace(riskBand)) query = ApplyRiskFilter(query, riskBand.Trim().ToUpperInvariant());

        var total = await query.CountAsync(ct);
        query = ApplySort(query, sortBy, sortDirection);
        var rows = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return new(rows.Select(ToResponse).ToList(), page, pageSize, total);
    }

    public async Task<MaterialPlanningTimelineResponse?> Timeline(Guid runId, Guid itemId, string? grain,
        CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var run = await db.MrpRuns.AsNoTracking().SingleOrDefaultAsync(x => x.Id == runId &&
            EligibleStatuses.Contains(x.Status), ct);
        if (run is null) return null;
        var item = await db.MrpRunItemInputs.AsNoTracking().SingleOrDefaultAsync(x => x.RunId == runId &&
            x.ItemId == itemId && (x.ItemType == ItemTypes.Part || x.ItemType == ItemTypes.Component), ct);
        if (item is null) return null;
        var summary = await db.MrpRunItemSummaries.AsNoTracking()
            .SingleOrDefaultAsync(x => x.RunId == runId && x.ItemId == itemId, ct);
        if (summary is null) return null;
        var rawDaily = await db.MrpRunDailyResults.AsNoTracking().Where(x => x.RunId == runId &&
                x.ItemId == itemId && x.Date >= run.AsOfDate && x.Date <= run.HorizonEndDate)
            .OrderBy(x => x.Date).Select(x => new RawDailyDbRow(x.Date, x.GrossDemand, x.FirmIncoming,
                x.NetRequirement, x.FirmProjected, x.PlanningBalance, x.Completeness)).ToListAsync(ct);
        var normalizedGrain = (grain ?? "DAILY").Trim().ToUpperInvariant();
        if (normalizedGrain is not ("DAILY" or "WEEKLY" or "MONTHLY"))
            throw MrpValidationException.Single("grain", "Grain must be DAILY, WEEKLY or MONTHLY.");
        var daily = DecorateTimeline(rawDaily, summary.StartingOnHand, item.CriticalDays,
            item.SafeStartDays, item.ExcessStartDays);
        var buckets = Aggregate(daily, normalizedGrain, run.AsOfDate, run.HorizonEndDate,
            summary.StockoutDate, summary.SafetyBreachDate);
        var usedByProducts = await LoadUsageProducts(db, run, itemId, ct);
        return new(Item(item), normalizedGrain, item.SafetyStockQty,
            summary.StockCoverageState, summary.StockCoverageDate,
            summary.StockCoverageDays, summary.FirmCoverageState, summary.FirmCoverageDate,
            summary.FirmCoverageDays, usedByProducts, buckets);
    }

    public async Task<MrpRequirementTraceResponse?> Trace(Guid runId, Guid itemId, DateOnly date,
        CancellationToken ct)
    {
        await EnsureEligibleRun(runId, ct);
        return await mrpService.Trace(runId, itemId, date, ct);
    }

    public async Task<MaterialPlanningExceptionListResponse> Exceptions(Guid runId, string? severity,
        Guid? itemId, DateOnly? date, string? code, bool? affectsDemand, string? sourceModule,
        string? sortBy, string? sortDirection, int page, int pageSize, CancellationToken ct)
    {
        ValidatePage(page, pageSize);
        await EnsureEligibleRun(runId, ct);
        await using var db = await factory.CreateDbContextAsync(ct);
        var query = from exception in db.MrpRunExceptions.AsNoTracking()
                    join item in db.MrpRunItemInputs.AsNoTracking().Where(x => x.RunId == runId)
                        on exception.ItemId equals (Guid?)item.ItemId into itemJoin
                    from item in itemJoin.DefaultIfEmpty()
                    where exception.RunId == runId
                    select new ExceptionDbRow
                    {
                        Id = exception.Id,
                        ItemId = exception.ItemId,
                        ItemCode = item == null ? null : item.ItemCode,
                        Date = exception.Date,
                        Code = exception.Code,
                        Severity = exception.Severity,
                        AffectsDemand = exception.AffectsDemand,
                        Message = exception.Message,
                        SourceReference = exception.SourceReference
                    };
        if (!string.IsNullOrWhiteSpace(severity))
        {
            var normalized = severity.Trim().ToUpperInvariant();
            query = query.Where(x => x.Severity == normalized);
        }
        if (itemId.HasValue) query = query.Where(x => x.ItemId == itemId);
        if (date.HasValue) query = query.Where(x => x.Date == date);
        if (!string.IsNullOrWhiteSpace(code))
        {
            var term = code.Trim();
            query = query.Where(x => EF.Functions.ILike(x.Code, $"%{term}%"));
        }
        if (affectsDemand.HasValue) query = query.Where(x => x.AffectsDemand == affectsDemand.Value);
        if (!string.IsNullOrWhiteSpace(sourceModule)) query = ApplySourceFilter(query, sourceModule.Trim().ToUpperInvariant());
        var total = await query.CountAsync(ct);
        var descending = string.Equals(sortDirection, "desc", StringComparison.OrdinalIgnoreCase);
        query = (sortBy ?? "severity").Trim().ToLowerInvariant() switch
        {
            "date" => descending ? query.OrderByDescending(x => x.Date).ThenBy(x => x.Code) : query.OrderBy(x => x.Date).ThenBy(x => x.Code),
            "code" => descending ? query.OrderByDescending(x => x.Code) : query.OrderBy(x => x.Code),
            _ => query.OrderBy(x => x.Severity).ThenBy(x => x.Date).ThenBy(x => x.Code)
        };
        var rows = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return new(rows.Select(x => new MaterialPlanningExceptionRow(x.Id, x.ItemId, x.ItemCode,
            x.Date, x.Code, x.Severity, x.AffectsDemand, x.Message, SourceModule(x.Code),
            x.SourceReference)).ToList(), page, pageSize, total);
    }

    private IReadOnlyList<MaterialPlanningTimelineBucket> Aggregate(IReadOnlyList<TimelineDailyRow> rows,
        string grain, DateOnly horizonStart, DateOnly horizonEnd,
        DateOnly? stockoutDate, DateOnly? safetyBreachDate)
    {
        if (grain == "DAILY") return rows.Select(x => new MaterialPlanningTimelineBucket(x.Date, x.Date,
            x.GrossDemand, x.FirmIncoming, x.NetRequirement, x.FirmProjected, x.PlanningBalance,
            x.StockOnlyProjected, x.CoverageDays, x.Zone, x.Completeness,
            x.Date == stockoutDate, x.Date == safetyBreachDate)).ToList();
        var groups = rows.GroupBy(x => Bucket(x.Date, grain, horizonStart, horizonEnd)).OrderBy(x => x.Key.Start);
        return groups.Select(group =>
        {
            var closing = group.OrderBy(x => x.Date).Last();
            var zone = group.OrderBy(x => ZoneRank(x.Zone)).Select(x => x.Zone).First();
            return new MaterialPlanningTimelineBucket(group.Key.Start, group.Key.End,
                group.Sum(x => x.GrossDemand), group.Sum(x => x.FirmIncoming),
                group.Sum(x => x.NetRequirement), closing.FirmProjected, closing.PlanningBalance,
                closing.StockOnlyProjected, closing.CoverageDays, zone,
                group.Any(x => x.Completeness == MrpCompleteness.Incomplete)
                    ? MrpCompleteness.Incomplete
                    : group.Any(x => x.Completeness == MrpCompleteness.CompleteWithWarnings)
                        ? MrpCompleteness.CompleteWithWarnings : MrpCompleteness.Complete,
                stockoutDate >= group.Key.Start && stockoutDate <= group.Key.End,
                safetyBreachDate >= group.Key.Start && safetyBreachDate <= group.Key.End);
        }).ToList();
    }

    private (DateOnly Start, DateOnly End) Bucket(DateOnly date, string grain,
        DateOnly horizonStart, DateOnly horizonEnd)
    {
        DateOnly start;
        DateOnly end;
        if (grain == "WEEKLY")
        {
            var offset = ((int)date.DayOfWeek - (int)DayOfWeek.Saturday + 7) % 7;
            start = date.AddDays(-offset);
            end = start.AddDays(6);
        }
        else
        {
            var (year, month) = calendar.GetJalaliPeriod(date);
            var bounds = calendar.GetPeriodBounds(year, month);
            start = bounds.StartDate;
            end = bounds.EndDate;
        }
        return (start < horizonStart ? horizonStart : start, end > horizonEnd ? horizonEnd : end);
    }

    private static IQueryable<SummaryDbRow> SummaryQuery(HadronDbContext db, Guid runId) =>
        from summary in db.MrpRunItemSummaries.AsNoTracking()
        join item in db.MrpRunItemInputs.AsNoTracking()
            on new { summary.RunId, summary.ItemId } equals new { item.RunId, item.ItemId }
        where summary.RunId == runId &&
            (item.ItemType == ItemTypes.Part || item.ItemType == ItemTypes.Component)
        select new SummaryDbRow
        {
            ItemId = item.ItemId,
            Code = item.ItemCode,
            NameFa = item.NameFa,
            NameEn = item.NameEn,
            ItemType = item.ItemType,
            UnitCode = item.UnitCode,
            UnitNameFa = item.UnitNameFa,
            UnitNameEn = item.UnitNameEn,
            CategoryId = item.CategoryId,
            CategoryCode = item.CategoryCode,
            CategoryNameFa = item.CategoryNameFa,
            CategoryNameEn = item.CategoryNameEn,
            SupplyMethod = item.SupplyMethod,
            CriticalDays = item.CriticalDays,
            SafeStartDays = item.SafeStartDays,
            ExcessStartDays = item.ExcessStartDays,
            StartingOnHand = summary.StartingOnHand,
            TotalGrossDemand = summary.TotalGrossDemand,
            TotalFirmIncoming = summary.TotalFirmIncoming,
            TotalNetRequirement = summary.TotalNetRequirement,
            OverdueFirmQty = summary.OverdueFirmQty,
            MissingEtaFirmQty = summary.MissingEtaFirmQty,
            StockoutDate = summary.StockoutDate,
            SafetyBreachDate = summary.SafetyBreachDate,
            StockCoverageState = summary.StockCoverageState,
            StockCoverageDate = summary.StockCoverageDate,
            StockCoverageDays = summary.StockCoverageDays,
            FirmCoverageState = summary.FirmCoverageState,
            FirmCoverageDate = summary.FirmCoverageDate,
            FirmCoverageDays = summary.FirmCoverageDays,
            Completeness = summary.Completeness,
            ExceptionCount = db.MrpRunExceptions.Count(e => e.RunId == runId && e.ItemId == item.ItemId),
            PrimaryExceptionCode = db.MrpRunExceptions.Where(e => e.RunId == runId && e.ItemId == item.ItemId)
                .OrderBy(e => e.Severity == MrpExceptionSeverities.Error ? 0 :
                    e.Severity == MrpExceptionSeverities.Warning ? 1 : 2)
                .ThenBy(e => e.Code).Select(e => e.Code).FirstOrDefault()
        };

    private static async Task<Dictionary<Guid, HashSet<Guid>>> LoadReachableMaterials(
        HadronDbContext db, Guid runId, IReadOnlyCollection<Guid> rootIds, CancellationToken ct)
    {
        var run = await db.MrpRuns.AsNoTracking().Where(x => x.Id == runId)
            .Select(x => new { x.AsOfDate, x.HorizonEndDate }).SingleAsync(ct);
        var adjacency = new Dictionary<Guid, HashSet<Guid>>();
        var loadedParents = new HashSet<Guid>();
        var frontier = rootIds.Distinct().ToHashSet();
        while (frontier.Count > 0)
        {
            var current = frontier.Where(x => !loadedParents.Contains(x)).ToArray();
            if (current.Length == 0) break;
            foreach (var batch in current.Chunk(500))
            {
                var edges = await db.MrpRunBomInputs.AsNoTracking().Where(x => x.RunId == runId &&
                        batch.Contains(x.ParentItemId) && x.EffectiveFrom <= run.HorizonEndDate &&
                        (x.EffectiveTo == null || x.EffectiveTo >= run.AsOfDate))
                    .Select(x => new { x.ParentItemId, x.ChildItemId }).Distinct().ToListAsync(ct);
                foreach (var edge in edges)
                {
                    if (!adjacency.TryGetValue(edge.ParentItemId, out var children))
                        adjacency[edge.ParentItemId] = children = [];
                    children.Add(edge.ChildItemId);
                }
            }
            loadedParents.UnionWith(current);
            frontier = current.Where(adjacency.ContainsKey).SelectMany(x => adjacency[x])
                .Where(x => !loadedParents.Contains(x)).ToHashSet();
        }

        var result = rootIds.Distinct().ToDictionary(x => x, _ => new HashSet<Guid>());
        foreach (var rootId in result.Keys)
        {
            var pending = new Queue<Guid>();
            pending.Enqueue(rootId);
            var visited = new HashSet<Guid> { rootId };
            while (pending.Count > 0)
            {
                var parent = pending.Dequeue();
                if (!adjacency.TryGetValue(parent, out var children)) continue;
                foreach (var child in children)
                {
                    result[rootId].Add(child);
                    if (visited.Add(child)) pending.Enqueue(child);
                }
            }
        }
        return result;
    }

    private static async Task<IReadOnlyList<MaterialPlanningUsageProduct>> LoadUsageProducts(
        HadronDbContext db, MrpRun run, Guid itemId, CancellationToken ct)
    {
        var ancestors = new HashSet<Guid> { itemId };
        var frontier = new HashSet<Guid> { itemId };
        while (frontier.Count > 0)
        {
            var parents = new HashSet<Guid>();
            foreach (var batch in frontier.Chunk(500))
            {
                var rows = await db.MrpRunBomInputs.AsNoTracking().Where(x => x.RunId == run.Id &&
                        batch.Contains(x.ChildItemId) && x.EffectiveFrom <= run.HorizonEndDate &&
                        (x.EffectiveTo == null || x.EffectiveTo >= run.AsOfDate))
                    .Select(x => x.ParentItemId).Distinct().ToListAsync(ct);
                parents.UnionWith(rows);
            }
            parents.ExceptWith(ancestors);
            if (parents.Count == 0) break;
            ancestors.UnionWith(parents);
            frontier = parents;
        }

        return await db.MrpRunItemInputs.AsNoTracking().Where(x => x.RunId == run.Id &&
                ancestors.Contains(x.ItemId) && x.ItemType == ItemTypes.Product &&
                db.MrpRunForecastPeriodInputs.Any(period => period.RunId == run.Id &&
                    period.ProductId == x.ItemId))
            .OrderBy(x => x.ItemCode)
            .Select(x => new MaterialPlanningUsageProduct(x.ItemId, x.ItemCode, x.NameFa, x.NameEn))
            .ToListAsync(ct);
    }

    private static IReadOnlyList<TimelineDailyRow> DecorateTimeline(IReadOnlyList<RawDailyDbRow> rows,
        decimal startingOnHand, int? criticalDays, int? safeStartDays, int? excessStartDays)
    {
        var nextUncovered = new DateOnly?[rows.Count];
        DateOnly? firstNegative = null;
        for (var index = rows.Count - 1; index >= 0; index--)
        {
            if (rows[index].FirmProjected < 0) firstNegative = rows[index].Date;
            nextUncovered[index] = firstNegative;
        }

        var result = new List<TimelineDailyRow>(rows.Count);
        var stockOnly = startingOnHand;
        for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index];
            stockOnly -= row.GrossDemand;
            var coverageDays = nextUncovered[index].HasValue
                ? Math.Max(0, nextUncovered[index]!.Value.DayNumber - row.Date.DayNumber)
                : (int?)null;
            var zone = TimelineZone(row, coverageDays, criticalDays, safeStartDays, excessStartDays);
            result.Add(new TimelineDailyRow(row.Date, row.GrossDemand, row.FirmIncoming,
                row.NetRequirement, row.FirmProjected, row.PlanningBalance, stockOnly,
                coverageDays, zone, row.Completeness));
        }
        return result;
    }

    private static string TimelineZone(RawDailyDbRow row, int? coverageDays, int? criticalDays,
        int? safeStartDays, int? excessStartDays)
    {
        if (row.Completeness == MrpCompleteness.Incomplete || criticalDays is null ||
            safeStartDays is null || excessStartDays is null) return MaterialRiskBands.Incomplete;
        if (row.FirmProjected < 0 || coverageDays < criticalDays) return MaterialRiskBands.Critical;
        if (coverageDays < safeStartDays) return MaterialRiskBands.Low;
        if (coverageDays < excessStartDays) return MaterialRiskBands.Safe;
        return MaterialRiskBands.Excess;
    }

    private static int ZoneRank(string zone) => zone switch
    {
        MaterialRiskBands.Incomplete => 0,
        MaterialRiskBands.Critical => 1,
        MaterialRiskBands.Low => 2,
        MaterialRiskBands.Safe => 3,
        MaterialRiskBands.Excess => 4,
        _ => 0
    };

    private static int RiskRank(SummaryDbRow row) => Risk(row).Risk switch
    {
        MaterialRiskBands.Incomplete => 0,
        MaterialRiskBands.Stockout => 1,
        MaterialRiskBands.Critical => 2,
        MaterialRiskBands.Low => 3,
        MaterialRiskBands.Safe => 4,
        MaterialRiskBands.Excess => 5,
        MaterialRiskBands.NoFutureDemand => 6,
        MaterialRiskBands.BeyondHorizon => 7,
        _ => 0
    };

    private static CoverageView AggregateCoverage(IReadOnlyCollection<SummaryDbRow> rows, bool firm)
    {
        var demanded = rows.Where(x => x.TotalGrossDemand > 0).ToList();
        if (demanded.Count == 0) return new(MrpCoverageStates.NoFutureDemand, null, null);
        var incomplete = demanded.FirstOrDefault(x => (firm ? x.FirmCoverageState : x.StockCoverageState) ==
            MrpCoverageStates.IncompleteData);
        if (incomplete is not null) return new(MrpCoverageStates.IncompleteData, null, null);
        var dated = demanded.Select(x => new CoverageView(
                firm ? x.FirmCoverageState : x.StockCoverageState,
                firm ? x.FirmCoverageDate : x.StockCoverageDate,
                firm ? x.FirmCoverageDays : x.StockCoverageDays))
            .Where(x => x.State == MrpCoverageStates.Date)
            .OrderBy(x => x.Days ?? int.MaxValue).ThenBy(x => x.Date).FirstOrDefault();
        if (dated is not null) return dated;
        if (demanded.Any(x => (firm ? x.FirmCoverageState : x.StockCoverageState) ==
            MrpCoverageStates.BeyondHorizon)) return new(MrpCoverageStates.BeyondHorizon, null, null);
        return new(MrpCoverageStates.NoFutureDemand, null, null);
    }

    private async Task EnsureEligibleRun(Guid runId, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        if (!await db.MrpRuns.AsNoTracking().AnyAsync(x => x.Id == runId && EligibleStatuses.Contains(x.Status), ct))
            throw new KeyNotFoundException();
    }

    private static IQueryable<SummaryDbRow> ApplyRiskFilter(IQueryable<SummaryDbRow> query, string risk) => risk switch
    {
        MaterialRiskBands.Incomplete => query.Where(x => x.CategoryId == null || x.CriticalDays == null ||
            x.SafeStartDays == null || x.ExcessStartDays == null || x.Completeness == MrpCompleteness.Incomplete ||
            x.FirmCoverageState == MrpCoverageStates.IncompleteData),
        MaterialRiskBands.Stockout => query.Where(x => x.CategoryId != null && x.CriticalDays != null &&
            x.SafeStartDays != null && x.ExcessStartDays != null && x.Completeness != MrpCompleteness.Incomplete &&
            x.FirmCoverageState != MrpCoverageStates.IncompleteData && x.StockoutDate != null),
        MaterialRiskBands.NoFutureDemand => query.Where(x => x.CategoryId != null && x.CriticalDays != null &&
            x.SafeStartDays != null && x.ExcessStartDays != null && x.Completeness != MrpCompleteness.Incomplete &&
            x.FirmCoverageState != MrpCoverageStates.IncompleteData && x.StockoutDate == null &&
            x.FirmCoverageState == MrpCoverageStates.NoFutureDemand),
        MaterialRiskBands.BeyondHorizon => query.Where(x => x.CategoryId != null && x.CriticalDays != null &&
            x.SafeStartDays != null && x.ExcessStartDays != null && x.Completeness != MrpCompleteness.Incomplete &&
            x.FirmCoverageState != MrpCoverageStates.IncompleteData && x.StockoutDate == null &&
            x.FirmCoverageState == MrpCoverageStates.BeyondHorizon),
        MaterialRiskBands.Critical => query.Where(x => x.CategoryId != null && x.CriticalDays != null &&
            x.SafeStartDays != null && x.ExcessStartDays != null && x.Completeness != MrpCompleteness.Incomplete &&
            x.StockoutDate == null && x.FirmCoverageState == MrpCoverageStates.Date &&
            x.FirmCoverageDays != null && x.FirmCoverageDays < x.CriticalDays),
        MaterialRiskBands.Low => query.Where(x => x.CategoryId != null && x.CriticalDays != null &&
            x.SafeStartDays != null && x.ExcessStartDays != null && x.Completeness != MrpCompleteness.Incomplete &&
            x.StockoutDate == null && x.FirmCoverageState == MrpCoverageStates.Date &&
            x.FirmCoverageDays != null && x.FirmCoverageDays >= x.CriticalDays &&
            x.FirmCoverageDays < x.SafeStartDays),
        MaterialRiskBands.Safe => query.Where(x => x.CategoryId != null && x.CriticalDays != null &&
            x.SafeStartDays != null && x.ExcessStartDays != null && x.Completeness != MrpCompleteness.Incomplete &&
            x.StockoutDate == null && x.FirmCoverageState == MrpCoverageStates.Date &&
            x.FirmCoverageDays != null && x.FirmCoverageDays >= x.SafeStartDays &&
            x.FirmCoverageDays < x.ExcessStartDays),
        MaterialRiskBands.Excess => query.Where(x => x.CategoryId != null && x.CriticalDays != null &&
            x.SafeStartDays != null && x.ExcessStartDays != null && x.Completeness != MrpCompleteness.Incomplete &&
            x.StockoutDate == null && x.FirmCoverageState == MrpCoverageStates.Date &&
            x.FirmCoverageDays != null && x.FirmCoverageDays >= x.ExcessStartDays),
        _ => query
    };

    private static IQueryable<SummaryDbRow> ApplySort(IQueryable<SummaryDbRow> query,
        string? sortBy, string? sortDirection)
    {
        var descending = string.Equals(sortDirection, "desc", StringComparison.OrdinalIgnoreCase);
        return (sortBy ?? "risk").Trim().ToLowerInvariant() switch
        {
            "name" => descending ? query.OrderByDescending(x => x.NameFa).ThenBy(x => x.Code) : query.OrderBy(x => x.NameFa).ThenBy(x => x.Code),
            "stockout" => descending ? query.OrderByDescending(x => x.StockoutDate).ThenBy(x => x.Code) : query.OrderBy(x => x.StockoutDate).ThenBy(x => x.Code),
            "firmcoverage" => descending ? query.OrderByDescending(x => x.FirmCoverageDays).ThenBy(x => x.Code) : query.OrderBy(x => x.FirmCoverageDays).ThenBy(x => x.Code),
            "netrequirement" => descending ? query.OrderByDescending(x => x.TotalNetRequirement).ThenBy(x => x.Code) : query.OrderBy(x => x.TotalNetRequirement).ThenBy(x => x.Code),
            "code" => descending ? query.OrderByDescending(x => x.Code) : query.OrderBy(x => x.Code),
            _ => ApplyRiskSort(query, descending)
        };
    }

    private static IQueryable<SummaryDbRow> ApplyRiskSort(IQueryable<SummaryDbRow> query,
        bool descending)
    {
        var ranked = query.Select(x => new
        {
            Row = x,
            Rank = x.CategoryId == null || x.CriticalDays == null || x.SafeStartDays == null ||
                x.ExcessStartDays == null || x.Completeness == MrpCompleteness.Incomplete ||
                x.FirmCoverageState == MrpCoverageStates.IncompleteData ? 0 :
                x.StockoutDate != null ? 1 :
                x.FirmCoverageState == MrpCoverageStates.Date && x.FirmCoverageDays != null &&
                    x.FirmCoverageDays < x.CriticalDays ? 2 :
                x.FirmCoverageState == MrpCoverageStates.Date && x.FirmCoverageDays != null &&
                    x.FirmCoverageDays < x.SafeStartDays ? 3 :
                x.FirmCoverageState == MrpCoverageStates.Date && x.FirmCoverageDays != null &&
                    x.FirmCoverageDays < x.ExcessStartDays ? 4 :
                x.FirmCoverageState == MrpCoverageStates.Date && x.FirmCoverageDays != null ? 5 :
                x.FirmCoverageState == MrpCoverageStates.NoFutureDemand ? 6 :
                x.FirmCoverageState == MrpCoverageStates.BeyondHorizon ? 7 : 0
        });
        return descending
            ? ranked.OrderByDescending(x => x.Rank).ThenBy(x => x.Row.StockoutDate == null)
                .ThenBy(x => x.Row.StockoutDate).ThenBy(x => x.Row.Code).Select(x => x.Row)
            : ranked.OrderBy(x => x.Rank).ThenBy(x => x.Row.StockoutDate == null)
                .ThenBy(x => x.Row.StockoutDate).ThenBy(x => x.Row.Code).Select(x => x.Row);
    }

    private static MaterialPlanningSummaryRow ToResponse(SummaryDbRow x)
    {
        var (risk, reason) = Risk(x);
        return new(Item(x), x.StartingOnHand, x.TotalGrossDemand, x.TotalFirmIncoming,
            x.TotalNetRequirement, x.OverdueFirmQty, x.MissingEtaFirmQty, x.StockoutDate,
            x.SafetyBreachDate, x.StockCoverageState, x.StockCoverageDate, x.StockCoverageDays,
            x.FirmCoverageState, x.FirmCoverageDate, x.FirmCoverageDays, x.Completeness,
            risk, reason, x.ExceptionCount, x.PrimaryExceptionCode);
    }

    private static (string Risk, string Reason) Risk(SummaryDbRow x)
    {
        if (x.CategoryId is null || x.CriticalDays is null || x.SafeStartDays is null || x.ExcessStartDays is null)
            return (MaterialRiskBands.Incomplete, "LEGACY_RUN_CONTEXT_INCOMPLETE");
        if (x.Completeness == MrpCompleteness.Incomplete || x.FirmCoverageState == MrpCoverageStates.IncompleteData)
            return (MaterialRiskBands.Incomplete, "CALCULATION_INCOMPLETE");
        if (x.StockoutDate.HasValue) return (MaterialRiskBands.Stockout, "PROJECTED_STOCKOUT");
        if (x.FirmCoverageState == MrpCoverageStates.NoFutureDemand)
            return (MaterialRiskBands.NoFutureDemand, MrpCoverageStates.NoFutureDemand);
        if (x.FirmCoverageState == MrpCoverageStates.BeyondHorizon)
            return (MaterialRiskBands.BeyondHorizon, MrpCoverageStates.BeyondHorizon);
        if (x.FirmCoverageState != MrpCoverageStates.Date || x.FirmCoverageDays is null)
            return (MaterialRiskBands.Incomplete, "FIRM_COVERAGE_CONTEXT_INCOMPLETE");
        if (x.FirmCoverageDays < x.CriticalDays) return (MaterialRiskBands.Critical, "BELOW_CRITICAL_DAYS");
        if (x.FirmCoverageDays < x.SafeStartDays) return (MaterialRiskBands.Low, "BELOW_SAFE_START_DAYS");
        if (x.FirmCoverageDays < x.ExcessStartDays) return (MaterialRiskBands.Safe, "WITHIN_SAFE_RANGE");
        return (MaterialRiskBands.Excess, "AT_OR_ABOVE_EXCESS_START_DAYS");
    }

    private static MaterialPlanningItemRef Item(MrpRunItemInput x) => new(x.ItemId, x.ItemCode,
        x.NameFa, x.NameEn, x.ItemType, x.UnitCode, x.UnitNameFa, x.UnitNameEn, x.CategoryId,
        x.CategoryCode, x.CategoryNameFa, x.CategoryNameEn, x.SupplyMethod);
    private static MaterialPlanningItemRef Item(SummaryDbRow x) => new(x.ItemId, x.Code,
        x.NameFa, x.NameEn, x.ItemType, x.UnitCode, x.UnitNameFa, x.UnitNameEn, x.CategoryId,
        x.CategoryCode, x.CategoryNameFa, x.CategoryNameEn, x.SupplyMethod);

    private static IQueryable<ExceptionDbRow> ApplySourceFilter(IQueryable<ExceptionDbRow> query, string module) => module switch
    {
        "FORECAST" => query.Where(x => x.Code.Contains("FORECAST") || x.Code.Contains("BUSINESS_PERIOD") || x.Code.Contains("REMAINDER")),
        "BOM" => query.Where(x => x.Code.Contains("BOM") || x.Code.Contains("CYCLE")),
        "INVENTORY" => query.Where(x => x.Code.Contains("INVENTORY") || x.Code.Contains("WAREHOUSE")),
        "PURCHASE_SUPPLY" => query.Where(x => x.Code.Contains("FIRM_SUPPLY") || x.Code.Contains("ETA")),
        "CATALOG" => query.Where(x => x.Code.Contains("BASE_UNIT") || x.Code.Contains("INACTIVE_ITEM") || x.Code.Contains("SOURCE")),
        "MRP" => query.Where(x => !x.Code.Contains("FORECAST") && !x.Code.Contains("BUSINESS_PERIOD") &&
            !x.Code.Contains("REMAINDER") && !x.Code.Contains("BOM") && !x.Code.Contains("CYCLE") &&
            !x.Code.Contains("INVENTORY") && !x.Code.Contains("WAREHOUSE") && !x.Code.Contains("FIRM_SUPPLY") &&
            !x.Code.Contains("ETA") && !x.Code.Contains("BASE_UNIT") && !x.Code.Contains("INACTIVE_ITEM") &&
            !x.Code.Contains("SOURCE")),
        _ => query
    };

    private static string SourceModule(string code)
    {
        if (code.Contains("FORECAST", StringComparison.Ordinal) || code.Contains("BUSINESS_PERIOD", StringComparison.Ordinal) || code.Contains("REMAINDER", StringComparison.Ordinal)) return "FORECAST";
        if (code.Contains("BOM", StringComparison.Ordinal) || code.Contains("CYCLE", StringComparison.Ordinal)) return "BOM";
        if (code.Contains("INVENTORY", StringComparison.Ordinal) || code.Contains("WAREHOUSE", StringComparison.Ordinal)) return "INVENTORY";
        if (code.Contains("FIRM_SUPPLY", StringComparison.Ordinal) || code.Contains("ETA", StringComparison.Ordinal)) return "PURCHASE_SUPPLY";
        if (code.Contains("BASE_UNIT", StringComparison.Ordinal) || code.Contains("INACTIVE_ITEM", StringComparison.Ordinal) || code.Contains("SOURCE", StringComparison.Ordinal)) return "CATALOG";
        return "MRP";
    }

    private static void ValidatePage(int page, int pageSize)
    {
        if (page < 1 || pageSize is < 1 or > 100)
            throw MrpValidationException.Single("page", "Page must be positive and pageSize between 1 and 100.");
    }

    private sealed class SummaryDbRow
    {
        public Guid ItemId { get; init; }
        public required string Code { get; init; }
        public required string NameFa { get; init; }
        public string? NameEn { get; init; }
        public required string ItemType { get; init; }
        public required string UnitCode { get; init; }
        public required string UnitNameFa { get; init; }
        public string? UnitNameEn { get; init; }
        public Guid? CategoryId { get; init; }
        public string? CategoryCode { get; init; }
        public string? CategoryNameFa { get; init; }
        public string? CategoryNameEn { get; init; }
        public string? SupplyMethod { get; init; }
        public int? CriticalDays { get; init; }
        public int? SafeStartDays { get; init; }
        public int? ExcessStartDays { get; init; }
        public decimal StartingOnHand { get; init; }
        public decimal TotalGrossDemand { get; init; }
        public decimal TotalFirmIncoming { get; init; }
        public decimal TotalNetRequirement { get; init; }
        public decimal OverdueFirmQty { get; init; }
        public decimal MissingEtaFirmQty { get; init; }
        public DateOnly? StockoutDate { get; init; }
        public DateOnly? SafetyBreachDate { get; init; }
        public string? StockCoverageState { get; init; }
        public DateOnly? StockCoverageDate { get; init; }
        public int? StockCoverageDays { get; init; }
        public string? FirmCoverageState { get; init; }
        public DateOnly? FirmCoverageDate { get; init; }
        public int? FirmCoverageDays { get; init; }
        public required string Completeness { get; init; }
        public int ExceptionCount { get; init; }
        public string? PrimaryExceptionCode { get; init; }
    }
    private sealed record RawDailyDbRow(DateOnly Date, decimal GrossDemand, decimal FirmIncoming,
        decimal NetRequirement, decimal FirmProjected, decimal PlanningBalance, string Completeness);
    private sealed record TimelineDailyRow(DateOnly Date, decimal GrossDemand, decimal FirmIncoming,
        decimal NetRequirement, decimal FirmProjected, decimal PlanningBalance,
        decimal StockOnlyProjected, int? CoverageDays, string Zone, string Completeness);
    private sealed record CoverageView(string? State, DateOnly? Date, int? Days);
    private sealed class ExceptionDbRow
    {
        public Guid Id { get; init; }
        public Guid? ItemId { get; init; }
        public string? ItemCode { get; init; }
        public DateOnly? Date { get; init; }
        public required string Code { get; init; }
        public required string Severity { get; init; }
        public bool AffectsDemand { get; init; }
        public required string Message { get; init; }
        public string? SourceReference { get; init; }
    }
}
