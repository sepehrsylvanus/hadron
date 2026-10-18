using System.Data;
using Hadron.Erp.Api.Infrastructure;
using Hadron.Erp.Api.Modules.CashFlow.Domain;
using Hadron.Erp.Api.Modules.PurchaseSupply.Domain;
using Microsoft.EntityFrameworkCore;

namespace Hadron.Erp.Api.Modules.CashFlow.Application;

public sealed class PurchasePriceHistoryService(
    IDbContextFactory<HadronDbContext> dbContextFactory,
    TimeProvider timeProvider)
{
    public const int MaximumPageSize = 100;
    private const string ReportingCurrencyCode = "USD";

    public async Task<PurchasePriceHistoryItemFilterOptionPage> GetItemFilterOptions(
        string? search, int page, int pageSize, CancellationToken cancellationToken)
    {
        var offset = ValidateLookupPaging(page, pageSize);
        var term = Clean(search);
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var query = db.PurchaseOrderLines.AsNoTracking()
            .Where(x => x.PurchaseOrder.OrderedDate.HasValue);
        if (term is not null)
            query = query.Where(x =>
                EF.Functions.ILike(x.Item.BusinessNumber, term, "\\") ||
                EF.Functions.ILike(x.Item.NameFa, term, "\\") ||
                (x.Item.NameEn != null && EF.Functions.ILike(x.Item.NameEn, term, "\\")));
        var options = query.Select(x => new
            {
                x.ItemId,
                ItemCode = x.Item.BusinessNumber,
                x.Item.NameFa,
                x.Item.NameEn
            }).Distinct();
        var total = await options.CountAsync(cancellationToken);
        var optionRows = await options.OrderBy(x => x.ItemCode).ThenBy(x => x.ItemId)
            .Skip(offset).Take(pageSize).ToArrayAsync(cancellationToken);
        var rows = optionRows.Select(x => new PurchasePriceHistoryItemFilterOption(
            x.ItemId, x.ItemCode, x.NameFa, x.NameEn)).ToArray();
        return new(rows, page, pageSize, total);
    }

    public async Task<PurchasePriceHistorySupplierFilterOptionPage> GetSupplierFilterOptions(
        string? search, int page, int pageSize, CancellationToken cancellationToken)
    {
        var offset = ValidateLookupPaging(page, pageSize);
        var term = Clean(search);
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var query = db.PurchaseOrderLines.AsNoTracking()
            .Where(x => x.PurchaseOrder.OrderedDate.HasValue);
        if (term is not null)
            query = query.Where(x =>
                EF.Functions.ILike(x.PurchaseOrder.Supplier.SupplierNumber, term, "\\") ||
                EF.Functions.ILike(x.PurchaseOrder.Supplier.Name, term, "\\"));
        var options = query.Select(x => new
            {
                SupplierId = x.PurchaseOrder.SupplierId,
                x.PurchaseOrder.Supplier.SupplierNumber,
                SupplierName = x.PurchaseOrder.Supplier.Name
            }).Distinct();
        var total = await options.CountAsync(cancellationToken);
        var optionRows = await options.OrderBy(x => x.SupplierNumber).ThenBy(x => x.SupplierId)
            .Skip(offset).Take(pageSize).ToArrayAsync(cancellationToken);
        var rows = optionRows.Select(x => new PurchasePriceHistorySupplierFilterOption(
            x.SupplierId, x.SupplierNumber, x.SupplierName)).ToArray();
        return new(rows, page, pageSize, total);
    }

    public async Task<PurchasePriceHistoryFilterValues> GetFilterValues(
        CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var eligible = db.PurchaseOrderLines.AsNoTracking()
            .Where(x => x.PurchaseOrder.OrderedDate.HasValue);
        var currencies = await eligible.Select(x => x.PurchaseOrder.Currency.Code)
            .Distinct().OrderBy(x => x).ToArrayAsync(cancellationToken);
        var statuses = await eligible.Select(x => x.PurchaseOrder.Status)
            .Distinct().OrderBy(x => x).ToArrayAsync(cancellationToken);
        return new(currencies, statuses);
    }

    public async Task<PurchasePriceHistoryPage> Get(
        string? search,
        Guid? itemId,
        Guid? supplierId,
        string? currencyCode,
        string? purchaseOrderStatus,
        DateOnly? fromOrderDate,
        DateOnly? toOrderDate,
        Guid? valuationFxRateSetId,
        DateOnly? valuationAsOfDate,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var offset = ValidateAndGetOffset(page, pageSize, fromOrderDate, toOrderDate,
            itemId, supplierId, valuationFxRateSetId, valuationAsOfDate);
        var term = Clean(search);
        var normalizedCurrencyCode = NormalizeCurrencyCode(currencyCode);
        var normalizedStatus = NormalizeStatus(purchaseOrderStatus);

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(
            IsolationLevel.RepeatableRead, cancellationToken);

        var filtered = db.PurchaseOrderLines.AsNoTracking()
            .Where(line => line.PurchaseOrder.OrderedDate.HasValue);

        if (itemId.HasValue)
            filtered = filtered.Where(line => line.ItemId == itemId.Value);
        if (supplierId.HasValue)
            filtered = filtered.Where(line => line.PurchaseOrder.SupplierId == supplierId.Value);
        if (normalizedCurrencyCode is not null)
            filtered = filtered.Where(line =>
                line.PurchaseOrder.Currency.NormalizedCode == normalizedCurrencyCode);
        if (normalizedStatus is not null)
            filtered = filtered.Where(line => line.PurchaseOrder.Status == normalizedStatus);
        if (fromOrderDate.HasValue)
            filtered = filtered.Where(line =>
                line.PurchaseOrder.OrderedDate >= fromOrderDate.Value);
        if (toOrderDate.HasValue)
            filtered = filtered.Where(line =>
                line.PurchaseOrder.OrderedDate <= toOrderDate.Value);
        if (term is not null)
        {
            filtered = filtered.Where(line =>
                EF.Functions.ILike(line.PurchaseOrder.PurchaseOrderNumber, term, "\\") ||
                EF.Functions.ILike(line.PurchaseOrder.Supplier.SupplierNumber, term, "\\") ||
                EF.Functions.ILike(line.PurchaseOrder.Supplier.Name, term, "\\") ||
                EF.Functions.ILike(line.Item.BusinessNumber, term, "\\") ||
                EF.Functions.ILike(line.Item.NameFa, term, "\\") ||
                (line.Item.NameEn != null && EF.Functions.ILike(line.Item.NameEn, term, "\\")));
        }

        var totalCount = await filtered.CountAsync(cancellationToken);
        IReadOnlyCollection<CurrencyReference> filteredCurrencies = [];
        if (valuationFxRateSetId.HasValue || valuationAsOfDate.HasValue)
        {
            var currencyRows = await filtered
                .Select(line => new
                {
                    Id = line.PurchaseOrder.CurrencyId,
                    line.PurchaseOrder.Currency.Code
                })
                .Distinct()
                .OrderBy(currency => currency.Code)
                .ToArrayAsync(cancellationToken);
            filteredCurrencies = currencyRows
                .Select(currency => new CurrencyReference(currency.Id, currency.Code))
                .ToArray();
        }

        var rows = await (
                from line in filtered
                orderby line.PurchaseOrder.OrderedDate descending,
                    line.PurchaseOrder.PurchaseOrderNumber descending,
                    line.LineNumber,
                    line.Id
                select new PurchasePriceHistoryProjection
                {
                    PurchaseOrderLineId = line.Id,
                    PurchaseOrderId = line.PurchaseOrderId,
                    PurchaseOrderNumber = line.PurchaseOrder.PurchaseOrderNumber,
                    PurchaseOrderStatus = line.PurchaseOrder.Status,
                    OrderedDate = line.PurchaseOrder.OrderedDate!.Value,
                    LineNumber = line.LineNumber,
                    ItemId = line.ItemId,
                    ItemCode = line.Item.BusinessNumber,
                    ItemNameFa = line.Item.NameFa,
                    ItemNameEn = line.Item.NameEn,
                    SupplierId = line.PurchaseOrder.SupplierId,
                    SupplierNumber = line.PurchaseOrder.Supplier.SupplierNumber,
                    SupplierName = line.PurchaseOrder.Supplier.Name,
                    CurrencyId = line.PurchaseOrder.CurrencyId,
                    CurrencyCode = line.PurchaseOrder.Currency.Code,
                    CurrencyDecimalPrecision = line.PurchaseOrder.Currency.DecimalPrecision,
                    UnitId = line.ItemBaseUnitId,
                    UnitCode = line.ItemBaseUnit.Code,
                    UnitNameFa = line.ItemBaseUnit.NameFa,
                    UnitNameEn = line.ItemBaseUnit.NameEn,
                    UnitSymbol = line.ItemBaseUnit.Symbol,
                    OrderedQuantity = line.OrderedQuantity,
                    UnitPrice = line.UnitPrice,
                    ReceiptCount = 0,
                    PostedReceiptQuantity = 0m,
                    FirstPostedReceiptDate = null,
                    LastPostedReceiptDate = null
                })
            .Skip(offset)
            .Take(pageSize)
            .ToArrayAsync(cancellationToken);

        // Receipt facts are aggregated only for the bounded page (maximum 100 lines).
        // Keeping this separate avoids provider-specific null materialization from a
        // typed aggregate LEFT JOIN while preserving exactly one response row per PO line.
        var pageLineIds = rows.Select(row => row.PurchaseOrderLineId).ToArray();
        if (pageLineIds.Length > 0)
        {
            var receiptRows = await db.PurchaseReceiptLines.AsNoTracking()
                .Where(line => pageLineIds.Contains(line.PurchaseOrderLineId) &&
                    line.Receipt.Status == PurchaseReceiptStatuses.Posted)
                .GroupBy(line => line.PurchaseOrderLineId)
                .Select(receipts => new ReceiptSummaryProjection
                {
                    PurchaseOrderLineId = receipts.Key,
                    ReceiptCount = receipts.Select(line => line.ReceiptId).Distinct().Count(),
                    Quantity = receipts.Sum(line => line.Quantity),
                    FirstDate = receipts.Min(line => line.Receipt.EffectiveDate),
                    LastDate = receipts.Max(line => line.Receipt.EffectiveDate)
                })
                .ToDictionaryAsync(row => row.PurchaseOrderLineId, cancellationToken);
            foreach (var row in rows)
            {
                if (!receiptRows.TryGetValue(row.PurchaseOrderLineId, out var receipt))
                    continue;
                row.ReceiptCount = receipt.ReceiptCount;
                row.PostedReceiptQuantity = receipt.Quantity;
                row.FirstPostedReceiptDate = receipt.FirstDate;
                row.LastPostedReceiptDate = receipt.LastDate;
            }
        }

        var valuation = await LoadValuation(
            db, valuationFxRateSetId, valuationAsOfDate, filteredCurrencies,
            cancellationToken);
        var responseRows = rows.Select(row => ToResponse(row, valuation)).ToArray();
        await transaction.CommitAsync(cancellationToken);

        return new PurchasePriceHistoryPage(
            responseRows,
            page,
            pageSize,
            totalCount,
            timeProvider.GetUtcNow().UtcDateTime,
            PurchasePriceHistorySemantics.CurrentOrderedPurchaseFacts,
            valuation.Context);
    }

    private static async Task<ValuationSelection> LoadValuation(
        HadronDbContext db,
        Guid? exactRateSetId,
        DateOnly? valuationAsOfDate,
        IReadOnlyCollection<CurrencyReference> currencies,
        CancellationToken cancellationToken)
    {
        if (!exactRateSetId.HasValue && !valuationAsOfDate.HasValue)
        {
            return new ValuationSelection(
                new PurchasePriceHistoryUsdValuationContext(
                    false,
                    PurchasePriceHistorySemantics.UsdValuationPurpose,
                    PurchasePriceHistoryFxSelectionModes.None,
                    null,
                    null,
                    null,
                    null,
                    PurchasePriceHistoryFxValuationStatuses.NotRequested,
                    []),
                null,
                new Dictionary<Guid, decimal>());
        }

        var mode = exactRateSetId.HasValue
            ? PurchasePriceHistoryFxSelectionModes.ExactRateSet
            : PurchasePriceHistoryFxSelectionModes.AsOfLatestPublished;

        var headerQuery = db.ReportingFxRateSets.AsNoTracking()
            .Where(rateSet =>
                rateSet.Status == ReportingFxRateSetStatuses.Published &&
                rateSet.BaseCurrency.NormalizedCode == ReportingCurrencyCode);

        if (exactRateSetId.HasValue)
            headerQuery = headerQuery.Where(rateSet => rateSet.Id == exactRateSetId.Value);
        else
            headerQuery = headerQuery.Where(rateSet =>
                rateSet.EffectiveDate <= valuationAsOfDate!.Value);

        var header = await headerQuery
            .OrderByDescending(rateSet => rateSet.EffectiveDate)
            .ThenByDescending(rateSet => rateSet.Revision)
            .ThenByDescending(rateSet => rateSet.PublishedAtUtc)
            .ThenByDescending(rateSet => rateSet.Id)
            .Select(rateSet => new ValuationHeader(
                rateSet.Id,
                rateSet.BaseCurrencyId,
                rateSet.EffectiveDate,
                rateSet.Revision))
            .FirstOrDefaultAsync(cancellationToken);

        if (exactRateSetId.HasValue && header is null)
        {
            throw Validation("valuationFxRateSetId",
                "مجموعه نرخ انتخاب‌شده وجود ندارد، منتشر نشده است یا مبنای آن USD نیست.");
        }

        if (header is null)
        {
            var missingSetCurrencies = currencies
                .Select(currency => currency.Code)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(code => code, StringComparer.Ordinal)
                .ToArray();
            return new ValuationSelection(
                new PurchasePriceHistoryUsdValuationContext(
                    true,
                    PurchasePriceHistorySemantics.UsdValuationPurpose,
                    mode,
                    valuationAsOfDate,
                    null,
                    null,
                    null,
                    PurchasePriceHistoryFxValuationStatuses.MissingSet,
                    missingSetCurrencies),
                null,
                new Dictionary<Guid, decimal>());
        }

        var rates = await db.ReportingFxRates.AsNoTracking()
            .Where(rate => rate.RateSetId == header.Id)
            .ToDictionaryAsync(rate => rate.CurrencyId, rate => rate.UnitsPerUsd,
                cancellationToken);
        var missing = currencies
            .Where(currency => currency.Id != header.BaseCurrencyId &&
                !rates.ContainsKey(currency.Id))
            .Select(currency => currency.Code)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(code => code, StringComparer.Ordinal)
            .ToArray();

        var status = missing.Length == 0
            ? PurchasePriceHistoryFxValuationStatuses.Complete
            : PurchasePriceHistoryFxValuationStatuses.Incomplete;
        return new ValuationSelection(
            new PurchasePriceHistoryUsdValuationContext(
                true,
                PurchasePriceHistorySemantics.UsdValuationPurpose,
                mode,
                valuationAsOfDate,
                header.Id,
                header.EffectiveDate,
                header.Revision,
                status,
                missing),
            header.BaseCurrencyId,
            rates);
    }

    private static PurchasePriceHistoryRow ToResponse(
        PurchasePriceHistoryProjection row,
        ValuationSelection valuation)
    {
        decimal? unitsPerUsd = null;
        decimal? unitPriceUsd = null;
        decimal? orderAmountUsd = null;
        decimal? receiptAmountUsd = null;
        var originalOrderAmount = Multiply(row.OrderedQuantity, row.UnitPrice,
            row.CurrencyCode);

        if (valuation.Context.Requested && valuation.Context.RateSetId.HasValue)
        {
            if (row.CurrencyId == valuation.BaseCurrencyId)
            {
                unitsPerUsd = 1m;
            }
            else if (valuation.Rates.TryGetValue(row.CurrencyId, out var configuredRate))
            {
                unitsPerUsd = configuredRate;
            }

            if (unitsPerUsd.HasValue)
            {
                unitPriceUsd = Divide(row.UnitPrice, unitsPerUsd.Value, row.CurrencyCode);
                orderAmountUsd = Divide(originalOrderAmount, unitsPerUsd.Value,
                    row.CurrencyCode);
                receiptAmountUsd = Divide(
                    Multiply(row.PostedReceiptQuantity, row.UnitPrice, row.CurrencyCode),
                    unitsPerUsd.Value,
                    row.CurrencyCode);
            }
        }

        return new PurchasePriceHistoryRow(
            row.PurchaseOrderLineId,
            row.PurchaseOrderId,
            row.PurchaseOrderNumber,
            row.PurchaseOrderStatus,
            row.OrderedDate,
            row.LineNumber,
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
            row.UnitId,
            row.UnitCode,
            row.UnitNameFa,
            row.UnitNameEn,
            row.UnitSymbol,
            row.OrderedQuantity,
            row.UnitPrice,
            originalOrderAmount,
            new PurchasePriceHistoryReceiptSummary(
                row.ReceiptCount,
                row.PostedReceiptQuantity,
                row.UnitId,
                row.UnitCode,
                row.FirstPostedReceiptDate,
                row.LastPostedReceiptDate),
            unitsPerUsd,
            unitPriceUsd,
            orderAmountUsd,
            receiptAmountUsd);
    }

    private static int ValidateAndGetOffset(
        int page,
        int pageSize,
        DateOnly? fromOrderDate,
        DateOnly? toOrderDate,
        Guid? itemId,
        Guid? supplierId,
        Guid? valuationFxRateSetId,
        DateOnly? valuationAsOfDate)
    {
        var errors = new Dictionary<string, string[]>();
        if (page < 1 || pageSize is < 1 or > MaximumPageSize)
            errors["paging"] = [$"pageSize must be between 1 and {MaximumPageSize}."];
        if (fromOrderDate.HasValue && toOrderDate.HasValue &&
            fromOrderDate > toOrderDate)
            errors["dateRange"] =
                ["تاریخ شروع سفارش نباید بعد از تاریخ پایان سفارش باشد."];
        if (itemId == Guid.Empty)
            errors["itemId"] = ["شناسه قلم معتبر نیست."];
        if (supplierId == Guid.Empty)
            errors["supplierId"] = ["شناسه تأمین‌کننده معتبر نیست."];
        if (valuationFxRateSetId == Guid.Empty)
            errors["valuationFxRateSetId"] = ["شناسه مجموعه نرخ معتبر نیست."];
        if (valuationFxRateSetId.HasValue && valuationAsOfDate.HasValue)
            errors["valuationSelection"] =
                ["برای ارزش‌گذاری USD فقط یکی از مجموعه نرخ یا تاریخ مبنا را انتخاب کنید."];

        var offset = page > 0 && pageSize > 0
            ? ((long)page - 1) * pageSize
            : 0;
        if (offset > int.MaxValue)
            errors["paging"] = ["شماره صفحه از محدوده مجاز خارج است."];
        if (errors.Count > 0) throw new CashFlowValidationException(errors);
        return (int)offset;
    }

    private static int ValidateLookupPaging(int page, int pageSize)
    {
        if (page < 1 || pageSize is < 1 or > MaximumPageSize ||
            ((long)page - 1) * pageSize > int.MaxValue)
            throw Validation("paging", $"pageSize must be between 1 and {MaximumPageSize}.");
        return checked((page - 1) * pageSize);
    }

    private static string? Clean(string? search)
    {
        if (string.IsNullOrWhiteSpace(search)) return null;
        var value = search.Trim();
        if (value.Length > 200)
            throw Validation("search", "عبارت جست‌وجو حداکثر ۲۰۰ نویسه است.");
        // Escape wildcard characters so search remains a literal contains filter.
        value = value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);
        return $"%{value}%";
    }

    private static string? NormalizeCurrencyCode(string? currencyCode)
    {
        if (string.IsNullOrWhiteSpace(currencyCode)) return null;
        var normalized = currencyCode.Trim().ToUpperInvariant();
        if (normalized.Length != 3 || normalized.Any(character =>
                character is < 'A' or > 'Z'))
            throw Validation("currencyCode", "کد ارز باید سه حرف لاتین معتبر باشد.");
        return normalized;
    }

    private static string? NormalizeStatus(string? status)
    {
        if (string.IsNullOrWhiteSpace(status)) return null;
        var normalized = status.Trim().ToUpperInvariant();
        if (normalized is not (PurchaseOrderStatuses.Ordered or
            PurchaseOrderStatuses.Shipped or
            PurchaseOrderStatuses.Customs or
            PurchaseOrderStatuses.PartiallyReceived or
            PurchaseOrderStatuses.Received or
            PurchaseOrderStatuses.Cancelled))
            throw Validation("purchaseOrderStatus",
                "وضعیت سفارش باید یکی از وضعیت‌های پس از ثبت سفارش باشد.");
        return normalized;
    }

    private static decimal Multiply(decimal left, decimal right, string currencyCode)
    {
        try
        {
            return left * right;
        }
        catch (OverflowException)
        {
            throw ValuationOutOfRange(currencyCode);
        }
    }

    private static decimal Divide(decimal amount, decimal unitsPerUsd, string currencyCode)
    {
        try
        {
            return amount / unitsPerUsd;
        }
        catch (OverflowException)
        {
            throw ValuationOutOfRange(currencyCode);
        }
    }

    private static CashFlowConflictException ValuationOutOfRange(string currencyCode) => new(
        "FX_CONVERSION_OUT_OF_RANGE",
        $"تبدیل ارزش‌گذاری ارز {currencyCode} از محدوده عددی پشتیبانی‌شده خارج است.");

    private static CashFlowValidationException Validation(string key, string message) =>
        new(new Dictionary<string, string[]> { [key] = [message] });

    private sealed record CurrencyReference(Guid Id, string Code);
    private sealed record ValuationHeader(
        Guid Id,
        Guid BaseCurrencyId,
        DateOnly EffectiveDate,
        int Revision);
    private sealed record ValuationSelection(
        PurchasePriceHistoryUsdValuationContext Context,
        Guid? BaseCurrencyId,
        IReadOnlyDictionary<Guid, decimal> Rates);

    private sealed class ReceiptSummaryProjection
    {
        public Guid PurchaseOrderLineId { get; init; }
        public int ReceiptCount { get; init; }
        public decimal Quantity { get; init; }
        public DateOnly FirstDate { get; init; }
        public DateOnly LastDate { get; init; }
    }

    private sealed class PurchasePriceHistoryProjection
    {
        public Guid PurchaseOrderLineId { get; init; }
        public Guid PurchaseOrderId { get; init; }
        public string PurchaseOrderNumber { get; init; } = string.Empty;
        public string PurchaseOrderStatus { get; init; } = string.Empty;
        public DateOnly OrderedDate { get; init; }
        public int LineNumber { get; init; }
        public Guid ItemId { get; init; }
        public string ItemCode { get; init; } = string.Empty;
        public string ItemNameFa { get; init; } = string.Empty;
        public string? ItemNameEn { get; init; }
        public Guid SupplierId { get; init; }
        public string SupplierNumber { get; init; } = string.Empty;
        public string SupplierName { get; init; } = string.Empty;
        public Guid CurrencyId { get; init; }
        public string CurrencyCode { get; init; } = string.Empty;
        public int CurrencyDecimalPrecision { get; init; }
        public Guid UnitId { get; init; }
        public string UnitCode { get; init; } = string.Empty;
        public string UnitNameFa { get; init; } = string.Empty;
        public string UnitNameEn { get; init; } = string.Empty;
        public string? UnitSymbol { get; init; }
        public decimal OrderedQuantity { get; init; }
        public decimal UnitPrice { get; init; }
        public int ReceiptCount { get; set; }
        public decimal PostedReceiptQuantity { get; set; }
        public DateOnly? FirstPostedReceiptDate { get; set; }
        public DateOnly? LastPostedReceiptDate { get; set; }
    }
}
