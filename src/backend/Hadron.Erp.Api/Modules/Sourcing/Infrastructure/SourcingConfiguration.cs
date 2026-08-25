using Hadron.Erp.Api.Modules.Sourcing.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Hadron.Erp.Api.Modules.Sourcing.Infrastructure;

internal sealed class SourcingOptionConfiguration : IEntityTypeConfiguration<SourcingOption>
{
    public void Configure(EntityTypeBuilder<SourcingOption> builder)
    {
        builder.ToTable("sourcing_options", "procurement", table =>
        {
            table.HasCheckConstraint("ck_sourcing_options_version", "\"Version\" >= 1");
            table.HasCheckConstraint("ck_sourcing_options_moq", "\"MinimumOrderQuantity\" IS NULL OR \"MinimumOrderQuantity\" >= 0");
            table.HasCheckConstraint("ck_sourcing_options_lead_times", "(\"SupplierLeadTimeDays\" IS NULL OR \"SupplierLeadTimeDays\" >= 0) AND (\"ForwarderToShipmentDays\" IS NULL OR \"ForwarderToShipmentDays\" >= 0) AND (\"ShippingPeriodDays\" IS NULL OR \"ShippingPeriodDays\" >= 0) AND (\"DestinationCustomsDays\" IS NULL OR \"DestinationCustomsDays\" >= 0)");
            table.HasCheckConstraint("ck_sourcing_options_preferred_active", "NOT \"IsPreferred\" OR \"IsActive\"");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.SourcingNumber).HasMaxLength(32).IsRequired();
        builder.Property(x => x.MinimumOrderQuantity).HasPrecision(18, 6);
        builder.Property(x => x.Version).IsConcurrencyToken();
        builder.Property(x => x.CreatedAtUtc).HasDefaultValueSql("CURRENT_TIMESTAMP");
        builder.Property(x => x.UpdatedAtUtc).HasDefaultValueSql("CURRENT_TIMESTAMP");
        builder.HasIndex(x => x.SourcingNumber).IsUnique();
        builder.HasIndex(x => new { x.ItemId, x.IsActive });
        builder.HasIndex(x => new { x.SupplierId, x.IsActive });
        builder.HasIndex(x => x.ItemId).IsUnique().HasFilter("\"IsActive\" AND \"IsPreferred\"")
            .HasDatabaseName("ux_sourcing_options_active_preferred_item");
        builder.HasOne(x => x.Item).WithMany().HasForeignKey(x => x.ItemId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Supplier).WithMany().HasForeignKey(x => x.SupplierId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.ItemBaseUnit).WithMany().HasForeignKey(x => x.ItemBaseUnitId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.ShippingMode).WithMany().HasForeignKey(x => x.ShippingModeId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Incoterm).WithMany().HasForeignKey(x => x.IncotermId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class SourcingPriceConfiguration : IEntityTypeConfiguration<SourcingPrice>
{
    public void Configure(EntityTypeBuilder<SourcingPrice> builder)
    {
        builder.ToTable("sourcing_prices", "procurement", table =>
        {
            table.HasCheckConstraint("ck_sourcing_prices_unit_price", "\"UnitPrice\" >= 0");
            table.HasCheckConstraint("ck_sourcing_prices_period", "\"EffectiveTo\" IS NULL OR \"EffectiveTo\" >= \"EffectiveFrom\"");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.UnitPrice).HasPrecision(18, 6);
        builder.Property(x => x.EffectiveFrom).HasColumnType("date");
        builder.Property(x => x.EffectiveTo).HasColumnType("date");
        builder.Property(x => x.CreatedAtUtc).HasDefaultValueSql("CURRENT_TIMESTAMP");
        builder.Property(x => x.UpdatedAtUtc).HasDefaultValueSql("CURRENT_TIMESTAMP");
        builder.HasIndex(x => new { x.SourcingOptionId, x.EffectiveFrom, x.EffectiveTo });
        builder.HasOne(x => x.SourcingOption).WithMany(x => x.Prices).HasForeignKey(x => x.SourcingOptionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Currency).WithMany().HasForeignKey(x => x.CurrencyId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class LeadTimeRecommendationApplicationConfiguration
    : IEntityTypeConfiguration<LeadTimeRecommendationApplication>
{
    public void Configure(EntityTypeBuilder<LeadTimeRecommendationApplication> builder)
    {
        builder.ToTable("lead_time_recommendation_applications", "procurement", table =>
        {
            table.HasCheckConstraint("ck_lead_time_application_stage",
                "\"StageCode\" IN ('SUPPLIER','FORWARDER_PRE_SHIPMENT','SHIPPING','DESTINATION_CUSTOMS')");
            table.HasCheckConstraint("ck_lead_time_application_values",
                "\"RecommendedDays\" >= 0 AND \"AppliedDays\" >= 0 AND \"SampleCount\" >= 5");
            table.HasCheckConstraint("ck_lead_time_application_window",
                "\"WindowEndInclusive\" > \"WindowStartExclusive\"");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.StageCode).HasMaxLength(32).IsRequired();
        builder.Property(x => x.Method).HasMaxLength(64).IsRequired();
        builder.Property(x => x.Reason).HasMaxLength(1000);
        builder.Property(x => x.ActorIdentifier).HasMaxLength(200);
        builder.Property(x => x.WindowStartExclusive).HasColumnType("date");
        builder.Property(x => x.WindowEndInclusive).HasColumnType("date");
        builder.Property(x => x.AppliedAtUtc).HasDefaultValueSql("CURRENT_TIMESTAMP");
        builder.HasIndex(x => new { x.SourcingOptionId, x.AppliedAtUtc });
        builder.HasOne(x => x.SourcingOption).WithMany().HasForeignKey(x => x.SourcingOptionId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
