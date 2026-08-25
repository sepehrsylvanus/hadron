using Hadron.Erp.Api.Modules.Mrp.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Hadron.Erp.Api.Modules.Mrp.Infrastructure;

internal static class MrpConfigurationHelpers
{
    public static void Quantity<T>(this EntityTypeBuilder<T> builder, System.Linq.Expressions.Expression<Func<T, decimal>> property)
        where T : class => builder.Property(property).HasPrecision(28, 12);
}

internal sealed class MrpRunConfiguration : IEntityTypeConfiguration<MrpRun>
{
    public void Configure(EntityTypeBuilder<MrpRun> b)
    {
        b.ToTable("mrp_runs", "planning", t =>
        {
            t.HasCheckConstraint("ck_mrp_runs_number", "\"RunNumber\" ~ '^MRP-RUN-[0-9]{6,}$'");
            t.HasCheckConstraint("ck_mrp_runs_horizon", "\"HorizonEndDate\" >= \"AsOfDate\"");
            t.HasCheckConstraint("ck_mrp_runs_status", "\"Status\" IN ('RUNNING','COMPLETED','COMPLETED_WITH_EXCEPTIONS','FAILED')");
            t.HasCheckConstraint("ck_mrp_runs_completeness", "\"Completeness\" IN ('COMPLETE','COMPLETE_WITH_WARNINGS','INCOMPLETE')");
        });
        b.HasKey(x => x.Id);
        b.Property(x => x.RunNumber).HasMaxLength(40).IsRequired();
        b.Property(x => x.EngineVersion).HasMaxLength(64).IsRequired();
        b.Property(x => x.Status).HasMaxLength(40).IsRequired();
        b.Property(x => x.Completeness).HasMaxLength(40).IsRequired();
        b.Property(x => x.Assumptions).HasMaxLength(1000).IsRequired();
        b.Property(x => x.ActorIdentifier).HasMaxLength(200);
        b.Property(x => x.FailureCode).HasMaxLength(100);
        b.Property(x => x.FailureMessage).HasMaxLength(2000);
        b.HasIndex(x => x.RunNumber).IsUnique();
        b.HasIndex(x => new { x.AsOfDate, x.StartedAtUtc });
    }
}

internal sealed class MrpRunItemInputConfiguration : IEntityTypeConfiguration<MrpRunItemInput>
{
    public void Configure(EntityTypeBuilder<MrpRunItemInput> b)
    {
        b.ToTable("mrp_run_item_inputs", "planning"); b.HasKey(x => x.Id);
        b.Property(x => x.ItemCode).HasMaxLength(32).IsRequired(); b.Property(x => x.ItemType).HasMaxLength(20).IsRequired();
        b.Property(x => x.NameFa).HasMaxLength(200).IsRequired(); b.Property(x => x.NameEn).HasMaxLength(200);
        b.Property(x => x.UnitCode).HasMaxLength(50).IsRequired(); b.Property(x => x.UnitNameFa).HasMaxLength(200).IsRequired();
        b.Property(x => x.UnitNameEn).HasMaxLength(200); b.Property(x => x.SupplyMethod).HasMaxLength(20);
        b.Property(x => x.CategoryCode).HasMaxLength(50); b.Property(x => x.CategoryNameFa).HasMaxLength(200);
        b.Property(x => x.CategoryNameEn).HasMaxLength(200);
        b.Quantity(x => x.SafetyStockQty); b.Quantity(x => x.DefaultScrapPercent);
        b.HasIndex(x => new { x.RunId, x.ItemId }).IsUnique();
        b.HasOne<MrpRun>().WithMany().HasForeignKey(x => x.RunId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class MrpRunForecastInputConfiguration : IEntityTypeConfiguration<MrpRunForecastInput>
{
    public void Configure(EntityTypeBuilder<MrpRunForecastInput> b)
    {
        b.ToTable("mrp_run_forecast_inputs", "planning"); b.HasKey(x => x.Id);
        b.Quantity(x => x.DemandQty); b.Quantity(x => x.PeriodForecastQty); b.Quantity(x => x.ActualMtd);
        b.HasIndex(x => new { x.RunId, x.ProductId, x.DemandDate }).IsUnique();
        b.HasIndex(x => new { x.RunId, x.BusinessPeriodId });
        b.HasOne<MrpRun>().WithMany().HasForeignKey(x => x.RunId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class MrpRunForecastPeriodInputConfiguration : IEntityTypeConfiguration<MrpRunForecastPeriodInput>
{
    public void Configure(EntityTypeBuilder<MrpRunForecastPeriodInput> b)
    {
        b.ToTable("mrp_run_forecast_period_inputs", "planning"); b.HasKey(x => x.Id);
        b.Property(x => x.OfficialForecastQty).HasPrecision(28, 12);
        b.Quantity(x => x.ActualMtdQty); b.Property(x => x.RemainingForecastQty).HasPrecision(28, 12);
        b.Property(x => x.AllocationState).HasMaxLength(60).IsRequired();
        b.Property(x => x.ExceptionCode).HasMaxLength(100);
        b.HasIndex(x => new { x.RunId, x.ProductId, x.BusinessPeriodId }).IsUnique();
        b.HasIndex(x => new { x.RunId, x.PeriodStartDate, x.PeriodEndDate });
        b.HasOne<MrpRun>().WithMany().HasForeignKey(x => x.RunId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class MrpRunBomInputConfiguration : IEntityTypeConfiguration<MrpRunBomInput>
{
    public void Configure(EntityTypeBuilder<MrpRunBomInput> b)
    {
        b.ToTable("mrp_run_bom_inputs", "planning"); b.HasKey(x => x.Id);
        b.Property(x => x.RevisionCode).HasMaxLength(32).IsRequired();
        b.Quantity(x => x.BaseOutputQty); b.Quantity(x => x.UsageQty);
        b.Property(x => x.ScrapOverridePercent).HasPrecision(28, 12);
        b.HasIndex(x => new { x.RunId, x.ParentItemId, x.EffectiveFrom, x.EffectiveTo });
        b.HasIndex(x => new { x.RunId, x.RevisionId, x.LineId }).IsUnique();
        b.HasOne<MrpRun>().WithMany().HasForeignKey(x => x.RunId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class MrpRunInventoryInputConfiguration : IEntityTypeConfiguration<MrpRunInventoryInput>
{
    public void Configure(EntityTypeBuilder<MrpRunInventoryInput> b)
    {
        b.ToTable("mrp_run_inventory_inputs", "planning"); b.HasKey(x => x.Id);
        b.Property(x => x.WarehouseCode).HasMaxLength(32).IsRequired(); b.Quantity(x => x.OnHandQty);
        b.HasIndex(x => new { x.RunId, x.ItemId, x.WarehouseId }).IsUnique();
        b.HasOne<MrpRun>().WithMany().HasForeignKey(x => x.RunId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class MrpRunFirmSupplyInputConfiguration : IEntityTypeConfiguration<MrpRunFirmSupplyInput>
{
    public void Configure(EntityTypeBuilder<MrpRunFirmSupplyInput> b)
    {
        b.ToTable("mrp_run_firm_supply_inputs", "planning"); b.HasKey(x => x.Id);
        b.Property(x => x.PurchaseOrderNumber).HasMaxLength(32).IsRequired(); b.Quantity(x => x.OpenQty);
        b.Property(x => x.EtaSource).HasMaxLength(64).IsRequired(); b.Property(x => x.EtaStatus).HasMaxLength(20).IsRequired();
        b.Property(x => x.PurchaseOrderStatus).HasMaxLength(32).IsRequired();
        b.Property(x => x.SupplierNumber).HasMaxLength(32); b.Property(x => x.SupplierNameFa).HasMaxLength(200);
        b.Property(x => x.SupplierNameEn).HasMaxLength(200);
        b.HasIndex(x => new { x.RunId, x.DeliveryScheduleId }).IsUnique();
        b.HasIndex(x => new { x.RunId, x.ItemId, x.Eta });
        b.HasOne<MrpRun>().WithMany().HasForeignKey(x => x.RunId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class MrpRunRouteAvailabilityInputConfiguration : IEntityTypeConfiguration<MrpRunRouteAvailabilityInput>
{
    public void Configure(EntityTypeBuilder<MrpRunRouteAvailabilityInput> b)
    {
        b.ToTable("mrp_run_route_availability_inputs", "planning"); b.HasKey(x => x.Id);
        b.HasIndex(x => new { x.RunId, x.ItemId, x.DemandDate }).IsUnique();
        b.HasOne<MrpRun>().WithMany().HasForeignKey(x => x.RunId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class MrpRunExternalSourceInputConfiguration : IEntityTypeConfiguration<MrpRunExternalSourceInput>
{
    public void Configure(EntityTypeBuilder<MrpRunExternalSourceInput> b)
    {
        b.ToTable("mrp_run_external_source_inputs", "planning"); b.HasKey(x => x.Id);
        b.HasIndex(x => new { x.RunId, x.ItemId });
        b.HasIndex(x => new { x.RunId, x.SourcingOptionId, x.PriceId }).IsUnique();
        b.HasOne<MrpRun>().WithMany().HasForeignKey(x => x.RunId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class MrpRunRouteDecisionConfiguration : IEntityTypeConfiguration<MrpRunRouteDecision>
{
    public void Configure(EntityTypeBuilder<MrpRunRouteDecision> b)
    {
        b.ToTable("mrp_run_route_decisions", "planning", t =>
            t.HasCheckConstraint("ck_mrp_run_route_decision", "\"Route\" IN ('BOM','EXTERNAL')"));
        b.HasKey(x => x.Id); b.Property(x => x.Route).HasMaxLength(20).IsRequired();
        b.Property(x => x.Source).HasMaxLength(20).IsRequired();
        b.HasIndex(x => new { x.RunId, x.ItemId }).IsUnique();
        b.HasOne<MrpRun>().WithMany().HasForeignKey(x => x.RunId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class MrpRunDailyResultConfiguration : IEntityTypeConfiguration<MrpRunDailyResult>
{
    public void Configure(EntityTypeBuilder<MrpRunDailyResult> b)
    {
        b.ToTable("mrp_run_daily_results", "planning"); b.HasKey(x => x.Id);
        b.Quantity(x => x.GrossDemand); b.Quantity(x => x.FirmIncoming); b.Quantity(x => x.FirmProjected);
        b.Quantity(x => x.PlanningBeforeNeed); b.Quantity(x => x.NetRequirement); b.Quantity(x => x.PlanningBalance);
        b.Property(x => x.Completeness).HasMaxLength(40).IsRequired();
        b.HasIndex(x => new { x.RunId, x.ItemId, x.Date }).IsUnique();
        b.HasOne<MrpRun>().WithMany().HasForeignKey(x => x.RunId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class MrpRunItemSummaryConfiguration : IEntityTypeConfiguration<MrpRunItemSummary>
{
    public void Configure(EntityTypeBuilder<MrpRunItemSummary> b)
    {
        b.ToTable("mrp_run_item_summaries", "planning"); b.HasKey(x => x.Id);
        b.Quantity(x => x.StartingOnHand); b.Quantity(x => x.TotalGrossDemand); b.Quantity(x => x.TotalFirmIncoming);
        b.Quantity(x => x.TotalNetRequirement); b.Quantity(x => x.OverdueFirmQty); b.Quantity(x => x.MissingEtaFirmQty);
        b.Property(x => x.StockCoverageState).HasMaxLength(40); b.Property(x => x.FirmCoverageState).HasMaxLength(40);
        b.Property(x => x.Completeness).HasMaxLength(40).IsRequired();
        b.HasIndex(x => new { x.RunId, x.ItemId }).IsUnique();
        b.HasOne<MrpRun>().WithMany().HasForeignKey(x => x.RunId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class MrpRunExceptionConfiguration : IEntityTypeConfiguration<MrpRunException>
{
    public void Configure(EntityTypeBuilder<MrpRunException> b)
    {
        b.ToTable("mrp_run_exceptions", "planning", t =>
            t.HasCheckConstraint("ck_mrp_run_exception_severity", "\"Severity\" IN ('ERROR','WARNING','INFO')"));
        b.HasKey(x => x.Id); b.Property(x => x.Code).HasMaxLength(100).IsRequired();
        b.Property(x => x.Severity).HasMaxLength(20).IsRequired(); b.Property(x => x.Message).HasMaxLength(1000).IsRequired();
        b.Property(x => x.SourceReference).HasMaxLength(300);
        b.HasIndex(x => new { x.RunId, x.ItemId, x.Date });
        b.HasOne<MrpRun>().WithMany().HasForeignKey(x => x.RunId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class MrpRunDemandTraceConfiguration : IEntityTypeConfiguration<MrpRunDemandTrace>
{
    public void Configure(EntityTypeBuilder<MrpRunDemandTrace> b)
    {
        b.ToTable("mrp_run_demand_traces", "planning"); b.HasKey(x => x.Id);
        b.Quantity(x => x.ParentNetRequirement); b.Quantity(x => x.BaseRequirement);
        b.Quantity(x => x.ScrapPercent); b.Quantity(x => x.ChildGrossRequirement);
        b.Property(x => x.ScrapSource).HasMaxLength(30).IsRequired();
        b.HasIndex(x => new { x.RunId, x.ChildItemId, x.Date });
        b.HasOne<MrpRun>().WithMany().HasForeignKey(x => x.RunId).OnDelete(DeleteBehavior.Restrict);
    }
}
