using Hadron.Erp.Api.Modules.Production.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Hadron.Erp.Api.Modules.Production.Infrastructure;

internal sealed class ProductionOrderConfiguration : IEntityTypeConfiguration<ProductionOrder>
{
    public void Configure(EntityTypeBuilder<ProductionOrder> builder)
    {
        builder.ToTable("orders", "production", table =>
        {
            table.HasCheckConstraint("ck_production_order_number", "\"BusinessNumber\" ~ '^MO-[0-9]{6,}$'");
            table.HasCheckConstraint("ck_production_order_status", "\"Status\" IN ('DRAFT','APPROVED','RELEASED','IN_PROGRESS','COMPLETED','CANCELLED')");
            table.HasCheckConstraint("ck_production_order_quantity", "\"PlannedQuantity\" > 0 AND \"ReportedGoodQuantity\" >= 0 AND \"ReportedRejectedQuantity\" >= 0");
            table.HasCheckConstraint("ck_production_order_dates", "\"PlannedStartDate\" <= \"PlannedEndDate\"");
            table.HasCheckConstraint("ck_production_order_version", "\"Version\" >= 1");
            table.HasCheckConstraint("ck_production_order_release_bom", "(\"Status\" IN ('DRAFT','APPROVED')) = (\"BomRevisionId\" IS NULL)");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.BusinessNumber).HasMaxLength(32).IsRequired();
        builder.Property(x => x.Status).HasMaxLength(32).IsRequired();
        builder.Property(x => x.PlannedQuantity).HasPrecision(18, 6);
        builder.Property(x => x.ReportedGoodQuantity).HasPrecision(18, 6);
        builder.Property(x => x.ReportedRejectedQuantity).HasPrecision(18, 6);
        builder.Property(x => x.Notes).HasMaxLength(2000);
        builder.Property(x => x.ActorIdentifier).HasMaxLength(200);
        builder.Property(x => x.Version).IsConcurrencyToken();
        builder.HasIndex(x => x.BusinessNumber).IsUnique();
        builder.HasIndex(x => new { x.Status, x.PlannedStartDate });
        builder.HasIndex(x => new { x.ProductId, x.PlannedStartDate });
        builder.HasOne(x => x.Product).WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.ProductBaseUnit).WithMany().HasForeignKey(x => x.ProductBaseUnitId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.DestinationWarehouse).WithMany().HasForeignKey(x => x.DestinationWarehouseId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.ForecastSeries).WithMany().HasForeignKey(x => x.ForecastSeriesId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.ForecastRevision).WithMany().HasForeignKey(x => x.ForecastRevisionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.BomRevision).WithMany().HasForeignKey(x => x.BomRevisionId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class ProductionOrderMaterialConfiguration : IEntityTypeConfiguration<ProductionOrderMaterial>
{
    public void Configure(EntityTypeBuilder<ProductionOrderMaterial> builder)
    {
        builder.ToTable("order_materials", "production", table =>
        {
            table.HasCheckConstraint("ck_production_material_quantity", "\"RequiredQuantity\" > 0 AND \"UsageQty\" > 0 AND \"BaseOutputQty\" > 0");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.RequiredQuantity).HasPrecision(18, 6);
        builder.Property(x => x.UsageQty).HasPrecision(18, 6);
        builder.Property(x => x.BaseOutputQty).HasPrecision(18, 6);
        builder.HasIndex(x => new { x.ProductionOrderId, x.ItemId }).IsUnique();
        builder.HasOne(x => x.ProductionOrder).WithMany(x => x.Materials).HasForeignKey(x => x.ProductionOrderId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.BomLine).WithMany().HasForeignKey(x => x.BomLineId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Item).WithMany().HasForeignKey(x => x.ItemId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Unit).WithMany().HasForeignKey(x => x.UnitId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class ProductionMaterialStagingConfiguration : IEntityTypeConfiguration<ProductionMaterialStaging>
{
    public void Configure(EntityTypeBuilder<ProductionMaterialStaging> builder)
    {
        builder.ToTable("material_stagings", "production", table =>
            table.HasCheckConstraint("ck_production_staging_quantity", "\"BaseQuantity\" > 0"));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.BaseQuantity).HasPrecision(18, 6);
        builder.HasIndex(x => x.InventoryTransferId).IsUnique();
        builder.HasOne(x => x.ProductionOrderMaterial).WithMany(x => x.Stagings)
            .HasForeignKey(x => x.ProductionOrderMaterialId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.InventoryTransfer).WithMany().HasForeignKey(x => x.InventoryTransferId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class ProductionReportConfiguration : IEntityTypeConfiguration<ProductionReport>
{
    public void Configure(EntityTypeBuilder<ProductionReport> builder)
    {
        builder.ToTable("reports", "production", table =>
            table.HasCheckConstraint("ck_production_report_quantity", "\"GoodQuantity\" > 0 AND \"RejectedQuantity\" >= 0"));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.GoodQuantity).HasPrecision(18, 6);
        builder.Property(x => x.RejectedQuantity).HasPrecision(18, 6);
        builder.Property(x => x.Notes).HasMaxLength(2000);
        builder.Property(x => x.ActorIdentifier).HasMaxLength(200);
        builder.HasIndex(x => new { x.ProductionOrderId, x.ProductionDate });
        builder.HasIndex(x => x.FinishedGoodsTransactionId).IsUnique();
        builder.HasOne(x => x.ProductionOrder).WithMany(x => x.Reports).HasForeignKey(x => x.ProductionOrderId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.DestinationWarehouse).WithMany().HasForeignKey(x => x.DestinationWarehouseId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.FinishedGoodsTransaction).WithMany().HasForeignKey(x => x.FinishedGoodsTransactionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.DailyActual).WithMany().HasForeignKey(x => x.DailyActualId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.DailyActualRevision).WithMany().HasForeignKey(x => x.DailyActualRevisionId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class ProductionReportMaterialConfiguration : IEntityTypeConfiguration<ProductionReportMaterial>
{
    public void Configure(EntityTypeBuilder<ProductionReportMaterial> builder)
    {
        builder.ToTable("report_materials", "production", table =>
        {
            table.HasCheckConstraint("ck_production_report_material_quantity", "\"StandardQuantity\" > 0 AND \"ActualQuantity\" > 0");
            table.HasCheckConstraint("ck_production_report_material_variance", "\"StandardQuantity\" = \"ActualQuantity\" OR NULLIF(BTRIM(\"VarianceReason\"), '') IS NOT NULL");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.StandardQuantity).HasPrecision(18, 6);
        builder.Property(x => x.ActualQuantity).HasPrecision(18, 6);
        builder.Property(x => x.VarianceReason).HasMaxLength(1000);
        builder.HasIndex(x => new { x.ProductionReportId, x.ProductionOrderMaterialId }).IsUnique();
        builder.HasIndex(x => x.InventoryTransactionId).IsUnique();
        builder.HasOne(x => x.ProductionReport).WithMany(x => x.Materials).HasForeignKey(x => x.ProductionReportId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.ProductionOrderMaterial).WithMany().HasForeignKey(x => x.ProductionOrderMaterialId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.InventoryTransaction).WithMany().HasForeignKey(x => x.InventoryTransactionId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class ProductionOrderLifecycleEventConfiguration : IEntityTypeConfiguration<ProductionOrderLifecycleEvent>
{
    public void Configure(EntityTypeBuilder<ProductionOrderLifecycleEvent> builder)
    {
        builder.ToTable("order_lifecycle_events", "production");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.FromStatus).HasMaxLength(32).IsRequired();
        builder.Property(x => x.ToStatus).HasMaxLength(32).IsRequired();
        builder.Property(x => x.Reason).HasMaxLength(1000);
        builder.Property(x => x.ActorIdentifier).HasMaxLength(200);
        builder.HasIndex(x => new { x.ProductionOrderId, x.OccurredAtUtc });
        builder.HasOne(x => x.ProductionOrder).WithMany().HasForeignKey(x => x.ProductionOrderId).OnDelete(DeleteBehavior.Restrict);
    }
}
