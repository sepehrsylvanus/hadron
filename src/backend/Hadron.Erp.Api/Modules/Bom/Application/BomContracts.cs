namespace Hadron.Erp.Api.Modules.Bom.Application;

public sealed record BomUnitRef(Guid Id, string Code, string NameFa, string? Symbol, bool IsActive);
public sealed record BomItemRef(Guid Id, string Code, string NameFa, string? NameEn,
    string ItemType, bool IsActive, string? SupplyMethod, BomUnitRef CurrentBaseUnit);
public sealed record BomLineResponse(Guid Id, BomItemRef ChildItem, BomUnitRef ChildBaseUnitSnapshot,
    decimal UsageQty, decimal? ScrapOverridePercent, decimal EffectiveScrapPercent, int SortOrder,
    IReadOnlyList<string> IntegrityReasonCodes);
public sealed record BomRevisionResponse(Guid Id, string RevisionCode, BomItemRef ParentItem,
    BomUnitRef ParentBaseUnitSnapshot, string Status, decimal BaseOutputQty, DateOnly? EffectiveFrom,
    DateOnly? EffectiveTo, string? RevisionNotes, Guid? CopiedFromRevisionId, long Version,
    DateTime CreatedAtUtc, DateTime UpdatedAtUtc, DateTime? ApprovedAtUtc, DateTime? ObsoletedAtUtc,
    int LineCount, IReadOnlyList<string> WarningCodes, IReadOnlyList<string> IntegrityReasonCodes,
    IReadOnlyList<BomLineResponse> Lines);
public sealed record BomRevisionListResponse(IReadOnlyList<BomRevisionResponse> Items, int Page, int PageSize,
    int TotalCount, DateOnly? EffectiveDate);

public sealed record CreateBomRevisionRequest(Guid ParentItemId, decimal BaseOutputQty, DateOnly? EffectiveFrom,
    DateOnly? EffectiveTo, string? RevisionNotes);
public sealed record CopyBomRevisionRequest(DateOnly? EffectiveFrom, DateOnly? EffectiveTo, string? RevisionNotes);
public sealed record UpdateBomRevisionRequest(decimal BaseOutputQty, DateOnly? EffectiveFrom, DateOnly? EffectiveTo,
    string? RevisionNotes, long ExpectedVersion);
public sealed record AddBomLineRequest(Guid ChildItemId, decimal UsageQty, decimal? ScrapOverridePercent,
    int SortOrder, long ExpectedVersion);
public sealed record UpdateBomLineRequest(decimal UsageQty, decimal? ScrapOverridePercent, int SortOrder,
    long ExpectedVersion);
public sealed record BomMutationRequest(long ExpectedVersion, string? Reason);
public sealed record ApproveBomRevisionRequest(long ExpectedVersion, bool ConfirmPreviousClosure,
    Guid? PreviousRevisionId, long? ExpectedPreviousVersion, string? Reason);
public sealed record ObsoleteBomRevisionRequest(long ExpectedVersion, DateOnly RetireFromDate, string? Reason);
public sealed record BomValidationResponse(bool IsValidForApproval, IReadOnlyList<string> WarningCodes,
    IReadOnlyList<string> BlockingCodes);

public sealed class BomValidationException(IReadOnlyDictionary<string, string[]> errors)
    : Exception("BOM validation failed.")
{ public IReadOnlyDictionary<string, string[]> Errors { get; } = errors; }

public sealed class BomConflictException(string code, string message, object? details = null) : Exception(message)
{
    public string Code { get; } = code;
    public object? Details { get; } = details;
}
