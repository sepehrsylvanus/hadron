using Hadron.Erp.Api.Modules.Catalog.Domain;
using Hadron.Erp.Api.Modules.ReferenceData.Domain;

namespace Hadron.Erp.Api.Modules.Bom.Domain;

public static class BomStatuses
{
    public const string Draft = "DRAFT";
    public const string Approved = "APPROVED";
    public const string Obsolete = "OBSOLETE";
}

public sealed class BomRevision
{
    private BomRevision() { }

    public BomRevision(string revisionCode, Guid parentItemId, Guid parentBaseUnitId, decimal baseOutputQty,
        DateOnly? effectiveFrom, DateOnly? effectiveTo, string? revisionNotes, Guid? copiedFromRevisionId = null)
    {
        Id = Guid.NewGuid();
        RevisionCode = revisionCode;
        ParentItemId = parentItemId;
        ParentBaseUnitId = parentBaseUnitId;
        BaseOutputQty = baseOutputQty;
        EffectiveFrom = effectiveFrom;
        EffectiveTo = effectiveTo;
        RevisionNotes = revisionNotes;
        CopiedFromRevisionId = copiedFromRevisionId;
    }

    public Guid Id { get; private set; }
    public string RevisionCode { get; private set; } = string.Empty;
    public Guid ParentItemId { get; private set; }
    public Item ParentItem { get; private set; } = null!;
    public Guid ParentBaseUnitId { get; private set; }
    public Unit ParentBaseUnit { get; private set; } = null!;
    public string Status { get; private set; } = BomStatuses.Draft;
    public decimal BaseOutputQty { get; private set; }
    public DateOnly? EffectiveFrom { get; private set; }
    public DateOnly? EffectiveTo { get; private set; }
    public string? RevisionNotes { get; private set; }
    public Guid? CopiedFromRevisionId { get; private set; }
    public BomRevision? CopiedFromRevision { get; private set; }
    public long Version { get; private set; } = 1;
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }
    public DateTime? ApprovedAtUtc { get; private set; }
    public string? ApprovedByActorId { get; private set; }
    public DateTime? ObsoletedAtUtc { get; private set; }
    public string? ObsoletedByActorId { get; private set; }
    public ICollection<BomLine> Lines { get; private set; } = [];

    public void EditDraft(decimal baseOutputQty, DateOnly? from, DateOnly? to, string? notes)
    {
        EnsureDraft();
        BaseOutputQty = baseOutputQty;
        EffectiveFrom = from;
        EffectiveTo = to;
        RevisionNotes = notes;
        Advance();
    }

    public void TouchDraft() { EnsureDraft(); Advance(); }

    public void Approve(string? actor)
    {
        EnsureDraft();
        Status = BomStatuses.Approved;
        ApprovedAtUtc = DateTime.UtcNow;
        ApprovedByActorId = actor;
        Advance();
    }

    public void CloseForSuccessor(DateOnly to)
    {
        if (Status != BomStatuses.Approved || EffectiveTo.HasValue) throw new InvalidOperationException();
        EffectiveTo = to;
        Advance();
    }

    public void Obsolete(DateOnly retireFrom, string? actor)
    {
        if (Status != BomStatuses.Approved || !EffectiveFrom.HasValue || retireFrom <= EffectiveFrom.Value)
            throw new InvalidOperationException();
        var closeTo = retireFrom.AddDays(-1);
        if (!EffectiveTo.HasValue || EffectiveTo.Value >= retireFrom) EffectiveTo = closeTo;
        Status = BomStatuses.Obsolete;
        ObsoletedAtUtc = DateTime.UtcNow;
        ObsoletedByActorId = actor;
        Advance();
    }

    private void EnsureDraft() { if (Status != BomStatuses.Draft) throw new InvalidOperationException(); }
    private void Advance() { Version++; UpdatedAtUtc = DateTime.UtcNow; }
}

public sealed class BomLine
{
    private BomLine() { }
    public BomLine(Guid revisionId, Guid childItemId, Guid childBaseUnitId, decimal usageQty,
        decimal? scrapOverridePercent, int sortOrder)
    {
        Id = Guid.NewGuid();
        BomRevisionId = revisionId;
        ChildItemId = childItemId;
        ChildBaseUnitId = childBaseUnitId;
        UsageQty = usageQty;
        ScrapOverridePercent = scrapOverridePercent;
        SortOrder = sortOrder;
    }

    public Guid Id { get; private set; }
    public Guid BomRevisionId { get; private set; }
    public BomRevision BomRevision { get; private set; } = null!;
    public Guid ChildItemId { get; private set; }
    public Item ChildItem { get; private set; } = null!;
    public Guid ChildBaseUnitId { get; private set; }
    public Unit ChildBaseUnit { get; private set; } = null!;
    public decimal UsageQty { get; private set; }
    public decimal? ScrapOverridePercent { get; private set; }
    public int SortOrder { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    public void Edit(decimal usageQty, decimal? scrap, int sortOrder)
    {
        UsageQty = usageQty;
        ScrapOverridePercent = scrap;
        SortOrder = sortOrder;
        UpdatedAtUtc = DateTime.UtcNow;
    }
}
