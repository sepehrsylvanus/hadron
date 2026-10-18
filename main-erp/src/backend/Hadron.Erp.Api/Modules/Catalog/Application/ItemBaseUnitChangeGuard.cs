namespace Hadron.Erp.Api.Modules.Catalog.Application;

public interface IItemBaseUnitChangeGuard
{
    Task EnsureCanChange(Guid itemId, Guid currentUnitId, Guid requestedUnitId, CancellationToken cancellationToken);
}

public sealed class ItemBaseUnitChangeBlockedException : Exception
{
    public ItemBaseUnitChangeBlockedException()
        : this("ITEM_BASE_UNIT_LOCKED_BY_INVENTORY",
            "پس از ثبت گردش موجودی، تغییر واحد پایه قلم مجاز نیست.")
    { }

    public ItemBaseUnitChangeBlockedException(string code, string message, object? details = null)
        : base(message)
    {
        Code = code;
        Details = details;
    }

    public string Code { get; }
    public object? Details { get; }
}
