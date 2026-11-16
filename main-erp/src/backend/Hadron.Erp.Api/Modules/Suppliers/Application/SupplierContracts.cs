namespace Hadron.Erp.Api.Modules.Suppliers.Application;

public sealed record SupplierRef(Guid Id, string Code, string NameFa, string NameEn, bool IsActive);
public sealed record PersonResponse(Guid Id, string DisplayNameFa, string? DisplayNameEn, bool IsActive, long Version, DateTime CreatedAtUtc, DateTime UpdatedAtUtc);
public sealed record PaymentProfileResponse(Guid? PaymentTermId, SupplierRef? PaymentTerm, decimal AdvancePercent, decimal BeforeShipmentPercent, decimal CreditPercent, int? CreditDays, string? CreditAnchor);
public sealed record ContactResponse(Guid Id, string FullName, string? JobTitle, string? Email, string? Phone, string? Mobile, bool IsPrimary, bool IsActive, DateTime CreatedAtUtc, DateTime UpdatedAtUtc);
public sealed record SupplierResponse(Guid Id, string SupplierNumber, string Name, SupplierRef Country, PersonResponse? Owner, PaymentProfileResponse PaymentProfile, int? ExpertLeadTimeScore, int? ExpertQualityScore, string? Notes, bool IsActive, long Version, DateTime CreatedAtUtc, DateTime UpdatedAtUtc, IReadOnlyList<ContactResponse> Contacts);
public sealed record SupplierMutationResponse(SupplierResponse Supplier, IReadOnlyList<string> Warnings);
public sealed record SupplierListResponse(IReadOnlyList<SupplierResponse> Items, int Page, int PageSize, int TotalCount);
public sealed record CreateSupplierRequest(string Name, Guid CountryId, Guid? OwnerId, Guid? PaymentTermId, decimal AdvancePercent, decimal BeforeShipmentPercent, decimal CreditPercent, int? CreditDays, string? CreditAnchor, int? ExpertLeadTimeScore, int? ExpertQualityScore, string? Notes);
public sealed record UpdateSupplierRequest(string Name, Guid CountryId, Guid? OwnerId, Guid? PaymentTermId, decimal AdvancePercent, decimal BeforeShipmentPercent, decimal CreditPercent, int? CreditDays, string? CreditAnchor, int? ExpertLeadTimeScore, int? ExpertQualityScore, string? Notes, long ExpectedVersion);
public sealed record ChangeSupplierStateRequest(long ExpectedVersion, string? Reason);
public sealed record CreatePersonRequest(string DisplayNameFa, string? DisplayNameEn);
public sealed record UpdatePersonRequest(string DisplayNameFa, string? DisplayNameEn, long ExpectedVersion);
public sealed record ChangePersonStateRequest(long ExpectedVersion, string? Reason);
public sealed record ContactRequest(string FullName, string? JobTitle, string? Email, string? Phone, string? Mobile, bool IsPrimary, long ExpectedVersion);

public sealed class SupplierValidationException(IReadOnlyDictionary<string, string[]> errors) : Exception("Supplier validation failed.") { public IReadOnlyDictionary<string, string[]> Errors { get; } = errors; }
public sealed class SupplierConflictException(string code, string message, object? details = null) : Exception(message) { public string Code { get; } = code; public object? Details { get; } = details; }
