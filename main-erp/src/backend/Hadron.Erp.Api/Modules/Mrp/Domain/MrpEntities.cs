namespace Hadron.Erp.Api.Modules.Mrp.Domain;

public static class MrpRunStatuses
{
    public const string Running = "RUNNING";
    public const string Completed = "COMPLETED";
    public const string CompletedWithExceptions = "COMPLETED_WITH_EXCEPTIONS";
    public const string Failed = "FAILED";
}

public static class MrpRunPurposes
{
    public const string Current = "CURRENT";
    public const string Certified = "CERTIFIED";
    public const string Published = "PUBLISHED";
    public const string Legacy = "LEGACY";
}

public static class MrpPlanSlotNames
{
    public const string Current = "CURRENT";
    public const string Certified = "CERTIFIED";
}

public static class MrpCompleteness
{
    public const string Complete = "COMPLETE";
    public const string CompleteWithWarnings = "COMPLETE_WITH_WARNINGS";
    public const string Incomplete = "INCOMPLETE";
}

public static class MrpExceptionSeverities
{
    public const string Error = "ERROR";
    public const string Warning = "WARNING";
    public const string Info = "INFO";
}

public static class MrpCoverageStates
{
    public const string Date = "DATE";
    public const string BeyondHorizon = "BEYOND_HORIZON";
    public const string NoFutureDemand = "NO_FUTURE_DEMAND";
    public const string IncompleteData = "INCOMPLETE_DATA";
}

public static class MrpRoutes
{
    public const string Bom = "BOM";
    public const string External = "EXTERNAL";
}

public static class MrpResolvedPlanningRoutes
{
    public const string External = "EXTERNAL";
    public const string InternalBom = "INTERNAL_BOM";
    public const string Incomplete = "INCOMPLETE";
}

public static class MrpRouteOrigins
{
    public const string Buy = "BUY";
    public const string Make = "MAKE";
    public const string OutsourceExternal = "OUTSOURCE_EXTERNAL";
    public const string OutsourceBom = "OUTSOURCE_BOM";
    public const string HybridExternal = "HYBRID_EXTERNAL";
    public const string HybridInternal = "HYBRID_INTERNAL";
}

public sealed class MrpRun
{
    private MrpRun() { }

    public MrpRun(string runNumber, DateOnly asOfDate, DateOnly horizonEndDate, string engineVersion,
        string? actorIdentifier, DateTime startedAtUtc, string purpose = MrpRunPurposes.Published)
    {
        Id = Guid.NewGuid();
        RunNumber = runNumber;
        AsOfDate = asOfDate;
        HorizonEndDate = horizonEndDate;
        EngineVersion = engineVersion;
        ActorIdentifier = actorIdentifier;
        Purpose = purpose;
        StartedAtUtc = startedAtUtc;
        Status = MrpRunStatuses.Running;
        Completeness = MrpCompleteness.Complete;
        Assumptions = "WORKING_DAY_MANUFACTURING_ROUTES;COMPANY_WIDE_INVENTORY_POOL";
    }

    public Guid Id { get; private set; }
    public string RunNumber { get; private set; } = string.Empty;
    public DateOnly AsOfDate { get; private set; }
    public DateOnly HorizonEndDate { get; private set; }
    public string EngineVersion { get; private set; } = string.Empty;
    public string Status { get; private set; } = string.Empty;
    public string Completeness { get; private set; } = string.Empty;
    public string Assumptions { get; private set; } = string.Empty;
    public string? ActorIdentifier { get; private set; }
    public string Purpose { get; private set; } = MrpRunPurposes.Legacy;
    public DateTime StartedAtUtc { get; private set; }
    public DateTime? SnapshotCapturedAtUtc { get; private set; }
    public DateTime? CompletedAtUtc { get; private set; }
    public string? FailureCode { get; private set; }
    public string? FailureMessage { get; private set; }

    public void MarkSnapshotCaptured(DateTime timestampUtc) => SnapshotCapturedAtUtc = timestampUtc;

    public void Complete(string completeness, bool hasExceptions, DateTime timestampUtc)
    {
        Status = hasExceptions ? MrpRunStatuses.CompletedWithExceptions : MrpRunStatuses.Completed;
        Completeness = completeness;
        CompletedAtUtc = timestampUtc;
    }

    public void Fail(string code, string message, DateTime timestampUtc)
    {
        Status = MrpRunStatuses.Failed;
        Completeness = MrpCompleteness.Incomplete;
        FailureCode = code;
        FailureMessage = message;
        CompletedAtUtc = timestampUtc;
    }
}

public sealed class MrpPlanSlot
{
    private MrpPlanSlot() { }

    public MrpPlanSlot(string slotName, Guid runId, DateOnly businessDate,
        DateTime sourceCutoffUtc, DateTime activatedAtUtc)
    {
        SlotName = slotName;
        RunId = runId;
        BusinessDate = businessDate;
        SourceCutoffUtc = sourceCutoffUtc;
        ActivatedAtUtc = activatedAtUtc;
        Version = 1;
    }

    public string SlotName { get; private set; } = string.Empty;
    public Guid RunId { get; private set; }
    public DateOnly BusinessDate { get; private set; }
    public DateTime SourceCutoffUtc { get; private set; }
    public DateTime ActivatedAtUtc { get; private set; }
    public int Version { get; private set; }

    public void Replace(Guid runId, DateOnly businessDate, DateTime sourceCutoffUtc, DateTime activatedAtUtc)
    {
        RunId = runId;
        BusinessDate = businessDate;
        SourceCutoffUtc = sourceCutoffUtc;
        ActivatedAtUtc = activatedAtUtc;
        Version++;
    }
}

public sealed class MrpDailyCertificate
{
    private MrpDailyCertificate() { }

    public MrpDailyCertificate(DateOnly businessDate, Guid sourceRunId, string runNumber,
        DateOnly asOfDate, DateOnly horizonEndDate, DateTime sourceCutoffUtc, DateTime certifiedAtUtc,
        string engineVersion, string status, string completeness, int itemCount,
        int dailyResultCount, int exceptionCount, string resultHash)
    {
        Id = Guid.NewGuid();
        BusinessDate = businessDate;
        SourceRunId = sourceRunId;
        RunNumber = runNumber;
        AsOfDate = asOfDate;
        HorizonEndDate = horizonEndDate;
        SourceCutoffUtc = sourceCutoffUtc;
        CertifiedAtUtc = certifiedAtUtc;
        EngineVersion = engineVersion;
        Status = status;
        Completeness = completeness;
        ItemCount = itemCount;
        DailyResultCount = dailyResultCount;
        ExceptionCount = exceptionCount;
        ResultHash = resultHash;
    }

    public Guid Id { get; private set; }
    public DateOnly BusinessDate { get; private set; }
    public Guid SourceRunId { get; private set; }
    public string RunNumber { get; private set; } = string.Empty;
    public DateOnly AsOfDate { get; private set; }
    public DateOnly HorizonEndDate { get; private set; }
    public DateTime SourceCutoffUtc { get; private set; }
    public DateTime CertifiedAtUtc { get; private set; }
    public string EngineVersion { get; private set; } = string.Empty;
    public string Status { get; private set; } = string.Empty;
    public string Completeness { get; private set; } = string.Empty;
    public int ItemCount { get; private set; }
    public int DailyResultCount { get; private set; }
    public int ExceptionCount { get; private set; }
    public string ResultHash { get; private set; } = string.Empty;
}

public sealed class MrpRunItemInput
{
    private MrpRunItemInput() { }
    public MrpRunItemInput(Guid runId, Guid itemId, string itemCode, string itemType, string nameFa,
        string? nameEn, bool isActive, Guid baseUnitId, string unitCode, string unitNameFa,
        string? unitNameEn, string? supplyMethod, decimal safetyStockQty, decimal defaultScrapPercent,
        Guid? categoryId, string? categoryCode, string? categoryNameFa, string? categoryNameEn,
        int? criticalDays, int? safeStartDays, int? excessStartDays)
    {
        Id = Guid.NewGuid(); RunId = runId; ItemId = itemId; ItemCode = itemCode; ItemType = itemType;
        NameFa = nameFa; NameEn = nameEn; IsActive = isActive; BaseUnitId = baseUnitId;
        UnitCode = unitCode; UnitNameFa = unitNameFa; UnitNameEn = unitNameEn;
        SupplyMethod = supplyMethod; SafetyStockQty = safetyStockQty; DefaultScrapPercent = defaultScrapPercent;
        CategoryId = categoryId; CategoryCode = categoryCode; CategoryNameFa = categoryNameFa;
        CategoryNameEn = categoryNameEn; CriticalDays = criticalDays; SafeStartDays = safeStartDays;
        ExcessStartDays = excessStartDays;
    }
    public Guid Id { get; private set; }
    public Guid RunId { get; private set; }
    public Guid ItemId { get; private set; }
    public string ItemCode { get; private set; } = string.Empty;
    public string ItemType { get; private set; } = string.Empty;
    public string NameFa { get; private set; } = string.Empty;
    public string? NameEn { get; private set; }
    public bool IsActive { get; private set; }
    public Guid BaseUnitId { get; private set; }
    public string UnitCode { get; private set; } = string.Empty;
    public string UnitNameFa { get; private set; } = string.Empty;
    public string? UnitNameEn { get; private set; }
    public string? SupplyMethod { get; private set; }
    public decimal SafetyStockQty { get; private set; }
    public decimal DefaultScrapPercent { get; private set; }
    public Guid? CategoryId { get; private set; }
    public string? CategoryCode { get; private set; }
    public string? CategoryNameFa { get; private set; }
    public string? CategoryNameEn { get; private set; }
    public int? CriticalDays { get; private set; }
    public int? SafeStartDays { get; private set; }
    public int? ExcessStartDays { get; private set; }
}

public sealed class MrpRunForecastInput
{
    private MrpRunForecastInput() { }
    public MrpRunForecastInput(Guid runId, Guid productId, Guid businessPeriodId, Guid forecastSeriesId,
        Guid forecastRevisionId, DateOnly demandDate, decimal demandQty, decimal periodForecastQty,
        decimal actualMtd, Guid unitId)
    {
        Id = Guid.NewGuid(); RunId = runId; ProductId = productId; BusinessPeriodId = businessPeriodId;
        ForecastSeriesId = forecastSeriesId; ForecastRevisionId = forecastRevisionId; DemandDate = demandDate;
        DemandQty = demandQty; PeriodForecastQty = periodForecastQty; ActualMtd = actualMtd; UnitId = unitId;
    }
    public Guid Id { get; private set; }
    public Guid RunId { get; private set; }
    public Guid ProductId { get; private set; }
    public Guid BusinessPeriodId { get; private set; }
    public Guid ForecastSeriesId { get; private set; }
    public Guid ForecastRevisionId { get; private set; }
    public DateOnly DemandDate { get; private set; }
    public decimal DemandQty { get; private set; }
    public decimal PeriodForecastQty { get; private set; }
    public decimal ActualMtd { get; private set; }
    public Guid UnitId { get; private set; }
}

public sealed class MrpRunForecastPeriodInput
{
    private MrpRunForecastPeriodInput() { }
    public MrpRunForecastPeriodInput(Guid runId, Guid productId, Guid businessPeriodId,
        Guid? forecastSeriesId, Guid? forecastRevisionId, decimal? officialForecastQty,
        decimal actualMtdQty, decimal? remainingForecastQty, Guid productBaseUnitId,
        DateOnly periodStartDate, DateOnly periodEndDate, DateOnly eligibleAllocationStart,
        string allocationState, string? exceptionCode)
    {
        Id = Guid.NewGuid(); RunId = runId; ProductId = productId; BusinessPeriodId = businessPeriodId;
        ForecastSeriesId = forecastSeriesId; ForecastRevisionId = forecastRevisionId;
        OfficialForecastQty = officialForecastQty; ActualMtdQty = actualMtdQty;
        RemainingForecastQty = remainingForecastQty; ProductBaseUnitId = productBaseUnitId;
        PeriodStartDate = periodStartDate; PeriodEndDate = periodEndDate;
        EligibleAllocationStart = eligibleAllocationStart; AllocationState = allocationState;
        ExceptionCode = exceptionCode;
    }
    public Guid Id { get; private set; }
    public Guid RunId { get; private set; }
    public Guid ProductId { get; private set; }
    public Guid BusinessPeriodId { get; private set; }
    public Guid? ForecastSeriesId { get; private set; }
    public Guid? ForecastRevisionId { get; private set; }
    public decimal? OfficialForecastQty { get; private set; }
    public decimal ActualMtdQty { get; private set; }
    public decimal? RemainingForecastQty { get; private set; }
    public Guid ProductBaseUnitId { get; private set; }
    public DateOnly PeriodStartDate { get; private set; }
    public DateOnly PeriodEndDate { get; private set; }
    public DateOnly EligibleAllocationStart { get; private set; }
    public string AllocationState { get; private set; } = string.Empty;
    public string? ExceptionCode { get; private set; }
}

public sealed class MrpRunBomInput
{
    private MrpRunBomInput() { }
    public MrpRunBomInput(Guid runId, Guid revisionId, string revisionCode, Guid lineId, Guid parentItemId,
        Guid childItemId, Guid parentBaseUnitId, Guid childBaseUnitId, decimal baseOutputQty,
        decimal usageQty, decimal? scrapOverridePercent, DateOnly effectiveFrom, DateOnly? effectiveTo)
    {
        Id = Guid.NewGuid(); RunId = runId; RevisionId = revisionId; RevisionCode = revisionCode; LineId = lineId;
        ParentItemId = parentItemId; ChildItemId = childItemId; ParentBaseUnitId = parentBaseUnitId;
        ChildBaseUnitId = childBaseUnitId; BaseOutputQty = baseOutputQty; UsageQty = usageQty;
        ScrapOverridePercent = scrapOverridePercent; EffectiveFrom = effectiveFrom; EffectiveTo = effectiveTo;
    }
    public Guid Id { get; private set; }
    public Guid RunId { get; private set; }
    public Guid RevisionId { get; private set; }
    public string RevisionCode { get; private set; } = string.Empty;
    public Guid LineId { get; private set; }
    public Guid ParentItemId { get; private set; }
    public Guid ChildItemId { get; private set; }
    public Guid ParentBaseUnitId { get; private set; }
    public Guid ChildBaseUnitId { get; private set; }
    public decimal BaseOutputQty { get; private set; }
    public decimal UsageQty { get; private set; }
    public decimal? ScrapOverridePercent { get; private set; }
    public DateOnly EffectiveFrom { get; private set; }
    public DateOnly? EffectiveTo { get; private set; }
}

public sealed class MrpRunInventoryInput
{
    private MrpRunInventoryInput() { }
    public MrpRunInventoryInput(Guid runId, Guid itemId, Guid warehouseId, string warehouseCode,
        bool warehouseIsActive, Guid unitId, decimal onHandQty)
    {
        Id = Guid.NewGuid(); RunId = runId; ItemId = itemId; WarehouseId = warehouseId;
        WarehouseCode = warehouseCode; WarehouseIsActive = warehouseIsActive; UnitId = unitId; OnHandQty = onHandQty;
    }
    public Guid Id { get; private set; }
    public Guid RunId { get; private set; }
    public Guid ItemId { get; private set; }
    public Guid WarehouseId { get; private set; }
    public string WarehouseCode { get; private set; } = string.Empty;
    public bool WarehouseIsActive { get; private set; }
    public Guid UnitId { get; private set; }
    public decimal OnHandQty { get; private set; }
}

public sealed class MrpRunFirmSupplyInput
{
    private MrpRunFirmSupplyInput() { }
    public MrpRunFirmSupplyInput(Guid runId, Guid itemId, Guid purchaseOrderId, string purchaseOrderNumber,
        Guid purchaseOrderLineId, Guid deliveryScheduleId, decimal openQty, DateOnly? eta, string etaSource,
        string etaStatus, string purchaseOrderStatus, Guid? supplierId, string? supplierNumber,
        string? supplierNameFa, string? supplierNameEn)
    {
        Id = Guid.NewGuid(); RunId = runId; ItemId = itemId; PurchaseOrderId = purchaseOrderId;
        PurchaseOrderNumber = purchaseOrderNumber; PurchaseOrderLineId = purchaseOrderLineId;
        DeliveryScheduleId = deliveryScheduleId; OpenQty = openQty; Eta = eta; EtaSource = etaSource;
        EtaStatus = etaStatus; PurchaseOrderStatus = purchaseOrderStatus;
        SupplierId = supplierId; SupplierNumber = supplierNumber; SupplierNameFa = supplierNameFa;
        SupplierNameEn = supplierNameEn;
    }
    public Guid Id { get; private set; }
    public Guid RunId { get; private set; }
    public Guid ItemId { get; private set; }
    public Guid PurchaseOrderId { get; private set; }
    public string PurchaseOrderNumber { get; private set; } = string.Empty;
    public Guid PurchaseOrderLineId { get; private set; }
    public Guid DeliveryScheduleId { get; private set; }
    public decimal OpenQty { get; private set; }
    public DateOnly? Eta { get; private set; }
    public string EtaSource { get; private set; } = string.Empty;
    public string EtaStatus { get; private set; } = string.Empty;
    public string PurchaseOrderStatus { get; private set; } = string.Empty;
    public Guid? SupplierId { get; private set; }
    public string? SupplierNumber { get; private set; }
    public string? SupplierNameFa { get; private set; }
    public string? SupplierNameEn { get; private set; }
}

public sealed class MrpRunRouteAvailabilityInput
{
    private MrpRunRouteAvailabilityInput() { }
    public MrpRunRouteAvailabilityInput(Guid runId, Guid itemId, DateOnly demandDate, bool bomReady, bool externalReady)
    { Id = Guid.NewGuid(); RunId = runId; ItemId = itemId; DemandDate = demandDate; BomReady = bomReady; ExternalReady = externalReady; }
    public Guid Id { get; private set; }
    public Guid RunId { get; private set; }
    public Guid ItemId { get; private set; }
    public DateOnly DemandDate { get; private set; }
    public bool BomReady { get; private set; }
    public bool ExternalReady { get; private set; }
}

public sealed class MrpRunManufacturingOperationInput
{
    private MrpRunManufacturingOperationInput() { }
    public MrpRunManufacturingOperationInput(Guid runId, Guid routeId, string routeCode,
        Guid itemId, Guid operationId, int sequence, Guid workCenterId, string workCenterCode,
        int workingWeekMask, int preparationWorkingDays, int executionWorkingDays)
    {
        Id = Guid.NewGuid(); RunId = runId; RouteId = routeId; RouteCode = routeCode;
        ItemId = itemId; OperationId = operationId; Sequence = sequence;
        WorkCenterId = workCenterId; WorkCenterCode = workCenterCode;
        WorkingWeekMask = workingWeekMask; PreparationWorkingDays = preparationWorkingDays;
        ExecutionWorkingDays = executionWorkingDays;
    }
    public Guid Id { get; private set; }
    public Guid RunId { get; private set; }
    public Guid RouteId { get; private set; }
    public string RouteCode { get; private set; } = string.Empty;
    public Guid ItemId { get; private set; }
    public Guid OperationId { get; private set; }
    public int Sequence { get; private set; }
    public Guid WorkCenterId { get; private set; }
    public string WorkCenterCode { get; private set; } = string.Empty;
    public int WorkingWeekMask { get; private set; }
    public int PreparationWorkingDays { get; private set; }
    public int ExecutionWorkingDays { get; private set; }
}

public sealed class MrpRunExternalSourceInput
{
    private MrpRunExternalSourceInput() { }
    public MrpRunExternalSourceInput(Guid runId, Guid sourcingOptionId, Guid itemId,
        bool optionIsActive, bool supplierIsActive, bool itemIsActive, Guid itemBaseUnitId,
        bool minimumOrderQuantityIsSet, bool shippingModeIsActive, bool incotermIsValid,
        bool allLeadStagesAreSet, Guid? priceId, bool? currencyIsActive,
        DateOnly? effectiveFrom, DateOnly? effectiveTo)
    {
        Id = Guid.NewGuid(); RunId = runId; SourcingOptionId = sourcingOptionId; ItemId = itemId;
        OptionIsActive = optionIsActive; SupplierIsActive = supplierIsActive; ItemIsActive = itemIsActive;
        ItemBaseUnitId = itemBaseUnitId; MinimumOrderQuantityIsSet = minimumOrderQuantityIsSet;
        ShippingModeIsActive = shippingModeIsActive; IncotermIsValid = incotermIsValid;
        AllLeadStagesAreSet = allLeadStagesAreSet; PriceId = priceId; CurrencyIsActive = currencyIsActive;
        EffectiveFrom = effectiveFrom; EffectiveTo = effectiveTo;
    }
    public Guid Id { get; private set; }
    public Guid RunId { get; private set; }
    public Guid SourcingOptionId { get; private set; }
    public Guid ItemId { get; private set; }
    public bool OptionIsActive { get; private set; }
    public bool SupplierIsActive { get; private set; }
    public bool ItemIsActive { get; private set; }
    public Guid ItemBaseUnitId { get; private set; }
    public bool MinimumOrderQuantityIsSet { get; private set; }
    public bool ShippingModeIsActive { get; private set; }
    public bool IncotermIsValid { get; private set; }
    public bool AllLeadStagesAreSet { get; private set; }
    public Guid? PriceId { get; private set; }
    public bool? CurrencyIsActive { get; private set; }
    public DateOnly? EffectiveFrom { get; private set; }
    public DateOnly? EffectiveTo { get; private set; }
}

public sealed class MrpRunRouteDecision
{
    private MrpRunRouteDecision() { }
    public MrpRunRouteDecision(Guid runId, Guid itemId, string route, string source)
    { Id = Guid.NewGuid(); RunId = runId; ItemId = itemId; Route = route; Source = source; }
    public Guid Id { get; private set; }
    public Guid RunId { get; private set; }
    public Guid ItemId { get; private set; }
    public string Route { get; private set; } = string.Empty;
    public string Source { get; private set; } = string.Empty;
}

public sealed class MrpRunResolvedRouteResult
{
    private MrpRunResolvedRouteResult() { }
    public MrpRunResolvedRouteResult(Guid runId, Guid itemId, DateOnly demandDate,
        string resolvedPlanningRoute, string? routeOrigin, string? reasonCode)
    {
        Id = Guid.NewGuid(); RunId = runId; ItemId = itemId; DemandDate = demandDate;
        ResolvedPlanningRoute = resolvedPlanningRoute; RouteOrigin = routeOrigin; ReasonCode = reasonCode;
    }
    public Guid Id { get; private set; }
    public Guid RunId { get; private set; }
    public Guid ItemId { get; private set; }
    public DateOnly DemandDate { get; private set; }
    public string ResolvedPlanningRoute { get; private set; } = string.Empty;
    public string? RouteOrigin { get; private set; }
    public string? ReasonCode { get; private set; }
}

public sealed class MrpRunDailyResult
{
    private MrpRunDailyResult() { }
    public MrpRunDailyResult(Guid runId, Guid itemId, DateOnly date, decimal grossDemand, decimal firmIncoming,
        decimal firmProjected, decimal planningBeforeNeed, decimal netRequirement, decimal planningBalance,
        bool isRootProduct, string completeness)
    {
        Id = Guid.NewGuid(); RunId = runId; ItemId = itemId; Date = date; GrossDemand = grossDemand;
        FirmIncoming = firmIncoming; FirmProjected = firmProjected; PlanningBeforeNeed = planningBeforeNeed;
        NetRequirement = netRequirement; PlanningBalance = planningBalance; IsRootProduct = isRootProduct;
        Completeness = completeness;
    }
    public Guid Id { get; private set; }
    public Guid RunId { get; private set; }
    public Guid ItemId { get; private set; }
    public DateOnly Date { get; private set; }
    public decimal GrossDemand { get; private set; }
    public decimal FirmIncoming { get; private set; }
    public decimal FirmProjected { get; private set; }
    public decimal PlanningBeforeNeed { get; private set; }
    public decimal NetRequirement { get; private set; }
    public decimal PlanningBalance { get; private set; }
    public bool IsRootProduct { get; private set; }
    public string Completeness { get; private set; } = string.Empty;
}

public sealed class MrpRunItemSummary
{
    private MrpRunItemSummary() { }
    public MrpRunItemSummary(Guid runId, Guid itemId, decimal startingOnHand, decimal totalGrossDemand,
        decimal totalFirmIncoming, decimal totalNetRequirement, decimal overdueFirmQty, decimal missingEtaFirmQty,
        DateOnly? stockoutDate, DateOnly? safetyBreachDate, string? stockCoverageState,
        DateOnly? stockCoverageDate, int? stockCoverageDays, string? firmCoverageState,
        DateOnly? firmCoverageDate, int? firmCoverageDays, string completeness)
    {
        Id = Guid.NewGuid(); RunId = runId; ItemId = itemId; StartingOnHand = startingOnHand;
        TotalGrossDemand = totalGrossDemand; TotalFirmIncoming = totalFirmIncoming;
        TotalNetRequirement = totalNetRequirement; OverdueFirmQty = overdueFirmQty;
        MissingEtaFirmQty = missingEtaFirmQty; StockoutDate = stockoutDate; SafetyBreachDate = safetyBreachDate;
        StockCoverageState = stockCoverageState; StockCoverageDate = stockCoverageDate; StockCoverageDays = stockCoverageDays;
        FirmCoverageState = firmCoverageState; FirmCoverageDate = firmCoverageDate; FirmCoverageDays = firmCoverageDays;
        Completeness = completeness;
    }
    public Guid Id { get; private set; }
    public Guid RunId { get; private set; }
    public Guid ItemId { get; private set; }
    public decimal StartingOnHand { get; private set; }
    public decimal TotalGrossDemand { get; private set; }
    public decimal TotalFirmIncoming { get; private set; }
    public decimal TotalNetRequirement { get; private set; }
    public decimal OverdueFirmQty { get; private set; }
    public decimal MissingEtaFirmQty { get; private set; }
    public DateOnly? StockoutDate { get; private set; }
    public DateOnly? SafetyBreachDate { get; private set; }
    public string? StockCoverageState { get; private set; }
    public DateOnly? StockCoverageDate { get; private set; }
    public int? StockCoverageDays { get; private set; }
    public string? FirmCoverageState { get; private set; }
    public DateOnly? FirmCoverageDate { get; private set; }
    public int? FirmCoverageDays { get; private set; }
    public string Completeness { get; private set; } = string.Empty;
}

public sealed class MrpRunException
{
    private MrpRunException() { }
    public MrpRunException(Guid runId, Guid? itemId, DateOnly? date, string code, string severity,
        bool affectsDemand, string message, string? sourceReference = null)
    {
        Id = Guid.NewGuid(); RunId = runId; ItemId = itemId; Date = date; Code = code; Severity = severity;
        AffectsDemand = affectsDemand; Message = message; SourceReference = sourceReference;
    }
    public Guid Id { get; private set; }
    public Guid RunId { get; private set; }
    public Guid? ItemId { get; private set; }
    public DateOnly? Date { get; private set; }
    public string Code { get; private set; } = string.Empty;
    public string Severity { get; private set; } = string.Empty;
    public bool AffectsDemand { get; private set; }
    public string Message { get; private set; } = string.Empty;
    public string? SourceReference { get; private set; }
}

public sealed class MrpRunDemandTrace
{
    private MrpRunDemandTrace() { }
    public MrpRunDemandTrace(Guid runId, DateOnly date, DateOnly parentDemandDate,
        Guid parentItemId, Guid childItemId,
        Guid? bomRevisionId, Guid? bomLineId, decimal parentNetRequirement, decimal baseRequirement,
        decimal scrapPercent, string scrapSource, decimal childGrossRequirement)
    {
        Id = Guid.NewGuid(); RunId = runId; Date = date; ParentDemandDate = parentDemandDate;
        ParentItemId = parentItemId; ChildItemId = childItemId;
        BomRevisionId = bomRevisionId; BomLineId = bomLineId; ParentNetRequirement = parentNetRequirement;
        BaseRequirement = baseRequirement; ScrapPercent = scrapPercent; ScrapSource = scrapSource;
        ChildGrossRequirement = childGrossRequirement;
    }
    public Guid Id { get; private set; }
    public Guid RunId { get; private set; }
    public DateOnly Date { get; private set; }
    public DateOnly ParentDemandDate { get; private set; }
    public Guid ParentItemId { get; private set; }
    public Guid ChildItemId { get; private set; }
    public Guid? BomRevisionId { get; private set; }
    public Guid? BomLineId { get; private set; }
    public decimal ParentNetRequirement { get; private set; }
    public decimal BaseRequirement { get; private set; }
    public decimal ScrapPercent { get; private set; }
    public string ScrapSource { get; private set; } = string.Empty;
    public decimal ChildGrossRequirement { get; private set; }
}
