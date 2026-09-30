using Hadron.Erp.Api.Modules.ReferenceData.Domain;

namespace Hadron.Erp.Api.Modules.CashFlow.Domain;

public static class ReportingFxRateSetStatuses
{
    public const string Draft = "DRAFT";
    public const string Published = "PUBLISHED";

    public static IReadOnlySet<string> All { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        Draft,
        Published
    };
}

public static class ReportingFxOperationTypes
{
    public const string UpdateDraft = "UPDATE_DRAFT";
    public const string Publish = "PUBLISH";

    public static IReadOnlySet<string> All { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        UpdateDraft,
        Publish
    };
}

public sealed class ReportingFxRateSet
{
    private ReportingFxRateSet() { }

    public ReportingFxRateSet(Guid baseCurrencyId, DateOnly effectiveDate, int revision,
        Guid? supersedesRateSetId, Guid clientRequestId, string requestFingerprint, string? changeReason,
        string? actorIdentifier, DateTime createdAtUtc,
        IEnumerable<(Guid CurrencyId, decimal UnitsPerUsd)> rates)
    {
        Id = Guid.NewGuid();
        BaseCurrencyId = baseCurrencyId;
        EffectiveDate = effectiveDate;
        Revision = revision;
        Status = ReportingFxRateSetStatuses.Draft;
        SupersedesRateSetId = supersedesRateSetId;
        ClientRequestId = clientRequestId;
        RequestFingerprint = requestFingerprint;
        ChangeReason = changeReason;
        CreatedByIdentifier = actorIdentifier;
        CreatedAtUtc = createdAtUtc;
        UpdatedAtUtc = createdAtUtc;
        ReplaceRateLines(rates);
    }

    public Guid Id { get; private set; }
    public Guid BaseCurrencyId { get; private set; }
    public Currency BaseCurrency { get; private set; } = null!;
    public DateOnly EffectiveDate { get; private set; }
    public int Revision { get; private set; }
    public string Status { get; private set; } = ReportingFxRateSetStatuses.Draft;
    public Guid? SupersedesRateSetId { get; private set; }
    public ReportingFxRateSet? SupersedesRateSet { get; private set; }
    public Guid ClientRequestId { get; private set; }
    public string RequestFingerprint { get; private set; } = string.Empty;
    public string? ChangeReason { get; private set; }
    public string? CreatedByIdentifier { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }
    public string? PublishedByIdentifier { get; private set; }
    public DateTime? PublishedAtUtc { get; private set; }
    public string? PublishReason { get; private set; }
    public long Version { get; private set; } = 1;
    public ICollection<ReportingFxRate> Rates { get; private set; } = [];

    public void ReplaceDraft(DateOnly effectiveDate, string? changeReason,
        string? actorIdentifier, DateTime updatedAtUtc,
        IEnumerable<(Guid CurrencyId, decimal UnitsPerUsd)> rates)
    {
        EnsureDraft();
        if (SupersedesRateSetId.HasValue && effectiveDate != EffectiveDate)
            throw new InvalidOperationException("A correction must retain its source effective date.");

        EffectiveDate = effectiveDate;
        ChangeReason = changeReason;
        CreatedByIdentifier ??= actorIdentifier;
        UpdatedAtUtc = updatedAtUtc;
        Version++;
        ReplaceRateLines(rates);
    }

    public void Publish(string reason, string? actorIdentifier, DateTime publishedAtUtc)
    {
        EnsureDraft();
        Status = ReportingFxRateSetStatuses.Published;
        PublishReason = reason;
        PublishedByIdentifier = actorIdentifier;
        PublishedAtUtc = publishedAtUtc;
        UpdatedAtUtc = publishedAtUtc;
        Version++;
    }

    private void ReplaceRateLines(IEnumerable<(Guid CurrencyId, decimal UnitsPerUsd)> rates)
    {
        // Replace the navigation instance instead of clearing a tracked required-child
        // collection. EF otherwise retains relationship snapshots for the removed rows and
        // can attempt to null their non-nullable FK during the next DetectChanges pass.
        Rates = rates
            .Select(rate => new ReportingFxRate(Id, rate.CurrencyId, rate.UnitsPerUsd))
            .ToList();
    }

    private void EnsureDraft()
    {
        if (Status != ReportingFxRateSetStatuses.Draft)
            throw new InvalidOperationException("A published reporting rate set is immutable.");
    }
}

public sealed class ReportingFxRate
{
    private ReportingFxRate() { }

    internal ReportingFxRate(Guid rateSetId, Guid currencyId, decimal unitsPerUsd)
    {
        Id = Guid.NewGuid();
        RateSetId = rateSetId;
        CurrencyId = currencyId;
        UnitsPerUsd = unitsPerUsd;
    }

    public Guid Id { get; private set; }
    public Guid RateSetId { get; private set; }
    public ReportingFxRateSet RateSet { get; private set; } = null!;
    public Guid CurrencyId { get; private set; }
    public Currency Currency { get; private set; } = null!;
    public decimal UnitsPerUsd { get; private set; }
}

/// <summary>
/// Immutable receipt for a mutation whose HTTP response may be lost. It prevents a transport
/// retry from applying the same logical Update or Publish operation twice.
/// </summary>
public sealed class ReportingFxOperationReceipt
{
    private ReportingFxOperationReceipt() { }

    internal ReportingFxOperationReceipt(Guid rateSetId, string operationType,
        Guid clientRequestId, string requestFingerprint, long resultVersion, DateTime createdAtUtc)
    {
        Id = Guid.NewGuid();
        RateSetId = rateSetId;
        OperationType = operationType;
        ClientRequestId = clientRequestId;
        RequestFingerprint = requestFingerprint;
        ResultVersion = resultVersion;
        CreatedAtUtc = createdAtUtc;
    }

    public Guid Id { get; private set; }
    public Guid RateSetId { get; private set; }
    public ReportingFxRateSet RateSet { get; private set; } = null!;
    public string OperationType { get; private set; } = string.Empty;
    public Guid ClientRequestId { get; private set; }
    public string RequestFingerprint { get; private set; } = string.Empty;
    public long ResultVersion { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
}
