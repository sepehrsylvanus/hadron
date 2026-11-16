using System.Text.Json.Serialization;

namespace Hadron.Erp.Api.Modules.CashFlow.Application;

public static class PurchasePriceHistoryFxSelectionModes
{
    public const string None = "NONE";
    public const string ExactRateSet = "EXACT_RATE_SET";
    public const string AsOfLatestPublished = "AS_OF_LATEST_PUBLISHED";
}

public static class PurchasePriceHistoryFxValuationStatuses
{
    public const string NotRequested = "NOT_REQUESTED";
    public const string Complete = "COMPLETE";
    public const string Incomplete = "INCOMPLETE";
    public const string MissingSet = "MISSING_SET";
}

public static class PurchasePriceHistorySemantics
{
    public const string CurrentOrderedPurchaseFacts =
        "CURRENT_ORDERED_PURCHASE_FACTS_NOT_BITEMPORAL";

    public const string UsdValuationPurpose =
        "REPORTING_VALUATION_NOT_TRANSACTION_FX";
}

public sealed record PurchasePriceHistoryReceiptSummary(
    int PostedReceiptCount,
    [property: JsonConverter(typeof(InvariantDecimalStringJsonConverter))]
    decimal PostedQuantity,
    Guid QuantityUnitId,
    string QuantityUnitCode,
    DateOnly? FirstPostedReceiptDate,
    DateOnly? LastPostedReceiptDate);

public sealed record PurchasePriceHistoryRow(
    Guid PurchaseOrderLineId,
    Guid PurchaseOrderId,
    string PurchaseOrderNumber,
    string PurchaseOrderStatus,
    DateOnly OrderedDate,
    int LineNumber,
    Guid ItemId,
    string ItemCode,
    string ItemNameFa,
    string? ItemNameEn,
    Guid SupplierId,
    string SupplierNumber,
    string SupplierName,
    Guid OriginalCurrencyId,
    string OriginalCurrencyCode,
    int OriginalCurrencyDecimalPrecision,
    Guid PriceBaseUnitId,
    string PriceBaseUnitCode,
    string PriceBaseUnitNameFa,
    string PriceBaseUnitNameEn,
    string? PriceBaseUnitSymbol,
    [property: JsonConverter(typeof(InvariantDecimalStringJsonConverter))]
    decimal OrderedQuantity,
    [property: JsonConverter(typeof(InvariantDecimalStringJsonConverter))]
    decimal OriginalUnitPrice,
    [property: JsonConverter(typeof(InvariantDecimalStringJsonConverter))]
    decimal OriginalOrderAmount,
    PurchasePriceHistoryReceiptSummary Receipts,
    [property: JsonConverter(typeof(NullableInvariantDecimalStringJsonConverter))]
    decimal? ValuationUnitsPerUsd,
    [property: JsonConverter(typeof(NullableInvariantDecimalStringJsonConverter))]
    decimal? ValuationUnitPriceUsd,
    [property: JsonConverter(typeof(NullableInvariantDecimalStringJsonConverter))]
    decimal? ValuationOrderAmountUsd,
    [property: JsonConverter(typeof(NullableInvariantDecimalStringJsonConverter))]
    decimal? ValuationPostedReceiptAmountUsd);

public sealed record PurchasePriceHistoryUsdValuationContext(
    bool Requested,
    string Purpose,
    string SelectionMode,
    DateOnly? ValuationAsOfDate,
    Guid? RateSetId,
    DateOnly? RateSetEffectiveDate,
    int? RateSetRevision,
    string Status,
    IReadOnlyList<string> MissingCurrencyCodes);

public sealed record PurchasePriceHistoryPage(
    IReadOnlyList<PurchasePriceHistoryRow> Items,
    int Page,
    int PageSize,
    int TotalCount,
    DateTime GeneratedAtUtc,
    string DataSemantics,
    PurchasePriceHistoryUsdValuationContext UsdValuationContext);

public sealed record PurchasePriceHistoryItemFilterOption(
    Guid ItemId,
    string ItemCode,
    string ItemNameFa,
    string? ItemNameEn);

public sealed record PurchasePriceHistoryItemFilterOptionPage(
    IReadOnlyList<PurchasePriceHistoryItemFilterOption> Items,
    int Page,
    int PageSize,
    int TotalCount);

public sealed record PurchasePriceHistorySupplierFilterOption(
    Guid SupplierId,
    string SupplierNumber,
    string SupplierName);

public sealed record PurchasePriceHistorySupplierFilterOptionPage(
    IReadOnlyList<PurchasePriceHistorySupplierFilterOption> Items,
    int Page,
    int PageSize,
    int TotalCount);

public sealed record PurchasePriceHistoryFilterValues(
    IReadOnlyList<string> CurrencyCodes,
    IReadOnlyList<string> PurchaseOrderStatuses);
