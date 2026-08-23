using Hadron.Erp.Api.Modules.ReferenceData.Domain;

namespace Hadron.Erp.Api.Modules.Suppliers.Domain;

public sealed class InternalPerson
{
    private InternalPerson() { }
    public InternalPerson(string displayNameFa, string? displayNameEn)
    { Id = Guid.NewGuid(); DisplayNameFa = displayNameFa; DisplayNameEn = displayNameEn; }
    public Guid Id { get; private set; }
    public string DisplayNameFa { get; private set; } = string.Empty;
    public string? DisplayNameEn { get; private set; }
    public bool IsActive { get; private set; } = true;
    public long Version { get; private set; } = 1;
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }
    public void Edit(string fa, string? en) { DisplayNameFa = fa; DisplayNameEn = en; Advance(); }
    public void ChangeState(bool active) { IsActive = active; Advance(); }
    private void Advance() { Version++; UpdatedAtUtc = DateTime.UtcNow; }
}

public sealed class Supplier
{
    private Supplier() { }
    public Supplier(string supplierNumber, string name, string normalizedName, Guid countryId, Guid? ownerId, string? notes,
        int? leadScore, int? qualityScore)
    {
        Id = Guid.NewGuid(); SupplierNumber = supplierNumber; Name = name; NormalizedName = normalizedName;
        CountryId = countryId; OwnerId = ownerId; Notes = notes; ExpertLeadTimeScore = leadScore; ExpertQualityScore = qualityScore;
        PaymentProfile = new SupplierPaymentProfile(Id);
    }
    public Guid Id { get; private set; }
    public string SupplierNumber { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public string NormalizedName { get; private set; } = string.Empty;
    public Guid CountryId { get; private set; }
    public ReferenceValue Country { get; private set; } = null!;
    public Guid? OwnerId { get; private set; }
    public InternalPerson? Owner { get; private set; }
    public int? ExpertLeadTimeScore { get; private set; }
    public int? ExpertQualityScore { get; private set; }
    public string? Notes { get; private set; }
    public bool IsActive { get; private set; }
    public long Version { get; private set; } = 1;
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }
    public SupplierPaymentProfile PaymentProfile { get; private set; } = null!;
    public ICollection<SupplierContact> Contacts { get; private set; } = [];
    public void Edit(string name, string normalizedName, Guid countryId, Guid? ownerId, string? notes, int? lead, int? quality)
    { Name = name; NormalizedName = normalizedName; CountryId = countryId; OwnerId = ownerId; Notes = notes; ExpertLeadTimeScore = lead; ExpertQualityScore = quality; Advance(); }
    public void ChangeState(bool active) { IsActive = active; Advance(); }
    public void Touch() => Advance();
    private void Advance() { Version++; UpdatedAtUtc = DateTime.UtcNow; }
}

public sealed class SupplierPaymentProfile
{
    private SupplierPaymentProfile() { }
    public SupplierPaymentProfile(Guid supplierId) { SupplierId = supplierId; }
    public Guid SupplierId { get; private set; }
    public Supplier Supplier { get; private set; } = null!;
    public Guid? PaymentTermId { get; private set; }
    public ReferenceValue? PaymentTerm { get; private set; }
    public decimal AdvancePercent { get; private set; }
    public decimal BeforeShipmentPercent { get; private set; }
    public decimal CreditPercent { get; private set; }
    public int? CreditDays { get; private set; }
    public string? CreditAnchor { get; private set; }
    public void Set(Guid? termId, decimal advance, decimal beforeShipment, decimal credit, int? days, string? anchor)
    { PaymentTermId = termId; AdvancePercent = advance; BeforeShipmentPercent = beforeShipment; CreditPercent = credit; CreditDays = credit == 0 ? null : days; CreditAnchor = credit == 0 ? null : anchor; }
}

public sealed class SupplierContact
{
    private SupplierContact() { }
    public SupplierContact(Guid supplierId, string fullName, string? jobTitle, string? email, string? phone, string? mobile, bool primary)
    { Id = Guid.NewGuid(); SupplierId = supplierId; FullName = fullName; JobTitle = jobTitle; Email = email; Phone = phone; Mobile = mobile; IsPrimary = primary; }
    public Guid Id { get; private set; }
    public Guid SupplierId { get; private set; }
    public Supplier Supplier { get; private set; } = null!;
    public string FullName { get; private set; } = string.Empty;
    public string? JobTitle { get; private set; }
    public string? Email { get; private set; }
    public string? Phone { get; private set; }
    public string? Mobile { get; private set; }
    public bool IsPrimary { get; private set; }
    public bool IsActive { get; private set; } = true;
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }
    public void Edit(string fullName, string? job, string? email, string? phone, string? mobile, bool primary)
    { FullName = fullName; JobTitle = job; Email = email; Phone = phone; Mobile = mobile; IsPrimary = primary; UpdatedAtUtc = DateTime.UtcNow; }
    public void Deactivate() { IsActive = false; IsPrimary = false; UpdatedAtUtc = DateTime.UtcNow; }
    public void Reactivate(bool primary) { IsActive = true; IsPrimary = primary; UpdatedAtUtc = DateTime.UtcNow; }
}
