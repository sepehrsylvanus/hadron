using System.Diagnostics;
using Hadron.Erp.Api.Infrastructure;
using Hadron.Erp.Api.Modules.Bom.Domain;
using Hadron.Erp.Api.Modules.Catalog.Domain;
using Hadron.Erp.Api.Modules.Inventory.Domain;
using Hadron.Erp.Api.Modules.Mrp.Domain;
using Hadron.Erp.Api.Modules.Planning.Domain;
using Hadron.Erp.Api.Modules.Production.Domain;
using Hadron.Erp.Api.Modules.PurchaseSupply.Domain;
using Hadron.Erp.Api.Modules.ReferenceData.Domain;
using Hadron.Erp.Api.Modules.Sourcing.Domain;
using Hadron.Erp.Api.Modules.Suppliers.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Options;

namespace Hadron.Erp.Api.Modules.Mrp.Application;

public sealed class AutomaticMrpRefreshOptions
{
    public bool Enabled { get; set; } = true;
    public int DebounceSeconds { get; set; } = 4;
    public int PollSeconds { get; set; } = 3;
    public int LeaseMinutes { get; set; } = 10;
    public int MaxAttempts { get; set; } = 3;
}

public sealed class MrpInputChangeInterceptor(
    TimeProvider timeProvider,
    IOptions<AutomaticMrpRefreshOptions> options) : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        Capture(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
        InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Capture(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void Capture(DbContext? context)
    {
        if (context is not HadronDbContext db || !options.Value.Enabled ||
            db.ChangeTracker.Entries<MrpRefreshOutboxMessage>().Any(x => x.State == EntityState.Added))
            return;

        var changes = db.ChangeTracker.Entries()
            .Select(Describe)
            .Where(x => x is not null)
            .Cast<InputChange>()
            .ToList();
        if (changes.Count == 0) return;

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var debounce = Math.Clamp(options.Value.DebounceSeconds, 1, 60);
        var sources = string.Join('+', changes.Select(x => x.Source).Distinct().Order());
        var types = string.Join(',', changes.Select(x => x.EntityType).Distinct().Order());
        var entityIds = changes.Select(x => x.EntityId).Where(x => x.HasValue).Distinct().ToList();
        db.MrpRefreshOutboxMessages.Add(new MrpRefreshOutboxMessage(
            sources.Length <= 120 ? sources : sources[..120],
            types.Length <= 500 ? types : types[..500],
            entityIds.Count == 1 ? entityIds[0] : null,
            Activity.Current?.TraceId.ToString(), now, now.AddSeconds(debounce)));
    }

    private static InputChange? Describe(EntityEntry entry)
    {
        if (entry.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted)) return null;
        var id = EntityId(entry);
        return entry.Entity switch
        {
            ForecastRevision when entry.State == EntityState.Added => Change("FORECAST", entry, id),
            DailyActualRevision when entry.State == EntityState.Added => Change("DAILY_ACTUAL", entry, id),
            BomRevision when entry.State == EntityState.Modified && Changed(entry, nameof(BomRevision.Status)) =>
                Change("BOM", entry, id),
            InventoryTransaction when entry.State == EntityState.Added => Change("INVENTORY", entry, id),
            Warehouse when entry.State == EntityState.Modified && Changed(entry, nameof(Warehouse.IsActive)) =>
                Change("INVENTORY", entry, id),
            PurchaseOrder when entry.State == EntityState.Modified && Changed(entry, nameof(PurchaseOrder.Status)) =>
                Change("FIRM_SUPPLY", entry, id),
            DeliverySchedulePlanRevision when entry.State == EntityState.Added => Change("FIRM_SUPPLY", entry, id),
            PurchaseReceipt when entry.State is EntityState.Added or EntityState.Modified =>
                Change("INVENTORY+FIRM_SUPPLY", entry, id),
            PurchaseScheduleCancellation when entry.State == EntityState.Added => Change("FIRM_SUPPLY", entry, id),
            DeliveryScheduleAllocationEvent when entry.State == EntityState.Added => Change("FIRM_SUPPLY", entry, id),
            SourcingOption => Change("SOURCING", entry, id),
            ManufacturingRoute when entry.State == EntityState.Modified &&
                Changed(entry, nameof(ManufacturingRoute.Status)) =>
                Change("MANUFACTURING_ROUTE", entry, id),
            ProductionWorkCenter when entry.State == EntityState.Modified &&
                Changed(entry, nameof(ProductionWorkCenter.WorkingWeekMask),
                    nameof(ProductionWorkCenter.IsActive)) =>
                Change("MANUFACTURING_ROUTE", entry, id),
            Supplier when entry.State == EntityState.Modified && Changed(entry, nameof(Supplier.IsActive)) =>
                Change("SOURCING", entry, id),
            Item when entry.State == EntityState.Modified && Changed(entry,
                nameof(Item.IsActive), nameof(Item.BaseUnitId)) => Change("CATALOG", entry, id),
            MaterialItemProfile when entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted =>
                Change("CATALOG", entry, id),
            ReferenceValue when entry.State == EntityState.Modified && Changed(entry, nameof(ReferenceValue.IsActive)) =>
                Change("REFERENCE_DATA", entry, id),
            Currency when entry.State == EntityState.Modified && Changed(entry, nameof(Currency.IsActive)) =>
                Change("REFERENCE_DATA", entry, id),
            Unit when entry.State == EntityState.Modified && Changed(entry, nameof(Unit.IsActive)) =>
                Change("REFERENCE_DATA", entry, id),
            _ => null
        };
    }

    private static bool Changed(EntityEntry entry, params string[] properties) =>
        properties.Any(name => entry.Properties.Any(property => property.Metadata.Name == name && property.IsModified));

    private static Guid? EntityId(EntityEntry entry)
    {
        var value = entry.Properties.FirstOrDefault(x => x.Metadata.Name == "Id")?.CurrentValue;
        return value is Guid id && id != Guid.Empty ? id : null;
    }

    private static InputChange Change(string source, EntityEntry entry, Guid? id) =>
        new(source, entry.Metadata.ClrType.Name, id);

    private sealed record InputChange(string Source, string EntityType, Guid? EntityId);
}
