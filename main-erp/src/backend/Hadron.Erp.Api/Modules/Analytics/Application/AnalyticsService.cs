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
        var searchPattern = string.IsNullOrWhiteSpace(search) ? string.Empty : $"%{search.Trim()}%";
        var contexts = await db.Database.SqlQueryRaw<LeadTimeContextDbRow>(LeadTimeContextSql,
            new NpgsqlParameter("cutoff", cutoff), new NpgsqlParameter("search", searchPattern),
            new NpgsqlParameter("limit", pageSize), new NpgsqlParameter("offset", (page - 1) * pageSize))
            .ToListAsync(ct);
        var total = contexts.FirstOrDefault()?.TotalCount ?? 0;
        var optionIds = contexts.Select(x => x.SourcingOptionId).ToArray();
        var earliest = evaluationDate.AddMonths(-24);
        var schedules = optionIds.Length == 0 ? [] : await db.DeliverySchedules.AsNoTracking()
            .Include(x => x.PurchaseOrderLine).ThenInclude(x => x.PurchaseOrder)
            .Include(x => x.PlanRevisions)
            .Include(x => x.ActualMilestones)
            .Include(x => x.ReceiptLines).ThenInclude(x => x.Receipt)
            .Include(x => x.Cancellations)
            .Where(x => optionIds.Contains(x.PurchaseOrderLine.SourcingOptionId) &&
                x.PurchaseOrderLine.PurchaseOrder.OrderedDate != null &&
                x.PurchaseOrderLine.PurchaseOrder.LifecycleEvents.Any(e =>
                    e.CreatedAtUtc <= cutoff && e.ToStatus == PurchaseOrderStatuses.Ordered) &&
                x.PurchaseOrderLine.PurchaseOrder.OrderedDate <= evaluationDate &&
                (x.PurchaseOrderLine.PurchaseOrder.OrderedDate > earliest ||
                    x.ReceiptLines.Any(y => y.Receipt.EffectiveDate > earliest &&
                        y.Receipt.EffectiveDate <= evaluationDate)))
            .AsSplitQuery().ToListAsync(ct);
        var scheduleStates = await AnalyticsCutoffContext.Schedules(db, schedules, cutoff, ct);

        var rows = contexts.Select(context => BuildLeadTime(context, scheduleStates.Where(x =>
            x.Source.PurchaseOrderLine.SourcingOptionId == context.SourcingOptionId).ToArray(), evaluationDate, cutoff))
            .ToList();
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
        var searchPattern = string.IsNullOrWhiteSpace(search) ? string.Empty : $"%{search.Trim()}%";
        var contexts = await db.Database.SqlQueryRaw<CommercialContextDbRow>(CommercialContextSql,
            new NpgsqlParameter("cutoff", cutoff), new NpgsqlParameter("search", searchPattern),
            new NpgsqlParameter("limit", pageSize), new NpgsqlParameter("offset", (page - 1) * pageSize))
            .ToListAsync(ct);
        var total = contexts.FirstOrDefault()?.TotalCount ?? 0;
        var pageIds = contexts.Select(x => x.PurchaseOrderId).ToArray();
        var loaded = await db.PurchaseOrders.AsNoTracking().Where(x => pageIds.Contains(x.Id))
            .Include(x => x.Lines).ThenInclude(x => x.Schedules).ThenInclude(x => x.PlanRevisions)
            .Include(x => x.Lines).ThenInclude(x => x.Schedules).ThenInclude(x => x.ActualMilestones)
            .Include(x => x.Lines).ThenInclude(x => x.Schedules).ThenInclude(x => x.ReceiptLines).ThenInclude(x => x.Receipt)
            .Include(x => x.Lines).ThenInclude(x => x.Schedules).ThenInclude(x => x.Cancellations)
            .Include(x => x.LifecycleEvents)
            .AsSplitQuery().ToListAsync(ct);
        var schedules = loaded.SelectMany(x => x.Lines).SelectMany(x => x.Schedules).ToArray();
        var scheduleStates = await AnalyticsCutoffContext.Schedules(db, schedules, cutoff, ct);
        var contextById = contexts.ToDictionary(x => x.PurchaseOrderId);
        var rows = loaded.Select(x => BuildCommercial(x, contextById[x.Id], scheduleStates.Where(s =>
                s.Source.PurchaseOrderLine.PurchaseOrderId == x.Id).ToArray(), evaluationDate, cutoff))
            .OrderBy(x => x.PurchaseOrderNumber).ToList();
        var totalRows = await db.Database.SqlQueryRaw<CurrencyCommitmentDbRow>(CommercialTotalsSql,
            new NpgsqlParameter("cutoff", cutoff), new NpgsqlParameter("search", searchPattern)).ToListAsync(ct);
        var totals = totalRows.Select(x => new CurrencyCommitmentTotal(x.CurrencyId, x.CurrencyCode,
            x.OriginalAmount)).OrderBy(x => x.CurrencyCode).ToList();
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
        var searchPattern = string.IsNullOrWhiteSpace(search) ? string.Empty : $"%{search.Trim()}%";
        var data = await db.Database.SqlQueryRaw<InventoryPositionDbRow>(InventoryPositionSql,
            new NpgsqlParameter("asOfDate", asOfDate), new NpgsqlParameter("cutoff", cutoff),
            new NpgsqlParameter("search", searchPattern), new NpgsqlParameter("limit", pageSize),
            new NpgsqlParameter("offset", (page - 1) * pageSize)).ToListAsync(ct);
        var total = data.FirstOrDefault()?.TotalCount ?? 0;
        var rows = data.Select(x => new InventoryPositionAnalytics(x.ItemId, x.ItemCode,
            x.ItemNameFa, x.ItemNameEn, x.WarehouseId, x.WarehouseCode, x.WarehouseNameFa,
            x.WarehouseNameEn, x.WarehouseIsActive, x.UnitId, x.UnitCode, x.UnitNameFa, x.UnitNameEn,
            x.OnHand, x.CompanyOnHand, !x.WarehouseIsActive && x.OnHand != 0)).ToList();
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
        var searchPattern = string.IsNullOrWhiteSpace(search) ? string.Empty : $"%{search.Trim()}%";
        var data = await db.Database.SqlQueryRaw<InventoryMovementDbRow>(InventoryMovementSql,
            new NpgsqlParameter("fromDate", fromDate), new NpgsqlParameter("toDate", toDate),
            new NpgsqlParameter("cutoff", cutoff), new NpgsqlParameter("search", searchPattern),
            new NpgsqlParameter("limit", pageSize), new NpgsqlParameter("offset", (page - 1) * pageSize))
            .ToListAsync(ct);
        var total = data.FirstOrDefault()?.TotalCount ?? 0;
        var rows = data.Select(x => new InventoryMovementAnalytics(x.TransactionId, x.TransactionType,
            x.EffectiveDate, x.ItemId, x.ItemCode, x.WarehouseId, x.WarehouseCode, x.Quantity,
            x.SourceDocumentType, x.SourceDocumentId, x.CreatedAtUtc)).ToList();
        return new(rows, page, pageSize, total, toDate, cutoff,
            Provenance(cutoff, new Dictionary<string, string?>
            {
                ["fromDate"] = fromDate.ToString("O"),
                ["toDate"] = toDate.ToString("O"),
                ["search"] = search
            }, "MRP-08 immutable inventory ledger effects"));
    }

    private static LeadTimeOptionAnalytics BuildLeadTime(LeadTimeContextDbRow option,
        DeliveryScheduleAtCutoff[] schedules,
        DateOnly evaluationDate, DateTime cutoff)
    {
        var observations = new List<LeadObservation>();
        foreach (var state in schedules)
        {
            var schedule = state.Source;
            var cancels = schedule.Cancellations.Where(x => x.CreatedAtUtc <= cutoff).Sum(x => x.Quantity);
            var validReceipts = schedule.ReceiptLines.Where(x => x.Receipt.PostedAtUtc <= cutoff &&
                (x.Receipt.ReversedAtUtc == null || x.Receipt.ReversedAtUtc > cutoff)).ToArray();
            if (cancels > 0 || validReceipts.Sum(x => x.Quantity) != state.AllocatedQuantity) continue;
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
        var reliability = schedules.Where(x => x.Source.PurchaseOrderLine.PurchaseOrder.OrderedDate > reliabilityStart &&
            x.Source.PurchaseOrderLine.PurchaseOrder.OrderedDate <= evaluationDate).ToArray();
        var allocated = reliability.Sum(x => x.AllocatedQuantity);
        var cancelledQuantities = reliability.Select(x => x.Source.Cancellations.Where(y => y.CreatedAtUtc <= cutoff)
            .Sum(y => y.Quantity)).ToArray();
        var cancelledCount = cancelledQuantities.Count(x => x > 0);
        var cancelledQty = cancelledQuantities.Sum();
        var received = reliability.Sum(x => x.Source.ReceiptLines.Where(y => y.Receipt.PostedAtUtc <= cutoff &&
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
        return new(option.SourcingOptionId, option.SourcingNumber, option.Version, option.ItemId, option.ItemCode,
            option.ItemNameFa, option.ItemNameEn, option.SupplierId, option.SupplierNumber,
            option.SupplierNameFa, option.SupplierNameEn, AnalyticsMath.Statistics(totalValues, selected.Label), stages,
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

    private static CommercialCommitmentRow BuildCommercial(PurchaseOrder po, CommercialContextDbRow context,
        DeliveryScheduleAtCutoff[] schedules, DateOnly evaluationDate, DateTime cutoff)
    {
        var required = schedules.Where(s =>
        {
            var received = EffectiveReceipts(s.Source, cutoff).Sum(x => x.Quantity);
            var cancelled = s.Source.Cancellations.Where(x => x.CreatedAtUtc <= cutoff).Sum(x => x.Quantity);
            return !(received == 0 && cancelled >= s.AllocatedQuantity);
        }).ToArray();
        var validReceipts = schedules.SelectMany(x => EffectiveReceipts(x.Source, cutoff)).ToArray();
        var receivedQuantity = validReceipts.Sum(x => x.Quantity);
        var cancelledQuantity = schedules.Sum(x => x.Source.Cancellations.Where(y => y.CreatedAtUtc <= cutoff)
            .Sum(y => y.Quantity));
        var orderedQuantity = schedules.Sum(x => x.AllocatedQuantity);
        var openQuantity = orderedQuantity - receivedQuantity - cancelledQuantity;
        var originalEtas = required.Select(x => x.Source.PlanRevisions.Where(y => y.CreatedAtUtc <= cutoff &&
            y.PlanType == PurchasePlanTypes.Original).OrderBy(y => y.RevisionNumber)
            .FirstOrDefault()?.WarehouseReceiptDate).ToArray();
        var currentEtas = required.Select(x => x.Source.PlanRevisions.Where(y => y.CreatedAtUtc <= cutoff &&
            y.PlanType == PurchasePlanTypes.Revised).OrderByDescending(y => y.RevisionNumber)
            .FirstOrDefault()?.WarehouseReceiptDate ?? x.Source.PlanRevisions.Where(y => y.CreatedAtUtc <= cutoff &&
                y.PlanType == PurchasePlanTypes.Original).OrderBy(y => y.RevisionNumber)
                .FirstOrDefault()?.WarehouseReceiptDate).ToArray();
        var originalEta = required.Length == 0 || originalEtas.Any(x => !x.HasValue) ? null : originalEtas.Max();
        var currentEta = required.Length == 0 || currentEtas.Any(x => !x.HasValue) ? null : currentEtas.Max();
        var firstReceipt = validReceipts.MinBy(x => x.Receipt.EffectiveDate)?.Receipt.EffectiveDate;
        var shippedAnchors = required.Select(s => ResolveShippedAnchor(s.Source, cutoff)).ToArray();
        var warehouseReceiptAnchors = required.Select(s => ResolveWarehouseReceiptAnchor(s.Source, cutoff)).ToArray();
        DateOnly? anchor = null;
        var finalReceipt = ResolveFinalReceipt(schedules, cutoff);
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
        return new(po.Id, po.PurchaseOrderNumber, po.SupplierId, context.SupplierNumber, context.SupplierNameFa,
            po.CurrencyId, context.CurrencyCode, po.Lines.Sum(x => x.OrderedQuantity * x.UnitPrice), orderedQuantity,
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

    private static DateOnly? ResolveFinalReceipt(IReadOnlyCollection<DeliveryScheduleAtCutoff> schedules,
        DateTime cutoff)
    {
        var received = schedules.Sum(x => EffectiveReceipts(x.Source, cutoff).Sum(y => y.Quantity));
        var cancelled = schedules.Sum(x => x.Source.Cancellations.Where(y => y.CreatedAtUtc <= cutoff).Sum(y => y.Quantity));
        var allocated = schedules.Sum(x => x.AllocatedQuantity);
        if (allocated - received - cancelled > 0 || received == 0) return null;
        return schedules.SelectMany(x => EffectiveReceipts(x.Source, cutoff)).MaxBy(x => x.Receipt.EffectiveDate)?.Receipt.EffectiveDate;
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

    private const string LeadTimeContextSql = """
        SELECT so."Id" AS "SourcingOptionId", so."SourcingNumber", so."ItemId", so."SupplierId",
               item."Code" AS "ItemCode", item."NameFa" AS "ItemNameFa", item."NameEn" AS "ItemNameEn",
               supplier."Code" AS "SupplierNumber", supplier."NameFa" AS "SupplierNameFa",
               supplier."NameEn" AS "SupplierNameEn", source."Version",
               source."SupplierLeadTimeDays", source."ForwarderToShipmentDays",
               source."ShippingPeriodDays", source."DestinationCustomsDays",
               COUNT(*) OVER()::integer AS "TotalCount"
        FROM procurement.sourcing_options so
        JOIN LATERAL (
          SELECT h."Code",h."NameFa",h."NameEn" FROM analytics.master_context_history h
          WHERE h."EntityType"='ITEM' AND h."EntityId"=so."ItemId" AND h."EffectiveAtUtc"<=@cutoff
          ORDER BY h."EffectiveAtUtc" DESC,h."Id" DESC LIMIT 1
        ) item ON true
        JOIN LATERAL (
          SELECT h."Code",h."NameFa",h."NameEn" FROM analytics.master_context_history h
          WHERE h."EntityType"='SUPPLIER' AND h."EntityId"=so."SupplierId" AND h."EffectiveAtUtc"<=@cutoff
          ORDER BY h."EffectiveAtUtc" DESC,h."Id" DESC LIMIT 1
        ) supplier ON true
        JOIN LATERAL (
          SELECT h."Version",h."SupplierLeadTimeDays",h."ForwarderToShipmentDays",
                 h."ShippingPeriodDays",h."DestinationCustomsDays"
          FROM analytics.sourcing_option_context_history h
          WHERE h."SourcingOptionId"=so."Id" AND h."EffectiveAtUtc"<=@cutoff
          ORDER BY h."EffectiveAtUtc" DESC,h."Id" DESC LIMIT 1
        ) source ON true
        WHERE so."CreatedAtUtc"<=@cutoff AND (@search='' OR so."SourcingNumber" ILIKE @search
          OR item."Code" ILIKE @search OR item."NameFa" ILIKE @search OR COALESCE(item."NameEn",'') ILIKE @search
          OR supplier."Code" ILIKE @search OR supplier."NameFa" ILIKE @search)
        ORDER BY so."SourcingNumber" LIMIT @limit OFFSET @offset
        """;

    private const string CommercialContextSql = """
        SELECT po."Id" AS "PurchaseOrderId", supplier."Code" AS "SupplierNumber",
               supplier."NameFa" AS "SupplierNameFa", currency."Code" AS "CurrencyCode",
               COUNT(*) OVER()::integer AS "TotalCount"
        FROM procurement.purchase_orders po
        JOIN LATERAL (
          SELECT h."Code",h."NameFa" FROM analytics.master_context_history h
          WHERE h."EntityType"='SUPPLIER' AND h."EntityId"=po."SupplierId" AND h."EffectiveAtUtc"<=@cutoff
          ORDER BY h."EffectiveAtUtc" DESC,h."Id" DESC LIMIT 1
        ) supplier ON true
        JOIN LATERAL (
          SELECT h."Code" FROM analytics.master_context_history h
          WHERE h."EntityType"='CURRENCY' AND h."EntityId"=po."CurrencyId" AND h."EffectiveAtUtc"<=@cutoff
          ORDER BY h."EffectiveAtUtc" DESC,h."Id" DESC LIMIT 1
        ) currency ON true
        WHERE po."CreatedAtUtc"<=@cutoff AND EXISTS (
          SELECT 1 FROM procurement.purchase_order_lifecycle_events e
          WHERE e."PurchaseOrderId"=po."Id" AND e."CreatedAtUtc"<=@cutoff
            AND e."ToStatus" IN ('ORDERED','SHIPPED','CUSTOMS','PARTIALLY_RECEIVED','RECEIVED','CANCELLED'))
          AND (@search='' OR po."PurchaseOrderNumber" ILIKE @search OR supplier."Code" ILIKE @search
               OR supplier."NameFa" ILIKE @search)
        ORDER BY po."PurchaseOrderNumber" LIMIT @limit OFFSET @offset
        """;

    private const string CommercialTotalsSql = """
        SELECT po."CurrencyId", currency."Code" AS "CurrencyCode",
               SUM(line."OrderedQuantity"*line."UnitPrice") AS "OriginalAmount"
        FROM procurement.purchase_orders po
        JOIN procurement.purchase_order_lines line ON line."PurchaseOrderId"=po."Id"
        JOIN LATERAL (
          SELECT h."Code",h."NameFa" FROM analytics.master_context_history h
          WHERE h."EntityType"='SUPPLIER' AND h."EntityId"=po."SupplierId" AND h."EffectiveAtUtc"<=@cutoff
          ORDER BY h."EffectiveAtUtc" DESC,h."Id" DESC LIMIT 1
        ) supplier ON true
        JOIN LATERAL (
          SELECT h."Code" FROM analytics.master_context_history h
          WHERE h."EntityType"='CURRENCY' AND h."EntityId"=po."CurrencyId" AND h."EffectiveAtUtc"<=@cutoff
          ORDER BY h."EffectiveAtUtc" DESC,h."Id" DESC LIMIT 1
        ) currency ON true
        WHERE po."CreatedAtUtc"<=@cutoff AND EXISTS (
          SELECT 1 FROM procurement.purchase_order_lifecycle_events e
          WHERE e."PurchaseOrderId"=po."Id" AND e."CreatedAtUtc"<=@cutoff
            AND e."ToStatus" IN ('ORDERED','SHIPPED','CUSTOMS','PARTIALLY_RECEIVED','RECEIVED','CANCELLED'))
          AND (@search='' OR po."PurchaseOrderNumber" ILIKE @search OR supplier."Code" ILIKE @search
               OR supplier."NameFa" ILIKE @search)
        GROUP BY po."CurrencyId",currency."Code"
        """;

    private const string InventoryPositionSql = """
        WITH eligible AS (
          SELECT e.* FROM inventory.ledger_effects e
          JOIN inventory.transactions t ON t."Id"=e."TransactionId"
          WHERE e."EffectiveDate"<=@asOfDate AND t."CreatedAtUtc"<=@cutoff
        ), balances AS (
          SELECT "ItemId","WarehouseId","UnitId",SUM("QuantityEffect") AS "OnHand"
          FROM eligible GROUP BY "ItemId","WarehouseId","UnitId"
        ), company AS (
          SELECT "ItemId","UnitId",SUM("QuantityEffect") AS "CompanyOnHand"
          FROM eligible GROUP BY "ItemId","UnitId"
        )
        SELECT b."ItemId",item."Code" AS "ItemCode",item."NameFa" AS "ItemNameFa",item."NameEn" AS "ItemNameEn",
               b."WarehouseId",warehouse."Code" AS "WarehouseCode",warehouse."NameFa" AS "WarehouseNameFa",
               warehouse."NameEn" AS "WarehouseNameEn",warehouse."IsActive" AS "WarehouseIsActive",
               b."UnitId",unit."Code" AS "UnitCode",unit."NameFa" AS "UnitNameFa",unit."NameEn" AS "UnitNameEn",
               b."OnHand",company."CompanyOnHand",COUNT(*) OVER()::integer AS "TotalCount"
        FROM balances b JOIN company ON company."ItemId"=b."ItemId" AND company."UnitId"=b."UnitId"
        JOIN LATERAL (SELECT h."Code",h."NameFa",h."NameEn" FROM analytics.master_context_history h
          WHERE h."EntityType"='ITEM' AND h."EntityId"=b."ItemId" AND h."EffectiveAtUtc"<=@cutoff
          ORDER BY h."EffectiveAtUtc" DESC,h."Id" DESC LIMIT 1) item ON true
        JOIN LATERAL (SELECT h."Code",h."NameFa",h."NameEn",h."IsActive" FROM analytics.master_context_history h
          WHERE h."EntityType"='WAREHOUSE' AND h."EntityId"=b."WarehouseId" AND h."EffectiveAtUtc"<=@cutoff
          ORDER BY h."EffectiveAtUtc" DESC,h."Id" DESC LIMIT 1) warehouse ON true
        JOIN LATERAL (SELECT h."Code",h."NameFa",h."NameEn" FROM analytics.master_context_history h
          WHERE h."EntityType"='UNIT' AND h."EntityId"=b."UnitId" AND h."EffectiveAtUtc"<=@cutoff
          ORDER BY h."EffectiveAtUtc" DESC,h."Id" DESC LIMIT 1) unit ON true
        WHERE b."OnHand"<>0 AND (@search='' OR item."Code" ILIKE @search OR item."NameFa" ILIKE @search
          OR COALESCE(item."NameEn",'') ILIKE @search OR warehouse."Code" ILIKE @search
          OR warehouse."NameFa" ILIKE @search OR COALESCE(warehouse."NameEn",'') ILIKE @search)
        ORDER BY item."Code",warehouse."Code" LIMIT @limit OFFSET @offset
        """;

    private const string InventoryMovementSql = """
        SELECT t."Id" AS "TransactionId",t."TransactionType",e."EffectiveDate",e."ItemId",
               item."Code" AS "ItemCode",e."WarehouseId",warehouse."Code" AS "WarehouseCode",
               e."QuantityEffect" AS "Quantity",t."SourceDocumentType",t."SourceDocumentId",t."CreatedAtUtc",
               COUNT(*) OVER()::integer AS "TotalCount"
        FROM inventory.ledger_effects e JOIN inventory.transactions t ON t."Id"=e."TransactionId"
        JOIN LATERAL (SELECT h."Code" FROM analytics.master_context_history h
          WHERE h."EntityType"='ITEM' AND h."EntityId"=e."ItemId" AND h."EffectiveAtUtc"<=@cutoff
          ORDER BY h."EffectiveAtUtc" DESC,h."Id" DESC LIMIT 1) item ON true
        JOIN LATERAL (SELECT h."Code" FROM analytics.master_context_history h
          WHERE h."EntityType"='WAREHOUSE' AND h."EntityId"=e."WarehouseId" AND h."EffectiveAtUtc"<=@cutoff
          ORDER BY h."EffectiveAtUtc" DESC,h."Id" DESC LIMIT 1) warehouse ON true
        WHERE e."EffectiveDate">=@fromDate AND e."EffectiveDate"<=@toDate AND t."CreatedAtUtc"<=@cutoff
          AND (@search='' OR item."Code" ILIKE @search OR warehouse."Code" ILIKE @search
               OR t."TransactionType" ILIKE @search)
        ORDER BY e."EffectiveDate" DESC,t."CreatedAtUtc",t."Id"
        LIMIT @limit OFFSET @offset
        """;

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
            SELECT scope."ProductId", item."Code" AS "ProductCode",
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
            JOIN LATERAL (
                SELECT history."Code",history."NameFa",history."NameEn"
                FROM analytics.master_context_history history
                WHERE history."EntityType"='ITEM' AND history."EntityId"=scope."ProductId"
                    AND history."EffectiveAtUtc"<=@cutoff
                ORDER BY history."EffectiveAtUtc" DESC,history."Id" DESC LIMIT 1
            ) item ON true
            LEFT JOIN planning.forecast_series series
                ON series."ProductId" = scope."ProductId"
                AND series."BusinessPeriodId" = scope."BusinessPeriodId"
                AND series."CreatedAtUtc" <= @cutoff
            WHERE @search = '' OR item."Code" ILIKE @search
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

    private sealed class LeadTimeContextDbRow
    {
        public Guid SourcingOptionId { get; init; }
        public string SourcingNumber { get; init; } = string.Empty;
        public Guid ItemId { get; init; }
        public string ItemCode { get; init; } = string.Empty;
        public string ItemNameFa { get; init; } = string.Empty;
        public string? ItemNameEn { get; init; }
        public Guid SupplierId { get; init; }
        public string SupplierNumber { get; init; } = string.Empty;
        public string SupplierNameFa { get; init; } = string.Empty;
        public string? SupplierNameEn { get; init; }
        public long Version { get; init; }
        public int? SupplierLeadTimeDays { get; init; }
        public int? ForwarderToShipmentDays { get; init; }
        public int? ShippingPeriodDays { get; init; }
        public int? DestinationCustomsDays { get; init; }
        public int TotalCount { get; init; }
    }

    private sealed class CommercialContextDbRow
    {
        public Guid PurchaseOrderId { get; init; }
        public string SupplierNumber { get; init; } = string.Empty;
        public string SupplierNameFa { get; init; } = string.Empty;
        public string CurrencyCode { get; init; } = string.Empty;
        public int TotalCount { get; init; }
    }

    private sealed class CurrencyCommitmentDbRow
    {
        public Guid CurrencyId { get; init; }
        public string CurrencyCode { get; init; } = string.Empty;
        public decimal OriginalAmount { get; init; }
    }

    private sealed class InventoryPositionDbRow
    {
        public Guid ItemId { get; init; }
        public string ItemCode { get; init; } = string.Empty;
        public string ItemNameFa { get; init; } = string.Empty;
        public string? ItemNameEn { get; init; }
        public Guid WarehouseId { get; init; }
        public string WarehouseCode { get; init; } = string.Empty;
        public string WarehouseNameFa { get; init; } = string.Empty;
        public string? WarehouseNameEn { get; init; }
        public bool WarehouseIsActive { get; init; }
        public Guid UnitId { get; init; }
        public string UnitCode { get; init; } = string.Empty;
        public string UnitNameFa { get; init; } = string.Empty;
        public string? UnitNameEn { get; init; }
        public decimal OnHand { get; init; }
        public decimal CompanyOnHand { get; init; }
        public int TotalCount { get; init; }
    }

    private sealed class InventoryMovementDbRow
    {
        public Guid TransactionId { get; init; }
        public string TransactionType { get; init; } = string.Empty;
        public DateOnly EffectiveDate { get; init; }
        public Guid ItemId { get; init; }
        public string ItemCode { get; init; } = string.Empty;
        public Guid WarehouseId { get; init; }
        public string WarehouseCode { get; init; } = string.Empty;
        public decimal Quantity { get; init; }
        public string? SourceDocumentType { get; init; }
        public Guid? SourceDocumentId { get; init; }
        public DateTime CreatedAtUtc { get; init; }
        public int TotalCount { get; init; }
    }

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
