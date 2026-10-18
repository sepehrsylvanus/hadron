using Hadron.Erp.Api.Modules.ReferenceData.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Hadron.Erp.Api.Modules.ReferenceData.Infrastructure;

internal static class ReferenceTypeDefinitions
{
    internal static readonly ReferenceType[] All =
    [
        new(Guid.Parse("10000000-0000-0000-0000-000000000001"), "COUNTRY", "کشور", "Country"),
        new(Guid.Parse("10000000-0000-0000-0000-000000000002"), "BUSINESS_UNIT", "واحد سازمانی", "Business Unit"),
        new(Guid.Parse("10000000-0000-0000-0000-000000000003"), "PRODUCT_CATEGORY", "گروه محصول", "Product Category"),
        new(Guid.Parse("10000000-0000-0000-0000-000000000004"), "PART_CATEGORY", "گروه قطعه", "Part Category"),
        new(Guid.Parse("10000000-0000-0000-0000-000000000005"), "COMPONENT_CATEGORY", "گروه کامپوننت", "Component Category"),
        new(Guid.Parse("10000000-0000-0000-0000-000000000006"), "PAYMENT_TERM", "شرایط پرداخت", "Payment Term"),
        new(Guid.Parse("10000000-0000-0000-0000-000000000007"), "SHIPPING_MODE", "روش حمل", "Shipping Mode"),
        new(Guid.Parse("10000000-0000-0000-0000-000000000008"), "INCOTERM", "اینکوترمز", "Incoterm")
    ];
}

internal abstract class MutableReferenceConfiguration<T> : IEntityTypeConfiguration<T> where T : MutableReferenceData
{
    public virtual void Configure(EntityTypeBuilder<T> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Code).HasMaxLength(50).IsRequired();
        builder.Property(x => x.NormalizedCode).HasMaxLength(50).IsRequired();
        builder.Property(x => x.NameFa).HasMaxLength(200).IsRequired();
        builder.Property(x => x.NameEn).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Version).IsConcurrencyToken();
        builder.Property(x => x.CreatedAtUtc).HasDefaultValueSql("CURRENT_TIMESTAMP");
        builder.Property(x => x.UpdatedAtUtc).HasDefaultValueSql("CURRENT_TIMESTAMP");
    }
}

internal sealed class ReferenceTypeConfiguration : IEntityTypeConfiguration<ReferenceType>
{
    public void Configure(EntityTypeBuilder<ReferenceType> builder)
    {
        builder.ToTable("reference_types", "reference_data");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Code).HasMaxLength(50).IsRequired();
        builder.HasIndex(x => x.Code).IsUnique();
        builder.Property(x => x.NameFa).HasMaxLength(200).IsRequired();
        builder.Property(x => x.NameEn).HasMaxLength(200).IsRequired();
        builder.Property(x => x.CreatedAtUtc).HasDefaultValueSql("CURRENT_TIMESTAMP");
        builder.HasData(ReferenceTypeDefinitions.All);
    }
}

internal sealed class ReferenceValueConfiguration : MutableReferenceConfiguration<ReferenceValue>
{
    public override void Configure(EntityTypeBuilder<ReferenceValue> builder)
    {
        base.Configure(builder);
        builder.ToTable("reference_values", "reference_data");
        builder.HasIndex(x => new { x.ReferenceTypeId, x.NormalizedCode }).IsUnique();
        builder.HasIndex(x => new { x.ReferenceTypeId, x.IsActive, x.SortOrder });
        builder.HasOne(x => x.ReferenceType).WithMany().HasForeignKey(x => x.ReferenceTypeId).OnDelete(DeleteBehavior.Restrict);
        builder.ToTable(t =>
        {
            t.HasCheckConstraint("ck_reference_values_sort_order", "\"SortOrder\" >= 0");
            t.HasCheckConstraint("ck_reference_values_version", "\"Version\" >= 1");
        });
    }
}

internal sealed class CurrencyConfiguration : MutableReferenceConfiguration<Currency>
{
    public override void Configure(EntityTypeBuilder<Currency> builder)
    {
        base.Configure(builder);
        builder.ToTable("currencies", "reference_data");
        builder.Property(x => x.Code).HasMaxLength(3);
        builder.Property(x => x.NormalizedCode).HasMaxLength(3);
        builder.Property(x => x.Symbol).HasMaxLength(20);
        builder.HasIndex(x => x.NormalizedCode).IsUnique();
        builder.HasIndex(x => x.IsActive);
        builder.ToTable(t =>
        {
            t.HasCheckConstraint("ck_currencies_decimal_precision", "\"DecimalPrecision\" >= 0 AND \"DecimalPrecision\" <= 9");
            t.HasCheckConstraint("ck_currencies_iso_alpha3", "\"Code\" ~ '^[A-Z]{3}$' AND \"NormalizedCode\" = \"Code\"");
            t.HasCheckConstraint("ck_currencies_version", "\"Version\" >= 1");
        });
    }
}

internal sealed class UnitConfiguration : MutableReferenceConfiguration<Unit>
{
    public override void Configure(EntityTypeBuilder<Unit> builder)
    {
        base.Configure(builder);
        builder.ToTable("units", "reference_data");
        builder.Property(x => x.Symbol).HasMaxLength(20);
        builder.HasIndex(x => x.NormalizedCode).IsUnique();
        builder.HasIndex(x => new { x.IsActive, x.SortOrder });
        builder.ToTable(t =>
        {
            t.HasCheckConstraint("ck_units_sort_order", "\"SortOrder\" >= 0");
            t.HasCheckConstraint("ck_units_version", "\"Version\" >= 1");
        });
    }
}
