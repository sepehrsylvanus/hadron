namespace Hadron.Erp.Api.Modules.Catalog.Application;

public interface IItemBaseUnitChangeGuard
{
    Task EnsureCanChange(Guid itemId, Guid currentUnitId, Guid requestedUnitId, CancellationToken cancellationToken);
}

public sealed class ItemBaseUnitChangeBlockedException : Exception
{
    public ItemBaseUnitChangeBlockedException()
        : base("پس از ثبت گردش موجودی، تغییر واحد پایه قلم مجاز نیست.") { }
}
