using Microsoft.EntityFrameworkCore;
using Hadron.Erp.Api.Modules.Catalog.Domain;
using Hadron.Erp.Api.Modules.ReferenceData.Domain;
using Hadron.Erp.Api.Modules.Products.Domain;
using Hadron.Erp.Api.Modules.Suppliers.Domain;
using Hadron.Erp.Api.Modules.Sourcing.Domain;
using Hadron.Erp.Api.Modules.Bom.Domain;
using Hadron.Erp.Api.Modules.Planning.Domain;
using Hadron.Erp.Api.Modules.Inventory.Domain;
using Hadron.Erp.Api.Modules.PurchaseSupply.Domain;
using Hadron.Erp.Api.Modules.Mrp.Domain;
using Hadron.Erp.Api.Modules.OrderPlanning.Domain;

namespace Hadron.Erp.Api.Infrastructure;

public sealed class HadronDbContext(DbContextOptions<HadronDbContext> options)
    : DbContext(options)
{
    public DbSet<ReferenceType> ReferenceTypes => Set<ReferenceType>();
    public DbSet<ReferenceValue> ReferenceValues => Set<ReferenceValue>();
    public DbSet<Currency> Currencies => Set<Currency>();
    public DbSet<Unit> Units => Set<Unit>();
    public DbSet<Item> Items => Set<Item>();
    public DbSet<MaterialItemProfile> MaterialItemProfiles => Set<MaterialItemProfile>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<InternalPerson> InternalPeople => Set<InternalPerson>();
    public DbSet<Supplier> Suppliers => Set<Supplier>();
    public DbSet<SupplierPaymentProfile> SupplierPaymentProfiles => Set<SupplierPaymentProfile>();
    public DbSet<SupplierContact> SupplierContacts => Set<SupplierContact>();
    public DbSet<SourcingOption> SourcingOptions => Set<SourcingOption>();
    public DbSet<SourcingPrice> SourcingPrices => Set<SourcingPrice>();
    public DbSet<LeadTimeRecommendationApplication> LeadTimeRecommendationApplications => Set<LeadTimeRecommendationApplication>();
    public DbSet<BomRevision> BomRevisions => Set<BomRevision>();
    public DbSet<BomLine> BomLines => Set<BomLine>();
    public DbSet<BusinessPeriod> BusinessPeriods => Set<BusinessPeriod>();
    public DbSet<ForecastSeries> ForecastSeries => Set<ForecastSeries>();
    public DbSet<ForecastRevision> ForecastRevisions => Set<ForecastRevision>();
    public DbSet<DailyActual> DailyActuals => Set<DailyActual>();
    public DbSet<DailyActualRevision> DailyActualRevisions => Set<DailyActualRevision>();
    public DbSet<PeriodLifecycleEvent> PeriodLifecycleEvents => Set<PeriodLifecycleEvent>();
    public DbSet<Warehouse> Warehouses => Set<Warehouse>();
    public DbSet<InventoryTransaction> InventoryTransactions => Set<InventoryTransaction>();
    public DbSet<InventoryLedgerEffect> InventoryLedgerEffects => Set<InventoryLedgerEffect>();
    public DbSet<PurchaseOrder> PurchaseOrders => Set<PurchaseOrder>();
    public DbSet<PurchaseOrderLine> PurchaseOrderLines => Set<PurchaseOrderLine>();
    public DbSet<DeliverySchedule> DeliverySchedules => Set<DeliverySchedule>();
    public DbSet<DeliverySchedulePlanRevision> DeliverySchedulePlanRevisions => Set<DeliverySchedulePlanRevision>();
    public DbSet<DeliveryScheduleActualMilestone> DeliveryScheduleActualMilestones => Set<DeliveryScheduleActualMilestone>();
    public DbSet<PurchaseOrderLifecycleEvent> PurchaseOrderLifecycleEvents => Set<PurchaseOrderLifecycleEvent>();
    public DbSet<PurchaseReceipt> PurchaseReceipts => Set<PurchaseReceipt>();
    public DbSet<PurchaseReceiptLine> PurchaseReceiptLines => Set<PurchaseReceiptLine>();
    public DbSet<PurchaseScheduleCancellation> PurchaseScheduleCancellations => Set<PurchaseScheduleCancellation>();
    public DbSet<DeliveryScheduleAllocationEvent> DeliveryScheduleAllocationEvents => Set<DeliveryScheduleAllocationEvent>();
    public DbSet<MrpRun> MrpRuns => Set<MrpRun>();
    public DbSet<MrpRunItemInput> MrpRunItemInputs => Set<MrpRunItemInput>();
    public DbSet<MrpRunForecastInput> MrpRunForecastInputs => Set<MrpRunForecastInput>();
    public DbSet<MrpRunForecastPeriodInput> MrpRunForecastPeriodInputs => Set<MrpRunForecastPeriodInput>();
    public DbSet<MrpRunBomInput> MrpRunBomInputs => Set<MrpRunBomInput>();
    public DbSet<MrpRunInventoryInput> MrpRunInventoryInputs => Set<MrpRunInventoryInput>();
    public DbSet<MrpRunFirmSupplyInput> MrpRunFirmSupplyInputs => Set<MrpRunFirmSupplyInput>();
    public DbSet<MrpRunRouteAvailabilityInput> MrpRunRouteAvailabilityInputs => Set<MrpRunRouteAvailabilityInput>();
    public DbSet<MrpRunExternalSourceInput> MrpRunExternalSourceInputs => Set<MrpRunExternalSourceInput>();
    public DbSet<MrpRunRouteDecision> MrpRunRouteDecisions => Set<MrpRunRouteDecision>();
    public DbSet<MrpRunResolvedRouteResult> MrpRunResolvedRouteResults => Set<MrpRunResolvedRouteResult>();
    public DbSet<MrpRunDailyResult> MrpRunDailyResults => Set<MrpRunDailyResult>();
    public DbSet<MrpRunItemSummary> MrpRunItemSummaries => Set<MrpRunItemSummary>();
    public DbSet<MrpRunException> MrpRunExceptions => Set<MrpRunException>();
    public DbSet<MrpRunDemandTrace> MrpRunDemandTraces => Set<MrpRunDemandTrace>();
    public DbSet<OrderPlanningGeneration> OrderPlanningGenerations => Set<OrderPlanningGeneration>();
    public DbSet<OrderRecommendation> OrderRecommendations => Set<OrderRecommendation>();
    public DbSet<OrderRecommendationCandidate> OrderRecommendationCandidates => Set<OrderRecommendationCandidate>();
    public DbSet<OrderRecommendationDecision> OrderRecommendationDecisions => Set<OrderRecommendationDecision>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasSequence<long>("product_number_seq", "catalog");
        modelBuilder.HasSequence<long>("part_number_seq", "catalog");
        modelBuilder.HasSequence<long>("component_number_seq", "catalog");
        modelBuilder.HasSequence<long>("supplier_number_seq", "procurement");
        modelBuilder.HasSequence<long>("sourcing_option_number_seq", "procurement");
        modelBuilder.HasSequence<long>("bom_revision_number_seq", "engineering");
        modelBuilder.HasSequence<long>("warehouse_number_seq", "inventory");
        modelBuilder.HasSequence<long>("purchase_order_number_seq", "procurement");
        modelBuilder.HasSequence<long>("purchase_receipt_number_seq", "procurement");
        modelBuilder.HasSequence<long>("mrp_run_number_seq", "planning");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(HadronDbContext).Assembly);
    }
}
