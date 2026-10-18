using Hadron.Erp.Api.Modules.CashFlow.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Hadron.Erp.Api.Modules.CashFlow.Infrastructure;

internal sealed class ReportingFxRateSetConfiguration : IEntityTypeConfiguration<ReportingFxRateSet>
{
    public void Configure(EntityTypeBuilder<ReportingFxRateSet> builder)
    {
        builder.ToTable("reporting_fx_rate_sets", "cash_flow", table =>
        {
            table.HasCheckConstraint("ck_reporting_fx_rate_sets_revision", "\"Revision\" >= 1");
            table.HasCheckConstraint("ck_reporting_fx_rate_sets_status",
                "\"Status\" IN ('DRAFT','PUBLISHED')");
            table.HasCheckConstraint("ck_reporting_fx_rate_sets_version", "\"Version\" >= 1");
            table.HasCheckConstraint("ck_reporting_fx_rate_sets_publish_state",
                "(\"Status\" = 'DRAFT' AND \"PublishedAtUtc\" IS NULL AND \"PublishReason\" IS NULL) OR " +
                "(\"Status\" = 'PUBLISHED' AND \"PublishedAtUtc\" IS NOT NULL AND \"PublishReason\" IS NOT NULL)");
            table.HasCheckConstraint("ck_reporting_fx_rate_sets_not_self_superseding",
                "\"SupersedesRateSetId\" IS NULL OR \"SupersedesRateSetId\" <> \"Id\"");
        });

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Status).HasMaxLength(16).IsRequired();
        builder.Property(x => x.ChangeReason).HasMaxLength(1000);
        builder.Property(x => x.RequestFingerprint).HasMaxLength(64).IsRequired();
        builder.Property(x => x.CreatedByIdentifier).HasMaxLength(200);
        builder.Property(x => x.PublishedByIdentifier).HasMaxLength(200);
        builder.Property(x => x.PublishReason).HasMaxLength(1000);
        builder.Property(x => x.Version).IsConcurrencyToken();
        builder.Property(x => x.CreatedAtUtc).HasDefaultValueSql("CURRENT_TIMESTAMP");
        builder.Property(x => x.UpdatedAtUtc).HasDefaultValueSql("CURRENT_TIMESTAMP");

        builder.HasOne(x => x.BaseCurrency).WithMany().HasForeignKey(x => x.BaseCurrencyId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.SupersedesRateSet).WithMany().HasForeignKey(x => x.SupersedesRateSetId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(x => x.Rates).WithOne(x => x.RateSet).HasForeignKey(x => x.RateSetId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => x.ClientRequestId).IsUnique();
        builder.HasIndex(x => new { x.BaseCurrencyId, x.EffectiveDate, x.Revision }).IsUnique();
        builder.HasIndex(x => new { x.BaseCurrencyId, x.EffectiveDate })
            .IsUnique().HasFilter("\"Status\" = 'DRAFT'");
        builder.HasIndex(x => x.SupersedesRateSetId).IsUnique()
            .HasFilter("\"SupersedesRateSetId\" IS NOT NULL");
        builder.HasIndex(x => new { x.BaseCurrencyId, x.Status, x.EffectiveDate, x.Revision });
    }
}

internal sealed class ReportingFxRateConfiguration : IEntityTypeConfiguration<ReportingFxRate>
{
    public void Configure(EntityTypeBuilder<ReportingFxRate> builder)
    {
        builder.ToTable("reporting_fx_rates", "cash_flow", table =>
            table.HasCheckConstraint("ck_reporting_fx_rates_positive", "\"UnitsPerUsd\" > 0"));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.UnitsPerUsd).HasPrecision(28, 12);
        builder.HasOne(x => x.Currency).WithMany().HasForeignKey(x => x.CurrencyId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.RateSetId, x.CurrencyId }).IsUnique();
        builder.HasIndex(x => x.CurrencyId);
    }
}

internal sealed class ReportingFxOperationReceiptConfiguration
    : IEntityTypeConfiguration<ReportingFxOperationReceipt>
{
    public void Configure(EntityTypeBuilder<ReportingFxOperationReceipt> builder)
    {
        builder.ToTable("reporting_fx_operation_receipts", "cash_flow", table =>
        {
            table.HasCheckConstraint("ck_reporting_fx_operation_receipts_type",
                "\"OperationType\" IN ('UPDATE_DRAFT','PUBLISH')");
            table.HasCheckConstraint("ck_reporting_fx_operation_receipts_version",
                "\"ResultVersion\" >= 1");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.OperationType).HasMaxLength(32).IsRequired();
        builder.Property(x => x.RequestFingerprint).HasMaxLength(64).IsRequired();
        builder.Property(x => x.CreatedAtUtc).HasDefaultValueSql("CURRENT_TIMESTAMP");
        builder.HasOne(x => x.RateSet).WithMany().HasForeignKey(x => x.RateSetId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.OperationType, x.ClientRequestId }).IsUnique();
        builder.HasIndex(x => x.RateSetId);
    }
}
