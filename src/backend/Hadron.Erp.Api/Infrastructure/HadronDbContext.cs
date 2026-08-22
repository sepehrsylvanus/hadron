using Microsoft.EntityFrameworkCore;
using Hadron.Erp.Api.Modules.ReferenceData.Domain;

namespace Hadron.Erp.Api.Infrastructure;

public sealed class HadronDbContext(DbContextOptions<HadronDbContext> options)
    : DbContext(options)
{
    public DbSet<ReferenceType> ReferenceTypes => Set<ReferenceType>();
    public DbSet<ReferenceValue> ReferenceValues => Set<ReferenceValue>();
    public DbSet<Currency> Currencies => Set<Currency>();
    public DbSet<Unit> Units => Set<Unit>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(HadronDbContext).Assembly);
    }
}
