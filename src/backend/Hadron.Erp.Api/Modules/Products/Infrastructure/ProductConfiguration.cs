using Hadron.Erp.Api.Modules.Products.Domain;
using Hadron.Erp.Api.Modules.ReferenceData.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Hadron.Erp.Api.Modules.Products.Infrastructure;

internal sealed class ItemConfiguration : IEntityTypeConfiguration<Item>
{
    public void Configure(EntityTypeBuilder<Item> builder)
    {
        builder.ToTable("items", "catalog", table =>
        {
            table.HasCheckConstraint("ck_items_item_type", "\"ItemType\" = 'PRODUCT'");
            table.HasCheckConstraint("ck_items_version", "\"Version\" >= 1");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.ItemType).HasMaxLength(20).IsRequired();
        builder.Property(x => x.BusinessNumber).HasMaxLength(32).IsRequired();
        builder.Property(x => x.Code).HasMaxLength(64).IsRequired();
        builder.Property(x => x.NormalizedCode).HasMaxLength(64).IsRequired();
        builder.Property(x => x.NameFa).HasMaxLength(200).IsRequired();
        builder.Property(x => x.NameEn).HasMaxLength(200);
        builder.Property(x => x.Notes).HasMaxLength(2000);
        builder.Property(x => x.Version).IsConcurrencyToken();
        builder.Property(x => x.CreatedAtUtc).HasDefaultValueSql("CURRENT_TIMESTAMP");
        builder.Property(x => x.UpdatedAtUtc).HasDefaultValueSql("CURRENT_TIMESTAMP");
        builder.HasIndex(x => x.BusinessNumber).IsUnique();
        builder.HasIndex(x => x.NormalizedCode).IsUnique();
        builder.HasIndex(x => new { x.IsActive, x.Code });
        builder.HasOne(x => x.BaseUnit).WithMany().HasForeignKey(x => x.BaseUnitId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> builder)
    {
        builder.ToTable("products", "catalog", table =>
        {
            table.HasCheckConstraint("ck_products_safety_stock", "\"SafetyStockQty\" >= 0");
            table.HasCheckConstraint("ck_products_thresholds", "\"CriticalDays\" >= 0 AND \"CriticalDays\" <= \"SafeStartDays\" AND \"SafeStartDays\" <= \"ExcessStartDays\"");
        });
        builder.HasKey(x => x.ItemId);
        builder.Property(x => x.SafetyStockQty).HasPrecision(18, 6);
        builder.HasOne(x => x.Item).WithOne().HasForeignKey<Product>(x => x.ItemId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.ProductCategory).WithMany().HasForeignKey(x => x.ProductCategoryId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.BusinessUnit).WithMany().HasForeignKey(x => x.BusinessUnitId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => x.ProductCategoryId);
        builder.HasIndex(x => x.BusinessUnitId);
    }
}
