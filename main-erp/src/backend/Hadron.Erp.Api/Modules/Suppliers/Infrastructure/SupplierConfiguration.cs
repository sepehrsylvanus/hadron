using Hadron.Erp.Api.Modules.Suppliers.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Hadron.Erp.Api.Modules.Suppliers.Infrastructure;

internal sealed class InternalPersonConfiguration : IEntityTypeConfiguration<InternalPerson>
{
    public void Configure(EntityTypeBuilder<InternalPerson> b)
    {
        b.ToTable("internal_people", "organization", t => t.HasCheckConstraint("ck_internal_people_version", "\"Version\" >= 1")); b.HasKey(x => x.Id);
        b.Property(x => x.DisplayNameFa).HasMaxLength(200).IsRequired(); b.Property(x => x.DisplayNameEn).HasMaxLength(200);
        b.Property(x => x.Version).IsConcurrencyToken(); b.Property(x => x.CreatedAtUtc).HasDefaultValueSql("CURRENT_TIMESTAMP"); b.Property(x => x.UpdatedAtUtc).HasDefaultValueSql("CURRENT_TIMESTAMP");
        b.HasIndex(x => new { x.IsActive, x.DisplayNameFa });
    }
}
internal sealed class SupplierConfiguration : IEntityTypeConfiguration<Supplier>
{
    public void Configure(EntityTypeBuilder<Supplier> b)
    {
        b.ToTable("suppliers", "procurement", t => { t.HasCheckConstraint("ck_suppliers_version", "\"Version\" >= 1"); t.HasCheckConstraint("ck_suppliers_lead_score", "\"ExpertLeadTimeScore\" IS NULL OR \"ExpertLeadTimeScore\" BETWEEN 1 AND 10"); t.HasCheckConstraint("ck_suppliers_quality_score", "\"ExpertQualityScore\" IS NULL OR \"ExpertQualityScore\" BETWEEN 1 AND 10"); }); b.HasKey(x => x.Id);
        b.Property(x => x.SupplierNumber).HasMaxLength(32).IsRequired(); b.Property(x => x.Name).HasMaxLength(200).IsRequired(); b.Property(x => x.NormalizedName).HasMaxLength(200).IsRequired(); b.Property(x => x.Notes).HasMaxLength(2000);
        b.Property(x => x.Version).IsConcurrencyToken(); b.Property(x => x.CreatedAtUtc).HasDefaultValueSql("CURRENT_TIMESTAMP"); b.Property(x => x.UpdatedAtUtc).HasDefaultValueSql("CURRENT_TIMESTAMP");
        b.HasIndex(x => x.SupplierNumber).IsUnique(); b.HasIndex(x => x.NormalizedName); b.HasIndex(x => new { x.IsActive, x.Name });
        b.HasOne(x => x.Country).WithMany().HasForeignKey(x => x.CountryId).OnDelete(DeleteBehavior.Restrict); b.HasOne(x => x.Owner).WithMany().HasForeignKey(x => x.OwnerId).OnDelete(DeleteBehavior.Restrict);
    }
}
internal sealed class SupplierPaymentProfileConfiguration : IEntityTypeConfiguration<SupplierPaymentProfile>
{
    public void Configure(EntityTypeBuilder<SupplierPaymentProfile> b)
    {
        b.ToTable("supplier_payment_profiles", "procurement", t => { t.HasCheckConstraint("ck_supplier_payment_percentages", "\"AdvancePercent\" BETWEEN 0 AND 100 AND \"BeforeShipmentPercent\" BETWEEN 0 AND 100 AND \"CreditPercent\" BETWEEN 0 AND 100"); t.HasCheckConstraint("ck_supplier_credit", "(\"CreditPercent\" = 0 AND \"CreditDays\" IS NULL AND \"CreditAnchor\" IS NULL) OR (\"CreditPercent\" > 0 AND \"CreditDays\" > 0 AND \"CreditAnchor\" IN ('ORDERED','SHIPPED','WAREHOUSE_RECEIPT'))"); });
        b.HasKey(x => x.SupplierId); b.Property(x => x.AdvancePercent).HasPrecision(5, 2); b.Property(x => x.BeforeShipmentPercent).HasPrecision(5, 2); b.Property(x => x.CreditPercent).HasPrecision(5, 2); b.Property(x => x.CreditAnchor).HasMaxLength(32);
        b.HasOne(x => x.Supplier).WithOne(x => x.PaymentProfile).HasForeignKey<SupplierPaymentProfile>(x => x.SupplierId).OnDelete(DeleteBehavior.Restrict); b.HasOne(x => x.PaymentTerm).WithMany().HasForeignKey(x => x.PaymentTermId).OnDelete(DeleteBehavior.Restrict);
    }
}
internal sealed class SupplierContactConfiguration : IEntityTypeConfiguration<SupplierContact>
{
    public void Configure(EntityTypeBuilder<SupplierContact> b)
    {
        b.ToTable("supplier_contacts", "procurement", t => t.HasCheckConstraint("ck_supplier_contacts_primary_active", "NOT \"IsPrimary\" OR \"IsActive\"")); b.HasKey(x => x.Id);
        b.Property(x => x.FullName).HasMaxLength(200).IsRequired(); b.Property(x => x.JobTitle).HasMaxLength(150); b.Property(x => x.Email).HasMaxLength(254); b.Property(x => x.Phone).HasMaxLength(50); b.Property(x => x.Mobile).HasMaxLength(50);
        b.Property(x => x.CreatedAtUtc).HasDefaultValueSql("CURRENT_TIMESTAMP"); b.Property(x => x.UpdatedAtUtc).HasDefaultValueSql("CURRENT_TIMESTAMP");
        b.HasOne(x => x.Supplier).WithMany(x => x.Contacts).HasForeignKey(x => x.SupplierId).OnDelete(DeleteBehavior.Restrict); b.HasIndex(x => x.SupplierId); b.HasIndex(x => x.SupplierId).IsUnique().HasFilter("\"IsActive\" AND \"IsPrimary\"").HasDatabaseName("ux_supplier_contacts_active_primary");
    }
}
