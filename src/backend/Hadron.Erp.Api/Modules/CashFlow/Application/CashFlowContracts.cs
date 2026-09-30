using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Hadron.Erp.Api.Modules.CashFlow.Application;

public sealed class InvariantDecimalStringJsonConverter : JsonConverter<decimal>
{
    public override decimal Read(ref Utf8JsonReader reader, Type typeToConvert,
        JsonSerializerOptions options) => reader.TokenType switch
    {
        JsonTokenType.String when decimal.TryParse(reader.GetString(), NumberStyles.Number,
            CultureInfo.InvariantCulture, out var value) => value,
        JsonTokenType.Number => reader.GetDecimal(),
        _ => throw new JsonException("Expected an invariant decimal string.")
    };

    public override void Write(Utf8JsonWriter writer, decimal value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToString(CultureInfo.InvariantCulture));
}

public sealed class NullableInvariantDecimalStringJsonConverter : JsonConverter<decimal?>
{
    public override decimal? Read(ref Utf8JsonReader reader, Type typeToConvert,
        JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null) return null;
        if (reader.TokenType == JsonTokenType.String && decimal.TryParse(reader.GetString(),
                NumberStyles.Number, CultureInfo.InvariantCulture, out var value)) return value;
        if (reader.TokenType == JsonTokenType.Number) return reader.GetDecimal();
        throw new JsonException("Expected a null or invariant decimal string.");
    }

    public override void Write(Utf8JsonWriter writer, decimal? value, JsonSerializerOptions options)
    {
        if (!value.HasValue) writer.WriteNullValue();
        else writer.WriteStringValue(value.Value.ToString(CultureInfo.InvariantCulture));
    }
}

public static class CashFlowObligationStages
{
    public const string Advance = "ADVANCE";
    public const string BeforeShipment = "BEFORE_SHIPMENT";
    public const string Credit = "CREDIT";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        Advance, BeforeShipment, Credit
    };
}

public static class CashFlowConfidenceLevels
{
    public const string Committed = "COMMITTED";
    public const string Planned = "PLANNED";
}

public static class CashFlowDateSources
{
    public const string Actual = "ACTUAL";
    public const string Revised = "REVISED";
    public const string Original = "ORIGINAL";
    public const string Ordered = "ORDERED";
    public const string Unresolved = "UNRESOLVED";
}

public static class CashFlowObligationStates
{
    public const string Overdue = "OVERDUE";
    public const string DueSoon = "DUE_SOON";
    public const string Scheduled = "SCHEDULED";
    public const string Unresolved = "UNRESOLVED";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        Overdue, DueSoon, Scheduled, Unresolved
    };
}

public static class CashFlowDataSemantics
{
    public const string CurrentValidFacts = "CURRENT_VALID_FACTS_NOT_BITEMPORAL";
    public const string CurrentValidFactsWithSourceDateCutoff =
        "CURRENT_VALID_FACTS_SOURCE_DATE_CUTOFF_NOT_BITEMPORAL";
}

public static class FxConversionCompleteness
{
    public const string Complete = "COMPLETE";
    public const string Incomplete = "INCOMPLETE";
    public const string MissingSet = "MISSING_SET";
}

public sealed record FxConversionContext(
    string ReportingCurrencyCode,
    Guid? RateSetId,
    DateOnly? EffectiveDate,
    int? Revision,
    string Completeness,
    IReadOnlyList<string> MissingCurrencyCodes);

public sealed record CashFlowJalaliDateResponse(string JalaliDate, DateOnly GregorianDate);

public sealed record CashFlowCurrencySummary(
    Guid CurrencyId,
    string CurrencyCode,
    int DecimalPrecision,
    [property: JsonConverter(typeof(InvariantDecimalStringJsonConverter))] decimal OverdueAmount,
    [property: JsonConverter(typeof(InvariantDecimalStringJsonConverter))] decimal Due7DaysAmount,
    [property: JsonConverter(typeof(InvariantDecimalStringJsonConverter))] decimal Due30DaysAmount,
    [property: JsonConverter(typeof(InvariantDecimalStringJsonConverter))] decimal Due90DaysAmount,
    [property: JsonConverter(typeof(InvariantDecimalStringJsonConverter))] decimal HorizonAmount,
    int ObligationCount);

public sealed record CashFlowBucket(
    DateOnly Date,
    Guid CurrencyId,
    string CurrencyCode,
    [property: JsonConverter(typeof(InvariantDecimalStringJsonConverter))] decimal Amount,
    int Count);

public sealed record CashFlowUsdSummary(
    [property: JsonConverter(typeof(NullableInvariantDecimalStringJsonConverter))] decimal? OverdueAmount,
    [property: JsonConverter(typeof(NullableInvariantDecimalStringJsonConverter))] decimal? Due7DaysAmount,
    [property: JsonConverter(typeof(NullableInvariantDecimalStringJsonConverter))] decimal? Due30DaysAmount,
    [property: JsonConverter(typeof(NullableInvariantDecimalStringJsonConverter))] decimal? Due90DaysAmount,
    [property: JsonConverter(typeof(NullableInvariantDecimalStringJsonConverter))] decimal? HorizonAmount);

public sealed record CashFlowUsdBucket(
    DateOnly Date,
    [property: JsonConverter(typeof(NullableInvariantDecimalStringJsonConverter))] decimal? Amount,
    int Count);

public sealed record CashFlowDashboardResponse(
    DateOnly AsOfDate,
    DateOnly HorizonEnd,
    bool IncludePlanned,
    DateTime GeneratedAtUtc,
    string DataSemantics,
    IReadOnlyList<CashFlowCurrencySummary> Currencies,
    IReadOnlyList<CashFlowBucket> Buckets,
    int UnresolvedCount,
    FxConversionContext FxConversionContext,
    CashFlowUsdSummary UsdSummary,
    IReadOnlyList<CashFlowUsdBucket> UsdBuckets);

public sealed record CashFlowObligationRow(
    string ObligationKey,
    DateOnly? ExpectedPaymentDate,
    Guid PurchaseOrderId,
    string PurchaseOrderNumber,
    string PurchaseOrderStatus,
    Guid? PurchaseOrderLineId,
    int? PurchaseOrderLineNumber,
    Guid? ItemId,
    string? ItemCode,
    string? ItemNameFa,
    string? ItemNameEn,
    Guid SupplierId,
    string SupplierNumber,
    string SupplierName,
    Guid CurrencyId,
    string CurrencyCode,
    int CurrencyDecimalPrecision,
    [property: JsonConverter(typeof(InvariantDecimalStringJsonConverter))] decimal ProjectedAmount,
    string Stage,
    string Confidence,
    string DateSource,
    string State,
    string AnchorCode,
    Guid? DeliveryScheduleId,
    int? ScheduleNumber,
    [property: JsonConverter(typeof(NullableInvariantDecimalStringJsonConverter))] decimal? UnitsPerUsd,
    [property: JsonConverter(typeof(NullableInvariantDecimalStringJsonConverter))] decimal? ProjectedAmountUsd);

public sealed record CashFlowObligationPage(
    IReadOnlyList<CashFlowObligationRow> Items,
    int Page,
    int PageSize,
    int TotalCount,
    DateOnly AsOfDate,
    DateTime GeneratedAtUtc,
    string DataSemantics,
    FxConversionContext FxConversionContext,
    [property: JsonConverter(typeof(NullableInvariantDecimalStringJsonConverter))] decimal? TotalUsdAmount);

public static class CostBasisKinds
{
    public const string Receipt = "RECEIPT";
    public const string PurchaseOrderEstimate = "PURCHASE_ORDER_ESTIMATE";
    public const string MaterialCostBook = "MATERIAL_COST_BOOK";
    public const string Missing = "MISSING";
}

public sealed record CostBasisSelectionContext(
    string Mode,
    string TraversalSemantics,
    Guid? PriceBookId,
    string? PriceBookNumber,
    Guid? PriceBookRevisionId,
    int? PriceBookRevision,
    string? PriceBookNameFa,
    string? PriceBookNameEn,
    DateOnly? PriceBookEffectiveDate,
    DateTime? PriceBookPublishedAtUtc);

public sealed record CostBasisResponse(
    string Kind,
    [property: JsonConverter(typeof(NullableInvariantDecimalStringJsonConverter))] decimal? UnitPrice,
    Guid? CurrencyId,
    string? CurrencyCode,
    int? CurrencyDecimalPrecision,
    Guid? UnitId,
    string? UnitCode,
    Guid? SourceDocumentId,
    string? SourceDocumentNumber,
    DateOnly? SourceDate,
    Guid? PurchaseOrderId,
    string? PurchaseOrderNumber,
    Guid? SupplierId,
    string? SupplierNumber,
    string? SupplierName,
    string? MissingReason,
    [property: JsonConverter(typeof(NullableInvariantDecimalStringJsonConverter))]
    decimal? OriginalPriceAmount,
    [property: JsonConverter(typeof(NullableInvariantDecimalStringJsonConverter))]
    decimal? PriceBasisQuantity,
    Guid? PriceUnitId,
    string? PriceUnitCode,
    [property: JsonConverter(typeof(NullableInvariantDecimalStringJsonConverter))]
    decimal? PriceUnitFactorToBase,
    Guid? PriceBookRevisionId);

public sealed record CostedBomLineResponse(
    string Path,
    int Level,
    bool IsLeaf,
    Guid ItemId,
    string ItemCode,
    string ItemNameFa,
    string? ItemNameEn,
    [property: JsonConverter(typeof(InvariantDecimalStringJsonConverter))] decimal Quantity,
    Guid UnitId,
    string UnitCode,
    [property: JsonConverter(typeof(InvariantDecimalStringJsonConverter))] decimal ScrapPercent,
    CostBasisResponse? CostBasis,
    [property: JsonConverter(typeof(NullableInvariantDecimalStringJsonConverter))] decimal? ExtendedCost,
    [property: JsonConverter(typeof(NullableInvariantDecimalStringJsonConverter))] decimal? ExtendedCostUsd);

public sealed record CostedBomCurrencyTotal(
    Guid CurrencyId,
    string CurrencyCode,
    int DecimalPrecision,
    [property: JsonConverter(typeof(InvariantDecimalStringJsonConverter))] decimal MaterialCost,
    [property: JsonConverter(typeof(NullableInvariantDecimalStringJsonConverter))] decimal? UnitsPerUsd,
    [property: JsonConverter(typeof(NullableInvariantDecimalStringJsonConverter))] decimal? MaterialCostUsd);

public sealed record CostedBomMissingCost(
    string Path,
    Guid ItemId,
    string ItemCode,
    string ItemNameFa,
    string? ItemNameEn,
    string Reason);

public sealed record CostedBomResponse(
    Guid ParentItemId,
    string ParentItemCode,
    string ParentItemNameFa,
    string? ParentItemNameEn,
    Guid BomRevisionId,
    string BomRevisionCode,
    [property: JsonConverter(typeof(InvariantDecimalStringJsonConverter))] decimal CostedOutputQuantity,
    Guid OutputUnitId,
    string OutputUnitCode,
    DateOnly AsOfDate,
    DateTime GeneratedAtUtc,
    string ValuationSemantics,
    CostBasisSelectionContext CostBasisSelection,
    IReadOnlyList<CostedBomLineResponse> Lines,
    IReadOnlyList<CostedBomCurrencyTotal> CurrencyTotals,
    IReadOnlyList<CostedBomMissingCost> MissingCosts,
    bool HasMixedCurrencies,
    FxConversionContext FxConversionContext,
    [property: JsonConverter(typeof(NullableInvariantDecimalStringJsonConverter))] decimal? MaterialCostUsd);

public sealed record CostedBomSummary(
    Guid ParentItemId,
    string ParentItemCode,
    string ParentItemNameFa,
    string? ParentItemNameEn,
    Guid BomRevisionId,
    string BomRevisionCode,
    [property: JsonConverter(typeof(InvariantDecimalStringJsonConverter))] decimal CostedOutputQuantity,
    Guid OutputUnitId,
    string OutputUnitCode,
    DateOnly AsOfDate,
    CostBasisSelectionContext CostBasisSelection,
    IReadOnlyList<CostedBomCurrencyTotal> CurrencyTotals,
    int MissingCostCount,
    bool HasMixedCurrencies,
    [property: JsonConverter(typeof(NullableInvariantDecimalStringJsonConverter))] decimal? MaterialCostUsd);

public sealed record CostedBomPage(
    IReadOnlyList<CostedBomSummary> Items,
    int Page,
    int PageSize,
    int TotalCount,
    DateOnly AsOfDate,
    DateTime GeneratedAtUtc,
    string ValuationSemantics,
    CostBasisSelectionContext CostBasisSelection,
    FxConversionContext FxConversionContext);

public sealed class CashFlowValidationException(IReadOnlyDictionary<string, string[]> errors)
    : Exception("Cash-flow validation failed.")
{
    public IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}

public sealed class CashFlowConflictException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
