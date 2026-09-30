using Hadron.Erp.Api.Modules.Planning.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Hadron.Erp.Api.Modules.Planning.Infrastructure;

internal sealed class ForecastPlanConfiguration : IEntityTypeConfiguration<ForecastPlan>
{
    public void Configure(EntityTypeBuilder<ForecastPlan> b)
    {
        b.ToTable("forecast_plans", "planning", t =>
        {
            t.HasCheckConstraint("ck_forecast_plans_name", "btrim(\"Name\") <> ''");
            t.HasCheckConstraint("ck_forecast_plans_start_month", "\"StartJalaliMonth\" BETWEEN 1 AND 12");
            t.HasCheckConstraint("ck_forecast_plans_end_month", "\"EndJalaliMonth\" BETWEEN 1 AND 12");
            t.HasCheckConstraint("ck_forecast_plans_dates", "\"EndDate\" >= \"StartDate\"");
        });
        b.HasKey(x => x.Id);
        b.Property(x => x.Name).HasMaxLength(200).IsRequired();
        b.Property(x => x.StartDate).HasColumnType("date");
        b.Property(x => x.EndDate).HasColumnType("date");
        b.Property(x => x.ActorIdentifier).HasMaxLength(200);
        b.Property(x => x.CreatedAtUtc).HasDefaultValueSql("CURRENT_TIMESTAMP");
        b.HasIndex(x => new { x.StartDate, x.EndDate });
        b.HasIndex(x => x.CreatedAtUtc);
    }
}

internal sealed class ForecastPlanEntryConfiguration : IEntityTypeConfiguration<ForecastPlanEntry>
{
    public void Configure(EntityTypeBuilder<ForecastPlanEntry> b)
    {
        b.ToTable("forecast_plan_entries", "planning", t =>
            t.HasCheckConstraint("ck_forecast_plan_entries_qty", "\"ForecastQty\" >= 0"));
        b.HasKey(x => x.Id);
        b.Property(x => x.ForecastQty).HasPrecision(18, 6);
        b.Property(x => x.CreatedAtUtc).HasDefaultValueSql("CURRENT_TIMESTAMP");
        b.HasIndex(x => new { x.ForecastPlanId, x.ProductId, x.BusinessPeriodId }).IsUnique()
            .HasDatabaseName("ux_forecast_plan_entry_plan_product_period");
        b.HasIndex(x => x.ForecastRevisionId);
        b.HasOne(x => x.ForecastPlan).WithMany(x => x.Entries).HasForeignKey(x => x.ForecastPlanId)
            .OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Product).WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.BusinessPeriod).WithMany().HasForeignKey(x => x.BusinessPeriodId)
            .OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.ForecastRevision).WithMany().HasForeignKey(x => x.ForecastRevisionId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class ForecastPlanEntryCorrectionConfiguration : IEntityTypeConfiguration<ForecastPlanEntryCorrection>
{
    public void Configure(EntityTypeBuilder<ForecastPlanEntryCorrection> b)
    {
        b.ToTable("forecast_plan_entry_corrections", "planning", t =>
        {
            t.HasCheckConstraint("ck_forecast_plan_entry_corrections_number", "\"RevisionNumber\" >= 2");
            t.HasCheckConstraint("ck_forecast_plan_entry_corrections_qty", "\"ForecastQty\" >= 0");
            t.HasCheckConstraint("ck_forecast_plan_entry_corrections_reason",
                "btrim(\"Reason\") <> ''");
        });
        b.HasKey(x => x.Id);
        b.Property(x => x.ForecastQty).HasPrecision(18, 6);
        b.Property(x => x.Reason).HasMaxLength(1000).IsRequired();
        b.Property(x => x.ActorIdentifier).HasMaxLength(200);
        b.Property(x => x.CreatedAtUtc).HasDefaultValueSql("CURRENT_TIMESTAMP");
        b.HasIndex(x => new { x.ForecastPlanEntryId, x.RevisionNumber }).IsUnique()
            .HasDatabaseName("ux_forecast_plan_entry_correction_revision");
        b.HasIndex(x => x.ForecastRevisionId);
        b.HasOne(x => x.ForecastPlanEntry).WithMany(x => x.Corrections)
            .HasForeignKey(x => x.ForecastPlanEntryId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.ForecastRevision).WithMany().HasForeignKey(x => x.ForecastRevisionId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class BusinessPeriodConfiguration : IEntityTypeConfiguration<BusinessPeriod>
{
    public void Configure(EntityTypeBuilder<BusinessPeriod> b)
    {
        b.ToTable("business_periods", "planning", t =>
        {
            t.HasCheckConstraint("ck_business_periods_jalali_month", "\"JalaliMonth\" BETWEEN 1 AND 12");
            t.HasCheckConstraint("ck_business_periods_dates", "\"EndDate\" >= \"StartDate\"");
            t.HasCheckConstraint("ck_business_periods_status", "\"Status\" IN ('OPEN','CLOSED')");
            t.HasCheckConstraint("ck_business_periods_version", "\"Version\" >= 1");
        });
        b.HasKey(x => x.Id);
        b.Property(x => x.StartDate).HasColumnType("date");
        b.Property(x => x.EndDate).HasColumnType("date");
        b.Property(x => x.Status).HasMaxLength(16).HasDefaultValue(BusinessPeriodStatuses.Open).IsRequired();
        b.Property(x => x.Version).IsConcurrencyToken();
        b.Property(x => x.CreatedAtUtc).HasDefaultValueSql("CURRENT_TIMESTAMP");
        b.Property(x => x.UpdatedAtUtc).HasDefaultValueSql("CURRENT_TIMESTAMP");
        b.HasIndex(x => new { x.JalaliYear, x.JalaliMonth }).IsUnique();
        b.HasIndex(x => new { x.StartDate, x.EndDate });
    }
}

internal sealed class ForecastSeriesConfiguration : IEntityTypeConfiguration<ForecastSeries>
{
    public void Configure(EntityTypeBuilder<ForecastSeries> b)
    {
        b.ToTable("forecast_series", "planning", t =>
            t.HasCheckConstraint("ck_forecast_series_version", "\"Version\" >= 1"));
        b.HasKey(x => x.Id);
        b.Property(x => x.Version).IsConcurrencyToken();
        b.Property(x => x.CreatedAtUtc).HasDefaultValueSql("CURRENT_TIMESTAMP");
        b.Property(x => x.UpdatedAtUtc).HasDefaultValueSql("CURRENT_TIMESTAMP");
        b.HasIndex(x => new { x.ProductId, x.BusinessPeriodId }).IsUnique();
        b.HasIndex(x => x.BusinessPeriodId);
        b.HasOne(x => x.Product).WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.BusinessPeriod).WithMany().HasForeignKey(x => x.BusinessPeriodId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.ProductBaseUnit).WithMany().HasForeignKey(x => x.ProductBaseUnitId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.CurrentRevision).WithMany().HasForeignKey(x => x.CurrentRevisionId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class ForecastRevisionConfiguration : IEntityTypeConfiguration<ForecastRevision>
{
    public void Configure(EntityTypeBuilder<ForecastRevision> b)
    {
        b.ToTable("forecast_revisions", "planning", t =>
        {
            t.HasCheckConstraint("ck_forecast_revisions_number", "\"RevisionNumber\" >= 1");
            t.HasCheckConstraint("ck_forecast_revisions_qty", "\"ForecastQty\" >= 0");
            t.HasCheckConstraint("ck_forecast_revisions_reason", "\"RevisionNumber\" = 1 OR (\"Reason\" IS NOT NULL AND btrim(\"Reason\") <> '')");
        });
        b.HasKey(x => x.Id);
        b.Property(x => x.ForecastQty).HasPrecision(18, 6);
        b.Property(x => x.Reason).HasMaxLength(1000);
        b.Property(x => x.ActorIdentifier).HasMaxLength(200);
        b.Property(x => x.CreatedAtUtc).HasDefaultValueSql("CURRENT_TIMESTAMP");
        b.HasIndex(x => new { x.ForecastSeriesId, x.RevisionNumber }).IsUnique();
        b.HasOne(x => x.ForecastSeries).WithMany(x => x.Revisions).HasForeignKey(x => x.ForecastSeriesId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class DailyActualConfiguration : IEntityTypeConfiguration<DailyActual>
{
    public void Configure(EntityTypeBuilder<DailyActual> b)
    {
        b.ToTable("daily_actuals", "planning", t =>
            t.HasCheckConstraint("ck_daily_actuals_version", "\"Version\" >= 1"));
        b.HasKey(x => x.Id);
        b.Property(x => x.ProductionDate).HasColumnType("date");
        b.Property(x => x.Version).IsConcurrencyToken();
        b.Property(x => x.CreatedAtUtc).HasDefaultValueSql("CURRENT_TIMESTAMP");
        b.Property(x => x.UpdatedAtUtc).HasDefaultValueSql("CURRENT_TIMESTAMP");
        b.HasIndex(x => new { x.ProductId, x.ProductionDate }).IsUnique();
        b.HasIndex(x => new { x.BusinessPeriodId, x.ProductId });
        b.HasOne(x => x.Product).WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.BusinessPeriod).WithMany().HasForeignKey(x => x.BusinessPeriodId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Unit).WithMany().HasForeignKey(x => x.UnitId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.CurrentRevision).WithMany().HasForeignKey(x => x.CurrentRevisionId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class DailyActualRevisionConfiguration : IEntityTypeConfiguration<DailyActualRevision>
{
    public void Configure(EntityTypeBuilder<DailyActualRevision> b)
    {
        b.ToTable("daily_actual_revisions", "planning", t =>
        {
            t.HasCheckConstraint("ck_daily_actual_revisions_number", "\"RevisionNumber\" >= 1");
            t.HasCheckConstraint("ck_daily_actual_revisions_qty", "\"ActualQty\" >= 0");
            t.HasCheckConstraint("ck_daily_actual_revisions_reason", "\"RevisionNumber\" = 1 OR (\"Reason\" IS NOT NULL AND btrim(\"Reason\") <> '')");
        });
        b.HasKey(x => x.Id);
        b.Property(x => x.ActualQty).HasPrecision(18, 6);
        b.Property(x => x.Reason).HasMaxLength(1000);
        b.Property(x => x.ActorIdentifier).HasMaxLength(200);
        b.Property(x => x.CreatedAtUtc).HasDefaultValueSql("CURRENT_TIMESTAMP");
        b.HasIndex(x => new { x.DailyActualId, x.RevisionNumber }).IsUnique();
        b.HasOne(x => x.DailyActual).WithMany(x => x.Revisions).HasForeignKey(x => x.DailyActualId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class PeriodLifecycleEventConfiguration : IEntityTypeConfiguration<PeriodLifecycleEvent>
{
    public void Configure(EntityTypeBuilder<PeriodLifecycleEvent> b)
    {
        b.ToTable("period_lifecycle_events", "planning", t =>
            t.HasCheckConstraint("ck_period_lifecycle_events_transition",
                "(\"FromStatus\" = 'OPEN' AND \"ToStatus\" = 'CLOSED') OR (\"FromStatus\" = 'CLOSED' AND \"ToStatus\" = 'OPEN')"));
        b.HasKey(x => x.Id);
        b.Property(x => x.FromStatus).HasMaxLength(16).IsRequired();
        b.Property(x => x.ToStatus).HasMaxLength(16).IsRequired();
        b.Property(x => x.Reason).HasMaxLength(1000);
        b.Property(x => x.ActorIdentifier).HasMaxLength(200);
        b.Property(x => x.OccurredAtUtc).HasDefaultValueSql("CURRENT_TIMESTAMP");
        b.HasIndex(x => new { x.BusinessPeriodId, x.OccurredAtUtc });
        b.HasOne(x => x.BusinessPeriod).WithMany().HasForeignKey(x => x.BusinessPeriodId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
