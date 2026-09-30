namespace Hadron.Erp.Api.Modules.ReferenceData.Application;

public sealed record ReferenceTypeResponse(Guid Id, string Code, string NameFa, string NameEn);

public sealed record ReferenceValueResponse(
    Guid Id, string TypeCode, string Code, string NameFa, string NameEn,
    bool IsActive, int SortOrder, long Version, DateTime CreatedAtUtc, DateTime UpdatedAtUtc);

public sealed record CurrencyResponse(
    Guid Id, string IsoCode, string NameFa, string NameEn, string? Symbol,
    int DecimalPrecision, bool IsActive, long Version, DateTime CreatedAtUtc, DateTime UpdatedAtUtc);

public sealed record UnitResponse(
    Guid Id, string Code, string NameFa, string NameEn, string? Symbol,
    bool IsActive, int SortOrder, long Version, DateTime CreatedAtUtc, DateTime UpdatedAtUtc);

public sealed record CreateReferenceValueRequest(string NameFa, string NameEn, int SortOrder);
public sealed record UpdateReferenceValueRequest(string NameFa, string NameEn, int SortOrder, long ExpectedVersion);
public sealed record CreateCurrencyRequest(string IsoCode, string NameFa, string NameEn, string? Symbol, int DecimalPrecision);
public sealed record UpdateCurrencyRequest(string NameFa, string NameEn, string? Symbol, int DecimalPrecision, long ExpectedVersion);
public sealed record CreateUnitRequest(string Code, string NameFa, string NameEn, string? Symbol, int SortOrder);
public sealed record UpdateUnitRequest(string NameFa, string NameEn, string? Symbol, int SortOrder, long ExpectedVersion);
public sealed record ChangeActiveStateRequest(long ExpectedVersion);

public sealed class ReferenceDataValidationException(IReadOnlyDictionary<string, string[]> errors)
    : Exception("Reference data validation failed.")
{
    public IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}

public sealed class ReferenceDataConflictException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
