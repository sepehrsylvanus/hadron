using Hadron.Erp.Api.Modules.Identity.Domain;
using Hadron.Erp.Api.Modules.Mrp.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Hadron.Erp.Api.Modules.Mrp.Infrastructure;

internal sealed class PlannerWorkItemConfiguration : IEntityTypeConfiguration<PlannerWorkItem>
{
    public void Configure(EntityTypeBuilder<PlannerWorkItem> b)
    {
        b.ToTable("planner_work_items", "planning", t =>
            t.HasCheckConstraint("ck_planner_work_item_status", "\"Status\" IN ('OPEN','ACKNOWLEDGED')"));
        b.HasKey(x => x.Id); b.Property(x => x.Status).HasMaxLength(24).IsRequired();
        b.Property(x => x.CreatedByIdentifier).HasMaxLength(200).IsRequired();
        b.HasIndex(x => x.ExceptionId).IsUnique(); b.HasIndex(x => new { x.Status, x.AssignedUserId, x.UpdatedAtUtc });
        b.HasOne<MrpRun>().WithMany().HasForeignKey(x => x.RunId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<MrpRunException>().WithMany().HasForeignKey(x => x.ExceptionId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.AssignedUserId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.AcknowledgedByUserId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class PlannerWorkItemEventConfiguration : IEntityTypeConfiguration<PlannerWorkItemEvent>
{
    public void Configure(EntityTypeBuilder<PlannerWorkItemEvent> b)
    {
        b.ToTable("planner_work_item_events", "planning"); b.HasKey(x => x.Id);
        b.Property(x => x.EventType).HasMaxLength(32).IsRequired();
        b.Property(x => x.ActorIdentifier).HasMaxLength(200).IsRequired();
        b.HasIndex(x => new { x.WorkItemId, x.OccurredAtUtc });
        b.HasOne<PlannerWorkItem>().WithMany().HasForeignKey(x => x.WorkItemId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.AssignedUserId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class PlannerSavedViewConfiguration : IEntityTypeConfiguration<PlannerSavedView>
{
    public void Configure(EntityTypeBuilder<PlannerSavedView> b)
    {
        b.ToTable("planner_saved_views", "planning"); b.HasKey(x => x.Id);
        b.Property(x => x.OwnerIdentifier).HasMaxLength(200).IsRequired();
        b.Property(x => x.Name).HasMaxLength(120).IsRequired(); b.Property(x => x.NormalizedName).HasMaxLength(120).IsRequired();
        b.Property(x => x.FiltersJson).HasColumnType("jsonb").IsRequired();
        b.HasIndex(x => new { x.OwnerIdentifier, x.NormalizedName }).IsUnique();
    }
}

internal sealed class WhatIfScenarioConfiguration : IEntityTypeConfiguration<WhatIfScenario>
{
    public void Configure(EntityTypeBuilder<WhatIfScenario> b)
    {
        b.ToTable("what_if_scenarios", "planning", t =>
            t.HasCheckConstraint("ck_what_if_scenario_status", "\"Status\" = 'CALCULATED'"));
        b.HasKey(x => x.Id); b.Property(x => x.Name).HasMaxLength(160).IsRequired();
        b.Property(x => x.OwnerIdentifier).HasMaxLength(200).IsRequired();
        b.Property(x => x.Status).HasMaxLength(24).IsRequired(); b.Property(x => x.Completeness).HasMaxLength(40).IsRequired();
        b.HasIndex(x => new { x.OwnerIdentifier, x.CreatedAtUtc });
        b.HasOne<MrpRun>().WithMany().HasForeignKey(x => x.BaseRunId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal abstract class WhatIfConfiguration<T> : IEntityTypeConfiguration<T> where T : class
{
    protected static void Scenario(EntityTypeBuilder<T> b)
        => b.HasOne<WhatIfScenario>().WithMany().HasForeignKey("ScenarioId").OnDelete(DeleteBehavior.Restrict);
    public abstract void Configure(EntityTypeBuilder<T> b);
}

internal sealed class WhatIfForecastOverrideConfiguration : WhatIfConfiguration<WhatIfForecastOverride>
{
    public override void Configure(EntityTypeBuilder<WhatIfForecastOverride> b) { b.ToTable("what_if_forecast_overrides", "planning"); b.HasKey(x => x.Id); b.Quantity(x => x.Quantity); b.HasIndex(x => new { x.ScenarioId, x.ProductId, x.BusinessPeriodId }).IsUnique(); Scenario(b); }
}
internal sealed class WhatIfInventoryOverrideConfiguration : WhatIfConfiguration<WhatIfInventoryOverride>
{
    public override void Configure(EntityTypeBuilder<WhatIfInventoryOverride> b) { b.ToTable("what_if_inventory_overrides", "planning"); b.HasKey(x => x.Id); b.Quantity(x => x.OnHandQty); b.HasIndex(x => new { x.ScenarioId, x.ItemId }).IsUnique(); Scenario(b); }
}
internal sealed class WhatIfFirmSupplyOverrideConfiguration : WhatIfConfiguration<WhatIfFirmSupplyOverride>
{
    public override void Configure(EntityTypeBuilder<WhatIfFirmSupplyOverride> b) { b.ToTable("what_if_firm_supply_overrides", "planning"); b.HasKey(x => x.Id); b.Quantity(x => x.OpenQty); b.HasIndex(x => new { x.ScenarioId, x.DeliveryScheduleId }).IsUnique(); Scenario(b); }
}
internal sealed class WhatIfDailyResultConfiguration : WhatIfConfiguration<WhatIfDailyResult>
{
    public override void Configure(EntityTypeBuilder<WhatIfDailyResult> b) { b.ToTable("what_if_daily_results", "planning"); b.HasKey(x => x.Id); b.Quantity(x => x.GrossDemand); b.Quantity(x => x.FirmIncoming); b.Quantity(x => x.FirmProjected); b.Quantity(x => x.PlanningBeforeNeed); b.Quantity(x => x.NetRequirement); b.Quantity(x => x.PlanningBalance); b.Property(x => x.Completeness).HasMaxLength(40).IsRequired(); b.HasIndex(x => new { x.ScenarioId, x.ItemId, x.Date }).IsUnique(); Scenario(b); }
}
internal sealed class WhatIfItemSummaryConfiguration : WhatIfConfiguration<WhatIfItemSummary>
{
    public override void Configure(EntityTypeBuilder<WhatIfItemSummary> b) { b.ToTable("what_if_item_summaries", "planning"); b.HasKey(x => x.Id); b.Quantity(x => x.StartingOnHand); b.Quantity(x => x.TotalGrossDemand); b.Quantity(x => x.TotalFirmIncoming); b.Quantity(x => x.TotalNetRequirement); b.Property(x => x.Completeness).HasMaxLength(40).IsRequired(); b.HasIndex(x => new { x.ScenarioId, x.ItemId }).IsUnique(); Scenario(b); }
}
internal sealed class WhatIfExceptionConfiguration : WhatIfConfiguration<WhatIfException>
{
    public override void Configure(EntityTypeBuilder<WhatIfException> b) { b.ToTable("what_if_exceptions", "planning", t => t.HasCheckConstraint("ck_what_if_exception_severity", "\"Severity\" IN ('ERROR','WARNING','INFO')")); b.HasKey(x => x.Id); b.Property(x => x.Code).HasMaxLength(100).IsRequired(); b.Property(x => x.Severity).HasMaxLength(20).IsRequired(); b.Property(x => x.Message).HasMaxLength(1000).IsRequired(); b.Property(x => x.SourceReference).HasMaxLength(300); b.HasIndex(x => new { x.ScenarioId, x.ItemId, x.Date }); Scenario(b); }
}
