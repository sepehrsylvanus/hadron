using Hadron.Erp.Api.Modules.Catalog.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Hadron.Erp.Api.Modules.Catalog.Infrastructure;

internal sealed class ItemConfiguration : IEntityTypeConfiguration<Item>
{
    public void Configure(EntityTypeBuilder<Item> builder)
    {
        builder.ToTable("items", "catalog", table =>
        {
            table.HasCheckConstraint("ck_items_item_type", "\"ItemType\" IN ('PRODUCT','PART','COMPONENT')");
            table.HasCheckConstraint("ck_items_code_pair", "(\"Code\" IS NULL) = (\"NormalizedCode\" IS NULL)");
            table.HasCheckConstraint("ck_items_product_code", "\"ItemType\" <> 'PRODUCT' OR (\"Code\" IS NOT NULL AND \"NormalizedCode\" IS NOT NULL)");
            table.HasCheckConstraint("ck_items_version", "\"Version\" >= 1");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.ItemType).HasMaxLength(20).IsRequired();
        builder.Property(x => x.BusinessNumber).HasMaxLength(32).IsRequired();
        builder.Property(x => x.Code).HasMaxLength(64);
        builder.Property(x => x.NormalizedCode).HasMaxLength(64);
        builder.Property(x => x.NameFa).HasMaxLength(200).IsRequired();
        builder.Property(x => x.NameEn).HasMaxLength(200);
        builder.Property(x => x.Notes).HasMaxLength(2000);
        builder.Property(x => x.Version).IsConcurrencyToken();
        builder.Property(x => x.CreatedAtUtc).HasDefaultValueSql("CURRENT_TIMESTAMP");
        builder.Property(x => x.UpdatedAtUtc).HasDefaultValueSql("CURRENT_TIMESTAMP");
        builder.HasIndex(x => x.BusinessNumber).IsUnique();
        builder.HasIndex(x => x.NormalizedCode).IsUnique().HasFilter("\"NormalizedCode\" IS NOT NULL");
        builder.HasIndex(x => new { x.ItemType, x.IsActive });
        builder.HasOne(x => x.BaseUnit).WithMany().HasForeignKey(x => x.BaseUnitId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class MaterialItemProfileConfiguration : IEntityTypeConfiguration<MaterialItemProfile>
{
    public void Configure(EntityTypeBuilder<MaterialItemProfile> builder)
    {
        builder.ToTable("material_item_profiles", "catalog", table =>
        {
            table.HasCheckConstraint("ck_material_profiles_supply_method", "\"SupplyMethod\" IN ('BUY','MAKE','OUTSOURCE','HYBRID')");
            table.HasCheckConstraint("ck_material_profiles_net_weight", "\"NetWeightKg\" IS NULL OR \"NetWeightKg\" >= 0");
            table.HasCheckConstraint("ck_material_profiles_scrap", "\"DefaultPlanningScrapPercent\" BETWEEN 0 AND 100");
            table.HasCheckConstraint("ck_material_profiles_safety_stock", "\"SafetyStockQty\" >= 0");
            table.HasCheckConstraint("ck_material_profiles_thresholds", "\"CriticalDays\" >= 0 AND \"CriticalDays\" <= \"SafeStartDays\" AND \"SafeStartDays\" <= \"ExcessStartDays\"");
        });
        builder.HasKey(x => x.ItemId);
        builder.Property(x => x.SupplyMethod).HasMaxLength(16).IsRequired();
        builder.Property(x => x.NetWeightKg).HasPrecision(18, 6);
        builder.Property(x => x.DefaultPlanningScrapPercent).HasPrecision(5, 2);
        builder.Property(x => x.SafetyStockQty).HasPrecision(18, 6);
        builder.HasOne(x => x.Item).WithOne().HasForeignKey<MaterialItemProfile>(x => x.ItemId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Category).WithMany().HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => x.CategoryId);
        builder.HasIndex(x => x.SupplyMethod);
    }
}
