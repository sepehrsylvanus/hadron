using Hadron.Erp.Api.Modules.Trading.Application;
using Hadron.Erp.Api.Platform.Security;

namespace Hadron.Erp.Api.Modules.Trading.Api;

public static class TradingEndpoints
{
    public static IEndpointRouteBuilder MapTradingEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/trading").WithTags("Trading");
        group.MapGet("/overview", (HttpContext context, TradingService service, CancellationToken ct) => Run(() => service.Overview(
                new TradingOverviewReadAccess(Can(context, Permissions.TradingShipmentView), Can(context, Permissions.TradingCustomsView)), ct)))
            .RequirePermission(Permissions.TradingProjectView);
        group.MapGet("/lookups", (bool? includeInactive, string? search, int? limit, TradingService service, CancellationToken ct) => Run(() => service.Lookups(includeInactive ?? false, search, limit ?? 100, ct)))
            .RequirePermission(Permissions.TradingProjectView);
        group.MapPost("/project-setup/resolve-jalali-date", (ResolveTradeProjectSetupDateRequest request, TradingService service) =>
                Run(() => Task.FromResult(service.ResolveSetupDate(request))))
            .RequirePermission(Permissions.TradingProjectManage);
        group.MapGet("/project-setup/lookups", (string kind, string? search, int? page, int? pageSize, TradingService service, CancellationToken ct) =>
                Run(() => service.ProjectSetupLookups(kind, search, page ?? 1, pageSize ?? 25, ct)))
            .RequirePermission(Permissions.TradingProjectManage);
        group.MapPost("/project-setup", (CreateTradeProjectSetupRequest request, HttpContext context, TradingService service, CancellationToken ct) =>
                Created(() => service.CreateProjectSetup(request, ReadAccess(context), ct), x => $"/api/trading/projects/{x.Project.Id}"))
            .RequirePermission(Permissions.TradingProjectManage);
        group.MapGet("/references", (string? search, string? referenceType, bool? includeInactive, int? page, int? pageSize, TradingService service, CancellationToken ct) => Run(() => service.ListReferences(search, referenceType, includeInactive ?? false, page ?? 1, pageSize ?? 25, ct)))
            .RequirePermission(Permissions.TradingReferenceManage);
        group.MapGet("/projects", (string? search, string? status, string? priority, string? sortBy, string? sortDirection, int? page, int? pageSize, TradingService service, CancellationToken ct) => Run(() => service.ListProjects(search, status, priority, sortBy, sortDirection, page ?? 1, pageSize ?? 25, ct)))
            .RequirePermission(Permissions.TradingProjectView);
        group.MapGet("/projects/{id:guid}", async (Guid id, HttpContext context, TradingService service, CancellationToken ct) =>
            await service.GetProject(id, new TradingProjectReadAccess(
                Can(context, Permissions.TradingOrderRegistrationView), Can(context, Permissions.TradingFxView),
                Can(context, Permissions.TradingShipmentView), Can(context, Permissions.TradingCustomsView),
                Can(context, Permissions.TradingCostView), Can(context, Permissions.TradingAuditView)), ct) is { } value
                ? Results.Ok(value) : Results.NotFound())
            .RequirePermission(Permissions.TradingProjectView);
        group.MapGet("/shipments", (string? search, string? status, string? sortBy, string? sortDirection, int? page, int? pageSize, TradingService service, CancellationToken ct) => Run(() => service.ListShipments(search, status, sortBy, sortDirection, page ?? 1, pageSize ?? 25, ct)))
            .RequirePermission(Permissions.TradingShipmentView);
        group.MapGet("/customs-clearances", (string? search, string? status, string? sortBy, string? sortDirection, int? page, int? pageSize, TradingService service, CancellationToken ct) => Run(() => service.ListCustoms(search, status, sortBy, sortDirection, page ?? 1, pageSize ?? 25, ct)))
            .RequirePermission(Permissions.TradingCustomsView);
        group.MapGet("/costs", (string? search, string? currencyCode, bool? includeVoided, string? sortBy, string? sortDirection, int? page, int? pageSize, TradingService service, CancellationToken ct) => Run(() => service.ListCosts(search, currencyCode, includeVoided ?? false, sortBy, sortDirection, page ?? 1, pageSize ?? 25, ct)))
            .RequirePermission(Permissions.TradingCostView);
        group.MapGet("/reports", (string? search, string? status, string? sortBy, string? sortDirection, int? page, int? pageSize, TradingService service, CancellationToken ct) => Run(() => service.ListReports(search, status, sortBy, sortDirection, page ?? 1, pageSize ?? 25, ct)))
            .RequirePermission(Permissions.TradingReportView);
        group.MapPost("/projects", (CreateTradeProjectRequest request, HttpContext context, TradingService service, CancellationToken ct) => Created(() => service.CreateProject(request, ReadAccess(context), ct), x => $"/api/trading/projects/{x.Project.Id}"))
            .RequirePermission(Permissions.TradingProjectManage);
        group.MapPut("/projects/{id:guid}", (Guid id, UpdateTradeProjectRequest request, HttpContext context, TradingService service, CancellationToken ct) => Run(() => service.UpdateProject(id, request, ReadAccess(context), ct)))
            .RequirePermission(Permissions.TradingProjectManage);
        group.MapPost("/projects/{id:guid}/status", (Guid id, ChangeTradeProjectStatusRequest request, HttpContext context, TradingService service, CancellationToken ct) => Run(() => service.ChangeProjectStatus(id, request, ReadAccess(context), ct)))
            .RequirePermission(Permissions.TradingProjectClose);
        group.MapPost("/projects/{id:guid}/reopen", (Guid id, ReopenTradeProjectRequest request, HttpContext context, TradingService service, CancellationToken ct) => Run(() => service.ReopenProject(id, request, ReadAccess(context), ct)))
            .RequirePermission(Permissions.TradingProjectReopen);

        group.MapPost("/references", (CreateTradeReferenceRequest request, TradingService service, CancellationToken ct) => Created(() => service.CreateReference(request, ct), x => $"/api/trading/references/{x.Id}"))
            .RequirePermission(Permissions.TradingReferenceManage);
        group.MapPut("/references/{id:guid}", (Guid id, UpdateTradeReferenceRequest request, TradingService service, CancellationToken ct) => Run(() => service.UpdateReference(id, request, ct)))
            .RequirePermission(Permissions.TradingReferenceManage);
        group.MapPost("/references/{id:guid}/state", (Guid id, ChangeTradeReferenceStateRequest request, TradingService service, CancellationToken ct) => Run(() => service.StateReference(id, request, ct)))
            .RequirePermission(Permissions.TradingReferenceManage);

        group.MapGet("/projects/{id:guid}/order-registrations", (Guid id, string? search, string? status, int? page, int? pageSize, TradingService service, CancellationToken ct) =>
                Run(() => service.ListProjectOrders(id, search, status, page ?? 1, pageSize ?? 25, ct)))
            .RequirePermission(Permissions.TradingOrderRegistrationView);
        group.MapGet("/order-registrations/currencies", (string? search, int? page, int? pageSize, TradingService service, CancellationToken ct) =>
                Run(() => service.OrderCurrencies(search, page ?? 1, pageSize ?? 25, ct)))
            .RequirePermission(Permissions.TradingOrderRegistrationManage);
        group.MapPost("/order-registrations/resolve-jalali-date", (ResolveTradeProjectSetupDateRequest request, TradingService service) =>
                Run(() => Task.FromResult(service.ResolveSetupDate(request))))
            .RequirePermission(Permissions.TradingOrderRegistrationManage);
        group.MapGet("/order-registrations/{id:guid}", (Guid id, TradingService service, CancellationToken ct) => Run(() => service.GetManagedOrder(id, ct)))
            .RequirePermission(Permissions.TradingOrderRegistrationView);
        group.MapPut("/order-registrations/{id:guid}", (Guid id, UpdateTradeOrderRequest request, TradingService service, CancellationToken ct) => Run(() => service.UpdateOrder(id, request, ct)))
            .RequirePermission(Permissions.TradingOrderRegistrationManage);
        group.MapPost("/order-registrations", (CreateOrderRegistrationRequest request, TradingService service, CancellationToken ct) => Created(() => service.CreateOrder(request, ct), x => $"/api/trading/order-registrations/{x.Id}"))
            .RequirePermission(Permissions.TradingOrderRegistrationManage);
        group.MapPost("/order-registrations/{id:guid}/status", (Guid id, ChangeOrderRegistrationStatusRequest request, TradingService service, CancellationToken ct) => Run(() => service.ChangeOrderStatus(id, request, ct)))
            .RequirePermission(Permissions.TradingOrderRegistrationManage);
        group.MapPost("/fx-allocations", (CreateFxAllocationRequest request, TradingService service, CancellationToken ct) => Created(() => service.CreateFx(request, ct), x => $"/api/trading/fx-allocations/{x.Id}"))
            .RequirePermission(Permissions.TradingFxManage);
        group.MapPost("/fx-allocations/{id:guid}/status", (Guid id, ChangeFxStatusRequest request, TradingService service, CancellationToken ct) => Run(() => service.ChangeFxStatus(id, request, ct)))
            .RequirePermission(Permissions.TradingFxManage);
        group.MapPost("/shipments", (CreateTradeShipmentRequest request, TradingService service, CancellationToken ct) => Created(() => service.CreateShipment(request, ct), x => $"/api/trading/shipments/{x.Shipment.Id}"))
            .RequirePermission(Permissions.TradingShipmentManage);
        group.MapPut("/shipments/{id:guid}/actuals", (Guid id, UpdateTradeShipmentActualsRequest request, TradingService service, CancellationToken ct) => Run(() => service.UpdateShipmentActuals(id, request, ct)))
            .RequirePermission(Permissions.TradingShipmentManage);
        group.MapPost("/shipments/{id:guid}/legs", (Guid id, CreateTradeShipmentLegRequest request, TradingService service, CancellationToken ct) => Created(() => service.CreateLeg(id, request, ct), x => $"/api/trading/shipment-legs/{x.Id}"))
            .RequirePermission(Permissions.TradingShipmentManage);
        group.MapPost("/shipment-legs/{id:guid}/status", (Guid id, ChangeShipmentLegStatusRequest request, TradingService service, CancellationToken ct) => Run(() => service.ChangeLegStatus(id, request, ct)))
            .RequirePermission(Permissions.TradingShipmentManage);
        group.MapPost("/shipments/{id:guid}/projects", (Guid id, LinkExistingShipmentRequest request, TradingService service, CancellationToken ct) => Created(() => service.LinkShipment(id, request, ct), x => $"/api/trading/project-shipments/{x.LinkId}"))
            .RequirePermission(Permissions.TradingShipmentManage);
        group.MapPut("/project-shipments/{id:guid}", (Guid id, UpdateShipmentAllocationRequest request, TradingService service, CancellationToken ct) => Run(() => service.UpdateShipmentAllocation(id, request, ct)))
            .RequirePermission(Permissions.TradingShipmentManage);
        group.MapPost("/customs-clearances", (CreateTradeCustomsRequest request, TradingService service, CancellationToken ct) => Created(() => service.CreateCustoms(request, ct), x => $"/api/trading/customs-clearances/{x.Id}"))
            .RequirePermission(Permissions.TradingCustomsManage);
        group.MapPost("/customs-clearances/{id:guid}/status", (Guid id, ChangeCustomsStatusRequest request, TradingService service, CancellationToken ct) => Run(() => service.ChangeCustomsStatus(id, request, ct)))
            .RequirePermission(Permissions.TradingCustomsManage);
        group.MapPost("/costs", (CreateTradeCostRequest request, TradingService service, CancellationToken ct) => Created(() => service.CreateCost(request, ct), x => $"/api/trading/costs/{x.Id}"))
            .RequirePermission(Permissions.TradingCostManage);
        group.MapPost("/costs/{id:guid}/void", (Guid id, VoidTradeCostRequest request, TradingService service, CancellationToken ct) => Run(() => service.VoidCost(id, request, ct)))
            .RequirePermission(Permissions.TradingCostManage);
        group.MapPost("/report-notes", async (CreateTradeReportNoteRequest request, TradingService service, CancellationToken ct) => { await service.AddReportNote(request, ct); return Results.NoContent(); })
            .RequirePermission(Permissions.TradingReportManage);
        return endpoints;
    }

    private static async Task<IResult> Run<T>(Func<Task<T>> action)
    { try { return Results.Ok(await action()); } catch (Exception ex) { return Problem(ex); } }
    private static async Task<IResult> Created<T>(Func<Task<T>> action, Func<T, string> location)
    { try { var value = await action(); return Results.Created(location(value), value); } catch (Exception ex) { return Problem(ex); } }
    private static IResult Problem(Exception ex) => ex switch
    {
        TradingValidationException value => Results.ValidationProblem(value.Errors),
        TradingConflictException value => Results.Problem(statusCode: 409, title: "Trading conflict", detail: value.Message,
            extensions: new Dictionary<string, object?> { ["code"] = value.Code }),
        KeyNotFoundException => Results.NotFound(),
        UnauthorizedAccessException => Results.Unauthorized(),
        _ => throw ex
    };

    private static bool Can(HttpContext context, string permission) =>
        !context.User.HasClaim(SecurityClaimTypes.PermissionDeny, permission) &&
        context.User.HasClaim(SecurityClaimTypes.Permission, permission);
    private static TradingProjectReadAccess ReadAccess(HttpContext context) => new(
        Can(context, Permissions.TradingOrderRegistrationView), Can(context, Permissions.TradingFxView),
        Can(context, Permissions.TradingShipmentView), Can(context, Permissions.TradingCustomsView),
        Can(context, Permissions.TradingCostView), Can(context, Permissions.TradingAuditView));
}
