namespace Hadron.Erp.Api.Modules.Mrp.Domain;

public static class MrpRefreshOutboxStatuses
{
    public const string Pending = "PENDING";
    public const string Processing = "PROCESSING";
    public const string Completed = "COMPLETED";
    public const string Failed = "FAILED";
}

public sealed class MrpRefreshOutboxMessage
{
    private MrpRefreshOutboxMessage() { }

    public MrpRefreshOutboxMessage(string source, string sourceEntityTypes, Guid? sourceEntityId,
        string? correlationId, DateTime occurredAtUtc, DateTime availableAtUtc)
    {
        Id = Guid.NewGuid();
        Source = source;
        SourceEntityTypes = sourceEntityTypes;
        SourceEntityId = sourceEntityId;
        CorrelationId = correlationId;
        OccurredAtUtc = occurredAtUtc;
        AvailableAtUtc = availableAtUtc;
        Status = MrpRefreshOutboxStatuses.Pending;
    }

    public Guid Id { get; private set; }
    public string Source { get; private set; } = string.Empty;
    public string SourceEntityTypes { get; private set; } = string.Empty;
    public Guid? SourceEntityId { get; private set; }
    public string? CorrelationId { get; private set; }
    public DateTime OccurredAtUtc { get; private set; }
    public DateTime AvailableAtUtc { get; private set; }
    public string Status { get; private set; } = string.Empty;
    public int AttemptCount { get; private set; }
    public DateTime? StartedAtUtc { get; private set; }
    public DateTime? CompletedAtUtc { get; private set; }
    public Guid? RunId { get; private set; }
    public string? ErrorCode { get; private set; }
    public string? ErrorMessage { get; private set; }

    public void MarkProcessing(DateTime timestampUtc)
    {
        Status = MrpRefreshOutboxStatuses.Processing;
        StartedAtUtc = timestampUtc;
        CompletedAtUtc = null;
        ErrorCode = null;
        ErrorMessage = null;
        AttemptCount++;
    }

    public void MarkCompleted(Guid runId, DateTime timestampUtc)
    {
        Status = MrpRefreshOutboxStatuses.Completed;
        RunId = runId;
        CompletedAtUtc = timestampUtc;
        ErrorCode = null;
        ErrorMessage = null;
    }

    public void MarkRetry(string errorCode, string errorMessage, DateTime availableAtUtc)
    {
        Status = MrpRefreshOutboxStatuses.Pending;
        StartedAtUtc = null;
        AvailableAtUtc = availableAtUtc;
        ErrorCode = errorCode;
        ErrorMessage = Truncate(errorMessage);
    }

    public void MarkFailed(string errorCode, string errorMessage, DateTime timestampUtc)
    {
        Status = MrpRefreshOutboxStatuses.Failed;
        CompletedAtUtc = timestampUtc;
        ErrorCode = errorCode;
        ErrorMessage = Truncate(errorMessage);
    }

    public void Recover(DateTime availableAtUtc)
    {
        Status = MrpRefreshOutboxStatuses.Pending;
        StartedAtUtc = null;
        AvailableAtUtc = availableAtUtc;
        ErrorCode = "WORKER_LEASE_EXPIRED";
        ErrorMessage = "The previous automatic refresh worker lease expired before completion.";
    }

    private static string Truncate(string value) => value.Length <= 1900 ? value : value[..1900];
}
