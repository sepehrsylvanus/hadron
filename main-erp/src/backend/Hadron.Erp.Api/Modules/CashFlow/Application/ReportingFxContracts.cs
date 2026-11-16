using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Hadron.Erp.Api.Modules.CashFlow.Application;

public sealed record ReportingFxRateInput(
    Guid CurrencyId,
    [property: JsonConverter(typeof(ReportingFxInputDecimalStringJsonConverter))] decimal UnitsPerUsd);

/// <summary>
/// Reporting rates cross a JavaScript boundary and may carry twelve significant
/// fractional digits. Requiring a JSON string prevents the browser from rounding a
/// numeric token before ASP.NET receives it. This strict converter is intentionally
/// scoped to reporting-FX input; response decimals continue to use the shared
/// invariant string converter.
/// </summary>
public sealed class ReportingFxInputDecimalStringJsonConverter : JsonConverter<decimal>
{
    private const NumberStyles AcceptedStyles =
        NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint;

    public override decimal Read(ref Utf8JsonReader reader, Type typeToConvert,
        JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String ||
            !decimal.TryParse(reader.GetString(), AcceptedStyles,
                CultureInfo.InvariantCulture, out var value))
        {
            throw new JsonException(
                "Reporting FX unitsPerUsd must be an invariant decimal JSON string.");
        }

        return value;
    }

    public override void Write(Utf8JsonWriter writer, decimal value,
        JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToString(CultureInfo.InvariantCulture));
}

public sealed record CreateReportingFxRateSetRequest(
    DateOnly EffectiveDate,
    Guid ClientRequestId,
    string? ChangeReason,
    IReadOnlyList<ReportingFxRateInput> Rates);

public sealed record UpdateReportingFxRateSetRequest(
    Guid ClientRequestId,
    DateOnly EffectiveDate,
    long ExpectedVersion,
    string? ChangeReason,
    IReadOnlyList<ReportingFxRateInput> Rates);

public sealed record PublishReportingFxRateSetRequest(
    Guid ClientRequestId,
    long ExpectedVersion,
    string Reason);

public sealed record CreateReportingFxCorrectionRequest(
    Guid ClientRequestId,
    long ExpectedVersion,
    string Reason);

public sealed record ReportingFxCurrencyOption(
    Guid Id,
    string Code,
    string NameFa,
    string? NameEn,
    bool IsActive,
    bool IsBaseCurrency);

public sealed record ReportingFxRateResponse(
    Guid CurrencyId,
    string CurrencyCode,
    string CurrencyNameFa,
    string? CurrencyNameEn,
    bool CurrencyIsActive,
    [property: JsonConverter(typeof(InvariantDecimalStringJsonConverter))] decimal UnitsPerUsd);

public sealed record ReportingFxRateSetSummary(
    Guid Id,
    Guid BaseCurrencyId,
    string BaseCurrencyCode,
    DateOnly EffectiveDate,
    int Revision,
    string Status,
    Guid? SupersedesRateSetId,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    DateTime? PublishedAtUtc,
    int RateCount,
    long Version);

public sealed record ReportingFxRateSetResponse(
    Guid Id,
    Guid BaseCurrencyId,
    string BaseCurrencyCode,
    DateOnly EffectiveDate,
    int Revision,
    string Status,
    Guid? SupersedesRateSetId,
    Guid ClientRequestId,
    string? ChangeReason,
    string? CreatedByIdentifier,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    string? PublishedByIdentifier,
    DateTime? PublishedAtUtc,
    string? PublishReason,
    long Version,
    IReadOnlyList<ReportingFxRateResponse> Rates);

public sealed record ReportingFxRateSetPage(
    IReadOnlyList<ReportingFxRateSetSummary> Items,
    int Page,
    int PageSize,
    int TotalCount);

public sealed record ReportingFxJalaliDateResponse(string JalaliDate, DateOnly GregorianDate);

public sealed class ReportingFxValidationException(IReadOnlyDictionary<string, string[]> errors)
    : Exception("Reporting FX validation failed.")
{
    public IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}

public sealed class ReportingFxConflictException(string code, string message, object? details = null)
    : Exception(message)
{
    public string Code { get; } = code;
    public object? Details { get; } = details;
}
