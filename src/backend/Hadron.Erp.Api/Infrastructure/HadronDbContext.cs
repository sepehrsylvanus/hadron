using Microsoft.EntityFrameworkCore;
using Hadron.Erp.Api.Modules.ReferenceData.Domain;
using Hadron.Erp.Api.Modules.Products.Domain;
using Hadron.Erp.Api.Modules.Suppliers.Domain;

namespace Hadron.Erp.Api.Infrastructure;

public sealed class HadronDbContext(DbContextOptions<HadronDbContext> options)
    : DbContext(options)
{
    public DbSet<ReferenceType> ReferenceTypes => Set<ReferenceType>();
    public DbSet<ReferenceValue> ReferenceValues => Set<ReferenceValue>();
    public DbSet<Currency> Currencies => Set<Currency>();
    public DbSet<Unit> Units => Set<Unit>();
    public DbSet<Item> Items => Set<Item>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<InternalPerson> InternalPeople => Set<InternalPerson>();
    public DbSet<Supplier> Suppliers => Set<Supplier>();
    public DbSet<SupplierPaymentProfile> SupplierPaymentProfiles => Set<SupplierPaymentProfile>();
    public DbSet<SupplierContact> SupplierContacts => Set<SupplierContact>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasSequence<long>("product_number_seq", "catalog");
        modelBuilder.HasSequence<long>("supplier_number_seq", "procurement");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(HadronDbContext).Assembly);
    }
}
