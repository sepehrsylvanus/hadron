using Hadron.Erp.Api.Modules.Suppliers.Application;
using Hadron.Erp.Api.Platform.Security;

namespace Hadron.Erp.Api.Modules.Suppliers.Api;

public static class SupplierEndpoints
{
    public static IEndpointRouteBuilder MapSupplierEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var s = endpoints.MapGroup("/api/suppliers").WithTags("Suppliers");
        s.MapGet("/", (string? search, bool? includeInactive, Guid? countryId, Guid? ownerId, int? page, int? pageSize, SupplierService service, CancellationToken ct) => Run(() => service.List(search, includeInactive ?? false, countryId, ownerId, page ?? 1, pageSize ?? 25, ct))).RequirePermission(Permissions.SupplierView);
        s.MapGet("/{id:guid}", async (Guid id, SupplierService service, CancellationToken ct) => await service.Get(id, ct) is { } value ? Results.Ok(value) : Results.NotFound()).RequirePermission(Permissions.SupplierView);
        s.MapPost("/", (CreateSupplierRequest request, SupplierService service, CancellationToken ct) => Created(() => service.Create(request, ct))).RequirePermission(Permissions.SupplierManage);
        s.MapPut("/{id:guid}", (Guid id, UpdateSupplierRequest request, SupplierService service, CancellationToken ct) => Run(() => service.Update(id, request, ct))).RequirePermission(Permissions.SupplierManage);
        s.MapPost("/{id:guid}/deactivate", (Guid id, ChangeSupplierStateRequest request, SupplierService service, CancellationToken ct) => Run(() => service.State(id, false, request, ct))).RequirePermission(Permissions.SupplierManage);
        s.MapPost("/{id:guid}/reactivate", (Guid id, ChangeSupplierStateRequest request, SupplierService service, CancellationToken ct) => Run(() => service.State(id, true, request, ct))).RequirePermission(Permissions.SupplierManage);
        s.MapPost("/{id:guid}/contacts", (Guid id, ContactRequest request, SupplierService service, CancellationToken ct) => Run(() => service.AddContact(id, request, ct))).RequirePermission(Permissions.SupplierManage);
        s.MapPut("/{id:guid}/contacts/{contactId:guid}", (Guid id, Guid contactId, ContactRequest request, SupplierService service, CancellationToken ct) => Run(() => service.UpdateContact(id, contactId, request, ct))).RequirePermission(Permissions.SupplierManage);
        s.MapPost("/{id:guid}/contacts/{contactId:guid}/deactivate", (Guid id, Guid contactId, ChangeSupplierStateRequest request, SupplierService service, CancellationToken ct) => Run(() => service.ContactState(id, contactId, false, request, ct))).RequirePermission(Permissions.SupplierManage);
        s.MapPost("/{id:guid}/contacts/{contactId:guid}/reactivate", (Guid id, Guid contactId, ChangeSupplierStateRequest request, SupplierService service, CancellationToken ct) => Run(() => service.ContactState(id, contactId, true, request, ct))).RequirePermission(Permissions.SupplierManage);
        var p = endpoints.MapGroup("/api/internal-people").WithTags("Internal People");
        p.MapGet("/", (bool? includeInactive, SupplierService service, CancellationToken ct) => Run(() => service.People(includeInactive ?? false, ct))).RequirePermission(Permissions.InternalPersonView);
        p.MapPost("/", (CreatePersonRequest request, SupplierService service, CancellationToken ct) => CreatedPerson(() => service.CreatePerson(request, ct))).RequirePermission(Permissions.InternalPersonManage);
        p.MapPut("/{id:guid}", (Guid id, UpdatePersonRequest request, SupplierService service, CancellationToken ct) => Run(() => service.UpdatePerson(id, request, ct))).RequirePermission(Permissions.InternalPersonManage);
        p.MapPost("/{id:guid}/deactivate", (Guid id, ChangePersonStateRequest request, SupplierService service, CancellationToken ct) => Run(() => service.StatePerson(id, false, request, ct))).RequirePermission(Permissions.InternalPersonManage);
        p.MapPost("/{id:guid}/reactivate", (Guid id, ChangePersonStateRequest request, SupplierService service, CancellationToken ct) => Run(() => service.StatePerson(id, true, request, ct))).RequirePermission(Permissions.InternalPersonManage);
        return endpoints;
    }
    private static async Task<IResult> Run<T>(Func<Task<T>> action) { try { return Results.Ok(await action()); } catch (Exception ex) { return Problem(ex); } }
    private static async Task<IResult> Created(Func<Task<SupplierMutationResponse>> action) { try { var r = await action(); return Results.Created($"/api/suppliers/{r.Supplier.Id}", r); } catch (Exception ex) { return Problem(ex); } }
    private static async Task<IResult> CreatedPerson(Func<Task<PersonResponse>> action) { try { var r = await action(); return Results.Created($"/api/internal-people/{r.Id}", r); } catch (Exception ex) { return Problem(ex); } }
    private static IResult Problem(Exception ex) => ex switch { SupplierValidationException v => Results.ValidationProblem(v.Errors), SupplierConflictException c => Results.Problem(statusCode: 409, title: "Supplier conflict", detail: c.Message, extensions: new Dictionary<string, object?> { { "code", c.Code }, { "details", c.Details } }), KeyNotFoundException => Results.NotFound(), _ => throw ex };
}
