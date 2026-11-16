using System.Data;
using Hadron.Erp.Api.Infrastructure;
using Hadron.Erp.Api.Modules.Bom.Domain;
using Hadron.Erp.Api.Modules.CashFlow.Domain;
using Hadron.Erp.Api.Modules.PurchaseSupply.Domain;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace Hadron.Erp.Api.Modules.CashFlow.Application;

public sealed class CashFlowQueryService(
    IDbContextFactory<HadronDbContext> factory,
    TimeProvider timeProvider)
{
    public const int MaximumPageSize = 100;
    public const int MaximumCostedBomPageSize = 50;
    private const int MaximumDashboardHorizonDays = 366;
    private const int MaximumBomGraphNodes = 5_000;
    private const int MaximumBomGraphEdges = 50_000;
    private const int MaximumBomExpandedRows = 20_000;
    private const string ReportingCurrencyCode = "USD";

    public async Task<CashFlowDashboardResponse> Dashboard(
        DateOnly asOfDate,
        DateOnly horizonEnd,
        bool includePlanned,
        CancellationToken ct)
    {
        if (horizonEnd < asOfDate)
            throw Validation("horizonEnd", "تاریخ پایان افق نباید قبل از تاریخ مبنا باشد.");
        if (horizonEnd > asOfDate.AddDays(MaximumDashboardHorizonDays))
            throw Validation("horizonEnd", "حداکثر افق مجاز برای داشبورد ۳۶۶ روز است.");

        var generatedAtUtc = timeProvider.GetUtcNow().UtcDateTime;
        var due7End = asOfDate.AddDays(7);
        var due30End = asOfDate.AddDays(30);
        var due90End = asOfDate.AddDays(90);
        await using var db = await factory.CreateDbContextAsync(ct);
        await using var transaction = await db.Database.BeginTransactionAsync(
            IsolationLevel.RepeatableRead, ct);
        var fx = await LoadReportingFx(db, asOfDate, ct);
        var visible = CreateObligationQuery(db, asOfDate, includePlanned, null, null, null)
            .Where(x => !x.ExpectedPaymentDate.HasValue ||
                x.ExpectedPaymentDate.Value <= horizonEnd);

        var currencyRows = await visible
            .GroupBy(x => new
            {
                x.CurrencyId,
                x.CurrencyCode,
                x.CurrencyDecimalPrecision
            })
            .Select(group => new CashFlowCurrencySummaryDbRow
            {
                CurrencyId = group.Key.CurrencyId,
                CurrencyCode = group.Key.CurrencyCode,
                DecimalPrecision = group.Key.CurrencyDecimalPrecision,
                OverdueAmount = group.Sum(x => x.ExpectedPaymentDate.HasValue &&
                    x.ExpectedPaymentDate.Value < asOfDate ? x.ProjectedAmount : 0m),
                Due7DaysAmount = group.Sum(x => x.ExpectedPaymentDate.HasValue &&
                    x.ExpectedPaymentDate.Value >= asOfDate &&
                    x.ExpectedPaymentDate.Value <= due7End ? x.ProjectedAmount : 0m),
                Due30DaysAmount = group.Sum(x => x.ExpectedPaymentDate.HasValue &&
                    x.ExpectedPaymentDate.Value >= asOfDate &&
                    x.ExpectedPaymentDate.Value <= due30End ? x.ProjectedAmount : 0m),
                Due90DaysAmount = group.Sum(x => x.ExpectedPaymentDate.HasValue &&
                    x.ExpectedPaymentDate.Value >= asOfDate &&
                    x.ExpectedPaymentDate.Value <= due90End ? x.ProjectedAmount : 0m),
                HorizonAmount = group.Sum(x => x.ExpectedPaymentDate.HasValue &&
                    x.ExpectedPaymentDate.Value >= asOfDate &&
                    x.ExpectedPaymentDate.Value <= horizonEnd ? x.ProjectedAmount : 0m),
                ObligationCount = group.Count()
            })
            .OrderBy(x => x.CurrencyCode)
            .ThenBy(x => x.CurrencyId)
            .ToArrayAsync(ct);

        var bucketRows = await visible
            .Where(x => x.ExpectedPaymentDate.HasValue &&
                x.ExpectedPaymentDate.Value >= asOfDate &&
                x.ExpectedPaymentDate.Value <= horizonEnd)
            .GroupBy(x => new
            {
                Date = x.ExpectedPaymentDate!.Value,
                x.CurrencyId,
                x.CurrencyCode
            })
            .Select(group => new CashFlowBucketDbRow
            {
                Date = group.Key.Date,
                CurrencyId = group.Key.CurrencyId,
                CurrencyCode = group.Key.CurrencyCode,
                Amount = group.Sum(x => x.ProjectedAmount),
                Count = group.Count()
            })
            .OrderBy(x => x.Date)
            .ThenBy(x => x.CurrencyCode)
            .ThenBy(x => x.CurrencyId)
            .ToArrayAsync(ct);

        var unresolvedCount = await visible.CountAsync(x => !x.ExpectedPaymentDate.HasValue, ct);
        await transaction.CommitAsync(ct);

        var currencies = currencyRows.Select(x => new CashFlowCurrencySummary(
            x.CurrencyId, x.CurrencyCode, x.DecimalPrecision, x.OverdueAmount,
            x.Due7DaysAmount, x.Due30DaysAmount, x.Due90DaysAmount, x.HorizonAmount,
            x.ObligationCount)).ToArray();
        var buckets = bucketRows.Select(x => new CashFlowBucket(
            x.Date, x.CurrencyId, x.CurrencyCode, x.Amount, x.Count)).ToArray();
        var fxContext = BuildFxContext(fx, currencyRows.Select(x =>
            new CurrencyKey(x.CurrencyId, x.CurrencyCode)));
        var usdSummary = new CashFlowUsdSummary(
            ConvertToUsd(currencyRows, x => x.OverdueAmount, fx),
            ConvertToUsd(currencyRows, x => x.Due7DaysAmount, fx),
            ConvertToUsd(currencyRows, x => x.Due30DaysAmount, fx),
            ConvertToUsd(currencyRows, x => x.Due90DaysAmount, fx),
            ConvertToUsd(currencyRows, x => x.HorizonAmount, fx));
        var usdBuckets = bucketRows.GroupBy(x => x.Date)
            .OrderBy(x => x.Key)
            .Select(group => new CashFlowUsdBucket(group.Key,
                ConvertToUsd(group, x => x.Amount, fx), group.Sum(x => x.Count)))
            .ToArray();

        return new(asOfDate, horizonEnd, includePlanned, generatedAtUtc,
            CashFlowDataSemantics.CurrentValidFacts, currencies, buckets,
            unresolvedCount, fxContext, usdSummary, usdBuckets);
    }

    public async Task<CashFlowObligationPage> Obligations(
        DateOnly asOfDate,
        DateOnly? fromDate,
        DateOnly? toDate,
        bool includePlanned,
        string? search,
        Guid? supplierId,
        string? currencyCode,
        string? stage,
        string? state,
        int page,
        int pageSize,
        CancellationToken ct)
    {
        ValidatePage(page, pageSize, MaximumPageSize);
        if (fromDate.HasValue && toDate.HasValue && fromDate > toDate)
            throw Validation("dateRange", "ابتدای بازه نباید بعد از انتهای بازه باشد.");

        var normalizedStage = NormalizeFilter(stage);
        if (normalizedStage is not null && !CashFlowObligationStages.All.Contains(normalizedStage))
            throw Validation("stage", "مرحله پرداخت معتبر نیست.");
        var normalizedState = NormalizeFilter(state);
        if (normalizedState is not null && !CashFlowObligationStates.All.Contains(normalizedState))
            throw Validation("state", "وضعیت تعهد معتبر نیست.");

        await using var db = await factory.CreateDbContextAsync(ct);
        await using var transaction = await db.Database.BeginTransactionAsync(
            IsolationLevel.RepeatableRead, ct);
        var fx = await LoadReportingFx(db, asOfDate, ct);
        IQueryable<CashFlowObligationDbRow> query = CreateObligationQuery(db, asOfDate,
            includePlanned, search, supplierId, NormalizeFilter(currencyCode));
        if (normalizedStage is not null) query = query.Where(x => x.Stage == normalizedStage);
        if (normalizedState is not null) query = query.Where(x => x.State == normalizedState);
        var unresolvedOnly = normalizedState == CashFlowObligationStates.Unresolved;
        if (fromDate.HasValue && !unresolvedOnly)
            query = query.Where(x => x.ExpectedPaymentDate.HasValue &&
                x.ExpectedPaymentDate.Value >= fromDate.Value);
        if (toDate.HasValue && !unresolvedOnly)
            query = query.Where(x => x.ExpectedPaymentDate.HasValue &&
                x.ExpectedPaymentDate.Value <= toDate.Value);

        var currencyTotals = await query.GroupBy(x => new
            {
                x.CurrencyId,
                x.CurrencyCode
            })
            .Select(group => new CashFlowCurrencyAmountDbRow
            {
                CurrencyId = group.Key.CurrencyId,
                CurrencyCode = group.Key.CurrencyCode,
                Amount = group.Sum(x => x.ProjectedAmount),
                Count = group.Count()
            })
            .ToArrayAsync(ct);
        var total = currencyTotals.Sum(x => x.Count);
        var rows = await query.OrderBy(x => x.ExpectedPaymentDate.HasValue ? 0 : 1)
            .ThenBy(x => x.ExpectedPaymentDate)
            .ThenBy(x => x.SupplierNumber)
            .ThenBy(x => x.PurchaseOrderNumber)
            .ThenBy(x => x.Stage)
            .ThenBy(x => x.PurchaseOrderLineNumber)
            .ThenBy(x => x.ScheduleNumber)
            .ThenBy(x => x.ObligationKey)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToArrayAsync(ct);
        await transaction.CommitAsync(ct);
        var items = rows.Select(row => ToResponse(row, fx)).ToArray();
        var fxContext = BuildFxContext(fx, currencyTotals.Select(x =>
            new CurrencyKey(x.CurrencyId, x.CurrencyCode)));
        var totalUsdAmount = ConvertToUsd(currencyTotals, x => x.Amount, fx);
        return new(items, page, pageSize, total, asOfDate, timeProvider.GetUtcNow().UtcDateTime,
            CashFlowDataSemantics.CurrentValidFacts, fxContext, totalUsdAmount);
    }

    public async Task<CostedBomPage> CostedBoms(
        DateOnly asOfDate,
        string? search,
        int page,
        int pageSize,
        string? costBasisMode,
        Guid? priceBookRevisionId,
        CancellationToken ct)
    {
        ValidatePage(page, pageSize, MaximumCostedBomPageSize);
        await using var db = await factory.CreateDbContextAsync(ct);
        await using var transaction = await db.Database.BeginTransactionAsync(
            IsolationLevel.RepeatableRead, ct);
        var query = db.BomRevisions.AsNoTracking().Where(x => x.Status == BomStatuses.Approved);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = $"%{search.Trim()}%";
            query = query.Where(x => EF.Functions.ILike(x.ParentItem.BusinessNumber, pattern) ||
                EF.Functions.ILike(x.ParentItem.NameFa, pattern) ||
                (x.ParentItem.NameEn != null && EF.Functions.ILike(x.ParentItem.NameEn, pattern)) ||
                EF.Functions.ILike(x.RevisionCode, pattern));
        }

        var total = await query.CountAsync(ct);
        var roots = await query.OrderBy(x => x.ParentItem.BusinessNumber)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => x.ParentItemId).ToArrayAsync(ct);
        var generatedAtUtc = timeProvider.GetUtcNow().UtcDateTime;
        var context = await LoadCostingContext(db, roots, asOfDate,
            costBasisMode, priceBookRevisionId, ct);
        var memo = new Dictionary<Guid, BomUnitSummary>();
        var budget = new BomGraphBudget();
        var summaries = roots.Select(root => BuildCostedBomSummary(root, asOfDate,
            context, memo, budget)).ToArray();
        var fxContext = BuildFxContext(context.Fx, summaries
            .SelectMany(x => x.CurrencyTotals)
            .Select(x => new CurrencyKey(x.CurrencyId, x.CurrencyCode)));
        await transaction.CommitAsync(ct);
        return new(summaries, page, pageSize, total, asOfDate, generatedAtUtc,
            ValuationSemantics(context.CostBasisSelection), context.CostBasisSelection, fxContext);
    }

    public async Task<CostedBomResponse> CostedBom(Guid parentItemId, DateOnly asOfDate,
        string? costBasisMode, Guid? priceBookRevisionId,
        CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        await using var transaction = await db.Database.BeginTransactionAsync(
            IsolationLevel.RepeatableRead, ct);
        var exists = await db.BomRevisions.AsNoTracking().AnyAsync(x =>
            x.ParentItemId == parentItemId && x.Status == BomStatuses.Approved, ct);
        if (!exists) throw new KeyNotFoundException();
        var context = await LoadCostingContext(db, [parentItemId], asOfDate,
            costBasisMode, priceBookRevisionId, ct);
        var response = BuildCostedBom(parentItemId, asOfDate,
            timeProvider.GetUtcNow().UtcDateTime, context);
        await transaction.CommitAsync(ct);
        return response;
    }

    private static IQueryable<CashFlowObligationDbRow> CreateObligationQuery(
        HadronDbContext db,
        DateOnly asOfDate,
        bool includePlanned,
        string? search,
        Guid? supplierId,
        string? currencyCode)
    {
        var searchPattern = string.IsNullOrWhiteSpace(search) ? null : $"%{search.Trim()}%";
        return db.Database.SqlQueryRaw<CashFlowObligationDbRow>(ObligationProjectionSql,
            new NpgsqlParameter<DateOnly>("asOfDate", asOfDate),
            new NpgsqlParameter<bool>("includePlanned", includePlanned),
            NullableParameter("searchPattern", NpgsqlDbType.Text, searchPattern),
            NullableParameter("supplierId", NpgsqlDbType.Uuid, supplierId),
            NullableParameter("currencyCode", NpgsqlDbType.Text, currencyCode));
    }

    private static CashFlowObligationRow ToResponse(CashFlowObligationDbRow row,
        ReportingFxSelection fx) => new(
        row.ObligationKey,
        row.ExpectedPaymentDate,
        row.PurchaseOrderId,
        row.PurchaseOrderNumber,
        row.PurchaseOrderStatus,
        row.PurchaseOrderLineId,
        row.PurchaseOrderLineNumber,
        row.ItemId,
        row.ItemCode,
        row.ItemNameFa,
        row.ItemNameEn,
        row.SupplierId,
        row.SupplierNumber,
        row.SupplierName,
        row.CurrencyId,
        row.CurrencyCode,
        row.CurrencyDecimalPrecision,
        row.ProjectedAmount,
        row.Stage,
        row.Confidence,
        row.DateSource,
        row.State,
        row.AnchorCode,
        row.DeliveryScheduleId,
        row.ScheduleNumber,
        UnitsPerUsd(fx, row.CurrencyId, row.CurrencyCode),
        ConvertToUsd(row.ProjectedAmount, row.CurrencyId, row.CurrencyCode, fx));

    private static async Task<ReportingFxSelection> LoadReportingFx(
        HadronDbContext db,
        DateOnly asOfDate,
        CancellationToken ct)
    {
        var rows = await db.Database.SqlQueryRaw<ReportingFxDbRow>(ReportingFxSelectionSql,
                new NpgsqlParameter<DateOnly>("asOfDate", asOfDate),
                new NpgsqlParameter<string>("publishedStatus",
                    ReportingFxRateSetStatuses.Published),
                new NpgsqlParameter<string>("reportingCurrencyCode", ReportingCurrencyCode))
            .ToArrayAsync(ct);
        var header = rows[0];
        if (!header.RateSetId.HasValue)
            return new(null, null, null, new Dictionary<Guid, decimal>());
        var rates = rows.Where(x => x.CurrencyId.HasValue && x.UnitsPerUsd.HasValue)
            .ToDictionary(x => x.CurrencyId!.Value, x => x.UnitsPerUsd!.Value);
        return new(header.RateSetId, header.EffectiveDate, header.Revision, rates);
    }

    private static FxConversionContext BuildFxContext(
        ReportingFxSelection fx,
        IEnumerable<CurrencyKey> currencies)
    {
        var missing = currencies
            .DistinctBy(x => x.CurrencyId)
            .Where(x => !UnitsPerUsd(fx, x.CurrencyId, x.CurrencyCode).HasValue)
            .Select(x => x.CurrencyCode)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToArray();
        var completeness = !fx.RateSetId.HasValue
            ? FxConversionCompleteness.MissingSet
            : missing.Length > 0
                ? FxConversionCompleteness.Incomplete
                : FxConversionCompleteness.Complete;
        return new(ReportingCurrencyCode, fx.RateSetId, fx.EffectiveDate, fx.Revision,
            completeness, missing);
    }

    private static decimal? UnitsPerUsd(ReportingFxSelection fx, Guid currencyId,
        string currencyCode)
    {
        if (string.Equals(currencyCode, ReportingCurrencyCode,
                StringComparison.OrdinalIgnoreCase)) return 1m;
        return fx.Rates.TryGetValue(currencyId, out var rate) ? rate : null;
    }

    private static decimal? ConvertToUsd(decimal amount, Guid currencyId,
        string currencyCode, ReportingFxSelection fx)
    {
        var rate = UnitsPerUsd(fx, currencyId, currencyCode);
        if (!rate.HasValue) return null;
        try
        {
            return amount / rate.Value;
        }
        catch (OverflowException)
        {
            throw FxConversionOutOfRange(currencyCode);
        }
    }

    private static decimal? ConvertToUsd<T>(IEnumerable<T> rows,
        Func<T, decimal> amountSelector,
        ReportingFxSelection fx)
        where T : ICurrencyAmountSource
    {
        var total = 0m;
        foreach (var row in rows)
        {
            var amount = amountSelector(row);
            // A zero monetary contribution needs no exchange rate. The conversion
            // context still reports that currency as missing for data readiness.
            if (amount == 0m) continue;
            var converted = ConvertToUsd(amount, row.CurrencyId, row.CurrencyCode, fx);
            if (!converted.HasValue) return null;
            total = AddUsd(total, converted.Value, row.CurrencyCode);
        }

        return total;
    }

    private static CostedBomCurrencyTotal CurrencyTotal(MutableCurrencyTotal total,
        ReportingFxSelection fx)
    {
        var rate = UnitsPerUsd(fx, total.CurrencyId, total.CurrencyCode);
        return new(total.CurrencyId, total.CurrencyCode, total.DecimalPrecision,
            total.MaterialCost, rate,
            ConvertToUsd(total.MaterialCost, total.CurrencyId, total.CurrencyCode, fx));
    }

    private static decimal? SumConvertedCurrencyTotals(
        IEnumerable<CostedBomCurrencyTotal> totals)
    {
        var result = 0m;
        foreach (var total in totals)
        {
            if (!total.MaterialCostUsd.HasValue) return null;
            result = AddUsd(result, total.MaterialCostUsd.Value, total.CurrencyCode);
        }

        return result;
    }

    private static decimal AddUsd(decimal current, decimal amount, string sourceCurrencyCode)
    {
        try
        {
            return current + amount;
        }
        catch (OverflowException)
        {
            throw FxConversionOutOfRange(sourceCurrencyCode);
        }
    }

    private static CashFlowConflictException FxConversionOutOfRange(string sourceCurrencyCode) =>
        new("FX_CONVERSION_OUT_OF_RANGE",
            $"مبلغ تبدیل‌شده ارز {sourceCurrencyCode} از محدوده عددی ایمن گزارش USD خارج است؛ نرخ یا مبلغ مبدأ را بررسی کنید.");

    private async Task<CostingContext> LoadCostingContext(HadronDbContext db,
        IReadOnlyCollection<Guid> rootIds, DateOnly asOfDate, string? costBasisMode,
        Guid? priceBookRevisionId, CancellationToken ct)
    {
        var normalizedCostBasisMode = NormalizeCostBasisMode(costBasisMode, priceBookRevisionId);
        var fx = await LoadReportingFx(db, asOfDate, ct);
        var revisions = new Dictionary<Guid, BomContext>();
        var expanded = new HashSet<Guid>();
        var frontier = new HashSet<Guid>(rootIds);
        var edgeCount = 0;
        while (frontier.Count > 0)
        {
            var parentIds = frontier.Where(expanded.Add).ToArray();
            frontier.Clear();
            if (parentIds.Length == 0) break;
            if (expanded.Count > MaximumBomGraphNodes)
                throw BomGraphLimitExceeded();
            var rows = await db.BomRevisions.AsNoTracking()
                .Where(x => parentIds.Contains(x.ParentItemId) && x.Status == BomStatuses.Approved)
                .Select(x => new BomContext(x.Id, x.ParentItemId, x.ParentBaseUnitId,
                    x.RevisionCode, x.BaseOutputQty,
                    x.Lines.OrderBy(line => line.SortOrder).ThenBy(line => line.Id)
                        .Select(line => new BomLineContext(line.Id, line.ChildItemId,
                            line.ChildBaseUnitId, line.UsageQty, line.ScrapOverridePercent,
                            line.SortOrder)).ToArray()))
                .ToArrayAsync(ct);
            foreach (var group in rows.GroupBy(x => x.ParentItemId))
            {
                if (group.Count() != 1)
                    throw new CashFlowConflictException("MULTIPLE_CURRENT_BOM_REVISIONS",
                        "برای یک قلم بیش از یک BOM تأییدشده جاری وجود دارد.");
                var revision = group.Single();
                edgeCount += revision.Lines.Length;
                if (edgeCount > MaximumBomGraphEdges)
                    throw BomGraphLimitExceeded();
                revisions.Add(revision.ParentItemId, revision);
                foreach (var childId in revision.Lines.Select(x => x.ChildItemId))
                    if (!expanded.Contains(childId)) frontier.Add(childId);
            }
        }

        foreach (var rootId in rootIds)
            if (!revisions.ContainsKey(rootId)) throw new KeyNotFoundException();

        var itemIds = revisions.Keys.Concat(revisions.Values.SelectMany(x => x.Lines)
                .Select(x => x.ChildItemId)).Distinct().ToArray();
        var items = await db.Items.AsNoTracking().Where(x => itemIds.Contains(x.Id))
            .Select(x => new ItemContext(x.Id, x.BusinessNumber, x.NameFa, x.NameEn,
                x.BaseUnitId)).ToDictionaryAsync(x => x.Id, ct);
        var defaultScrap = await db.MaterialItemProfiles.AsNoTracking()
            .Where(x => itemIds.Contains(x.ItemId))
            .ToDictionaryAsync(x => x.ItemId, x => x.DefaultPlanningScrapPercent, ct);
        var unitIds = items.Values.Select(x => x.BaseUnitId)
            .Concat(revisions.Values.Select(x => x.ParentBaseUnitId))
            .Concat(revisions.Values.SelectMany(x => x.Lines).Select(x => x.ChildBaseUnitId))
            .Distinct().ToArray();
        var units = await db.Units.AsNoTracking().Where(x => unitIds.Contains(x.Id))
            .Select(x => new UnitContext(x.Id, x.Code))
            .ToDictionaryAsync(x => x.Id, ct);
        var leafIds = itemIds.Where(x => !revisions.TryGetValue(x, out var revision) ||
            revision.Lines.Length == 0).ToArray();
        IReadOnlyDictionary<Guid, CostBasisContext> costBases;
        CostBasisSelectionContext selection;
        if (normalizedCostBasisMode == MaterialCostBookCostBasisModes.MaterialCostBook)
        {
            (costBases, selection) = await LoadMaterialCostBookCostBases(
                db, leafIds, asOfDate, priceBookRevisionId!.Value, ct);
        }
        else
        {
            costBases = await LoadPurchaseCostBases(db, leafIds, asOfDate, ct);
            selection = PurchaseHistorySelection();
        }
        return new(revisions, items, units, defaultScrap, costBases, fx, selection);
    }

    private static CashFlowConflictException BomGraphLimitExceeded() => new(
        "COSTED_BOM_GRAPH_LIMIT_EXCEEDED",
        $"ساختار BOM از سقف ایمن {MaximumBomGraphNodes:N0} قلم یا {MaximumBomGraphEdges:N0} رابطه عبور کرده است.");

    private static async Task<IReadOnlyDictionary<Guid, CostBasisContext>> LoadPurchaseCostBases(
        HadronDbContext db,
        Guid[] itemIds,
        DateOnly asOfDate,
        CancellationToken ct)
    {
        if (itemIds.Length == 0) return new Dictionary<Guid, CostBasisContext>();
        var receiptRows = await db.Database.SqlQueryRaw<CostBasisDbRow>(ReceiptCostBasisSql,
            GuidArrayParameter(itemIds), new NpgsqlParameter<DateOnly>("asOfDate", asOfDate))
            .ToArrayAsync(ct);
        var result = receiptRows.ToDictionary(x => x.ItemId, CostBasis);
        var remaining = itemIds.Where(x => !result.ContainsKey(x)).ToArray();
        if (remaining.Length == 0) return result;
        var estimateRows = await db.Database.SqlQueryRaw<CostBasisDbRow>(PurchaseOrderCostBasisSql,
            GuidArrayParameter(remaining), new NpgsqlParameter<DateOnly>("asOfDate", asOfDate))
            .ToArrayAsync(ct);
        foreach (var row in estimateRows) result[row.ItemId] = CostBasis(row);
        return result;
    }

    private static async Task<(IReadOnlyDictionary<Guid, CostBasisContext> CostBases,
        CostBasisSelectionContext Selection)> LoadMaterialCostBookCostBases(
        HadronDbContext db, Guid[] itemIds, DateOnly asOfDate, Guid revisionId,
        CancellationToken ct)
    {
        var revision = await db.MaterialCostBookRevisions.AsNoTracking()
            .Include(x => x.MaterialCostBook)
            .SingleOrDefaultAsync(x => x.Id == revisionId, ct)
            ?? throw new KeyNotFoundException();
        if (revision.Status != MaterialCostBookRevisionStatuses.Published)
            throw new CashFlowConflictException("PRICE_BOOK_NOT_PUBLISHED",
                "نسخه انتخاب‌شده دفتر بهای مواد هنوز منتشر نشده است.");
        if (revision.EffectiveDate > asOfDate)
            throw new CashFlowConflictException("PRICE_BOOK_NOT_EFFECTIVE",
                "تاریخ اثر نسخه انتخاب‌شده دفتر بهای مواد بعد از تاریخ مبنای محاسبه است.");
        var rows = await db.MaterialCostBookLines.AsNoTracking()
            .Where(x => x.MaterialCostBookRevisionId == revisionId &&
                itemIds.Contains(x.ItemId))
            .ToArrayAsync(ct);
        var result = rows.ToDictionary(x => x.ItemId, x => new CostBasisContext(
            CostBasisKinds.MaterialCostBook,
            x.NormalizedUnitPrice,
            x.CurrencyId,
            x.CurrencyCode,
            x.CurrencyDecimalPrecision,
            x.ItemBaseUnitId,
            x.ItemBaseUnitCode,
            revision.Id,
            revision.MaterialCostBook.BookNumber,
            revision.EffectiveDate,
            null,
            null,
            x.SupplierId,
            x.SupplierNumber,
            x.SupplierName,
            x.OriginalPriceAmount,
            x.PriceBasisQuantity,
            x.PriceUnitId,
            x.PriceUnitCode,
            x.PriceUnitFactorToBase,
            revision.Id));
        var selection = new CostBasisSelectionContext(
            MaterialCostBookCostBasisModes.MaterialCostBook,
            "CURRENT_APPROVED_BOM_STRUCTURE_NOT_MRP_ROUTE_AWARE",
            revision.MaterialCostBookId,
            revision.MaterialCostBook.BookNumber,
            revision.Id,
            revision.Revision,
            revision.NameFa,
            revision.NameEn,
            revision.EffectiveDate,
            revision.PublishedAtUtc);
        return (result, selection);
    }

    private static CostedBomResponse BuildCostedBom(Guid rootId, DateOnly asOfDate,
        DateTime generatedAtUtc, CostingContext context)
    {
        var revision = context.Revisions[rootId];
        var root = context.Items[rootId];
        EnsureBaseUnitIntegrity(root, revision.ParentBaseUnitId);
        var outputUnit = context.Units[revision.ParentBaseUnitId];
        var lines = new List<CostedBomLineResponse>();
        var totals = new Dictionary<Guid, MutableCurrencyTotal>();
        var missing = new List<CostedBomMissingCost>();
        Expand(rootId, 1m, root.Code, 0, [], context, lines, totals, missing,
            new BomExpansionBudget());
        var currencyTotals = totals.Values.OrderBy(x => x.CurrencyCode, StringComparer.Ordinal)
            .Select(x => CurrencyTotal(x, context.Fx)).ToArray();
        var fxContext = BuildFxContext(context.Fx, currencyTotals.Select(x =>
            new CurrencyKey(x.CurrencyId, x.CurrencyCode)));
        var materialCostUsd = missing.Count > 0
            ? null
            : SumConvertedCurrencyTotals(currencyTotals);
        return new(rootId, root.Code, root.NameFa, root.NameEn,
            revision.RevisionId, revision.RevisionCode,
            1m, outputUnit.Id, outputUnit.Code, asOfDate, generatedAtUtc,
            ValuationSemantics(context.CostBasisSelection), context.CostBasisSelection, lines,
            currencyTotals, missing, currencyTotals.Length > 1, fxContext, materialCostUsd);
    }

    private static CostedBomSummary BuildCostedBomSummary(
        Guid rootId,
        DateOnly asOfDate,
        CostingContext context,
        Dictionary<Guid, BomUnitSummary> memo,
        BomGraphBudget budget)
    {
        var revision = context.Revisions[rootId];
        var root = context.Items[rootId];
        EnsureBaseUnitIntegrity(root, revision.ParentBaseUnitId);
        var outputUnit = context.Units[revision.ParentBaseUnitId];
        var summary = SummarizeUnit(rootId, 0, [], context, memo, budget);
        var currencyTotals = summary.CurrencyTotals.Values
            .OrderBy(x => x.CurrencyCode, StringComparer.Ordinal)
            .Select(x => CurrencyTotal(x, context.Fx))
            .ToArray();
        var materialCostUsd = summary.MissingCostCount > 0
            ? null
            : SumConvertedCurrencyTotals(currencyTotals);
        return new(rootId, root.Code, root.NameFa, root.NameEn,
            revision.RevisionId, revision.RevisionCode, 1m, outputUnit.Id, outputUnit.Code,
            asOfDate, context.CostBasisSelection, currencyTotals, summary.MissingCostCount,
            currencyTotals.Length > 1,
            materialCostUsd);
    }

    private static BomUnitSummary SummarizeUnit(
        Guid parentItemId,
        int level,
        HashSet<Guid> ancestors,
        CostingContext context,
        Dictionary<Guid, BomUnitSummary> memo,
        BomGraphBudget budget)
    {
        if (memo.TryGetValue(parentItemId, out var cached)) return cached;
        if (!ancestors.Add(parentItemId))
            throw new CashFlowConflictException("BOM_CYCLE_DETECTED",
                "چرخه در BOM تأییدشده شناسایی شد و محاسبه بهای محصول متوقف شد.");
        if (level > 100)
            throw new CashFlowConflictException("BOM_DEPTH_LIMIT_EXCEEDED",
                "عمق BOM از حد ایمن محاسبه عبور کرده است.");

        try
        {
            budget.ConsumeNode();
            var revision = context.Revisions[parentItemId];
            EnsureBaseUnitIntegrity(context.Items[parentItemId], revision.ParentBaseUnitId);
            var totals = new Dictionary<Guid, MutableCurrencyTotal>();
            var missingCount = 0;
            foreach (var line in revision.Lines)
            {
                budget.ConsumeEdge();
                var item = context.Items[line.ChildItemId];
                EnsureBaseUnitIntegrity(item, line.ChildBaseUnitId);
                var scrap = line.ScrapOverridePercent ??
                    context.DefaultScrap.GetValueOrDefault(line.ChildItemId);
                var factor = CostedBomUsageFactor(line.UsageQuantity,
                    revision.BaseOutputQuantity, scrap, item.Code);
                var isLeaf = !context.Revisions.TryGetValue(line.ChildItemId, out var childRevision) ||
                    childRevision.Lines.Length == 0;
                if (!isLeaf)
                {
                    if (childRevision!.ParentBaseUnitId != line.ChildBaseUnitId)
                        throw new CashFlowConflictException("BASE_UNIT_CHANGED",
                            $"واحد پایه snapshot قلم {item.Code} بین شاخه والد و BOM زیرساخت یکسان نیست.");
                    var child = SummarizeUnit(line.ChildItemId, level + 1, ancestors,
                        context, memo, budget);
                    foreach (var childTotal in child.CurrencyTotals.Values)
                        AddCurrencyTotal(totals, childTotal,
                            MultiplyCostedBomValue(childTotal.MaterialCost, factor, item.Code),
                            item.Code);
                    try { missingCount = checked(missingCount + child.MissingCostCount); }
                    catch (OverflowException)
                    {
                        throw new CashFlowConflictException("COSTED_BOM_SUMMARY_LIMIT_EXCEEDED",
                            "تعداد مسیرهای فاقد بهای BOM از ظرفیت ایمن گزارش عبور کرده است.");
                    }
                    continue;
                }

                if (!context.CostBases.TryGetValue(item.Id, out var basis) ||
                    basis.UnitId != line.ChildBaseUnitId || item.BaseUnitId != line.ChildBaseUnitId)
                {
                    try { missingCount = checked(missingCount + 1); }
                    catch (OverflowException)
                    {
                        throw new CashFlowConflictException("COSTED_BOM_SUMMARY_LIMIT_EXCEEDED",
                            "تعداد مسیرهای فاقد بهای BOM از ظرفیت ایمن گزارش عبور کرده است.");
                    }
                    continue;
                }

                AddCurrencyTotal(totals,
                    new MutableCurrencyTotal(basis.CurrencyId, basis.CurrencyCode,
                        basis.CurrencyDecimalPrecision),
                    MultiplyCostedBomValue(factor, basis.UnitPrice, item.Code),
                    item.Code);
            }

            var result = new BomUnitSummary(totals, missingCount);
            memo.Add(parentItemId, result);
            return result;
        }
        finally
        {
            ancestors.Remove(parentItemId);
        }
    }

    private static void AddCurrencyTotal(
        Dictionary<Guid, MutableCurrencyTotal> totals,
        MutableCurrencyTotal source,
        decimal amount,
        string itemCode)
    {
        if (!totals.TryGetValue(source.CurrencyId, out var total))
        {
            total = new(source.CurrencyId, source.CurrencyCode, source.DecimalPrecision);
            totals.Add(source.CurrencyId, total);
        }

        total.MaterialCost = AddCostedBomValue(total.MaterialCost, amount, itemCode);
    }

    private static void Expand(
        Guid parentItemId,
        decimal parentQuantity,
        string parentPath,
        int level,
        HashSet<Guid> ancestors,
        CostingContext context,
        List<CostedBomLineResponse> rows,
        Dictionary<Guid, MutableCurrencyTotal> totals,
        List<CostedBomMissingCost> missing,
        BomExpansionBudget budget)
    {
        if (!ancestors.Add(parentItemId))
            throw new CashFlowConflictException("BOM_CYCLE_DETECTED",
                "چرخه در BOM تأییدشده شناسایی شد و محاسبه بهای محصول متوقف شد.");
        if (level > 100)
            throw new CashFlowConflictException("BOM_DEPTH_LIMIT_EXCEEDED",
                "عمق BOM از حد ایمن محاسبه عبور کرده است.");
        var revision = context.Revisions[parentItemId];
        EnsureBaseUnitIntegrity(context.Items[parentItemId], revision.ParentBaseUnitId);
        foreach (var line in revision.Lines)
        {
            budget.ConsumeRow();
            var item = context.Items[line.ChildItemId];
            EnsureBaseUnitIntegrity(item, line.ChildBaseUnitId);
            var unit = context.Units[line.ChildBaseUnitId];
            var scrap = line.ScrapOverridePercent ?? context.DefaultScrap.GetValueOrDefault(line.ChildItemId);
            var factor = CostedBomUsageFactor(line.UsageQuantity,
                revision.BaseOutputQuantity, scrap, item.Code);
            var quantity = MultiplyCostedBomValue(parentQuantity, factor, item.Code);
            var path = $"{parentPath}/{item.Code}";
            var isLeaf = !context.Revisions.TryGetValue(line.ChildItemId, out var childRevision) ||
                childRevision.Lines.Length == 0;
            if (!isLeaf)
            {
                if (childRevision!.ParentBaseUnitId != line.ChildBaseUnitId)
                    throw new CashFlowConflictException("BASE_UNIT_CHANGED",
                        $"واحد پایه snapshot قلم {item.Code} بین شاخه والد و BOM زیرساخت یکسان نیست.");
                rows.Add(new(path, level + 1, false, item.Id, item.Code, item.NameFa,
                    item.NameEn,
                    quantity, unit.Id, unit.Code, scrap, null, null, null));
                Expand(line.ChildItemId, quantity, path, level + 1,
                    new HashSet<Guid>(ancestors), context, rows, totals, missing, budget);
                continue;
            }

            if (!context.CostBases.TryGetValue(item.Id, out var basis))
            {
                var reason = context.CostBasisSelection.Mode ==
                    MaterialCostBookCostBasisModes.MaterialCostBook
                    ? "NO_MATERIAL_COST_BOOK_PRICE"
                    : "NO_VALID_PURCHASE_COST";
                rows.Add(new(path, level + 1, true, item.Id, item.Code, item.NameFa,
                    item.NameEn,
                    quantity, unit.Id, unit.Code, scrap, MissingBasis(reason), null, null));
                missing.Add(new(path, item.Id, item.Code, item.NameFa, item.NameEn, reason));
                continue;
            }
            if (basis.UnitId != line.ChildBaseUnitId || item.BaseUnitId != line.ChildBaseUnitId)
            {
                var reason = context.CostBasisSelection.Mode ==
                    MaterialCostBookCostBasisModes.MaterialCostBook
                    ? "COST_BOOK_BASE_UNIT_MISMATCH"
                    : "COST_UNIT_MISMATCH";
                rows.Add(new(path, level + 1, true, item.Id, item.Code, item.NameFa,
                    item.NameEn,
                    quantity, unit.Id, unit.Code, scrap, Response(basis, reason), null, null));
                missing.Add(new(path, item.Id, item.Code, item.NameFa, item.NameEn, reason));
                continue;
            }

            var extended = MultiplyCostedBomValue(quantity, basis.UnitPrice, item.Code);
            AddCurrencyTotal(totals,
                new MutableCurrencyTotal(basis.CurrencyId, basis.CurrencyCode,
                    basis.CurrencyDecimalPrecision),
                extended,
                item.Code);
            rows.Add(new(path, level + 1, true, item.Id, item.Code, item.NameFa,
                item.NameEn,
                quantity, unit.Id, unit.Code, scrap, Response(basis, null), extended,
                ConvertToUsd(extended, basis.CurrencyId, basis.CurrencyCode, context.Fx)));
        }
    }

    private static void EnsureBaseUnitIntegrity(ItemContext item, Guid snapshotUnitId)
    {
        if (item.BaseUnitId != snapshotUnitId)
            throw new CashFlowConflictException("BASE_UNIT_CHANGED",
                $"واحد پایه جاری قلم {item.Code} با snapshot تأییدشده BOM یکسان نیست.");
    }

    private static decimal CostedBomUsageFactor(decimal usageQuantity,
        decimal baseOutputQuantity, decimal scrapPercent, string itemCode)
    {
        try
        {
            return checked(checked(usageQuantity / baseOutputQuantity) *
                checked(1m + scrapPercent / 100m));
        }
        catch (OverflowException)
        {
            throw CostedBomCalculationOutOfRange(itemCode);
        }
    }

    private static decimal MultiplyCostedBomValue(decimal left, decimal right,
        string itemCode)
    {
        try
        {
            return checked(left * right);
        }
        catch (OverflowException)
        {
            throw CostedBomCalculationOutOfRange(itemCode);
        }
    }

    private static decimal AddCostedBomValue(decimal current, decimal amount,
        string itemCode)
    {
        try
        {
            return checked(current + amount);
        }
        catch (OverflowException)
        {
            throw CostedBomCalculationOutOfRange(itemCode);
        }
    }

    private static CashFlowConflictException CostedBomCalculationOutOfRange(string itemCode) =>
        new("COSTED_BOM_CALCULATION_OUT_OF_RANGE",
            $"مقدار یا بهای محاسبه‌شده در مسیر قلم {itemCode} از محدوده عددی ایمن خارج است؛ مقدار مصرف، خروجی مبنا و بهای واحد را بررسی کنید.");

    private static CostBasisResponse MissingBasis(string reason) => new(
        Kind: CostBasisKinds.Missing,
        UnitPrice: null,
        CurrencyId: null,
        CurrencyCode: null,
        CurrencyDecimalPrecision: null,
        UnitId: null,
        UnitCode: null,
        SourceDocumentId: null,
        SourceDocumentNumber: null,
        SourceDate: null,
        PurchaseOrderId: null,
        PurchaseOrderNumber: null,
        SupplierId: null,
        SupplierNumber: null,
        SupplierName: null,
        MissingReason: reason,
        OriginalPriceAmount: null,
        PriceBasisQuantity: null,
        PriceUnitId: null,
        PriceUnitCode: null,
        PriceUnitFactorToBase: null,
        PriceBookRevisionId: null);

    private static CostBasisResponse Response(CostBasisContext basis, string? missingReason) => new(
        missingReason is null ? basis.Kind : CostBasisKinds.Missing,
        basis.UnitPrice,
        basis.CurrencyId,
        basis.CurrencyCode,
        basis.CurrencyDecimalPrecision,
        basis.UnitId,
        basis.UnitCode,
        basis.SourceDocumentId,
        basis.SourceDocumentNumber,
        basis.SourceDate,
        basis.PurchaseOrderId,
        basis.PurchaseOrderNumber,
        basis.SupplierId,
        basis.SupplierNumber,
        basis.SupplierName,
        missingReason,
        basis.OriginalPriceAmount,
        basis.PriceBasisQuantity,
        basis.PriceUnitId,
        basis.PriceUnitCode,
        basis.PriceUnitFactorToBase,
        basis.PriceBookRevisionId);

    private static CostBasisContext CostBasis(CostBasisDbRow row) => new(
        row.Kind, row.UnitPrice, row.CurrencyId, row.CurrencyCode, row.CurrencyDecimalPrecision,
        row.UnitId, row.UnitCode, row.SourceDocumentId, row.SourceDocumentNumber, row.SourceDate,
        row.PurchaseOrderId, row.PurchaseOrderNumber, row.SupplierId, row.SupplierNumber,
        row.SupplierName, row.UnitPrice, 1m, row.UnitId, row.UnitCode, 1m, null);

    private static NpgsqlParameter GuidArrayParameter(Guid[] values) => new("itemIds",
        NpgsqlDbType.Array | NpgsqlDbType.Uuid) { Value = values };

    private static NpgsqlParameter NullableParameter(string name, NpgsqlDbType type,
        object? value) => new(name, type) { Value = value ?? DBNull.Value };

    private static string? NormalizeFilter(string? value) => string.IsNullOrWhiteSpace(value)
        ? null
        : value.Trim().ToUpperInvariant();

    private static string NormalizeCostBasisMode(string? value, Guid? priceBookRevisionId)
    {
        var normalized = string.IsNullOrWhiteSpace(value)
            ? MaterialCostBookCostBasisModes.PurchaseHistory
            : value.Trim().ToUpperInvariant();
        if (normalized is not (MaterialCostBookCostBasisModes.PurchaseHistory or
            MaterialCostBookCostBasisModes.MaterialCostBook))
            throw Validation("costBasisMode", "مبنای بهای مواد معتبر نیست.");
        if (normalized == MaterialCostBookCostBasisModes.PurchaseHistory &&
            priceBookRevisionId.HasValue)
            throw Validation("priceBookRevisionId",
                "در حالت سابقه خرید نباید نسخه دفتر بهای مواد ارسال شود.");
        if (normalized == MaterialCostBookCostBasisModes.MaterialCostBook &&
            !priceBookRevisionId.HasValue)
            throw Validation("priceBookRevisionId",
                "در حالت دفتر بهای مواد انتخاب نسخه منتشرشده الزامی است.");
        return normalized;
    }

    private static CostBasisSelectionContext PurchaseHistorySelection() => new(
        MaterialCostBookCostBasisModes.PurchaseHistory,
        "CURRENT_APPROVED_BOM_STRUCTURE_NOT_MRP_ROUTE_AWARE",
        null, null, null, null, null, null, null, null);

    private static string ValuationSemantics(CostBasisSelectionContext selection) =>
        selection.Mode == MaterialCostBookCostBasisModes.MaterialCostBook
            ? "PUBLISHED_MATERIAL_COST_BOOK_STRICT_NO_FALLBACK"
            : CashFlowDataSemantics.CurrentValidFactsWithSourceDateCutoff;

    private static void ValidatePage(int page, int pageSize, int maximumPageSize)
    {
        if (page < 1 || pageSize is < 1 || pageSize > maximumPageSize)
            throw Validation("paging", $"pageSize must be between 1 and {maximumPageSize}.");
    }

    private static CashFlowValidationException Validation(string key, string message) =>
        new(new Dictionary<string, string[]> { [key] = [message] });

    private const string ReportingFxSelectionSql = """
        WITH selected_rate_set AS (
            SELECT
                rate_set."Id" AS rate_set_id,
                rate_set."EffectiveDate" AS effective_date,
                rate_set."Revision" AS revision
            FROM cash_flow.reporting_fx_rate_sets rate_set
            JOIN reference_data.currencies base_currency
              ON base_currency."Id" = rate_set."BaseCurrencyId"
            WHERE rate_set."Status" = @publishedStatus
              AND rate_set."EffectiveDate" <= @asOfDate
              AND UPPER(base_currency."Code") = UPPER(@reportingCurrencyCode)
            ORDER BY rate_set."EffectiveDate" DESC,
                     rate_set."Revision" DESC,
                     rate_set."PublishedAtUtc" DESC NULLS LAST,
                     rate_set."Id" DESC
            LIMIT 1
        )
        SELECT
            selected.rate_set_id AS "RateSetId",
            selected.effective_date AS "EffectiveDate",
            selected.revision AS "Revision",
            rate."CurrencyId" AS "CurrencyId",
            currency."Code" AS "CurrencyCode",
            rate."UnitsPerUsd" AS "UnitsPerUsd"
        FROM (VALUES (1)) singleton(dummy)
        LEFT JOIN selected_rate_set selected ON TRUE
        LEFT JOIN cash_flow.reporting_fx_rates rate
          ON rate."RateSetId" = selected.rate_set_id
        LEFT JOIN reference_data.currencies currency
          ON currency."Id" = rate."CurrencyId"
        ORDER BY currency."Code", rate."CurrencyId"
        """;

    private const string ObligationProjectionSql = """
        WITH candidate_orders AS (
            SELECT
                po."Id" AS purchase_order_id,
                po."PurchaseOrderNumber" AS purchase_order_number,
                po."Status" AS purchase_order_status,
                po."OrderedDate" AS ordered_date,
                po."AdvancePercent" AS advance_percent,
                po."BeforeShipmentPercent" AS before_shipment_percent,
                po."CreditPercent" AS credit_percent,
                po."CreditDays" AS credit_days,
                po."CreditAnchor" AS credit_anchor,
                po."SupplierId" AS supplier_id,
                supplier."SupplierNumber" AS supplier_number,
                supplier."Name" AS supplier_name,
                po."CurrencyId" AS currency_id,
                currency."Code" AS currency_code,
                currency."DecimalPrecision" AS currency_decimal_precision
            FROM procurement.purchase_orders po
            JOIN procurement.suppliers supplier ON supplier."Id" = po."SupplierId"
            JOIN reference_data.currencies currency ON currency."Id" = po."CurrencyId"
            WHERE (
                    po."Status" IN ('ORDERED','SHIPPED','CUSTOMS','PARTIALLY_RECEIVED','RECEIVED')
                    OR (@includePlanned AND po."Status" = 'APPROVED')
                  )
              AND (@supplierId IS NULL OR po."SupplierId" = @supplierId)
              AND (@currencyCode IS NULL OR currency."Code" = @currencyCode)
              AND (
                    @searchPattern IS NULL
                    OR po."PurchaseOrderNumber" ILIKE @searchPattern
                    OR supplier."SupplierNumber" ILIKE @searchPattern
                    OR supplier."Name" ILIKE @searchPattern
                    OR EXISTS (
                        SELECT 1
                        FROM procurement.purchase_order_lines search_line
                        JOIN catalog.items search_item ON search_item."Id" = search_line."ItemId"
                        WHERE search_line."PurchaseOrderId" = po."Id"
                          AND (
                              search_item."BusinessNumber" ILIKE @searchPattern
                              OR search_item."NameFa" ILIKE @searchPattern
                              OR search_item."NameEn" ILIKE @searchPattern
                          )
                    )
                  )
        ),
        effective_schedules AS (
            SELECT
                schedule."Id" AS schedule_id,
                schedule."ScheduleNumber" AS schedule_number,
                GREATEST(schedule."AllocatedQuantity" -
                    COALESCE(cancellation.cancelled_quantity, 0), 0) AS effective_quantity,
                po_line."Id" AS purchase_order_line_id,
                po_line."LineNumber" AS purchase_order_line_number,
                po_line."ItemId" AS item_id,
                item."BusinessNumber" AS item_code,
                item."NameFa" AS item_name_fa,
                item."NameEn" AS item_name_en,
                po_line."UnitPrice" AS unit_price,
                candidate.*
            FROM candidate_orders candidate
            JOIN procurement.purchase_order_lines po_line
              ON po_line."PurchaseOrderId" = candidate.purchase_order_id
            JOIN procurement.delivery_schedules schedule
              ON schedule."PurchaseOrderLineId" = po_line."Id"
            JOIN catalog.items item ON item."Id" = po_line."ItemId"
            LEFT JOIN LATERAL (
                SELECT SUM(entry."Quantity") AS cancelled_quantity
                FROM procurement.purchase_schedule_cancellations entry
                WHERE entry."DeliveryScheduleId" = schedule."Id"
            ) cancellation ON TRUE
            WHERE GREATEST(schedule."AllocatedQuantity" -
                COALESCE(cancellation.cancelled_quantity, 0), 0) > 0
        ),
        latest_plan_rows AS (
            SELECT DISTINCT ON (plan."DeliveryScheduleId", plan."PlanType")
                plan."DeliveryScheduleId" AS schedule_id,
                plan."PlanType" AS plan_type,
                plan."SupplierReadyDate" AS supplier_ready_date,
                plan."ShippedDate" AS shipped_date,
                plan."WarehouseReceiptDate" AS warehouse_receipt_date
            FROM procurement.delivery_schedule_plan_revisions plan
            JOIN effective_schedules schedule
              ON schedule.schedule_id = plan."DeliveryScheduleId"
            WHERE plan."PlanType" IN ('ORIGINAL','REVISED')
            ORDER BY plan."DeliveryScheduleId", plan."PlanType",
                     plan."RevisionNumber" DESC, plan."CreatedAtUtc" DESC, plan."Id" DESC
        ),
        plan_dates AS (
            SELECT
                schedule_id,
                MAX(supplier_ready_date) FILTER (WHERE plan_type = 'REVISED')
                    AS revised_supplier_ready_date,
                MAX(shipped_date) FILTER (WHERE plan_type = 'REVISED')
                    AS revised_shipped_date,
                MAX(warehouse_receipt_date) FILTER (WHERE plan_type = 'REVISED')
                    AS revised_warehouse_receipt_date,
                MAX(supplier_ready_date) FILTER (WHERE plan_type = 'ORIGINAL')
                    AS original_supplier_ready_date,
                MAX(shipped_date) FILTER (WHERE plan_type = 'ORIGINAL')
                    AS original_shipped_date,
                MAX(warehouse_receipt_date) FILTER (WHERE plan_type = 'ORIGINAL')
                    AS original_warehouse_receipt_date
            FROM latest_plan_rows
            GROUP BY schedule_id
        ),
        current_actual_rows AS (
            SELECT DISTINCT ON (actual."DeliveryScheduleId", actual."MilestoneCode")
                actual."DeliveryScheduleId" AS schedule_id,
                actual."MilestoneCode" AS milestone_code,
                actual."ActualDate" AS actual_date
            FROM procurement.delivery_schedule_actual_milestones actual
            JOIN effective_schedules schedule
              ON schedule.schedule_id = actual."DeliveryScheduleId"
            WHERE actual."MilestoneCode" IN ('SUPPLIER_READY','SHIPPED')
              AND NOT EXISTS (
                  SELECT 1
                  FROM procurement.delivery_schedule_actual_milestones successor
                  WHERE successor."SupersedesMilestoneId" = actual."Id"
              )
            ORDER BY actual."DeliveryScheduleId", actual."MilestoneCode",
                     actual."CreatedAtUtc" DESC, actual."Id" DESC
        ),
        actual_dates AS (
            SELECT
                schedule_id,
                MAX(actual_date) FILTER (WHERE milestone_code = 'SUPPLIER_READY')
                    AS supplier_ready_date,
                MAX(actual_date) FILTER (WHERE milestone_code = 'SHIPPED')
                    AS shipped_date
            FROM current_actual_rows
            GROUP BY schedule_id
        ),
        posted_receipt_running AS (
            SELECT
                schedule.schedule_id,
                receipt."EffectiveDate" AS effective_date,
                receipt."PostedAtUtc" AS posted_at_utc,
                receipt_line."ReceiptId" AS receipt_id,
                receipt_line."Id" AS receipt_line_id,
                schedule.effective_quantity,
                SUM(receipt_line."Quantity") OVER (
                    PARTITION BY schedule.schedule_id
                    ORDER BY receipt."EffectiveDate", receipt."PostedAtUtc" NULLS LAST,
                             receipt_line."ReceiptId", receipt_line."Id"
                    ROWS BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW
                ) AS cumulative_quantity
            FROM effective_schedules schedule
            JOIN procurement.purchase_receipt_lines receipt_line
              ON receipt_line."DeliveryScheduleId" = schedule.schedule_id
            JOIN procurement.purchase_receipts receipt
              ON receipt."Id" = receipt_line."ReceiptId"
            WHERE receipt."Status" = 'POSTED'
        ),
        receipt_completion AS (
            SELECT DISTINCT ON (schedule_id)
                schedule_id,
                effective_date AS completion_date
            FROM posted_receipt_running
            WHERE cumulative_quantity >= effective_quantity
            ORDER BY schedule_id, effective_date, posted_at_utc NULLS LAST,
                     receipt_id, receipt_line_id
        ),
        schedule_dates AS (
            SELECT
                schedule.*,
                COALESCE(actual.supplier_ready_date,
                         plan.revised_supplier_ready_date,
                         plan.original_supplier_ready_date) AS supplier_ready_date,
                CASE
                    WHEN actual.supplier_ready_date IS NOT NULL THEN 'ACTUAL'
                    WHEN plan.revised_supplier_ready_date IS NOT NULL THEN 'REVISED'
                    WHEN plan.original_supplier_ready_date IS NOT NULL THEN 'ORIGINAL'
                    ELSE 'UNRESOLVED'
                END AS supplier_ready_source,
                COALESCE(actual.shipped_date,
                         plan.revised_shipped_date,
                         plan.original_shipped_date) AS shipped_date,
                CASE
                    WHEN actual.shipped_date IS NOT NULL THEN 'ACTUAL'
                    WHEN plan.revised_shipped_date IS NOT NULL THEN 'REVISED'
                    WHEN plan.original_shipped_date IS NOT NULL THEN 'ORIGINAL'
                    ELSE 'UNRESOLVED'
                END AS shipped_source,
                COALESCE(completion.completion_date,
                         plan.revised_warehouse_receipt_date,
                         plan.original_warehouse_receipt_date) AS warehouse_receipt_date,
                CASE
                    WHEN completion.completion_date IS NOT NULL THEN 'ACTUAL'
                    WHEN plan.revised_warehouse_receipt_date IS NOT NULL THEN 'REVISED'
                    WHEN plan.original_warehouse_receipt_date IS NOT NULL THEN 'ORIGINAL'
                    ELSE 'UNRESOLVED'
                END AS warehouse_receipt_source
            FROM effective_schedules schedule
            LEFT JOIN plan_dates plan ON plan.schedule_id = schedule.schedule_id
            LEFT JOIN actual_dates actual ON actual.schedule_id = schedule.schedule_id
            LEFT JOIN receipt_completion completion ON completion.schedule_id = schedule.schedule_id
        ),
        schedule_payment_anchors AS (
            SELECT
                schedule.*,
                CASE schedule.credit_anchor
                    WHEN 'ORDERED' THEN schedule.ordered_date
                    WHEN 'SHIPPED' THEN schedule.shipped_date
                    WHEN 'WAREHOUSE_RECEIPT' THEN schedule.warehouse_receipt_date
                    ELSE NULL
                END AS credit_anchor_date,
                CASE schedule.credit_anchor
                    WHEN 'ORDERED' THEN CASE WHEN schedule.ordered_date IS NOT NULL
                        THEN 'ORDERED' ELSE 'UNRESOLVED' END
                    WHEN 'SHIPPED' THEN schedule.shipped_source
                    WHEN 'WAREHOUSE_RECEIPT' THEN schedule.warehouse_receipt_source
                    ELSE 'UNRESOLVED'
                END AS credit_anchor_source
            FROM schedule_dates schedule
        ),
        advance_rows AS (
            SELECT
                REPLACE(schedule.purchase_order_id::text, '-', '') || ':ADVANCE'
                    AS obligation_key,
                schedule.ordered_date AS expected_payment_date,
                schedule.purchase_order_id,
                schedule.purchase_order_number,
                schedule.purchase_order_status,
                NULL::uuid AS purchase_order_line_id,
                NULL::integer AS purchase_order_line_number,
                NULL::uuid AS item_id,
                NULL::text AS item_code,
                NULL::text AS item_name_fa,
                NULL::text AS item_name_en,
                schedule.supplier_id,
                schedule.supplier_number,
                schedule.supplier_name,
                schedule.currency_id,
                schedule.currency_code,
                schedule.currency_decimal_precision,
                trim_scale(SUM((schedule.advance_percent / 100::numeric) *
                    schedule.effective_quantity * schedule.unit_price)) AS projected_amount,
                'ADVANCE'::text AS stage,
                CASE WHEN schedule.purchase_order_status = 'APPROVED'
                    THEN 'PLANNED' ELSE 'COMMITTED' END AS confidence,
                CASE WHEN schedule.ordered_date IS NOT NULL
                    THEN 'ORDERED' ELSE 'UNRESOLVED' END AS date_source,
                'ORDERED'::text AS anchor_code,
                NULL::uuid AS delivery_schedule_id,
                NULL::integer AS schedule_number
            FROM schedule_payment_anchors schedule
            WHERE schedule.advance_percent > 0
            GROUP BY schedule.purchase_order_id, schedule.purchase_order_number,
                     schedule.purchase_order_status, schedule.ordered_date,
                     schedule.advance_percent, schedule.supplier_id,
                     schedule.supplier_number, schedule.supplier_name,
                     schedule.currency_id, schedule.currency_code,
                     schedule.currency_decimal_precision
        ),
        before_shipment_rows AS (
            SELECT
                REPLACE(schedule.purchase_order_id::text, '-', '') || ':' ||
                    REPLACE(schedule.schedule_id::text, '-', '') || ':BEFORE_SHIPMENT'
                    AS obligation_key,
                schedule.supplier_ready_date AS expected_payment_date,
                schedule.purchase_order_id,
                schedule.purchase_order_number,
                schedule.purchase_order_status,
                schedule.purchase_order_line_id,
                schedule.purchase_order_line_number,
                schedule.item_id,
                schedule.item_code,
                schedule.item_name_fa,
                schedule.item_name_en,
                schedule.supplier_id,
                schedule.supplier_number,
                schedule.supplier_name,
                schedule.currency_id,
                schedule.currency_code,
                schedule.currency_decimal_precision,
                trim_scale((schedule.before_shipment_percent / 100::numeric) *
                    schedule.effective_quantity * schedule.unit_price) AS projected_amount,
                'BEFORE_SHIPMENT'::text AS stage,
                CASE WHEN schedule.purchase_order_status = 'APPROVED'
                    THEN 'PLANNED' ELSE 'COMMITTED' END AS confidence,
                schedule.supplier_ready_source AS date_source,
                'SUPPLIER_READY'::text AS anchor_code,
                schedule.schedule_id AS delivery_schedule_id,
                schedule.schedule_number
            FROM schedule_payment_anchors schedule
            WHERE schedule.before_shipment_percent > 0
        ),
        credit_rows AS (
            SELECT
                REPLACE(schedule.purchase_order_id::text, '-', '') || ':' ||
                    REPLACE(schedule.schedule_id::text, '-', '') || ':CREDIT'
                    AS obligation_key,
                CASE WHEN schedule.credit_anchor_date IS NOT NULL
                           AND schedule.credit_days IS NOT NULL
                    THEN schedule.credit_anchor_date + schedule.credit_days
                    ELSE NULL
                END AS expected_payment_date,
                schedule.purchase_order_id,
                schedule.purchase_order_number,
                schedule.purchase_order_status,
                schedule.purchase_order_line_id,
                schedule.purchase_order_line_number,
                schedule.item_id,
                schedule.item_code,
                schedule.item_name_fa,
                schedule.item_name_en,
                schedule.supplier_id,
                schedule.supplier_number,
                schedule.supplier_name,
                schedule.currency_id,
                schedule.currency_code,
                schedule.currency_decimal_precision,
                trim_scale((schedule.credit_percent / 100::numeric) *
                    schedule.effective_quantity * schedule.unit_price) AS projected_amount,
                'CREDIT'::text AS stage,
                CASE WHEN schedule.purchase_order_status = 'APPROVED'
                    THEN 'PLANNED' ELSE 'COMMITTED' END AS confidence,
                CASE WHEN schedule.credit_anchor_date IS NOT NULL
                           AND schedule.credit_days IS NOT NULL
                    THEN schedule.credit_anchor_source
                    ELSE 'UNRESOLVED'
                END AS date_source,
                COALESCE(schedule.credit_anchor, 'UNRESOLVED') AS anchor_code,
                schedule.schedule_id AS delivery_schedule_id,
                schedule.schedule_number
            FROM schedule_payment_anchors schedule
            WHERE schedule.credit_percent > 0
        ),
        obligation_rows AS (
            SELECT * FROM advance_rows
            UNION ALL
            SELECT * FROM before_shipment_rows
            UNION ALL
            SELECT * FROM credit_rows
        )
        SELECT
            obligation_key AS "ObligationKey",
            expected_payment_date AS "ExpectedPaymentDate",
            purchase_order_id AS "PurchaseOrderId",
            purchase_order_number AS "PurchaseOrderNumber",
            purchase_order_status AS "PurchaseOrderStatus",
            purchase_order_line_id AS "PurchaseOrderLineId",
            purchase_order_line_number AS "PurchaseOrderLineNumber",
            item_id AS "ItemId",
            item_code AS "ItemCode",
            item_name_fa AS "ItemNameFa",
            item_name_en AS "ItemNameEn",
            supplier_id AS "SupplierId",
            supplier_number AS "SupplierNumber",
            supplier_name AS "SupplierName",
            currency_id AS "CurrencyId",
            currency_code AS "CurrencyCode",
            currency_decimal_precision AS "CurrencyDecimalPrecision",
            projected_amount AS "ProjectedAmount",
            stage AS "Stage",
            confidence AS "Confidence",
            date_source AS "DateSource",
            CASE
                WHEN expected_payment_date IS NULL THEN 'UNRESOLVED'
                WHEN expected_payment_date < @asOfDate THEN 'OVERDUE'
                WHEN expected_payment_date <= @asOfDate + 7 THEN 'DUE_SOON'
                ELSE 'SCHEDULED'
            END AS "State",
            anchor_code AS "AnchorCode",
            delivery_schedule_id AS "DeliveryScheduleId",
            schedule_number AS "ScheduleNumber"
        FROM obligation_rows
        """;

    private const string ReceiptCostBasisSql = """
        SELECT DISTINCT ON (receipt_line."ItemId")
            receipt_line."ItemId" AS "ItemId",
            'RECEIPT' AS "Kind",
            po_line."UnitPrice" AS "UnitPrice",
            po."CurrencyId" AS "CurrencyId",
            currency."Code" AS "CurrencyCode",
            currency."DecimalPrecision" AS "CurrencyDecimalPrecision",
            po_line."ItemBaseUnitId" AS "UnitId",
            unit."Code" AS "UnitCode",
            receipt."Id" AS "SourceDocumentId",
            receipt."ReceiptNumber" AS "SourceDocumentNumber",
            receipt."EffectiveDate" AS "SourceDate",
            po."Id" AS "PurchaseOrderId",
            po."PurchaseOrderNumber" AS "PurchaseOrderNumber",
            po."SupplierId" AS "SupplierId",
            supplier."SupplierNumber" AS "SupplierNumber",
            supplier."Name" AS "SupplierName"
        FROM procurement.purchase_receipt_lines receipt_line
        JOIN procurement.purchase_receipts receipt ON receipt."Id" = receipt_line."ReceiptId"
        JOIN procurement.purchase_order_lines po_line ON po_line."Id" = receipt_line."PurchaseOrderLineId"
        JOIN procurement.purchase_orders po ON po."Id" = po_line."PurchaseOrderId"
        JOIN procurement.suppliers supplier ON supplier."Id" = po."SupplierId"
        JOIN reference_data.currencies currency ON currency."Id" = po."CurrencyId"
        JOIN reference_data.units unit ON unit."Id" = po_line."ItemBaseUnitId"
        WHERE receipt_line."ItemId" = ANY(@itemIds)
          AND receipt."Status" = 'POSTED'
          AND receipt_line."Quantity" > 0
          AND po_line."UnitPrice" > 0
          AND receipt."EffectiveDate" <= @asOfDate
        ORDER BY receipt_line."ItemId", receipt."EffectiveDate" DESC,
                 receipt."PostedAtUtc" DESC NULLS LAST, receipt_line."Id" DESC
        """;

    private const string PurchaseOrderCostBasisSql = """
        SELECT DISTINCT ON (po_line."ItemId")
            po_line."ItemId" AS "ItemId",
            'PURCHASE_ORDER_ESTIMATE' AS "Kind",
            po_line."UnitPrice" AS "UnitPrice",
            po."CurrencyId" AS "CurrencyId",
            currency."Code" AS "CurrencyCode",
            currency."DecimalPrecision" AS "CurrencyDecimalPrecision",
            po_line."ItemBaseUnitId" AS "UnitId",
            unit."Code" AS "UnitCode",
            po."Id" AS "SourceDocumentId",
            po."PurchaseOrderNumber" AS "SourceDocumentNumber",
            COALESCE(po."OrderedDate", (po."ApprovedAtUtc" AT TIME ZONE 'Asia/Tehran')::date) AS "SourceDate",
            po."Id" AS "PurchaseOrderId",
            po."PurchaseOrderNumber" AS "PurchaseOrderNumber",
            po."SupplierId" AS "SupplierId",
            supplier."SupplierNumber" AS "SupplierNumber",
            supplier."Name" AS "SupplierName"
        FROM procurement.purchase_order_lines po_line
        JOIN procurement.purchase_orders po ON po."Id" = po_line."PurchaseOrderId"
        JOIN procurement.suppliers supplier ON supplier."Id" = po."SupplierId"
        JOIN reference_data.currencies currency ON currency."Id" = po."CurrencyId"
        JOIN reference_data.units unit ON unit."Id" = po_line."ItemBaseUnitId"
        WHERE po_line."ItemId" = ANY(@itemIds)
          AND po."Status" = ANY(ARRAY['APPROVED','ORDERED','SHIPPED','CUSTOMS','PARTIALLY_RECEIVED','RECEIVED'])
          AND po_line."UnitPrice" > 0
          AND EXISTS (
              SELECT 1
              FROM procurement.delivery_schedules schedule
              WHERE schedule."PurchaseOrderLineId" = po_line."Id"
                AND schedule."AllocatedQuantity" > COALESCE((
                    SELECT SUM(cancellation."Quantity")
                    FROM procurement.purchase_schedule_cancellations cancellation
                    WHERE cancellation."DeliveryScheduleId" = schedule."Id"
                ), 0)
          )
          AND COALESCE(po."OrderedDate", (po."ApprovedAtUtc" AT TIME ZONE 'Asia/Tehran')::date) <= @asOfDate
        ORDER BY po_line."ItemId",
                 COALESCE(po."OrderedDate", (po."ApprovedAtUtc" AT TIME ZONE 'Asia/Tehran')::date) DESC,
                 po."UpdatedAtUtc" DESC, po_line."Id" DESC
        """;

    private sealed class CashFlowObligationDbRow
    {
        public string ObligationKey { get; init; } = string.Empty;
        public DateOnly? ExpectedPaymentDate { get; init; }
        public Guid PurchaseOrderId { get; init; }
        public string PurchaseOrderNumber { get; init; } = string.Empty;
        public string PurchaseOrderStatus { get; init; } = string.Empty;
        public Guid? PurchaseOrderLineId { get; init; }
        public int? PurchaseOrderLineNumber { get; init; }
        public Guid? ItemId { get; init; }
        public string? ItemCode { get; init; }
        public string? ItemNameFa { get; init; }
        public string? ItemNameEn { get; init; }
        public Guid SupplierId { get; init; }
        public string SupplierNumber { get; init; } = string.Empty;
        public string SupplierName { get; init; } = string.Empty;
        public Guid CurrencyId { get; init; }
        public string CurrencyCode { get; init; } = string.Empty;
        public int CurrencyDecimalPrecision { get; init; }
        public decimal ProjectedAmount { get; init; }
        public string Stage { get; init; } = string.Empty;
        public string Confidence { get; init; } = string.Empty;
        public string DateSource { get; init; } = string.Empty;
        public string State { get; init; } = string.Empty;
        public string AnchorCode { get; init; } = string.Empty;
        public Guid? DeliveryScheduleId { get; init; }
        public int? ScheduleNumber { get; init; }
    }

    private interface ICurrencyAmountSource
    {
        Guid CurrencyId { get; }
        string CurrencyCode { get; }
    }

    private sealed record CurrencyKey(Guid CurrencyId, string CurrencyCode);

    private sealed record ReportingFxSelection(
        Guid? RateSetId,
        DateOnly? EffectiveDate,
        int? Revision,
        IReadOnlyDictionary<Guid, decimal> Rates);

    private sealed class ReportingFxDbRow
    {
        public Guid? RateSetId { get; init; }
        public DateOnly? EffectiveDate { get; init; }
        public int? Revision { get; init; }
        public Guid? CurrencyId { get; init; }
        public string? CurrencyCode { get; init; }
        public decimal? UnitsPerUsd { get; init; }
    }

    private sealed class CashFlowCurrencyAmountDbRow : ICurrencyAmountSource
    {
        public Guid CurrencyId { get; init; }
        public string CurrencyCode { get; init; } = string.Empty;
        public decimal Amount { get; init; }
        public int Count { get; init; }
    }

    private sealed class CashFlowCurrencySummaryDbRow : ICurrencyAmountSource
    {
        public Guid CurrencyId { get; init; }
        public string CurrencyCode { get; init; } = string.Empty;
        public int DecimalPrecision { get; init; }
        public decimal OverdueAmount { get; init; }
        public decimal Due7DaysAmount { get; init; }
        public decimal Due30DaysAmount { get; init; }
        public decimal Due90DaysAmount { get; init; }
        public decimal HorizonAmount { get; init; }
        public int ObligationCount { get; init; }
    }

    private sealed class CashFlowBucketDbRow : ICurrencyAmountSource
    {
        public DateOnly Date { get; init; }
        public Guid CurrencyId { get; init; }
        public string CurrencyCode { get; init; } = string.Empty;
        public decimal Amount { get; init; }
        public int Count { get; init; }
    }

    private sealed record BomContext(Guid RevisionId, Guid ParentItemId, Guid ParentBaseUnitId,
        string RevisionCode, decimal BaseOutputQuantity, BomLineContext[] Lines);
    private sealed record BomLineContext(Guid Id, Guid ChildItemId, Guid ChildBaseUnitId,
        decimal UsageQuantity, decimal? ScrapOverridePercent, int SortOrder);
    private sealed record ItemContext(Guid Id, string Code, string NameFa, string? NameEn,
        Guid BaseUnitId);
    private sealed record UnitContext(Guid Id, string Code);
    private sealed record CostingContext(
        IReadOnlyDictionary<Guid, BomContext> Revisions,
        IReadOnlyDictionary<Guid, ItemContext> Items,
        IReadOnlyDictionary<Guid, UnitContext> Units,
        IReadOnlyDictionary<Guid, decimal> DefaultScrap,
        IReadOnlyDictionary<Guid, CostBasisContext> CostBases,
        ReportingFxSelection Fx,
        CostBasisSelectionContext CostBasisSelection);

    private sealed record CostBasisContext(
        string Kind,
        decimal UnitPrice,
        Guid CurrencyId,
        string CurrencyCode,
        int CurrencyDecimalPrecision,
        Guid UnitId,
        string UnitCode,
        Guid SourceDocumentId,
        string SourceDocumentNumber,
        DateOnly SourceDate,
        Guid? PurchaseOrderId,
        string? PurchaseOrderNumber,
        Guid? SupplierId,
        string? SupplierNumber,
        string? SupplierName,
        decimal OriginalPriceAmount,
        decimal PriceBasisQuantity,
        Guid PriceUnitId,
        string PriceUnitCode,
        decimal PriceUnitFactorToBase,
        Guid? PriceBookRevisionId);

    private sealed class MutableCurrencyTotal(Guid currencyId, string currencyCode, int decimalPrecision)
    {
        public Guid CurrencyId { get; } = currencyId;
        public string CurrencyCode { get; } = currencyCode;
        public int DecimalPrecision { get; } = decimalPrecision;
        public decimal MaterialCost { get; set; }
    }

    private sealed record BomUnitSummary(
        IReadOnlyDictionary<Guid, MutableCurrencyTotal> CurrencyTotals,
        int MissingCostCount);

    private sealed class BomGraphBudget
    {
        private int _nodes;
        private int _edges;

        public void ConsumeNode()
        {
            if (++_nodes > MaximumBomGraphNodes) throw BomGraphLimitExceeded();
        }

        public void ConsumeEdge()
        {
            if (++_edges > MaximumBomGraphEdges) throw BomGraphLimitExceeded();
        }
    }

    private sealed class BomExpansionBudget
    {
        private int _rows;

        public void ConsumeRow()
        {
            if (++_rows > MaximumBomExpandedRows)
                throw new CashFlowConflictException("COSTED_BOM_EXPANSION_LIMIT_EXCEEDED",
                    $"تعداد مسیرهای بازشده BOM از سقف ایمن {MaximumBomExpandedRows:N0} ردیف عبور کرده است.");
        }
    }

    private sealed class CostBasisDbRow
    {
        public Guid ItemId { get; init; }
        public string Kind { get; init; } = string.Empty;
        public decimal UnitPrice { get; init; }
        public Guid CurrencyId { get; init; }
        public string CurrencyCode { get; init; } = string.Empty;
        public int CurrencyDecimalPrecision { get; init; }
        public Guid UnitId { get; init; }
        public string UnitCode { get; init; } = string.Empty;
        public Guid SourceDocumentId { get; init; }
        public string SourceDocumentNumber { get; init; } = string.Empty;
        public DateOnly SourceDate { get; init; }
        public Guid PurchaseOrderId { get; init; }
        public string PurchaseOrderNumber { get; init; } = string.Empty;
        public Guid SupplierId { get; init; }
        public string SupplierNumber { get; init; } = string.Empty;
        public string SupplierName { get; init; } = string.Empty;
    }
}
