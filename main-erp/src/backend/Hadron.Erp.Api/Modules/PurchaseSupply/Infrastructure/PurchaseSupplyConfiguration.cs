using Hadron.Erp.Api.Modules.PurchaseSupply.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Hadron.Erp.Api.Modules.OrderPlanning.Domain;
using Hadron.Erp.Api.Modules.Mrp.Domain;

namespace Hadron.Erp.Api.Modules.PurchaseSupply.Infrastructure;

public sealed class PurchaseOrderConfiguration : IEntityTypeConfiguration<PurchaseOrder>
{
    public void Configure(EntityTypeBuilder<PurchaseOrder> builder)
    {
        builder.ToTable("purchase_orders", "procurement", table =>
        {
            table.HasCheckConstraint("ck_purchase_orders_number", "\"PurchaseOrderNumber\" ~ '^PO-[0-9]{6,}$'");
            table.HasCheckConstraint("ck_purchase_orders_status",
                "\"Status\" IN ('DRAFT','APPROVED','ORDERED','SHIPPED','CUSTOMS','PARTIALLY_RECEIVED','RECEIVED','CANCELLED')");
            table.HasCheckConstraint("ck_purchase_orders_payment_percentages",
                "\"AdvancePercent\" BETWEEN 0 AND 100 AND \"BeforeShipmentPercent\" BETWEEN 0 AND 100 AND \"CreditPercent\" BETWEEN 0 AND 100 AND \"AdvancePercent\" + \"BeforeShipmentPercent\" + \"CreditPercent\" = 100");
            table.HasCheckConstraint("ck_purchase_orders_credit",
                "(\"CreditPercent\" = 0 AND \"CreditDays\" IS NULL AND \"CreditAnchor\" IS NULL) OR (\"CreditPercent\" > 0 AND \"CreditDays\" > 0 AND \"CreditAnchor\" IN ('ORDERED','SHIPPED','WAREHOUSE_RECEIPT'))");
            table.HasCheckConstraint("ck_purchase_orders_ordered_fields",
                "(\"Status\" IN ('DRAFT','APPROVED') AND \"OrderedDate\" IS NULL AND \"OrderedAtUtc\" IS NULL) OR (\"Status\" NOT IN ('DRAFT','APPROVED') AND \"OrderedDate\" IS NOT NULL AND \"OrderedAtUtc\" IS NOT NULL)");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.PurchaseOrderNumber).HasMaxLength(32).IsRequired();
        builder.HasIndex(x => x.PurchaseOrderNumber).IsUnique();
        builder.Property(x => x.Status).HasMaxLength(32).IsRequired();
        builder.Property(x => x.AdvancePercent).HasPrecision(5, 2);
        builder.Property(x => x.BeforeShipmentPercent).HasPrecision(5, 2);
        builder.Property(x => x.CreditPercent).HasPrecision(5, 2);
        builder.Property(x => x.CreditAnchor).HasMaxLength(32);
        builder.Property(x => x.Notes).HasMaxLength(2000);
        builder.Property(x => x.CreationSource).HasMaxLength(32).IsRequired();
        builder.Property(x => x.Version).IsConcurrencyToken();
        builder.Property(x => x.CreatedAtUtc).HasDefaultValueSql("CURRENT_TIMESTAMP");
        builder.Property(x => x.UpdatedAtUtc).HasDefaultValueSql("CURRENT_TIMESTAMP");
        builder.HasIndex(x => new { x.Status, x.SupplierId });
        builder.HasIndex(x => new { x.Status, x.CurrencyId });
        builder.HasOne(x => x.Supplier).WithMany().HasForeignKey(x => x.SupplierId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Currency).WithMany().HasForeignKey(x => x.CurrencyId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.PaymentTerm).WithMany().HasForeignKey(x => x.PaymentTermId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.ShippingMode).WithMany().HasForeignKey(x => x.ShippingModeId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Incoterm).WithMany().HasForeignKey(x => x.IncotermId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class PurchaseOrderLineConfiguration : IEntityTypeConfiguration<PurchaseOrderLine>
{
    public void Configure(EntityTypeBuilder<PurchaseOrderLine> builder)
    {
        builder.ToTable("purchase_order_lines", "procurement", table =>
        {
            table.HasCheckConstraint("ck_purchase_order_lines_number", "\"LineNumber\" > 0");
            table.HasCheckConstraint("ck_purchase_order_lines_quantity", "\"OrderedQuantity\" > 0");
            table.HasCheckConstraint("ck_purchase_order_lines_price", "\"UnitPrice\" >= 0");
            table.HasCheckConstraint("ck_purchase_order_lines_moq", "\"MinimumOrderQuantity\" IS NULL OR \"MinimumOrderQuantity\" >= 0");
            table.HasCheckConstraint("ck_purchase_order_lines_lead_times",
                "(\"SupplierLeadTimeDays\" IS NULL OR \"SupplierLeadTimeDays\" >= 0) AND (\"ForwarderToShipmentDays\" IS NULL OR \"ForwarderToShipmentDays\" >= 0) AND (\"ShippingPeriodDays\" IS NULL OR \"ShippingPeriodDays\" >= 0) AND (\"DestinationCustomsDays\" IS NULL OR \"DestinationCustomsDays\" >= 0)");
        });
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => new { x.PurchaseOrderId, x.LineNumber }).IsUnique();
        builder.Property(x => x.OrderedQuantity).HasPrecision(18, 6);
        builder.Property(x => x.UnitPrice).HasPrecision(18, 6);
        builder.Property(x => x.MinimumOrderQuantity).HasPrecision(18, 6);
        builder.Property(x => x.Notes).HasMaxLength(1000);
        builder.HasIndex(x => x.ExpertDecisionId).IsUnique().HasFilter("\"ExpertDecisionId\" IS NOT NULL");
        builder.Property(x => x.CreatedAtUtc).HasDefaultValueSql("CURRENT_TIMESTAMP");
        builder.Property(x => x.UpdatedAtUtc).HasDefaultValueSql("CURRENT_TIMESTAMP");
        builder.Ignore(x => x.LineTotal);
        builder.HasOne(x => x.PurchaseOrder).WithMany(x => x.Lines).HasForeignKey(x => x.PurchaseOrderId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Item).WithMany().HasForeignKey(x => x.ItemId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.SourcingOption).WithMany().HasForeignKey(x => x.SourcingOptionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.SourcingPrice).WithMany().HasForeignKey(x => x.SourcingPriceId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.ItemBaseUnit).WithMany().HasForeignKey(x => x.ItemBaseUnitId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<OrderRecommendationDecision>().WithMany().HasForeignKey(x => x.ExpertDecisionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<OrderRecommendation>().WithMany().HasForeignKey(x => x.RecommendationId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<OrderPlanningGeneration>().WithMany().HasForeignKey(x => x.OrderPlanningGenerationId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<MrpRun>().WithMany().HasForeignKey(x => x.MrpRunId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class DeliveryScheduleConfiguration : IEntityTypeConfiguration<DeliverySchedule>
{
    public void Configure(EntityTypeBuilder<DeliverySchedule> builder)
    {
        builder.ToTable("delivery_schedules", "procurement", table =>
        {
            table.HasCheckConstraint("ck_delivery_schedules_number", "\"ScheduleNumber\" > 0");
            table.HasCheckConstraint("ck_delivery_schedules_quantity", "\"AllocatedQuantity\" > 0");
        });
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => new { x.PurchaseOrderLineId, x.ScheduleNumber }).IsUnique();
        builder.Property(x => x.AllocatedQuantity).HasPrecision(18, 6);
        builder.Property(x => x.CreatedAtUtc).HasDefaultValueSql("CURRENT_TIMESTAMP");
        builder.Property(x => x.UpdatedAtUtc).HasDefaultValueSql("CURRENT_TIMESTAMP");
        builder.HasOne(x => x.PurchaseOrderLine).WithMany(x => x.Schedules).HasForeignKey(x => x.PurchaseOrderLineId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class DeliverySchedulePlanRevisionConfiguration : IEntityTypeConfiguration<DeliverySchedulePlanRevision>
{
    public void Configure(EntityTypeBuilder<DeliverySchedulePlanRevision> builder)
    {
        builder.ToTable("delivery_schedule_plan_revisions", "procurement", table =>
        {
            table.HasCheckConstraint("ck_delivery_plan_type", "\"PlanType\" IN ('ORIGINAL','REVISED')");
            table.HasCheckConstraint("ck_delivery_plan_revision", "\"RevisionNumber\" > 0");
            table.HasCheckConstraint("ck_delivery_plan_reason", "\"PlanType\" = 'ORIGINAL' OR length(btrim(\"Reason\")) > 0");
            table.HasCheckConstraint("ck_delivery_plan_revision_kind",
                "(\"PlanType\" = 'ORIGINAL' AND \"RevisionNumber\" = 1) OR (\"PlanType\" = 'REVISED' AND \"RevisionNumber\" <> 1)");
            table.HasCheckConstraint("ck_delivery_plan_core_chronology",
                "(\"SupplierReadyDate\" IS NULL OR \"ShippedDate\" IS NULL OR \"SupplierReadyDate\" <= \"ShippedDate\") AND (\"ShippedDate\" IS NULL OR \"ArrivedDestinationDate\" IS NULL OR \"ShippedDate\" <= \"ArrivedDestinationDate\") AND (\"ArrivedDestinationDate\" IS NULL OR \"WarehouseReceiptDate\" IS NULL OR \"ArrivedDestinationDate\" <= \"WarehouseReceiptDate\")");
        });
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => new { x.DeliveryScheduleId, x.RevisionNumber }).IsUnique();
        builder.HasIndex(x => x.DeliveryScheduleId).IsUnique().HasFilter("\"PlanType\" = 'ORIGINAL'")
            .HasDatabaseName("ux_delivery_schedule_single_original");
        builder.Property(x => x.PlanType).HasMaxLength(16).IsRequired();
        builder.Property(x => x.Reason).HasMaxLength(1000);
        builder.Property(x => x.ActorIdentifier).HasMaxLength(200);
        builder.HasOne(x => x.DeliverySchedule).WithMany(x => x.PlanRevisions).HasForeignKey(x => x.DeliveryScheduleId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class DeliveryScheduleActualMilestoneConfiguration : IEntityTypeConfiguration<DeliveryScheduleActualMilestone>
{
    public void Configure(EntityTypeBuilder<DeliveryScheduleActualMilestone> builder)
    {
        builder.ToTable("delivery_schedule_actual_milestones", "procurement", table =>
        {
            table.HasCheckConstraint("ck_delivery_actual_milestone",
                "\"MilestoneCode\" IN ('SUPPLIER_READY','HANDOVER_TO_FORWARDER','SHIPPED','ARRIVED_DESTINATION','CUSTOMS_CLEARED','WAREHOUSE_RECEIPT')");
            table.HasCheckConstraint("ck_delivery_actual_source", "length(btrim(\"SourceType\")) > 0");
        });
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => new { x.DeliveryScheduleId, x.MilestoneCode, x.SourceType, x.SourceId }).IsUnique();
        builder.Property(x => x.MilestoneCode).HasMaxLength(40).IsRequired();
        builder.Property(x => x.SourceType).HasMaxLength(60).IsRequired();
        builder.Property(x => x.ActorIdentifier).HasMaxLength(200);
        builder.Property(x => x.Reason).HasMaxLength(1000);
        builder.HasIndex(x => x.SupersedesMilestoneId).IsUnique().HasFilter("\"SupersedesMilestoneId\" IS NOT NULL");
        builder.HasOne(x => x.DeliverySchedule).WithMany(x => x.ActualMilestones).HasForeignKey(x => x.DeliveryScheduleId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.SupersedesMilestone).WithMany().HasForeignKey(x => x.SupersedesMilestoneId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class PurchaseReceiptConfiguration : IEntityTypeConfiguration<PurchaseReceipt>
{
    public void Configure(EntityTypeBuilder<PurchaseReceipt> b)
    {
        b.ToTable("purchase_receipts", "procurement", t =>
        {
            t.HasCheckConstraint("ck_purchase_receipt_number", "\"ReceiptNumber\" ~ '^RCV-[0-9]{6,}$'");
            t.HasCheckConstraint("ck_purchase_receipt_status", "\"Status\" IN ('DRAFT','POSTED','REVERSED')");
        });
        b.HasKey(x => x.Id); b.Property(x => x.ReceiptNumber).HasMaxLength(32).IsRequired();
        b.HasIndex(x => x.ReceiptNumber).IsUnique(); b.Property(x => x.Status).HasMaxLength(16).IsRequired();
        b.Property(x => x.Notes).HasMaxLength(2000); b.Property(x => x.ReversalReason).HasMaxLength(1000);
        b.Property(x => x.ActorIdentifier).HasMaxLength(200); b.Property(x => x.Version).IsConcurrencyToken();
        b.Property(x => x.CreatedAtUtc).HasDefaultValueSql("CURRENT_TIMESTAMP");
        b.Property(x => x.UpdatedAtUtc).HasDefaultValueSql("CURRENT_TIMESTAMP");
        b.HasOne(x => x.PurchaseOrder).WithMany().HasForeignKey(x => x.PurchaseOrderId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Warehouse).WithMany().HasForeignKey(x => x.WarehouseId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.PurchaseOrderId, x.Status });
    }
}

public sealed class PurchaseReceiptLineConfiguration : IEntityTypeConfiguration<PurchaseReceiptLine>
{
    public void Configure(EntityTypeBuilder<PurchaseReceiptLine> b)
    {
        b.ToTable("purchase_receipt_lines", "procurement", t =>
            t.HasCheckConstraint("ck_purchase_receipt_line_qty", "\"Quantity\" > 0"));
        b.HasKey(x => x.Id); b.Property(x => x.Quantity).HasPrecision(18, 6);
        b.Property(x => x.CreatedAtUtc).HasDefaultValueSql("CURRENT_TIMESTAMP");
        b.HasOne(x => x.Receipt).WithMany(x => x.Lines).HasForeignKey(x => x.ReceiptId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.PurchaseOrderLine).WithMany().HasForeignKey(x => x.PurchaseOrderLineId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.DeliverySchedule).WithMany(x => x.ReceiptLines).HasForeignKey(x => x.DeliveryScheduleId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Item).WithMany().HasForeignKey(x => x.ItemId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Unit).WithMany().HasForeignKey(x => x.UnitId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.InventoryTransaction).WithMany().HasForeignKey(x => x.InventoryTransactionId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.ReversalInventoryTransaction).WithMany().HasForeignKey(x => x.ReversalInventoryTransactionId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => x.InventoryTransactionId).IsUnique().HasFilter("\"InventoryTransactionId\" IS NOT NULL");
        b.HasIndex(x => x.ReversalInventoryTransactionId).IsUnique().HasFilter("\"ReversalInventoryTransactionId\" IS NOT NULL");
    }
}

public sealed class PurchaseScheduleCancellationConfiguration : IEntityTypeConfiguration<PurchaseScheduleCancellation>
{
    public void Configure(EntityTypeBuilder<PurchaseScheduleCancellation> b)
    {
        b.ToTable("purchase_schedule_cancellations", "procurement", t =>
            t.HasCheckConstraint("ck_purchase_cancellation_qty", "\"Quantity\" > 0"));
        b.HasKey(x => x.Id); b.Property(x => x.Quantity).HasPrecision(18, 6);
        b.Property(x => x.Reason).HasMaxLength(1000).IsRequired(); b.Property(x => x.ActorIdentifier).HasMaxLength(200);
        b.HasOne(x => x.PurchaseOrder).WithMany().HasForeignKey(x => x.PurchaseOrderId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.PurchaseOrderLine).WithMany().HasForeignKey(x => x.PurchaseOrderLineId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.DeliverySchedule).WithMany(x => x.Cancellations).HasForeignKey(x => x.DeliveryScheduleId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class DeliveryScheduleAllocationEventConfiguration : IEntityTypeConfiguration<DeliveryScheduleAllocationEvent>
{
    public void Configure(EntityTypeBuilder<DeliveryScheduleAllocationEvent> b)
    {
        b.ToTable("delivery_schedule_allocation_events", "procurement"); b.HasKey(x => x.Id);
        b.Property(x => x.BeforeJson).HasColumnType("jsonb"); b.Property(x => x.AfterJson).HasColumnType("jsonb");
        b.Property(x => x.Reason).HasMaxLength(1000).IsRequired(); b.Property(x => x.ActorIdentifier).HasMaxLength(200);
        b.HasIndex(x => new { x.PurchaseOrderId, x.CreatedAtUtc });
    }
}

public sealed class PurchaseOrderLifecycleEventConfiguration : IEntityTypeConfiguration<PurchaseOrderLifecycleEvent>
{
    public void Configure(EntityTypeBuilder<PurchaseOrderLifecycleEvent> builder)
    {
        builder.ToTable("purchase_order_lifecycle_events", "procurement");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.FromStatus).HasMaxLength(32).IsRequired();
        builder.Property(x => x.ToStatus).HasMaxLength(32).IsRequired();
        builder.Property(x => x.Reason).HasMaxLength(1000);
        builder.Property(x => x.ActorIdentifier).HasMaxLength(200);
        builder.HasIndex(x => new { x.PurchaseOrderId, x.CreatedAtUtc });
        builder.HasOne(x => x.PurchaseOrder).WithMany(x => x.LifecycleEvents).HasForeignKey(x => x.PurchaseOrderId).OnDelete(DeleteBehavior.Restrict);
    }
}
