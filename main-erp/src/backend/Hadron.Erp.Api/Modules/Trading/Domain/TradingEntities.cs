namespace Hadron.Erp.Api.Modules.Trading.Domain;

public static class TradeProjectStatuses
{
    public static readonly string[] All = ["DRAFT", "ACTIVE", "WAITING_ORDER", "FX_PROCESS", "READY_TO_SHIP", "IN_TRANSIT", "IN_CUSTOMS", "CLEARED", "DELIVERED", "CLOSED", "CANCELLED"];
}

public sealed class TradeReferenceValue
{
    private TradeReferenceValue() { }
    public TradeReferenceValue(string referenceType, string code, string nameFa, string? nameEn, int sortOrder)
    { Id = Guid.NewGuid(); ReferenceType = referenceType; Code = code; NameFa = nameFa; NameEn = nameEn; SortOrder = sortOrder; }
    public Guid Id { get; private set; }
    public string ReferenceType { get; private set; } = string.Empty;
    public string Code { get; private set; } = string.Empty;
    public string NameFa { get; private set; } = string.Empty;
    public string? NameEn { get; private set; }
    public int SortOrder { get; private set; }
    public bool IsActive { get; private set; } = true;
    public long Version { get; private set; } = 1;
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }
    public void Edit(string nameFa, string? nameEn, int sortOrder) { NameFa = nameFa; NameEn = nameEn; SortOrder = sortOrder; Advance(); }
    public void ChangeState(bool active) { IsActive = active; Advance(); }
    private void Advance() { Version++; UpdatedAtUtc = DateTime.UtcNow; }
}

public sealed class TradeProject
{
    private TradeProject() { }
    public TradeProject(string projectNumber, string titleFa, string? titleEn, Guid? customerId,
        string? customerCode, string? customerNameFa, string? customerNameEn, Guid? supplierId,
        string? supplierNumber, string? supplierName, string priority, DateOnly? targetDate, Guid createdByUserId, string? notes)
    {
        Id = Guid.NewGuid(); ProjectNumber = projectNumber; TitleFa = titleFa; TitleEn = titleEn;
        CustomerId = customerId; CustomerCodeSnapshot = customerCode; CustomerNameFaSnapshot = customerNameFa;
        CustomerNameEnSnapshot = customerNameEn; SupplierId = supplierId; SupplierNumberSnapshot = supplierNumber;
        SupplierNameSnapshot = supplierName; Priority = priority; TargetDate = targetDate; CreatedByUserId = createdByUserId; Notes = notes;
    }
    public Guid Id { get; private set; }
    public string ProjectNumber { get; private set; } = string.Empty;
    public string TitleFa { get; private set; } = string.Empty;
    public string? TitleEn { get; private set; }
    public Guid? CustomerId { get; private set; }
    public string? CustomerCodeSnapshot { get; private set; }
    public string? CustomerNameFaSnapshot { get; private set; }
    public string? CustomerNameEnSnapshot { get; private set; }
    public Guid? SupplierId { get; private set; }
    public string? SupplierNumberSnapshot { get; private set; }
    public string? SupplierNameSnapshot { get; private set; }
    public string Priority { get; private set; } = "NORMAL";
    public string Status { get; private set; } = "DRAFT";
    public DateOnly? TargetDate { get; private set; }
    public Guid CreatedByUserId { get; private set; }
    public string? Notes { get; private set; }
    public long Version { get; private set; } = 1;
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }
    public ICollection<TradeOrderRegistration> OrderRegistrations { get; private set; } = [];
    public ICollection<TradeFxAllocation> FxAllocations { get; private set; } = [];
    public ICollection<TradeProjectShipment> ShipmentLinks { get; private set; } = [];
    public ICollection<TradeCost> Costs { get; private set; } = [];
    // Additive first-step facts; null for historical projects, never fabricated during migration.
    public DateOnly? StartDate { get; private set; }
    public Guid? BusinessCardId { get; private set; }
    public string? BusinessCardCodeSnapshot { get; private set; }
    public string? BusinessCardNameFaSnapshot { get; private set; }
    public string? BusinessCardNameEnSnapshot { get; private set; }
    public Guid? ResponsibleUserId { get; private set; }
    public string? ResponsibleUserNameSnapshot { get; private set; }

    public void SetInitialSetup(DateOnly startDate, Guid responsibleUserId, string responsibleUserName,
        Guid? businessCardId, string? cardCode, string? cardNameFa, string? cardNameEn)
    {
        if (StartDate.HasValue || startDate == default || responsibleUserId == Guid.Empty || string.IsNullOrWhiteSpace(responsibleUserName))
            throw new InvalidOperationException("Initial project setup must be valid and assigned only once.");
        StartDate = startDate; ResponsibleUserId = responsibleUserId; ResponsibleUserNameSnapshot = responsibleUserName;
        BusinessCardId = businessCardId; BusinessCardCodeSnapshot = cardCode;
        BusinessCardNameFaSnapshot = cardNameFa; BusinessCardNameEnSnapshot = cardNameEn;
    }
    public void Edit(string titleFa, string? titleEn, string priority, DateOnly? targetDate, string? notes)
    { TitleFa = titleFa; TitleEn = titleEn; Priority = priority; TargetDate = targetDate; Notes = notes; Advance(); }
    public void ChangeStatus(string status) { Status = status; Advance(); }
    private void Advance() { Version++; UpdatedAtUtc = DateTime.UtcNow; }
}

public sealed class TradeOrderRegistration
{
    private TradeOrderRegistration() { }
    public TradeOrderRegistration(Guid projectId, string registrationNumber, string status, DateOnly registrationDate,
        decimal registeredAmount, Guid currencyId, string currencyCode, string currencyNameFa, string? currencyNameEn, string? notes)
    { Id = Guid.NewGuid(); ProjectId = projectId; RegistrationNumber = registrationNumber; Status = status; RegistrationDate = registrationDate; RegisteredAmount = registeredAmount; CurrencyId = currencyId; CurrencyCodeSnapshot = currencyCode; CurrencyNameFaSnapshot = currencyNameFa; CurrencyNameEnSnapshot = currencyNameEn; Notes = notes; }
    public Guid Id { get; private set; }
    public Guid ProjectId { get; private set; }
    public TradeProject Project { get; private set; } = null!;
    public string RegistrationNumber { get; private set; } = string.Empty;
    public string Status { get; private set; } = "DRAFT";
    public DateOnly RegistrationDate { get; private set; }
    public decimal RegisteredAmount { get; private set; }
    public Guid CurrencyId { get; private set; }
    public string CurrencyCodeSnapshot { get; private set; } = string.Empty;
    public string CurrencyNameFaSnapshot { get; private set; } = string.Empty;
    public string? CurrencyNameEnSnapshot { get; private set; }
    public string? Notes { get; private set; }
    public long Version { get; private set; } = 1;
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }
    public void EditDetails(string registrationNumber, DateOnly date, decimal amount, Guid currencyId,
        string currencyCode, string currencyNameFa, string? currencyNameEn, string? notes)
    {
        if (Status != "DRAFT") throw new InvalidOperationException("Only draft orders are editable.");
        RegistrationNumber = registrationNumber; RegistrationDate = date; RegisteredAmount = amount;
        CurrencyId = currencyId; CurrencyCodeSnapshot = currencyCode; CurrencyNameFaSnapshot = currencyNameFa;
        CurrencyNameEnSnapshot = currencyNameEn; Notes = notes; Advance();
    }
    public void Edit(string status, DateOnly date, decimal amount, string? notes) { Status = status; RegistrationDate = date; RegisteredAmount = amount; Notes = notes; Advance(); }
    public void ChangeStatus(string status) { Status = status; Advance(); }
    private void Advance() { Version++; UpdatedAtUtc = DateTime.UtcNow; }
}

public sealed class TradeFxAllocation
{
    private TradeFxAllocation() { }
    public TradeFxAllocation(Guid projectId, Guid? orderRegistrationId, string processType, string dealStatus, string receiptStatus,
        decimal amount, Guid currencyId, string currencyCode, string currencyNameFa, string? currencyNameEn, Guid? bankId,
        string? bankCode, string? bankNameFa, string? bankNameEn, DateOnly requestDate, DateOnly? allocationDate, string? referenceNumber, string? notes)
    { Id = Guid.NewGuid(); ProjectId = projectId; OrderRegistrationId = orderRegistrationId; ProcessType = processType; DealStatus = dealStatus; ReceiptStatus = receiptStatus; Amount = amount; CurrencyId = currencyId; CurrencyCodeSnapshot = currencyCode; CurrencyNameFaSnapshot = currencyNameFa; CurrencyNameEnSnapshot = currencyNameEn; BankId = bankId; BankCodeSnapshot = bankCode; BankNameFaSnapshot = bankNameFa; BankNameEnSnapshot = bankNameEn; RequestDate = requestDate; AllocationDate = allocationDate; ReferenceNumber = referenceNumber; Notes = notes; }
    public Guid Id { get; private set; }
    public Guid ProjectId { get; private set; }
    public TradeProject Project { get; private set; } = null!;
    public Guid? OrderRegistrationId { get; private set; }
    public TradeOrderRegistration? OrderRegistration { get; private set; }
    public string ProcessType { get; private set; } = string.Empty;
    public string DealStatus { get; private set; } = string.Empty;
    public string ReceiptStatus { get; private set; } = string.Empty;
    public decimal Amount { get; private set; }
    public Guid CurrencyId { get; private set; }
    public string CurrencyCodeSnapshot { get; private set; } = string.Empty;
    public string CurrencyNameFaSnapshot { get; private set; } = string.Empty;
    public string? CurrencyNameEnSnapshot { get; private set; }
    public Guid? BankId { get; private set; }
    public string? BankCodeSnapshot { get; private set; }
    public string? BankNameFaSnapshot { get; private set; }
    public string? BankNameEnSnapshot { get; private set; }
    public DateOnly RequestDate { get; private set; }
    public DateOnly? AllocationDate { get; private set; }
    public string? ReferenceNumber { get; private set; }
    public string? Notes { get; private set; }
    public long Version { get; private set; } = 1;
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }
    public void ChangeStatus(string processType, string dealStatus, string receiptStatus, DateOnly? allocationDate, string? notes)
    { ProcessType = processType; DealStatus = dealStatus; ReceiptStatus = receiptStatus; AllocationDate = allocationDate; Notes = notes; Advance(); }
    private void Advance() { Version++; UpdatedAtUtc = DateTime.UtcNow; }
}

public sealed class TradeShipment
{
    private TradeShipment() { }
    public TradeShipment(string shipmentNumber, string status, string transportMode, Guid? forwarderId,
        string? forwarderCode, string? forwarderNameFa, string? forwarderNameEn, string origin, string destination,
        DateOnly? estimatedDeparture, DateOnly? estimatedArrival, decimal? grossWeight, decimal? volumeCbm, string? notes)
    { Id = Guid.NewGuid(); ShipmentNumber = shipmentNumber; Status = status; TransportMode = transportMode; ForwarderId = forwarderId; ForwarderCodeSnapshot = forwarderCode; ForwarderNameFaSnapshot = forwarderNameFa; ForwarderNameEnSnapshot = forwarderNameEn; Origin = origin; Destination = destination; EstimatedDeparture = estimatedDeparture; EstimatedArrival = estimatedArrival; GrossWeight = grossWeight; VolumeCbm = volumeCbm; Notes = notes; }
    public Guid Id { get; private set; }
    public string ShipmentNumber { get; private set; } = string.Empty;
    public string Status { get; private set; } = "DRAFT";
    public string TransportMode { get; private set; } = string.Empty;
    public Guid? ForwarderId { get; private set; }
    public string? ForwarderCodeSnapshot { get; private set; }
    public string? ForwarderNameFaSnapshot { get; private set; }
    public string? ForwarderNameEnSnapshot { get; private set; }
    public string Origin { get; private set; } = string.Empty;
    public string Destination { get; private set; } = string.Empty;
    public DateOnly? EstimatedDeparture { get; private set; }
    public DateOnly? EstimatedArrival { get; private set; }
    public DateOnly? ActualDeparture { get; private set; }
    public DateOnly? ActualArrival { get; private set; }
    public decimal? GrossWeight { get; private set; }
    public decimal? VolumeCbm { get; private set; }
    public string? Notes { get; private set; }
    public long Version { get; private set; } = 1;
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }
    public ICollection<TradeShipmentLeg> Legs { get; private set; } = [];
    public ICollection<TradeProjectShipment> ProjectLinks { get; private set; } = [];
    public ICollection<TradeCustomsClearance> CustomsClearances { get; private set; } = [];
    public void UpdateActuals(string status, DateOnly? departure, DateOnly? arrival, string? notes) { Status = status; ActualDeparture = departure; ActualArrival = arrival; Notes = notes; Advance(); }
    private void Advance() { Version++; UpdatedAtUtc = DateTime.UtcNow; }
}

public sealed class TradeShipmentLeg
{
    private TradeShipmentLeg() { }
    public TradeShipmentLeg(Guid shipmentId, int sequence, string fromLocation, string toLocation, string transportMode,
        string status, DateOnly? estimatedDeparture, DateOnly? estimatedArrival, string? trackingNumber, string? notes)
    { Id = Guid.NewGuid(); ShipmentId = shipmentId; Sequence = sequence; FromLocation = fromLocation; ToLocation = toLocation; TransportMode = transportMode; Status = status; EstimatedDeparture = estimatedDeparture; EstimatedArrival = estimatedArrival; TrackingNumber = trackingNumber; Notes = notes; }
    public Guid Id { get; private set; }
    public Guid ShipmentId { get; private set; }
    public TradeShipment Shipment { get; private set; } = null!;
    public int Sequence { get; private set; }
    public string FromLocation { get; private set; } = string.Empty;
    public string ToLocation { get; private set; } = string.Empty;
    public string TransportMode { get; private set; } = string.Empty;
    public string Status { get; private set; } = string.Empty;
    public DateOnly? EstimatedDeparture { get; private set; }
    public DateOnly? EstimatedArrival { get; private set; }
    public DateOnly? ActualDeparture { get; private set; }
    public DateOnly? ActualArrival { get; private set; }
    public string? TrackingNumber { get; private set; }
    public string? Notes { get; private set; }
    public long Version { get; private set; } = 1;
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }
    public void ChangeStatus(string status, DateOnly? actualDeparture, DateOnly? actualArrival, string? notes)
    { Status = status; ActualDeparture = actualDeparture; ActualArrival = actualArrival; Notes = notes; Advance(); }
    private void Advance() { Version++; UpdatedAtUtc = DateTime.UtcNow; }
}

public sealed class TradeProjectShipment
{
    private TradeProjectShipment() { }
    public TradeProjectShipment(Guid projectId, Guid shipmentId, string allocationMethod, decimal allocationValue, string? notes)
    { Id = Guid.NewGuid(); ProjectId = projectId; ShipmentId = shipmentId; AllocationMethod = allocationMethod; AllocationValue = allocationValue; Notes = notes; }
    public Guid Id { get; private set; }
    public Guid ProjectId { get; private set; }
    public TradeProject Project { get; private set; } = null!;
    public Guid ShipmentId { get; private set; }
    public TradeShipment Shipment { get; private set; } = null!;
    public string AllocationMethod { get; private set; } = string.Empty;
    public decimal AllocationValue { get; private set; }
    public string? Notes { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }
    public long Version { get; private set; } = 1;
    public void Edit(string allocationMethod, decimal allocationValue, string? notes)
    { AllocationMethod = allocationMethod; AllocationValue = allocationValue; Notes = notes; Version++; UpdatedAtUtc = DateTime.UtcNow; }
}

public sealed class TradeCustomsClearance
{
    private TradeCustomsClearance() { }
    public TradeCustomsClearance(Guid shipmentId, string clearanceNumber, string clearanceType, string status,
        Guid? officeId, string? officeCode, string? officeNameFa, string? officeNameEn,
        DateOnly? declarationDate, DateOnly? releaseDate, string? notes)
    { Id = Guid.NewGuid(); ShipmentId = shipmentId; ClearanceNumber = clearanceNumber; ClearanceType = clearanceType; Status = status; CustomsOfficeId = officeId; CustomsOfficeCodeSnapshot = officeCode; CustomsOfficeNameFaSnapshot = officeNameFa; CustomsOfficeNameEnSnapshot = officeNameEn; DeclarationDate = declarationDate; ReleaseDate = releaseDate; Notes = notes; }
    public Guid Id { get; private set; }
    public Guid ShipmentId { get; private set; }
    public TradeShipment Shipment { get; private set; } = null!;
    public string ClearanceNumber { get; private set; } = string.Empty;
    public string ClearanceType { get; private set; } = string.Empty;
    public string Status { get; private set; } = string.Empty;
    public Guid? CustomsOfficeId { get; private set; }
    public string? CustomsOfficeCodeSnapshot { get; private set; }
    public string? CustomsOfficeNameFaSnapshot { get; private set; }
    public string? CustomsOfficeNameEnSnapshot { get; private set; }
    public DateOnly? DeclarationDate { get; private set; }
    public DateOnly? ReleaseDate { get; private set; }
    public string? Notes { get; private set; }
    public long Version { get; private set; } = 1;
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }
    public void ChangeStatus(string status, DateOnly? releaseDate, string? notes)
    { Status = status; ReleaseDate = releaseDate; Notes = notes; Advance(); }
    private void Advance() { Version++; UpdatedAtUtc = DateTime.UtcNow; }
}

public sealed class TradeCost
{
    private TradeCost() { }
    public TradeCost(Guid projectId, string scopeType, Guid scopeId, string costType, decimal amount, Guid currencyId,
        string currencyCode, string currencyNameFa, string? currencyNameEn, DateOnly effectiveDate, string? referenceNumber, string? notes)
    { Id = Guid.NewGuid(); ProjectId = projectId; ScopeType = scopeType; ScopeId = scopeId; CostType = costType; Amount = amount; CurrencyId = currencyId; CurrencyCodeSnapshot = currencyCode; CurrencyNameFaSnapshot = currencyNameFa; CurrencyNameEnSnapshot = currencyNameEn; EffectiveDate = effectiveDate; ReferenceNumber = referenceNumber; Notes = notes; }
    public Guid Id { get; private set; }
    public Guid ProjectId { get; private set; }
    public TradeProject Project { get; private set; } = null!;
    public string ScopeType { get; private set; } = string.Empty;
    public Guid ScopeId { get; private set; }
    public string CostType { get; private set; } = string.Empty;
    public decimal Amount { get; private set; }
    public Guid CurrencyId { get; private set; }
    public string CurrencyCodeSnapshot { get; private set; } = string.Empty;
    public string CurrencyNameFaSnapshot { get; private set; } = string.Empty;
    public string? CurrencyNameEnSnapshot { get; private set; }
    public DateOnly EffectiveDate { get; private set; }
    public string? ReferenceNumber { get; private set; }
    public string? Notes { get; private set; }
    public bool IsVoided { get; private set; }
    public Guid? VoidedByUserId { get; private set; }
    public DateTime? VoidedAtUtc { get; private set; }
    public string? VoidReason { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public void Void(Guid userId, string reason) { IsVoided = true; VoidedByUserId = userId; VoidedAtUtc = DateTime.UtcNow; VoidReason = reason; }
}

public sealed class TradeAuditEvent
{
    private TradeAuditEvent() { }
    public TradeAuditEvent(Guid actorUserId, string entityType, Guid entityId, string action, string? previousJson, string? currentJson, string? reason)
    { Id = Guid.NewGuid(); ActorUserId = actorUserId; EntityType = entityType; EntityId = entityId; Action = action; PreviousJson = previousJson; CurrentJson = currentJson; Reason = reason; }
    public Guid Id { get; private set; }
    public Guid ActorUserId { get; private set; }
    public string EntityType { get; private set; } = string.Empty;
    public Guid EntityId { get; private set; }
    public string Action { get; private set; } = string.Empty;
    public string? PreviousJson { get; private set; }
    public string? CurrentJson { get; private set; }
    public string? Reason { get; private set; }
    public DateTime OccurredAtUtc { get; private set; }
}

public sealed class TradeReportNote
{
    private TradeReportNote() { }
    public TradeReportNote(Guid projectId, Guid authorUserId, string note) { Id = Guid.NewGuid(); ProjectId = projectId; AuthorUserId = authorUserId; Note = note; }
    public Guid Id { get; private set; }
    public Guid ProjectId { get; private set; }
    public TradeProject Project { get; private set; } = null!;
    public Guid AuthorUserId { get; private set; }
    public string Note { get; private set; } = string.Empty;
    public DateTime CreatedAtUtc { get; private set; }
}
