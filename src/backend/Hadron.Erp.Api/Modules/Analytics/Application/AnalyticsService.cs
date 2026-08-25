using Hadron.Erp.Api.Infrastructure;
using Hadron.Erp.Api.Modules.Catalog.Domain;
using Hadron.Erp.Api.Modules.Inventory.Domain;
using Hadron.Erp.Api.Modules.Planning.Domain;
using Hadron.Erp.Api.Modules.PurchaseSupply.Domain;
using Hadron.Erp.Api.Modules.Sourcing.Application;
using Hadron.Erp.Api.Modules.Sourcing.Domain;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Hadron.Erp.Api.Modules.Analytics.Application;

public sealed class AnalyticsService(IDbContextFactory<HadronDbContext> factory, SourcingService sourcing,
    TimeProvider timeProvider)
{
    private static readonly string[] OperationalPurchaseOrderStatuses =
    [
        PurchaseOrderStatuses.Ordered,
        PurchaseOrderStatuses.Shipped,
        PurchaseOrderStatuses.Customs,
        PurchaseOrderStatuses.PartiallyReceived,
        PurchaseOrderStatuses.Received,
        PurchaseOrderStatuses.Cancelled
    ];

    public const int MaximumPageSize = 200;
    public const int MaximumExportRows = 10_000;

    public async Task<AnalyticsPage<LeadTimeOptionAnalytics>> LeadTime(DateOnly evaluationDate,
        DateTime? dataCutoffUtc, string? search, int page, int pageSize, CancellationToken ct)
    {
        ValidatePage(page, pageSize);
        var cutoff = ValidateCutoff(dataCutoffUtc);
        await using var db = await factory.CreateDbContextAsync(ct);
        var query = db.SourcingOptions.AsNoTracking().Include(x => x.Item).Include(x => x.Supplier)
            .Where(x => x.CreatedAtUtc <= cutoff);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var value = search.Trim();
            query = query.Where(x => x.SourcingNumber.Contains(value) || x.Item.BusinessNumber.Contains(value) ||
                x.Item.NameFa.Contains(value) || x.Supplier.SupplierNumber.Contains(value) || x.Supplier.Name.Contains(value));
        }
        var total = await query.CountAsync(ct);
        var options = await query.OrderBy(x => x.SourcingNumber).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        var optionIds = options.Select(x => x.Id).ToArray();
        var earliest = evaluationDate.AddMonths(-24);
        var schedules = optionIds.Length == 0 ? [] : await db.DeliverySchedules.AsNoTracking()
            .Include(x => x.PurchaseOrderLine).ThenInclude(x => x.PurchaseOrder)
            .Include(x => x.PlanRevisions)
            .Include(x => x.ActualMilestones)
            .Include(x => x.ReceiptLines).ThenInclude(x => x.Receipt)
            .Include(x => x.Cancellations)
            .Where(x => optionIds.Contains(x.PurchaseOrderLine.SourcingOptionId) &&
                x.PurchaseOrderLine.PurchaseOrder.OrderedDate != null &&
                x.PurchaseOrderLine.PurchaseOrder.OrderedDate <= evaluationDate &&
                (x.PurchaseOrderLine.PurchaseOrder.OrderedDate > earliest ||
                    x.ReceiptLines.Any(y => y.Receipt.EffectiveDate > earliest &&
                        y.Receipt.EffectiveDate <= evaluationDate)))
            .AsSplitQuery().ToListAsync(ct);

        var rows = options.Select(option => BuildLeadTime(option, schedules.Where(x =>
            x.PurchaseOrderLine.SourcingOptionId == option.Id).ToArray(), evaluationDate, cutoff)).ToList();
        return new(rows, page, pageSize, total, evaluationDate, cutoff,
            Provenance(cutoff, new Dictionary<string, string?> { ["search"] = search, ["evaluationDate"] = evaluationDate.ToString("O") },
                "MRP-09 schedules + MRP-13 operational facts"));
    }

    public async Task<IReadOnlyList<LeadTimeRecommendationApplicationResponse>> ApplyLeadTime(
        Guid sourcingOptionId, ApplyLeadTimeAnalyticsRequest request, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var optionNumber = await db.SourcingOptions.AsNoTracking().Where(x => x.Id == sourcingOptionId)
            .Select(x => x.SourcingNumber).SingleOrDefaultAsync(ct) ?? throw new KeyNotFoundException();
        var analytics = await LeadTime(request.EvaluationDate, null, optionNumber, 1, MaximumPageSize, ct);
        var row = analytics.Items.SingleOrDefault(x => x.SourcingOptionId == sourcingOptionId)
            ?? throw new KeyNotFoundException();
        if (request.StageCodes.Count == 0 || request.StageCodes.Distinct(StringComparer.Ordinal).Count() != request.StageCodes.Count)
            throw Validation("stageCodes", "حداقل یک مرحله یکتا باید انتخاب شود.");
        var stages = new List<LeadTimeStageApplication>();
        foreach (var code in request.StageCodes)
        {
            var stage = row.Stages.SingleOrDefault(x => x.StageCode == code)
                ?? throw Validation("stageCodes", "مرحله Lead Time معتبر نیست.");
            if (stage.Statistics?.RecommendedDays is not int recommended)
                throw Validation("stageCodes", "نمونه معتبر این مرحله برای اعمال P80 کافی نیست.");
            var months = stage.Statistics.SampleWindow == "12M" ? 12 : 24;
            stages.Add(new(code, recommended, stage.Statistics.SampleCount,
                request.EvaluationDate.AddMonths(-months), request.EvaluationDate));
        }
        return await sourcing.ApplyLeadTimeRecommendation(sourcingOptionId,
            new(request.ExpectedVersion, stages, request.Reason), request.EvaluationDate, ct);
    }

    public Task<IReadOnlyList<LeadTimeRecommendationApplicationResponse>> LeadTimeHistory(Guid id,
        CancellationToken ct) => sourcing.ListLeadTimeApplications(id, ct);

    public async Task<ForecastAccuracyResponse> ForecastAccuracy(DateTime? dataCutoffUtc, string? search,
        int page, int pageSize, CancellationToken ct)
    {
        ValidatePage(page, pageSize);
        var cutoff = ValidateCutoff(dataCutoffUtc);
        await using var db = await factory.CreateDbContextAsync(ct);
        var searchPattern = string.IsNullOrWhiteSpace(search) ? string.Empty : $"%{search.Trim()}%";
        var aggregate = await db.Database.SqlQueryRaw<ForecastAccuracyAggregateDbRow>(
            ForecastAccuracyFactsSql + ForecastAccuracyAggregateSql,
            ForecastParameters(cutoff, searchPattern)).SingleAsync(ct);
        var data = await db.Database.SqlQueryRaw<ForecastAccuracyDbRow>(
            ForecastAccuracyFactsSql + ForecastAccuracyPageSql,
            ForecastParameters(cutoff, searchPattern,
                new NpgsqlParameter("limit", pageSize),
                new NpgsqlParameter("offset", (page - 1) * pageSize))).ToListAsync(ct);
        var pageRows = data.Select(BuildForecastRow).ToList();
        var summary = new ForecastAccuracySummary(aggregate.RowCount,
            Percentage(aggregate.BaselineAbsoluteError, aggregate.BaselineActual),
            Percentage(aggregate.BaselineError, aggregate.BaselineActual),
            Percentage(aggregate.FinalAbsoluteError, aggregate.FinalActual),
            Percentage(aggregate.FinalError, aggregate.FinalActual));
        var resultPage = new AnalyticsPage<ForecastAccuracyRow>(pageRows, page, pageSize, aggregate.RowCount,
            DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime), cutoff,
            Provenance(cutoff, new Dictionary<string, string?> { ["search"] = search },
                "Closed planning periods + immutable forecast/actual revision history"));
        return new(resultPage, summary);
    }

    public async Task<CommercialCommitmentResponse> Commercial(DateOnly evaluationDate, DateTime? dataCutoffUtc,
        string? search, int page, int pageSize, CancellationToken ct)
    {
        ValidatePage(page, pageSize);
        var cutoff = ValidateCutoff(dataCutoffUtc);
        await using var db = await factory.CreateDbContextAsync(ct);
        var query = db.PurchaseOrders.AsNoTracking().Where(x => x.CreatedAtUtc <= cutoff &&
            x.LifecycleEvents.Any(e => e.CreatedAtUtc <= cutoff &&
                OperationalPurchaseOrderStatuses.Contains(e.ToStatus)));
        if (!string.IsNullOrWhiteSpace(search))
        {
            var value = search.Trim();
            query = query.Where(x => x.PurchaseOrderNumber.Contains(value) || x.Supplier.SupplierNumber.Contains(value) ||
                x.Supplier.Name.Contains(value));
        }
        var total = await query.CountAsync(ct);
        var pageIds = await query.OrderBy(x => x.PurchaseOrderNumber).Select(x => x.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).ToArrayAsync(ct);
        var loaded = await db.PurchaseOrders.AsNoTracking().Where(x => pageIds.Contains(x.Id))
            .Include(x => x.Supplier).Include(x => x.Currency)
            .Include(x => x.Lines).ThenInclude(x => x.Schedules).ThenInclude(x => x.PlanRevisions)
            .Include(x => x.Lines).ThenInclude(x => x.Schedules).ThenInclude(x => x.ActualMilestones)
            .Include(x => x.Lines).ThenInclude(x => x.Schedules).ThenInclude(x => x.ReceiptLines).ThenInclude(x => x.Receipt)
            .Include(x => x.Lines).ThenInclude(x => x.Schedules).ThenInclude(x => x.Cancellations)
            .Include(x => x.LifecycleEvents)
            .AsSplitQuery().ToListAsync(ct);
        var rows = loaded.Select(x => BuildCommercial(x, evaluationDate, cutoff))
            .OrderBy(x => x.PurchaseOrderNumber).ToList();
        var filteredOrderIds = query.Select(x => x.Id);
        var totals = await db.PurchaseOrderLines.AsNoTracking()
            .Where(x => filteredOrderIds.Contains(x.PurchaseOrderId))
            .GroupBy(x => new { x.PurchaseOrder.CurrencyId, x.PurchaseOrder.Currency.Code })
            .Select(x => new CurrencyCommitmentTotal(x.Key.CurrencyId, x.Key.Code,
                x.Sum(y => y.OrderedQuantity * y.UnitPrice)))
            .ToListAsync(ct);
        totals = totals.OrderBy(x => x.CurrencyCode).ToList();
        var resultPage = new AnalyticsPage<CommercialCommitmentRow>(rows, page, pageSize, total, evaluationDate, cutoff,
            Provenance(cutoff, new Dictionary<string, string?>
            {
                ["search"] = search,
                ["evaluationDate"] = evaluationDate.ToString("O")
            }, "MRP-09 committed snapshots + MRP-13 receipts/cancellations"));
        return new(resultPage, totals);
    }

    public async Task<AnalyticsPage<InventoryPositionAnalytics>> InventoryPositions(DateOnly asOfDate,
        DateTime? dataCutoffUtc, string? search, int page, int pageSize, CancellationToken ct)
    {
        ValidatePage(page, pageSize);
        var cutoff = ValidateCutoff(dataCutoffUtc);
        await using var db = await factory.CreateDbContextAsync(ct);
        var eligibleEffects = from effect in db.InventoryLedgerEffects.AsNoTracking()
                              join transaction in db.InventoryTransactions.AsNoTracking()
                                  on effect.TransactionId equals transaction.Id
                              where effect.EffectiveDate <= asOfDate && transaction.CreatedAtUtc <= cutoff
                              select effect;
        var balances = eligibleEffects
            .GroupBy(x => new { x.ItemId, x.WarehouseId, x.UnitId }).Select(x => new
            { x.Key.ItemId, x.Key.WarehouseId, x.Key.UnitId, OnHand = x.Sum(y => y.QuantityEffect) });
        var companyBalances = eligibleEffects.GroupBy(x => new { x.ItemId, x.UnitId }).Select(x => new
        { x.Key.ItemId, x.Key.UnitId, OnHand = x.Sum(y => y.QuantityEffect) });
        var query = from balance in balances
                    join company in companyBalances
                        on new { balance.ItemId, balance.UnitId } equals new { company.ItemId, company.UnitId }
                    join item in db.Items.AsNoTracking() on balance.ItemId equals item.Id
                    join warehouse in db.Warehouses.AsNoTracking() on balance.WarehouseId equals warehouse.Id
                    join unit in db.Units.AsNoTracking() on balance.UnitId equals unit.Id
                    where balance.OnHand != 0
                    select new
                    {
                        Item = item,
                        Warehouse = warehouse,
                        Unit = unit,
                        balance.OnHand,
                        CompanyOnHand = company.OnHand
                    };
        if (!string.IsNullOrWhiteSpace(search))
        {
            var value = search.Trim();
            query = query.Where(x => x.Item.BusinessNumber.Contains(value) || x.Item.NameFa.Contains(value) ||
                x.Warehouse.BusinessNumber.Contains(value) || x.Warehouse.NameFa.Contains(value));
        }
        var total = await query.CountAsync(ct);
        var loaded = await query.OrderBy(x => x.Item.BusinessNumber).ThenBy(x => x.Warehouse.BusinessNumber)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        var rows = loaded.Select(x => new InventoryPositionAnalytics(x.Item.Id, x.Item.BusinessNumber,
            x.Item.NameFa, x.Item.NameEn, x.Warehouse.Id, x.Warehouse.BusinessNumber, x.Warehouse.NameFa,
            x.Warehouse.NameEn, x.Warehouse.IsActive, x.Unit.Id, x.Unit.Code, x.Unit.NameFa, x.Unit.NameEn,
            x.OnHand, x.CompanyOnHand, !x.Warehouse.IsActive && x.OnHand != 0)).ToList();
        return new(rows, page, pageSize, total, asOfDate, cutoff,
            Provenance(cutoff, new Dictionary<string, string?> { ["asOfDate"] = asOfDate.ToString("O"), ["search"] = search },
                "MRP-08 immutable inventory ledger effects"));
    }

    public async Task<AnalyticsPage<InventoryMovementAnalytics>> InventoryMovements(DateOnly fromDate,
        DateOnly toDate, DateTime? dataCutoffUtc, string? search, int page, int pageSize, CancellationToken ct)
    {
        ValidatePage(page, pageSize);
        if (toDate < fromDate)
            throw new AnalyticsValidationException(new Dictionary<string, string[]>
            { ["toDate"] = ["To date must be on or after from date."] });
        var cutoff = ValidateCutoff(dataCutoffUtc);
        await using var db = await factory.CreateDbContextAsync(ct);
        var query = from effect in db.InventoryLedgerEffects.AsNoTracking()
                    join transaction in db.InventoryTransactions.AsNoTracking()
                        on effect.TransactionId equals transaction.Id
                    join item in db.Items.AsNoTracking() on effect.ItemId equals item.Id
                    join warehouse in db.Warehouses.AsNoTracking() on effect.WarehouseId equals warehouse.Id
                    where effect.EffectiveDate >= fromDate && effect.EffectiveDate <= toDate &&
                        transaction.CreatedAtUtc <= cutoff
                    select new { Effect = effect, Transaction = transaction, Item = item, Warehouse = warehouse };
        if (!string.IsNullOrWhiteSpace(search))
        {
            var value = search.Trim();
            query = query.Where(x => x.Item.BusinessNumber.Contains(value) ||
                x.Warehouse.BusinessNumber.Contains(value) || x.Transaction.TransactionType.Contains(value));
        }
        var total = await query.CountAsync(ct);
        var rows = await query.OrderByDescending(x => x.Effect.EffectiveDate)
            .ThenBy(x => x.Transaction.CreatedAtUtc).ThenBy(x => x.Transaction.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new InventoryMovementAnalytics(x.Transaction.Id, x.Transaction.TransactionType,
                x.Effect.EffectiveDate, x.Item.Id, x.Item.BusinessNumber, x.Warehouse.Id,
                x.Warehouse.BusinessNumber, x.Effect.QuantityEffect, x.Transaction.SourceDocumentType,
                x.Transaction.SourceDocumentId, x.Transaction.CreatedAtUtc)).ToListAsync(ct);
        return new(rows, page, pageSize, total, toDate, cutoff,
            Provenance(cutoff, new Dictionary<string, string?>
            {
                ["fromDate"] = fromDate.ToString("O"),
                ["toDate"] = toDate.ToString("O"),
                ["search"] = search
            }, "MRP-08 immutable inventory ledger effects"));
    }

    private static LeadTimeOptionAnalytics BuildLeadTime(SourcingOption option, DeliverySchedule[] schedules,
        DateOnly evaluationDate, DateTime cutoff)
    {
        var observations = new List<LeadObservation>();
        foreach (var schedule in schedules)
        {
            var cancels = schedule.Cancellations.Where(x => x.CreatedAtUtc <= cutoff).Sum(x => x.Quantity);
            var validReceipts = schedule.ReceiptLines.Where(x => x.Receipt.PostedAtUtc <= cutoff &&
                (x.Receipt.ReversedAtUtc == null || x.Receipt.ReversedAtUtc > cutoff)).ToArray();
            if (cancels > 0 || validReceipts.Sum(x => x.Quantity) != schedule.AllocatedQuantity) continue;
            var completion = validReceipts.MaxBy(x => x.Receipt.EffectiveDate)?.Receipt.EffectiveDate;
            if (completion is null || completion > evaluationDate || schedule.PurchaseOrderLine.PurchaseOrder.OrderedDate is null) continue;
            var currentActuals = CurrentMilestones(schedule.ActualMilestones.Where(x => x.CreatedAtUtc <= cutoff));
            DateOnly? Actual(string code) => currentActuals.FirstOrDefault(x => x.MilestoneCode == code)?.ActualDate;
            var original = schedule.PlanRevisions.Where(x => x.CreatedAtUtc <= cutoff && x.PlanType == PurchasePlanTypes.Original)
                .OrderBy(x => x.RevisionNumber).FirstOrDefault();
            observations.Add(new(schedule.PurchaseOrderLine.PurchaseOrder.OrderedDate.Value, completion.Value,
                Actual(PurchaseMilestoneCodes.SupplierReady), Actual(PurchaseMilestoneCodes.Shipped),
                Actual(PurchaseMilestoneCodes.ArrivedDestination), original?.WarehouseReceiptDate));
        }
        var selected = SelectWindow(observations, evaluationDate);
        var reliabilityStart = evaluationDate.AddMonths(selected.Label == "12M" ? -12 : -24);
        var reliability = schedules.Where(x => x.PurchaseOrderLine.PurchaseOrder.OrderedDate > reliabilityStart &&
            x.PurchaseOrderLine.PurchaseOrder.OrderedDate <= evaluationDate).ToArray();
        var allocated = reliability.Sum(x => x.AllocatedQuantity);
        var cancelledQuantities = reliability.Select(x => x.Cancellations.Where(y => y.CreatedAtUtc <= cutoff)
            .Sum(y => y.Quantity)).ToArray();
        var cancelledCount = cancelledQuantities.Count(x => x > 0);
        var cancelledQty = cancelledQuantities.Sum();
        var received = reliability.Sum(x => x.ReceiptLines.Where(y => y.Receipt.PostedAtUtc <= cutoff &&
            (y.Receipt.ReversedAtUtc == null || y.Receipt.ReversedAtUtc > cutoff)).Sum(y => y.Quantity));
        var totalValues = selected.Items.Select(x => x.Completion.DayNumber - x.Ordered.DayNumber).ToArray();
        var stages = new[]
        {
            Stage(LeadTimeStageCodes.Supplier, selected, x => Days(x.Ordered, x.SupplierReady), option.SupplierLeadTimeDays),
            Stage(LeadTimeStageCodes.ForwarderPreShipment, selected, x => Days(x.SupplierReady, x.Shipped), option.ForwarderToShipmentDays),
            Stage(LeadTimeStageCodes.Shipping, selected, x => Days(x.Shipped, x.Arrived), option.ShippingPeriodDays),
            Stage(LeadTimeStageCodes.DestinationCustoms, selected, x => Days(x.Arrived, x.Completion), option.DestinationCustomsDays)
        };
        var onTimeDenominator = selected.Items.Count(x => x.OriginalWarehouseReceipt.HasValue);
        decimal? onTime = onTimeDenominator == 0 ? null : selected.Items.Count(x => x.OriginalWarehouseReceipt >= x.Completion) * 100m / onTimeDenominator;
        return new(option.Id, option.SourcingNumber, option.Version, option.ItemId, option.Item.BusinessNumber,
            option.Item.NameFa, option.Item.NameEn, option.SupplierId, option.Supplier.SupplierNumber,
            option.Supplier.Name, null, AnalyticsMath.Statistics(totalValues, selected.Label), stages,
            selected.Items.Count, cancelledCount, cancelledQty, allocated == 0 ? 0 : received / allocated * 100m, onTime);
    }

    private static LeadTimeStageAnalytics Stage(string code, Window window, Func<LeadObservation, int?> selector,
        int? configured)
    {
        var values = window.Items.Select(selector).Where(x => x.HasValue).Select(x => x!.Value).ToArray();
        var stats = AnalyticsMath.Statistics(values, window.Label);
        return new(code, stats, configured, stats?.RecommendedDays is null ? "INSUFFICIENT_SAMPLE" : "RECOMMENDATION_AVAILABLE");
    }

    private static int? Days(DateOnly? from, DateOnly? to) => from.HasValue && to.HasValue && to >= from
        ? to.Value.DayNumber - from.Value.DayNumber : null;

    private static Window SelectWindow(IReadOnlyList<LeadObservation> all, DateOnly evaluationDate)
    {
        var twelveStart = evaluationDate.AddMonths(-12);
        var twelve = all.Where(x => x.Completion > twelveStart && x.Completion <= evaluationDate).ToArray();
        if (twelve.Length >= 5) return new(twelve, "12M");
        var twentyFourStart = evaluationDate.AddMonths(-24);
        return new(all.Where(x => x.Completion > twentyFourStart && x.Completion <= evaluationDate).ToArray(), "24M");
    }

    private static DeliveryScheduleActualMilestone[] CurrentMilestones(IEnumerable<DeliveryScheduleActualMilestone> source)
    {
        var items = source.ToArray();
        var superseded = items.Where(x => x.SupersedesMilestoneId.HasValue).Select(x => x.SupersedesMilestoneId!.Value).ToHashSet();
        return items.Where(x => !superseded.Contains(x.Id)).GroupBy(x => x.MilestoneCode)
            .Select(x => x.OrderByDescending(y => y.CreatedAtUtc).First()).ToArray();
    }

    private static ForecastAccuracyRow BuildForecastRow(ForecastAccuracyDbRow row)
    {
        decimal? Error(decimal? forecast) => forecast.HasValue ? forecast.Value - row.Actual : null;
        decimal? Abs(decimal? forecast) => forecast.HasValue ? decimal.Abs(forecast.Value - row.Actual) : null;
        decimal? Ape(decimal? forecast) => forecast.HasValue ? AnalyticsMath.Ape(forecast.Value, row.Actual) : null;
        return new(row.ProductId, row.ProductCode, row.ProductNameFa, row.ProductNameEn, row.PeriodId,
            row.JalaliYear, row.JalaliMonth, row.PeriodStart, row.PeriodEnd, row.EffectiveCloseAtUtc,
            row.BaselineForecast, row.FinalForecast, row.Actual, Error(row.BaselineForecast),
            Abs(row.BaselineForecast), Ape(row.BaselineForecast), Error(row.FinalForecast),
            Abs(row.FinalForecast), Ape(row.FinalForecast),
            row.BaselineForecast.HasValue ? "AVAILABLE" : "MISSING_FORECAST",
            row.FinalForecast.HasValue ? "AVAILABLE" : "MISSING_FORECAST");
    }

    private static decimal? Percentage(decimal? numerator, decimal? denominator) =>
        numerator.HasValue && denominator is not null and not 0m ? numerator.Value / denominator.Value * 100m : null;

    private static object[] ForecastParameters(DateTime cutoff, string search, params NpgsqlParameter[] additional) =>
    [
        new NpgsqlParameter("cutoff", cutoff),
        new NpgsqlParameter("search", search),
        .. additional
    ];

    private static CommercialCommitmentRow BuildCommercial(PurchaseOrder po, DateOnly evaluationDate, DateTime cutoff)
    {
        var schedules = po.Lines.SelectMany(x => x.Schedules).ToArray();
        var required = schedules.Where(s =>
        {
            var received = EffectiveReceipts(s, cutoff).Sum(x => x.Quantity);
            var cancelled = s.Cancellations.Where(x => x.CreatedAtUtc <= cutoff).Sum(x => x.Quantity);
            return !(received == 0 && cancelled >= s.AllocatedQuantity);
        }).ToArray();
        var validReceipts = schedules.SelectMany(x => EffectiveReceipts(x, cutoff)).ToArray();
        var receivedQuantity = validReceipts.Sum(x => x.Quantity);
        var cancelledQuantity = schedules.Sum(x => x.Cancellations.Where(y => y.CreatedAtUtc <= cutoff)
            .Sum(y => y.Quantity));
        var orderedQuantity = schedules.Sum(x => x.AllocatedQuantity);
        var openQuantity = orderedQuantity - receivedQuantity - cancelledQuantity;
        var originalEtas = required.Select(x => x.PlanRevisions.Where(y => y.CreatedAtUtc <= cutoff &&
            y.PlanType == PurchasePlanTypes.Original).OrderBy(y => y.RevisionNumber)
            .FirstOrDefault()?.WarehouseReceiptDate).ToArray();
        var currentEtas = required.Select(x => x.PlanRevisions.Where(y => y.CreatedAtUtc <= cutoff &&
            y.PlanType == PurchasePlanTypes.Revised).OrderByDescending(y => y.RevisionNumber)
            .FirstOrDefault()?.WarehouseReceiptDate ?? x.PlanRevisions.Where(y => y.CreatedAtUtc <= cutoff &&
                y.PlanType == PurchasePlanTypes.Original).OrderBy(y => y.RevisionNumber)
                .FirstOrDefault()?.WarehouseReceiptDate).ToArray();
        var originalEta = required.Length == 0 || originalEtas.Any(x => !x.HasValue) ? null : originalEtas.Max();
        var currentEta = required.Length == 0 || currentEtas.Any(x => !x.HasValue) ? null : currentEtas.Max();
        var firstReceipt = validReceipts.MinBy(x => x.Receipt.EffectiveDate)?.Receipt.EffectiveDate;
        var shippedAnchors = required.Select(s => ResolveShippedAnchor(s, cutoff)).ToArray();
        var warehouseReceiptAnchors = required.Select(s => ResolveWarehouseReceiptAnchor(s, cutoff)).ToArray();
        DateOnly? anchor = null;
        var finalReceipt = ResolveFinalReceipt(po, cutoff);
        if (po.CreditAnchor == PurchaseMilestoneCodes.WarehouseReceipt)
            anchor = finalReceipt ?? (required.Length == 0 || warehouseReceiptAnchors.Any(x => !x.HasValue)
                ? null : warehouseReceiptAnchors.Max());
        else if (po.CreditAnchor == "ORDERED") anchor = po.OrderedDate;
        else if (po.CreditAnchor == "SHIPPED") anchor = required.Length == 0 || shippedAnchors.Any(x => !x.HasValue) ? null : shippedAnchors.Max();
        DateOnly? expected = anchor.HasValue && po.CreditDays.HasValue ? anchor.Value.AddDays(po.CreditDays.Value) : null;
        var completion = openQuantity > 0 ? "OPEN" : receivedQuantity > 0 && cancelledQuantity > 0
            ? "RECEIVED_WITH_CANCELLATION" : receivedQuantity > 0 ? "RECEIVED" : "CANCELLED";
        var status = po.LifecycleEvents.Where(x => x.CreatedAtUtc <= cutoff)
            .OrderByDescending(x => x.CreatedAtUtc).ThenByDescending(x => x.Id)
            .Select(x => x.ToStatus).FirstOrDefault() ?? PurchaseOrderStatuses.Draft;
        return new(po.Id, po.PurchaseOrderNumber, po.SupplierId, po.Supplier.SupplierNumber, po.Supplier.Name,
            po.CurrencyId, po.Currency.Code, po.Lines.Sum(x => x.OrderedQuantity * x.UnitPrice), orderedQuantity,
            receivedQuantity, cancelledQuantity, openQuantity, originalEta, currentEta, firstReceipt, finalReceipt,
            completion, status, po.CreditAnchor ?? string.Empty, anchor, expected,
            anchor.HasValue ? "RESOLVED" : "UNRESOLVED", required.Length);
    }

    private static DateOnly? ResolveShippedAnchor(DeliverySchedule schedule, DateTime cutoff)
    {
        var current = CurrentMilestones(schedule.ActualMilestones.Where(x => x.CreatedAtUtc <= cutoff));
        var actual = current.FirstOrDefault(x => x.MilestoneCode == PurchaseMilestoneCodes.Shipped)?.ActualDate;
        if (actual.HasValue) return actual;
        var revised = schedule.PlanRevisions.Where(x => x.CreatedAtUtc <= cutoff && x.PlanType == PurchasePlanTypes.Revised)
            .OrderByDescending(x => x.RevisionNumber).FirstOrDefault()?.ShippedDate;
        return revised ?? schedule.PlanRevisions.Where(x => x.CreatedAtUtc <= cutoff && x.PlanType == PurchasePlanTypes.Original)
            .OrderBy(x => x.RevisionNumber).FirstOrDefault()?.ShippedDate;
    }

    private static DateOnly? ResolveWarehouseReceiptAnchor(DeliverySchedule schedule, DateTime cutoff)
    {
        var current = CurrentMilestones(schedule.ActualMilestones.Where(x => x.CreatedAtUtc <= cutoff));
        var actual = current.FirstOrDefault(x =>
            x.MilestoneCode == PurchaseMilestoneCodes.WarehouseReceipt)?.ActualDate;
        if (actual.HasValue) return actual;
        var revised = schedule.PlanRevisions.Where(x => x.CreatedAtUtc <= cutoff &&
                x.PlanType == PurchasePlanTypes.Revised)
            .OrderByDescending(x => x.RevisionNumber).FirstOrDefault()?.WarehouseReceiptDate;
        return revised ?? schedule.PlanRevisions.Where(x => x.CreatedAtUtc <= cutoff &&
                x.PlanType == PurchasePlanTypes.Original)
            .OrderBy(x => x.RevisionNumber).FirstOrDefault()?.WarehouseReceiptDate;
    }

    private static DateOnly? ResolveFinalReceipt(PurchaseOrder po, DateTime cutoff)
    {
        var schedules = po.Lines.SelectMany(x => x.Schedules).ToArray();
        var received = schedules.Sum(x => EffectiveReceipts(x, cutoff).Sum(y => y.Quantity));
        var cancelled = schedules.Sum(x => x.Cancellations.Where(y => y.CreatedAtUtc <= cutoff).Sum(y => y.Quantity));
        var allocated = schedules.Sum(x => x.AllocatedQuantity);
        if (allocated - received - cancelled > 0 || received == 0) return null;
        return schedules.SelectMany(x => EffectiveReceipts(x, cutoff)).MaxBy(x => x.Receipt.EffectiveDate)?.Receipt.EffectiveDate;
    }

    private static PurchaseReceiptLine[] EffectiveReceipts(DeliverySchedule schedule, DateTime cutoff) =>
        schedule.ReceiptLines.Where(x => x.Receipt.PostedAtUtc <= cutoff &&
            (x.Receipt.ReversedAtUtc == null || x.Receipt.ReversedAtUtc > cutoff)).ToArray();

    private static DateTime TehranStartUtc(DateOnly date)
    {
        var zone = ResolveTehran();
        return TimeZoneInfo.ConvertTimeToUtc(date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified), zone);
    }

    private static TimeZoneInfo ResolveTehran()
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById("Asia/Tehran"); }
        catch (TimeZoneNotFoundException) { return TimeZoneInfo.FindSystemTimeZoneById("Iran Standard Time"); }
    }

    private DateTime ValidateCutoff(DateTime? requested)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        if (requested.HasValue && requested.Value.Kind != DateTimeKind.Utc)
            throw Validation("dataCutoffUtc", "Data cutoff must be UTC.");
        if (requested > now) throw Validation("dataCutoffUtc", "Data cutoff cannot be in the future.");
        return requested ?? now;
    }

    private static AnalyticsProvenance Provenance(DateTime cutoff, IReadOnlyDictionary<string, string?> filters,
        string source) => new("v1", DateTime.UtcNow, cutoff, filters, source);

    private static void ValidatePage(int page, int pageSize)
    {
        if (page < 1 || pageSize is < 1 or > MaximumPageSize)
            throw Validation("paging", $"pageSize must be between 1 and {MaximumPageSize}.");
    }

    private static AnalyticsValidationException Validation(string key, string message) =>
        new(new Dictionary<string, string[]> { [key] = [message] });

    private const string ForecastAccuracyFactsSql = """
        WITH effective_periods AS (
            SELECT bp."Id", bp."JalaliYear", bp."JalaliMonth", bp."StartDate", bp."EndDate",
                lifecycle."OccurredAtUtc" AS "EffectiveCloseAtUtc"
            FROM planning.business_periods bp
            JOIN LATERAL (
                SELECT event."ToStatus", event."OccurredAtUtc"
                FROM planning.period_lifecycle_events event
                WHERE event."BusinessPeriodId" = bp."Id" AND event."OccurredAtUtc" <= @cutoff
                ORDER BY event."OccurredAtUtc" DESC, event."Id" DESC
                LIMIT 1
            ) lifecycle ON lifecycle."ToStatus" = 'CLOSED'
        ), scope_keys AS (
            SELECT series."ProductId", series."BusinessPeriodId"
            FROM planning.forecast_series series
            JOIN effective_periods period ON period."Id" = series."BusinessPeriodId"
            WHERE series."CreatedAtUtc" <= @cutoff
            UNION
            SELECT actual."ProductId", actual."BusinessPeriodId"
            FROM planning.daily_actuals actual
            JOIN effective_periods period ON period."Id" = actual."BusinessPeriodId"
            WHERE actual."CreatedAtUtc" <= @cutoff
        ), facts AS (
            SELECT item."Id" AS "ProductId", item."BusinessNumber" AS "ProductCode",
                item."NameFa" AS "ProductNameFa", item."NameEn" AS "ProductNameEn",
                period."Id" AS "PeriodId", period."JalaliYear", period."JalaliMonth",
                period."StartDate" AS "PeriodStart", period."EndDate" AS "PeriodEnd",
                period."EffectiveCloseAtUtc",
                (
                    SELECT revision."ForecastQty"
                    FROM planning.forecast_revisions revision
                    WHERE revision."ForecastSeriesId" = series."Id"
                        AND revision."CreatedAtUtc" <= LEAST(
                            @cutoff, period."StartDate"::timestamp AT TIME ZONE 'Asia/Tehran')
                    ORDER BY revision."CreatedAtUtc" DESC, revision."RevisionNumber" DESC
                    LIMIT 1
                ) AS "BaselineForecast",
                (
                    SELECT revision."ForecastQty"
                    FROM planning.forecast_revisions revision
                    WHERE revision."ForecastSeriesId" = series."Id"
                        AND revision."CreatedAtUtc" < period."EffectiveCloseAtUtc"
                        AND revision."CreatedAtUtc" <= @cutoff
                    ORDER BY revision."CreatedAtUtc" DESC, revision."RevisionNumber" DESC
                    LIMIT 1
                ) AS "FinalForecast",
                COALESCE((
                    SELECT SUM(COALESCE((
                        SELECT revision."ActualQty"
                        FROM planning.daily_actual_revisions revision
                        WHERE revision."DailyActualId" = actual."Id"
                            AND revision."CreatedAtUtc" <= period."EffectiveCloseAtUtc"
                            AND revision."CreatedAtUtc" <= @cutoff
                        ORDER BY revision."CreatedAtUtc" DESC, revision."RevisionNumber" DESC
                        LIMIT 1
                    ), 0))
                    FROM planning.daily_actuals actual
                    WHERE actual."ProductId" = scope."ProductId"
                        AND actual."BusinessPeriodId" = scope."BusinessPeriodId"
                        AND actual."CreatedAtUtc" <= period."EffectiveCloseAtUtc"
                        AND actual."CreatedAtUtc" <= @cutoff
                ), 0) AS "Actual"
            FROM scope_keys scope
            JOIN effective_periods period ON period."Id" = scope."BusinessPeriodId"
            JOIN catalog.items item ON item."Id" = scope."ProductId"
            LEFT JOIN planning.forecast_series series
                ON series."ProductId" = scope."ProductId"
                AND series."BusinessPeriodId" = scope."BusinessPeriodId"
                AND series."CreatedAtUtc" <= @cutoff
            WHERE @search = '' OR item."BusinessNumber" ILIKE @search
                OR item."NameFa" ILIKE @search OR COALESCE(item."NameEn", '') ILIKE @search
        )
        """;

    private const string ForecastAccuracyAggregateSql = """
        SELECT COUNT(*)::integer AS "RowCount",
            SUM(ABS("BaselineForecast" - "Actual")) FILTER (WHERE "BaselineForecast" IS NOT NULL)
                AS "BaselineAbsoluteError",
            SUM("BaselineForecast" - "Actual") FILTER (WHERE "BaselineForecast" IS NOT NULL)
                AS "BaselineError",
            SUM("Actual") FILTER (WHERE "BaselineForecast" IS NOT NULL) AS "BaselineActual",
            SUM(ABS("FinalForecast" - "Actual")) FILTER (WHERE "FinalForecast" IS NOT NULL)
                AS "FinalAbsoluteError",
            SUM("FinalForecast" - "Actual") FILTER (WHERE "FinalForecast" IS NOT NULL) AS "FinalError",
            SUM("Actual") FILTER (WHERE "FinalForecast" IS NOT NULL) AS "FinalActual"
        FROM facts
        """;

    private const string ForecastAccuracyPageSql = """
        SELECT "ProductId", "ProductCode", "ProductNameFa", "ProductNameEn", "PeriodId",
            "JalaliYear", "JalaliMonth", "PeriodStart", "PeriodEnd", "EffectiveCloseAtUtc",
            "BaselineForecast", "FinalForecast", "Actual"
        FROM facts
        ORDER BY "PeriodStart" DESC, "ProductCode"
        LIMIT @limit OFFSET @offset
        """;

    private sealed class ForecastAccuracyDbRow
    {
        public Guid ProductId { get; init; }
        public string ProductCode { get; init; } = string.Empty;
        public string ProductNameFa { get; init; } = string.Empty;
        public string? ProductNameEn { get; init; }
        public Guid PeriodId { get; init; }
        public int JalaliYear { get; init; }
        public int JalaliMonth { get; init; }
        public DateOnly PeriodStart { get; init; }
        public DateOnly PeriodEnd { get; init; }
        public DateTime EffectiveCloseAtUtc { get; init; }
        public decimal? BaselineForecast { get; init; }
        public decimal? FinalForecast { get; init; }
        public decimal Actual { get; init; }
    }

    private sealed class ForecastAccuracyAggregateDbRow
    {
        public int RowCount { get; init; }
        public decimal? BaselineAbsoluteError { get; init; }
        public decimal? BaselineError { get; init; }
        public decimal? BaselineActual { get; init; }
        public decimal? FinalAbsoluteError { get; init; }
        public decimal? FinalError { get; init; }
        public decimal? FinalActual { get; init; }
    }

    private sealed record LeadObservation(DateOnly Ordered, DateOnly Completion, DateOnly? SupplierReady,
        DateOnly? Shipped, DateOnly? Arrived, DateOnly? OriginalWarehouseReceipt);
    private sealed record Window(IReadOnlyList<LeadObservation> Items, string Label);
}
