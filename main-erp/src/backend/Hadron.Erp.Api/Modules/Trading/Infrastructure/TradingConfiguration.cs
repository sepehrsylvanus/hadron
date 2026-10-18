using Hadron.Erp.Api.Modules.Trading.Domain;
using Hadron.Erp.Api.Modules.Identity.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Hadron.Erp.Api.Modules.Trading.Infrastructure;

internal static class TradingConfigurationSupport
{
    public static void Audit<TEntity>(EntityTypeBuilder<TEntity> b) where TEntity : class
    {
        b.Property<DateTime>("CreatedAtUtc").HasDefaultValueSql("CURRENT_TIMESTAMP");
        b.Property<DateTime>("UpdatedAtUtc").HasDefaultValueSql("CURRENT_TIMESTAMP");
        b.Property<long>("Version").IsConcurrencyToken();
    }
}

internal sealed class TradeReferenceValueConfiguration : IEntityTypeConfiguration<TradeReferenceValue>
{
    public void Configure(EntityTypeBuilder<TradeReferenceValue> b)
    {
        b.ToTable("reference_values", "trading", t => t.HasCheckConstraint("ck_trade_reference_type", "\"ReferenceType\" IN ('CUSTOMER','BUSINESS_CARD','BANK','FORWARDER','CARRIER','DESTINATION','INSURANCE_COMPANY','INSPECTION_COMPANY','CUSTOMS_OFFICE','CUSTOMS_BROKER')"));
        b.HasKey(x => x.Id); b.Property(x => x.ReferenceType).HasMaxLength(40).IsRequired(); b.Property(x => x.Code).HasMaxLength(40).IsRequired();
        b.Property(x => x.NameFa).HasMaxLength(200).IsRequired(); b.Property(x => x.NameEn).HasMaxLength(200); TradingConfigurationSupport.Audit(b);
        b.HasIndex(x => new { x.ReferenceType, x.Code }).IsUnique(); b.HasIndex(x => new { x.ReferenceType, x.IsActive, x.SortOrder });
    }
}

internal sealed class TradeProjectConfiguration : IEntityTypeConfiguration<TradeProject>
{
    public void Configure(EntityTypeBuilder<TradeProject> b)
    {
        b.ToTable("projects", "trading", t => { t.HasCheckConstraint("ck_trade_project_status", "\"Status\" IN ('DRAFT','ACTIVE','WAITING_ORDER','FX_PROCESS','READY_TO_SHIP','IN_TRANSIT','IN_CUSTOMS','CLEARED','DELIVERED','CLOSED','CANCELLED')"); t.HasCheckConstraint("ck_trade_project_priority", "\"Priority\" IN ('LOW','NORMAL','HIGH','CRITICAL')"); });
        b.HasKey(x => x.Id); b.Property(x => x.ProjectNumber).HasMaxLength(32).IsRequired(); b.Property(x => x.TitleFa).HasMaxLength(250).IsRequired(); b.Property(x => x.TitleEn).HasMaxLength(250);
        b.Property(x => x.CustomerCodeSnapshot).HasMaxLength(40); b.Property(x => x.CustomerNameFaSnapshot).HasMaxLength(200); b.Property(x => x.CustomerNameEnSnapshot).HasMaxLength(200);
        b.Property(x => x.SupplierNumberSnapshot).HasMaxLength(40); b.Property(x => x.SupplierNameSnapshot).HasMaxLength(200); b.Property(x => x.Priority).HasMaxLength(16).IsRequired(); b.Property(x => x.Status).HasMaxLength(32).IsRequired(); b.Property(x => x.Notes).HasMaxLength(4000);
        b.Property(x => x.StartDate).HasColumnType("date");
        b.Property(x => x.BusinessCardCodeSnapshot).HasMaxLength(40);
        b.Property(x => x.BusinessCardNameFaSnapshot).HasMaxLength(200);
        b.Property(x => x.BusinessCardNameEnSnapshot).HasMaxLength(200);
        b.Property(x => x.ResponsibleUserNameSnapshot).HasMaxLength(500);
        b.HasOne<TradeReferenceValue>().WithMany().HasForeignKey(x => x.BusinessCardId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.ResponsibleUserId).OnDelete(DeleteBehavior.Restrict);
        TradingConfigurationSupport.Audit(b); b.HasIndex(x => x.ProjectNumber).IsUnique(); b.HasIndex(x => new { x.Status, x.Priority, x.TargetDate }); b.HasIndex(x => x.CustomerId); b.HasIndex(x => x.SupplierId);
    }
}

internal sealed class TradeOrderRegistrationConfiguration : IEntityTypeConfiguration<TradeOrderRegistration>
{
    public void Configure(EntityTypeBuilder<TradeOrderRegistration> b)
    {
        b.ToTable("order_registrations", "trading", t => { t.HasCheckConstraint("ck_trade_order_amount", "\"RegisteredAmount\" > 0"); t.HasCheckConstraint("ck_trade_order_status", "\"Status\" IN ('DRAFT','SUBMITTED','ACTIVE','SUSPENDED','CANCELLED')"); }); b.HasKey(x => x.Id);
        b.Property(x => x.RegistrationNumber).HasMaxLength(80).IsRequired(); b.Property(x => x.Status).HasMaxLength(24).IsRequired(); b.Property(x => x.RegisteredAmount).HasPrecision(24, 6);
        b.Property(x => x.CurrencyCodeSnapshot).HasMaxLength(16).IsRequired(); b.Property(x => x.CurrencyNameFaSnapshot).HasMaxLength(100).IsRequired(); b.Property(x => x.CurrencyNameEnSnapshot).HasMaxLength(100); b.Property(x => x.Notes).HasMaxLength(2000);
        TradingConfigurationSupport.Audit(b); b.HasOne(x => x.Project).WithMany(x => x.OrderRegistrations).HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => x.RegistrationNumber).IsUnique(); b.HasIndex(x => new { x.ProjectId, x.Status });
    }
}

internal sealed class TradeFxAllocationConfiguration : IEntityTypeConfiguration<TradeFxAllocation>
{
    public void Configure(EntityTypeBuilder<TradeFxAllocation> b)
    {
        b.ToTable("fx_allocations", "trading", t => { t.HasCheckConstraint("ck_trade_fx_amount", "\"Amount\" > 0"); t.HasCheckConstraint("ck_trade_fx_process", "\"ProcessType\" IN ('REQUEST','ALLOCATION','DEAL','RECEIPT','CORRECTION')"); t.HasCheckConstraint("ck_trade_fx_deal", "\"DealStatus\" IN ('NOT_STARTED','IN_QUEUE','PRIORITY_CONFIRMED','ALLOCATED','DEAL_DONE','CANCELLED')"); t.HasCheckConstraint("ck_trade_fx_receipt", "\"ReceiptStatus\" IN ('NOT_RECEIVED','PARTIAL','COMPLETE','CANCELLED')"); t.HasCheckConstraint("ck_trade_fx_dates", "\"AllocationDate\" IS NULL OR \"AllocationDate\" >= \"RequestDate\""); }); b.HasKey(x => x.Id);
        b.Property(x => x.ProcessType).HasMaxLength(24).IsRequired(); b.Property(x => x.DealStatus).HasMaxLength(32).IsRequired(); b.Property(x => x.ReceiptStatus).HasMaxLength(24).IsRequired(); b.Property(x => x.Amount).HasPrecision(24, 6);
        b.Property(x => x.CurrencyCodeSnapshot).HasMaxLength(16).IsRequired(); b.Property(x => x.CurrencyNameFaSnapshot).HasMaxLength(100).IsRequired(); b.Property(x => x.CurrencyNameEnSnapshot).HasMaxLength(100);
        b.Property(x => x.BankCodeSnapshot).HasMaxLength(40); b.Property(x => x.BankNameFaSnapshot).HasMaxLength(200); b.Property(x => x.BankNameEnSnapshot).HasMaxLength(200); b.Property(x => x.ReferenceNumber).HasMaxLength(100); b.Property(x => x.Notes).HasMaxLength(2000);
        TradingConfigurationSupport.Audit(b); b.HasOne(x => x.Project).WithMany(x => x.FxAllocations).HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Restrict); b.HasOne(x => x.OrderRegistration).WithMany().HasForeignKey(x => x.OrderRegistrationId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.ProjectId, x.DealStatus, x.RequestDate });
    }
}

internal sealed class TradeShipmentConfiguration : IEntityTypeConfiguration<TradeShipment>
{
    public void Configure(EntityTypeBuilder<TradeShipment> b)
    {
        b.ToTable("shipments", "trading", t => { t.HasCheckConstraint("ck_trade_shipment_weight", "\"GrossWeight\" IS NULL OR \"GrossWeight\" >= 0"); t.HasCheckConstraint("ck_trade_shipment_volume", "\"VolumeCbm\" IS NULL OR \"VolumeCbm\" >= 0"); t.HasCheckConstraint("ck_trade_shipment_status", "\"Status\" IN ('DRAFT','WAITING_LINK','READY','IN_TRANSIT','ARRIVED','IN_CUSTOMS','PARTIAL_CLEARED','CLEARED','DELIVERED','CANCELLED')"); t.HasCheckConstraint("ck_trade_shipment_mode", "\"TransportMode\" IN ('SEA','AIR','ROAD','RAIL','MULTIMODAL')"); t.HasCheckConstraint("ck_trade_shipment_estimated_dates", "\"EstimatedArrival\" IS NULL OR \"EstimatedDeparture\" IS NULL OR \"EstimatedArrival\" >= \"EstimatedDeparture\""); t.HasCheckConstraint("ck_trade_shipment_actual_dates", "\"ActualArrival\" IS NULL OR \"ActualDeparture\" IS NULL OR \"ActualArrival\" >= \"ActualDeparture\""); }); b.HasKey(x => x.Id);
        b.Property(x => x.ShipmentNumber).HasMaxLength(32).IsRequired(); b.Property(x => x.Status).HasMaxLength(32).IsRequired(); b.Property(x => x.TransportMode).HasMaxLength(24).IsRequired();
        b.Property(x => x.ForwarderCodeSnapshot).HasMaxLength(40); b.Property(x => x.ForwarderNameFaSnapshot).HasMaxLength(200); b.Property(x => x.ForwarderNameEnSnapshot).HasMaxLength(200); b.Property(x => x.Origin).HasMaxLength(200).IsRequired(); b.Property(x => x.Destination).HasMaxLength(200).IsRequired();
        b.Property(x => x.GrossWeight).HasPrecision(24, 6); b.Property(x => x.VolumeCbm).HasPrecision(24, 6); b.Property(x => x.Notes).HasMaxLength(2000); TradingConfigurationSupport.Audit(b);
        b.HasIndex(x => x.ShipmentNumber).IsUnique(); b.HasIndex(x => new { x.Status, x.EstimatedArrival });
    }
}

internal sealed class TradeShipmentLegConfiguration : IEntityTypeConfiguration<TradeShipmentLeg>
{
    public void Configure(EntityTypeBuilder<TradeShipmentLeg> b)
    {
        b.ToTable("shipment_legs", "trading", t => { t.HasCheckConstraint("ck_trade_shipment_leg_sequence", "\"Sequence\" > 0"); t.HasCheckConstraint("ck_trade_leg_status", "\"Status\" IN ('PLANNED','DEPARTED','IN_TRANSIT','ARRIVED','DELAYED','CANCELLED')"); t.HasCheckConstraint("ck_trade_leg_mode", "\"TransportMode\" IN ('SEA','AIR','ROAD','RAIL','MULTIMODAL')"); t.HasCheckConstraint("ck_trade_leg_estimated_dates", "\"EstimatedArrival\" IS NULL OR \"EstimatedDeparture\" IS NULL OR \"EstimatedArrival\" >= \"EstimatedDeparture\""); t.HasCheckConstraint("ck_trade_leg_actual_dates", "\"ActualArrival\" IS NULL OR \"ActualDeparture\" IS NULL OR \"ActualArrival\" >= \"ActualDeparture\""); }); b.HasKey(x => x.Id);
        b.Property(x => x.FromLocation).HasMaxLength(200).IsRequired(); b.Property(x => x.ToLocation).HasMaxLength(200).IsRequired(); b.Property(x => x.TransportMode).HasMaxLength(24).IsRequired(); b.Property(x => x.Status).HasMaxLength(24).IsRequired(); b.Property(x => x.TrackingNumber).HasMaxLength(100); b.Property(x => x.Notes).HasMaxLength(2000);
        TradingConfigurationSupport.Audit(b); b.HasOne(x => x.Shipment).WithMany(x => x.Legs).HasForeignKey(x => x.ShipmentId).OnDelete(DeleteBehavior.Restrict); b.HasIndex(x => new { x.ShipmentId, x.Sequence }).IsUnique();
    }
}

internal sealed class TradeProjectShipmentConfiguration : IEntityTypeConfiguration<TradeProjectShipment>
{
    public void Configure(EntityTypeBuilder<TradeProjectShipment> b)
    {
        b.ToTable("project_shipments", "trading", t => { t.HasCheckConstraint("ck_trade_link_method", "\"AllocationMethod\" IN ('GOODS_VALUE','CBM','WEIGHT','COUNT','MANUAL_PERCENT','EQUAL')"); t.HasCheckConstraint("ck_trade_link_value", "\"AllocationValue\" >= 0"); }); b.HasKey(x => x.Id);
        b.Property(x => x.AllocationMethod).HasMaxLength(24).IsRequired(); b.Property(x => x.AllocationValue).HasPrecision(24, 6); b.Property(x => x.Notes).HasMaxLength(2000); TradingConfigurationSupport.Audit(b);
        b.HasOne(x => x.Project).WithMany(x => x.ShipmentLinks).HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Restrict); b.HasOne(x => x.Shipment).WithMany(x => x.ProjectLinks).HasForeignKey(x => x.ShipmentId).OnDelete(DeleteBehavior.Restrict); b.HasIndex(x => new { x.ProjectId, x.ShipmentId }).IsUnique();
    }
}

internal sealed class TradeCustomsClearanceConfiguration : IEntityTypeConfiguration<TradeCustomsClearance>
{
    public void Configure(EntityTypeBuilder<TradeCustomsClearance> b)
    {
        b.ToTable("customs_clearances", "trading", t => { t.HasCheckConstraint("ck_trade_customs_status", "\"Status\" IN ('NOT_STARTED','DOCUMENTS_PENDING','IN_PROGRESS','PARTIAL','COMPLETE','BLOCKED','CANCELLED')"); t.HasCheckConstraint("ck_trade_customs_type", "\"ClearanceType\" IN ('DEFINITIVE','TEMPORARY','TRANSIT','RETURN')"); t.HasCheckConstraint("ck_trade_customs_dates", "\"ReleaseDate\" IS NULL OR \"DeclarationDate\" IS NULL OR \"ReleaseDate\" >= \"DeclarationDate\""); }); b.HasKey(x => x.Id); b.Property(x => x.ClearanceNumber).HasMaxLength(80).IsRequired(); b.Property(x => x.ClearanceType).HasMaxLength(24).IsRequired(); b.Property(x => x.Status).HasMaxLength(24).IsRequired();
        b.Property(x => x.CustomsOfficeCodeSnapshot).HasMaxLength(40); b.Property(x => x.CustomsOfficeNameFaSnapshot).HasMaxLength(200); b.Property(x => x.CustomsOfficeNameEnSnapshot).HasMaxLength(200); b.Property(x => x.Notes).HasMaxLength(2000); TradingConfigurationSupport.Audit(b);
        b.HasOne(x => x.Shipment).WithMany(x => x.CustomsClearances).HasForeignKey(x => x.ShipmentId).OnDelete(DeleteBehavior.Restrict); b.HasIndex(x => x.ClearanceNumber).IsUnique(); b.HasIndex(x => new { x.ShipmentId, x.Status });
    }
}

internal sealed class TradeCostConfiguration : IEntityTypeConfiguration<TradeCost>
{
    public void Configure(EntityTypeBuilder<TradeCost> b)
    {
        b.ToTable("costs", "trading", t => { t.HasCheckConstraint("ck_trade_cost_scope", "\"ScopeType\" IN ('PROJECT','SHIPMENT','SHIPMENT_LEG','CUSTOMS')"); t.HasCheckConstraint("ck_trade_cost_amount", "\"Amount\" > 0"); }); b.HasKey(x => x.Id);
        b.Property(x => x.ScopeType).HasMaxLength(24).IsRequired(); b.Property(x => x.CostType).HasMaxLength(50).IsRequired(); b.Property(x => x.Amount).HasPrecision(24, 6); b.Property(x => x.CurrencyCodeSnapshot).HasMaxLength(16).IsRequired(); b.Property(x => x.CurrencyNameFaSnapshot).HasMaxLength(100).IsRequired(); b.Property(x => x.CurrencyNameEnSnapshot).HasMaxLength(100); b.Property(x => x.ReferenceNumber).HasMaxLength(100); b.Property(x => x.Notes).HasMaxLength(2000); b.Property(x => x.VoidReason).HasMaxLength(1000); b.Property(x => x.CreatedAtUtc).HasDefaultValueSql("CURRENT_TIMESTAMP");
        b.HasOne(x => x.Project).WithMany(x => x.Costs).HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Restrict); b.HasIndex(x => new { x.ProjectId, x.CurrencyCodeSnapshot, x.IsVoided, x.EffectiveDate }); b.HasIndex(x => new { x.ScopeType, x.ScopeId });
    }
}

internal sealed class TradeAuditEventConfiguration : IEntityTypeConfiguration<TradeAuditEvent>
{
    public void Configure(EntityTypeBuilder<TradeAuditEvent> b)
    {
        b.ToTable("audit_events", "trading"); b.HasKey(x => x.Id); b.Property(x => x.EntityType).HasMaxLength(80).IsRequired(); b.Property(x => x.Action).HasMaxLength(50).IsRequired(); b.Property(x => x.PreviousJson).HasColumnType("jsonb"); b.Property(x => x.CurrentJson).HasColumnType("jsonb"); b.Property(x => x.Reason).HasMaxLength(1000); b.Property(x => x.OccurredAtUtc).HasDefaultValueSql("CURRENT_TIMESTAMP"); b.HasIndex(x => new { x.EntityType, x.EntityId, x.OccurredAtUtc });
    }
}

internal sealed class TradeReportNoteConfiguration : IEntityTypeConfiguration<TradeReportNote>
{
    public void Configure(EntityTypeBuilder<TradeReportNote> b)
    {
        b.ToTable("report_notes", "trading"); b.HasKey(x => x.Id); b.Property(x => x.Note).HasMaxLength(4000).IsRequired(); b.Property(x => x.CreatedAtUtc).HasDefaultValueSql("CURRENT_TIMESTAMP"); b.HasOne(x => x.Project).WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Restrict); b.HasIndex(x => new { x.ProjectId, x.CreatedAtUtc });
    }
}
