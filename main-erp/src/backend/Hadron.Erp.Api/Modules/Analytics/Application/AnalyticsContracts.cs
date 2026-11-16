namespace Hadron.Erp.Api.Modules.Analytics.Application;

public sealed record AnalyticsPage<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount,
    DateOnly EvaluationDate, DateTime DataCutoffUtc, AnalyticsProvenance Provenance);
public sealed record AnalyticsProvenance(string ContractVersion, DateTime GeneratedAtUtc, DateTime DataCutoffUtc,
    IReadOnlyDictionary<string, string?> Filters, string Source);

public sealed record PercentileStatistics(int SampleCount, decimal Mean, decimal P50, decimal P80, decimal P90,
    int Minimum, int Maximum, int? RecommendedDays, string SampleWindow);
public sealed record LeadTimeStageAnalytics(string StageCode, PercentileStatistics? Statistics,
    int? ConfiguredDays, string State);
public sealed record LeadTimeOptionAnalytics(Guid SourcingOptionId, string SourcingNumber, long Version,
    Guid ItemId, string ItemCode, string ItemNameFa, string? ItemNameEn, Guid SupplierId,
    string SupplierNumber, string SupplierNameFa, string? SupplierNameEn, PercentileStatistics? Total,
    IReadOnlyList<LeadTimeStageAnalytics> Stages, int CleanCompletedCount, int CancelledScheduleCount,
    decimal CancelledQuantity, decimal FulfillmentPercent, decimal? OnTimePercent);
public sealed record ApplyLeadTimeAnalyticsRequest(long ExpectedVersion, DateOnly EvaluationDate,
    IReadOnlyList<string> StageCodes, string? Reason);

public sealed record ForecastAccuracyRow(Guid ProductId, string ProductCode, string ProductNameFa,
    string? ProductNameEn, Guid PeriodId, int JalaliYear, int JalaliMonth, DateOnly PeriodStart,
    DateOnly PeriodEnd, DateTime EffectiveCloseAtUtc, decimal? BaselineForecast, decimal? FinalForecast,
    decimal Actual, decimal? BaselineError, decimal? BaselineAbsoluteError, decimal? BaselineApe,
    decimal? FinalError, decimal? FinalAbsoluteError, decimal? FinalApe,
    string BaselineState, string FinalState);
public sealed record ForecastAccuracySummary(int RowCount, decimal? BaselineWape, decimal? BaselineBias,
    decimal? FinalWape, decimal? FinalBias);
public sealed record ForecastAccuracyResponse(AnalyticsPage<ForecastAccuracyRow> Page,
    ForecastAccuracySummary Summary);

public sealed record CommercialCommitmentRow(Guid PurchaseOrderId, string PurchaseOrderNumber,
    Guid SupplierId, string SupplierNumber, string SupplierNameFa, Guid CurrencyId, string CurrencyCode,
    decimal OriginalAmount, decimal OrderedQuantity, decimal ReceivedQuantity, decimal CancelledQuantity,
    decimal OpenQuantity, DateOnly? OriginalEta, DateOnly? CurrentEta, DateOnly? FirstReceiptDate,
    DateOnly? FinalReceiptDate, string CompletionOutcome, string Status, string CreditAnchor,
    DateOnly? CreditAnchorDate, DateOnly? ExpectedCreditDate, string AnchorState, int RequiredScheduleCount);
public sealed record CurrencyCommitmentTotal(Guid CurrencyId, string CurrencyCode, decimal OriginalAmount);
public sealed record CommercialCommitmentResponse(AnalyticsPage<CommercialCommitmentRow> Page,
    IReadOnlyList<CurrencyCommitmentTotal> CurrencyTotals);

public sealed record InventoryPositionAnalytics(Guid ItemId, string ItemCode, string ItemNameFa,
    string? ItemNameEn, Guid WarehouseId, string WarehouseCode, string WarehouseNameFa,
    string? WarehouseNameEn, bool WarehouseIsActive, Guid UnitId, string UnitCode,
    string UnitNameFa, string? UnitNameEn, decimal OnHand, decimal CompanyOnHand,
    bool InactiveWarehouseWarning);
public sealed record InventoryMovementAnalytics(Guid TransactionId, string TransactionType, DateOnly EffectiveDate,
    Guid ItemId, string ItemCode, Guid WarehouseId, string WarehouseCode, decimal Quantity,
    string? SourceDocumentType, Guid? SourceDocumentId, DateTime CreatedAtUtc);

public sealed record AnalyticsExportRequest(string Report, string Format, DateOnly EvaluationDate,
    DateTime? DataCutoffUtc, string Language, int MaximumRows, string? Search = null,
    Guid? RunId = null, Guid? GenerationId = null, DateOnly? FromDate = null, DateOnly? ToDate = null);
public sealed record AnalyticsFile(byte[] Content, string ContentType, string FileName);

public sealed class AnalyticsValidationException(IReadOnlyDictionary<string, string[]> errors)
    : Exception("Analytics validation failed.")
{
    public IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}

public sealed class AnalyticsConflictException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
