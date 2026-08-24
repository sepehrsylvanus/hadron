using Microsoft.EntityFrameworkCore;
using Hadron.Erp.Api.Modules.Catalog.Domain;
using Hadron.Erp.Api.Modules.ReferenceData.Domain;
using Hadron.Erp.Api.Modules.Products.Domain;
using Hadron.Erp.Api.Modules.Suppliers.Domain;
using Hadron.Erp.Api.Modules.Sourcing.Domain;
using Hadron.Erp.Api.Modules.Bom.Domain;
using Hadron.Erp.Api.Modules.Planning.Domain;

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
    public DbSet<BomRevision> BomRevisions => Set<BomRevision>();
    public DbSet<BomLine> BomLines => Set<BomLine>();
    public DbSet<BusinessPeriod> BusinessPeriods => Set<BusinessPeriod>();
    public DbSet<ForecastSeries> ForecastSeries => Set<ForecastSeries>();
    public DbSet<ForecastRevision> ForecastRevisions => Set<ForecastRevision>();
    public DbSet<DailyActual> DailyActuals => Set<DailyActual>();
    public DbSet<DailyActualRevision> DailyActualRevisions => Set<DailyActualRevision>();
    public DbSet<PeriodLifecycleEvent> PeriodLifecycleEvents => Set<PeriodLifecycleEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasSequence<long>("product_number_seq", "catalog");
        modelBuilder.HasSequence<long>("part_number_seq", "catalog");
        modelBuilder.HasSequence<long>("component_number_seq", "catalog");
        modelBuilder.HasSequence<long>("supplier_number_seq", "procurement");
        modelBuilder.HasSequence<long>("sourcing_option_number_seq", "procurement");
        modelBuilder.HasSequence<long>("bom_revision_number_seq", "engineering");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(HadronDbContext).Assembly);
    }
}
