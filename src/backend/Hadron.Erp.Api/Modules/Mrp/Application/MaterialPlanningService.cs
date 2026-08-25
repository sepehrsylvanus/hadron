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
        var query = db.MrpRuns.AsNoTracking().Where(x => EligibleStatuses.Contains(x.Status) && x.CompletedAtUtc != null);
        var total = await query.CountAsync(ct);
        var rows = await query.OrderByDescending(x => x.CompletedAtUtc).ThenByDescending(x => x.RunNumber)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new MaterialPlanningRunItem(x.Id, x.RunNumber, x.AsOfDate, x.HorizonEndDate,
                x.EngineVersion, x.Status, x.Completeness, x.CompletedAtUtc!.Value,
                db.MrpRunExceptions.Count(e => e.RunId == x.Id))).ToListAsync(ct);
        return new(rows, page, pageSize, total);
    }

    public async Task<MaterialPlanningRunHeader?> Header(Guid runId, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var latestId = await db.MrpRuns.AsNoTracking()
            .Where(x => EligibleStatuses.Contains(x.Status) && x.CompletedAtUtc != null)
            .OrderByDescending(x => x.CompletedAtUtc).ThenByDescending(x => x.RunNumber)
            .Select(x => (Guid?)x.Id).FirstOrDefaultAsync(ct);
        return await db.MrpRuns.AsNoTracking()
            .Where(x => x.Id == runId && EligibleStatuses.Contains(x.Status) && x.CompletedAtUtc != null)
            .Select(x => new MaterialPlanningRunHeader(x.Id, x.RunNumber, x.AsOfDate, x.HorizonEndDate,
                x.EngineVersion, x.Status, x.Completeness, x.StartedAtUtc, x.SnapshotCapturedAtUtc,
                x.CompletedAtUtc!.Value, latestId == x.Id,
                db.MrpRunItemInputs.Count(i => i.RunId == x.Id &&
                    (i.ItemType == ItemTypes.Part || i.ItemType == ItemTypes.Component)),
                db.MrpRunExceptions.Count(e => e.RunId == x.Id))).SingleOrDefaultAsync(ct);
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
        var query = from summary in db.MrpRunItemSummaries.AsNoTracking()
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
        var daily = await db.MrpRunDailyResults.AsNoTracking().Where(x => x.RunId == runId &&
                x.ItemId == itemId && x.Date >= run.AsOfDate && x.Date <= run.HorizonEndDate)
            .OrderBy(x => x.Date).Select(x => new DailyDbRow(x.Date, x.GrossDemand, x.FirmIncoming,
                x.NetRequirement, x.FirmProjected, x.PlanningBalance, x.Completeness)).ToListAsync(ct);
        var normalizedGrain = (grain ?? "DAILY").Trim().ToUpperInvariant();
        if (normalizedGrain is not ("DAILY" or "WEEKLY" or "MONTHLY"))
            throw MrpValidationException.Single("grain", "Grain must be DAILY, WEEKLY or MONTHLY.");
        var buckets = Aggregate(daily, normalizedGrain, run.AsOfDate, run.HorizonEndDate,
            summary.StockoutDate, summary.SafetyBreachDate);
        return new(Item(item), normalizedGrain, item.SafetyStockQty,
            summary.StockCoverageState, summary.StockCoverageDate,
            summary.StockCoverageDays, summary.FirmCoverageState, summary.FirmCoverageDate,
            summary.FirmCoverageDays, buckets);
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

    private IReadOnlyList<MaterialPlanningTimelineBucket> Aggregate(IReadOnlyList<DailyDbRow> rows,
        string grain, DateOnly horizonStart, DateOnly horizonEnd,
        DateOnly? stockoutDate, DateOnly? safetyBreachDate)
    {
        if (grain == "DAILY") return rows.Select(x => new MaterialPlanningTimelineBucket(x.Date, x.Date,
            x.GrossDemand, x.FirmIncoming, x.NetRequirement, x.FirmProjected, x.PlanningBalance,
            x.Completeness, x.Date == stockoutDate, x.Date == safetyBreachDate)).ToList();
        var groups = rows.GroupBy(x => Bucket(x.Date, grain, horizonStart, horizonEnd)).OrderBy(x => x.Key.Start);
        return groups.Select(group =>
        {
            var closing = group.OrderBy(x => x.Date).Last();
            return new MaterialPlanningTimelineBucket(group.Key.Start, group.Key.End,
                group.Sum(x => x.GrossDemand), group.Sum(x => x.FirmIncoming),
                group.Sum(x => x.NetRequirement), closing.FirmProjected, closing.PlanningBalance,
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
    private sealed record DailyDbRow(DateOnly Date, decimal GrossDemand, decimal FirmIncoming,
        decimal NetRequirement, decimal FirmProjected, decimal PlanningBalance, string Completeness);
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
