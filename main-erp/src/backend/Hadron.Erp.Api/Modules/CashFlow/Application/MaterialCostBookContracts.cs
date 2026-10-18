using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Hadron.Erp.Api.Modules.CashFlow.Application;

public static class MaterialCostBookCostBasisModes
{
    public const string PurchaseHistory = "PURCHASE_HISTORY";
    public const string MaterialCostBook = "MATERIAL_COST_BOOK";
}

public sealed class MaterialCostBookInputDecimalStringJsonConverter : JsonConverter<decimal>
{
    private const NumberStyles AcceptedStyles =
        NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint;

    public override decimal Read(ref Utf8JsonReader reader, Type typeToConvert,
        JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String ||
            !decimal.TryParse(reader.GetString(), AcceptedStyles,
                CultureInfo.InvariantCulture, out var value))
            throw new JsonException("Material cost-book decimal values must be invariant JSON strings.");
        return value;
    }

    public override void Write(Utf8JsonWriter writer, decimal value,
        JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToString(CultureInfo.InvariantCulture));
}

public sealed record CreateMaterialCostBookRequest(
    string NameFa,
    string? NameEn,
    DateOnly EffectiveDate,
    Guid ClientRequestId,
    string? ChangeReason);

public sealed record UpdateMaterialCostBookDraftRequest(
    string NameFa,
    string? NameEn,
    DateOnly EffectiveDate,
    long ExpectedVersion,
    Guid ClientRequestId,
    string? ChangeReason);

public sealed record UpsertMaterialCostBookLineRequest(
    Guid CurrencyId,
    [property: JsonConverter(typeof(MaterialCostBookInputDecimalStringJsonConverter))]
    decimal OriginalPriceAmount,
    [property: JsonConverter(typeof(MaterialCostBookInputDecimalStringJsonConverter))]
    decimal PriceBasisQuantity,
    Guid PriceUnitId,
    Guid? SupplierId,
    string? SourceReference,
    string? Notes,
    long ExpectedVersion,
    Guid ClientRequestId);

public sealed record RemoveMaterialCostBookLineRequest(
    long ExpectedVersion,
    Guid ClientRequestId,
    string Reason);

public sealed record PublishMaterialCostBookRequest(
    long ExpectedVersion,
    Guid ClientRequestId,
    string Reason);

public sealed record CreateMaterialCostBookCorrectionRequest(
    long ExpectedVersion,
    Guid ClientRequestId,
    string Reason);

public sealed record MaterialCostBookSummaryResponse(
    Guid BookId,
    string BookNumber,
    Guid RevisionId,
    int Revision,
    string NameFa,
    string? NameEn,
    DateOnly EffectiveDate,
    string Status,
    Guid? SupersedesRevisionId,
    int LineCount,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    DateTime? PublishedAtUtc,
    long Version);

public sealed record MaterialCostBookPageResponse(
    IReadOnlyList<MaterialCostBookSummaryResponse> Items,
    int Page,
    int PageSize,
    int TotalCount);

public sealed record MaterialCostBookRevisionResponse(
    Guid BookId,
    string BookNumber,
    Guid RevisionId,
    int Revision,
    string NameFa,
    string? NameEn,
    DateOnly EffectiveDate,
    string Status,
    Guid? SupersedesRevisionId,
    Guid ClientRequestId,
    string? ChangeReason,
    string? CreatedByIdentifier,
    DateTime CreatedAtUtc,
    string? UpdatedByIdentifier,
    DateTime UpdatedAtUtc,
    string? PublishedByIdentifier,
    DateTime? PublishedAtUtc,
    string? PublishReason,
    int LineCount,
    long Version);

public sealed record MaterialCostBookLineResponse(
    Guid Id,
    Guid ItemId,
    string ItemCode,
    string ItemNameFa,
    string? ItemNameEn,
    Guid CurrencyId,
    string CurrencyCode,
    string CurrencyNameFa,
    string? CurrencyNameEn,
    int CurrencyDecimalPrecision,
    [property: JsonConverter(typeof(InvariantDecimalStringJsonConverter))]
    decimal OriginalPriceAmount,
    [property: JsonConverter(typeof(InvariantDecimalStringJsonConverter))]
    decimal PriceBasisQuantity,
    Guid PriceUnitId,
    string PriceUnitCode,
    string PriceUnitNameFa,
    string? PriceUnitNameEn,
    [property: JsonConverter(typeof(InvariantDecimalStringJsonConverter))]
    decimal PriceUnitFactorToBase,
    Guid ItemBaseUnitId,
    string ItemBaseUnitCode,
    string ItemBaseUnitNameFa,
    string? ItemBaseUnitNameEn,
    [property: JsonConverter(typeof(InvariantDecimalStringJsonConverter))]
    decimal NormalizedUnitPrice,
    Guid? SupplierId,
    string? SupplierNumber,
    string? SupplierName,
    string? SourceReference,
    string? Notes,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);

public sealed record MaterialCostBookLinePageResponse(
    IReadOnlyList<MaterialCostBookLineResponse> Items,
    int Page,
    int PageSize,
    int TotalCount,
    long RevisionVersion);

public sealed record MaterialCostBookLineMutationResponse(
    MaterialCostBookLineResponse Line,
    long RevisionVersion);

public sealed record MaterialCostBookPriceUnitOption(
    Guid UnitId,
    string UnitCode,
    string UnitNameFa,
    string? UnitNameEn,
    string? UnitSymbol,
    [property: JsonConverter(typeof(InvariantDecimalStringJsonConverter))]
    decimal FactorToBase,
    bool IsBaseUnit);

public sealed record MaterialCostBookItemOption(
    Guid ItemId,
    string ItemCode,
    string ItemNameFa,
    string? ItemNameEn,
    Guid BaseUnitId,
    string BaseUnitCode,
    IReadOnlyList<MaterialCostBookPriceUnitOption> PriceUnits);

public sealed record MaterialCostBookItemOptionPage(
    IReadOnlyList<MaterialCostBookItemOption> Items,
    int Page,
    int PageSize,
    int TotalCount);

public sealed record MaterialCostBookCurrencyOption(
    Guid CurrencyId,
    string CurrencyCode,
    string CurrencyNameFa,
    string? CurrencyNameEn,
    string? Symbol,
    int DecimalPrecision);

public sealed record MaterialCostBookSupplierOption(
    Guid SupplierId,
    string SupplierNumber,
    string SupplierName,
    bool IsActive);

public sealed record MaterialCostBookSupplierOptionPage(
    IReadOnlyList<MaterialCostBookSupplierOption> Items,
    int Page,
    int PageSize,
    int TotalCount,
    MaterialCostBookSupplierOption? Selected);

public sealed record MaterialCostBookSelectionOption(
    Guid BookId,
    string BookNumber,
    Guid RevisionId,
    int Revision,
    string NameFa,
    string? NameEn,
    DateOnly EffectiveDate,
    DateTime PublishedAtUtc,
    int LineCount);

public sealed class MaterialCostBookValidationException(
    IReadOnlyDictionary<string, string[]> errors)
    : Exception("Material cost-book validation failed.")
{
    public IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}

public sealed class MaterialCostBookConflictException(
    string code, string message, object? details = null) : Exception(message)
{
    public string Code { get; } = code;
    public object? Details { get; } = details;
}
