using Hadron.Erp.Api.Modules.Catalog.Domain;
using Hadron.Erp.Api.Modules.Products.Domain;
using Hadron.Erp.Api.Modules.ReferenceData.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Hadron.Erp.Api.Modules.Products.Infrastructure;

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
