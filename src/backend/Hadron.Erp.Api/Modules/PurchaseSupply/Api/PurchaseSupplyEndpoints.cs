using Hadron.Erp.Api.Modules.PurchaseSupply.Application;
using Hadron.Erp.Api.Modules.Inventory.Application;

namespace Hadron.Erp.Api.Modules.PurchaseSupply.Api;

public static class PurchaseSupplyEndpoints
{
    public static IEndpointRouteBuilder MapPurchaseSupplyEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/purchase-orders").WithTags("Purchase Supply");
        group.MapGet("/", (PurchaseSupplyService service, DateOnly? evaluationDate, string? search, string? status,
            Guid? supplierId, Guid? currencyId, int? page, int? pageSize, CancellationToken ct) => Execute(() => service.List(
                RequireDate(evaluationDate), search, status, supplierId, currencyId, page ?? 1, pageSize ?? 25, ct)));
        group.MapGet("/{id:guid}", async (PurchaseSupplyService service, Guid id, CancellationToken ct) =>
        {
            try { return await service.Get(id, ct) is { } value ? Results.Ok(value) : Results.NotFound(); }
            catch (Exception exception) { return Problem(exception); }
        });
        group.MapPost("/", (PurchaseSupplyService service, CreatePurchaseOrderRequest request, CancellationToken ct) =>
            ExecuteCreated(() => service.Create(request, ct), x => $"/api/purchase-orders/{x.Id}"));
        group.MapPut("/{id:guid}", (PurchaseSupplyService service, Guid id, UpdatePurchaseOrderRequest request,
            CancellationToken ct) => Execute(() => service.Update(id, request, ct)));
        group.MapPut("/{id:guid}/lines/{lineId:guid}", (PurchaseSupplyService service, Guid id, Guid lineId,
            UpdatePurchaseOrderLineRequest request, CancellationToken ct) => Execute(() => service.UpdateLine(id, lineId, request, ct)));
        group.MapPost("/{id:guid}/lines", (PurchaseSupplyService service, Guid id,
            AddPurchaseOrderLineRequest request, CancellationToken ct) => Execute(() => service.AddLine(id, request, ct)));
        group.MapPost("/{id:guid}/lines/{lineId:guid}/remove", (PurchaseSupplyService service, Guid id, Guid lineId,
            ExpectedVersionRequest request, CancellationToken ct) => Execute(() => service.RemoveLine(id, lineId, request, ct)));
        group.MapPut("/{id:guid}/lines/{lineId:guid}/schedules", (PurchaseSupplyService service, Guid id, Guid lineId,
            ReplaceSchedulesRequest request, CancellationToken ct) => Execute(() => service.ReplaceSchedules(id, lineId, request, ct)));
        group.MapPost("/{id:guid}/refresh-snapshot", (PurchaseSupplyService service, Guid id,
            ExpectedVersionRequest request, CancellationToken ct) => Execute(() => service.RefreshSnapshot(id, request, ct)));
        group.MapPost("/{id:guid}/approve", (PurchaseSupplyService service, Guid id,
            ExpectedVersionRequest request, CancellationToken ct) => Execute(() => service.Approve(id, request, ct)));
        group.MapPost("/{id:guid}/return-to-draft", (PurchaseSupplyService service, Guid id,
            ReturnToDraftRequest request, CancellationToken ct) => Execute(() => service.ReturnToDraft(id, request, ct)));
        group.MapPost("/{id:guid}/mark-ordered", (PurchaseSupplyService service, Guid id,
            MarkOrderedRequest request, CancellationToken ct) => Execute(() => service.MarkOrdered(id, request, ct)));
        group.MapPost("/{id:guid}/schedules/{scheduleId:guid}/revise-plan", (PurchaseSupplyService service,
            Guid id, Guid scheduleId, ReviseSchedulePlanRequest request, CancellationToken ct) =>
            Execute(() => service.ReviseSchedule(id, scheduleId, request, ct)));
        group.MapGet("/firm-incoming", (PurchaseSupplyService service, DateOnly? evaluationDate, Guid? itemId,
            DateOnly? etaFrom, DateOnly? etaTo, CancellationToken ct) => Execute(() => service.FirmIncoming(
                RequireDate(evaluationDate), itemId, etaFrom, etaTo, ct)));
        group.MapPost("/convert-decisions", (PurchaseOrderOperationsService service,
            ConvertExpertDecisionsRequest request, CancellationToken ct) => Execute(() => service.Convert(request, ct)));
        group.MapPost("/{id:guid}/schedules/{scheduleId:guid}/cancel", (PurchaseOrderOperationsService operations,
            PurchaseSupplyService supply, Guid id, Guid scheduleId, CancelScheduleRequest request, CancellationToken ct) =>
            Execute(() => operations.Cancel(id, scheduleId, request, supply, ct)));
        group.MapPost("/{id:guid}/schedules/{scheduleId:guid}/actual-milestones", (PurchaseOrderOperationsService operations,
            PurchaseSupplyService supply, Guid id, Guid scheduleId, RecordActualMilestoneRequest request, CancellationToken ct) =>
            Execute(() => operations.RecordMilestone(id, scheduleId, request, supply, ct)));
        group.MapPost("/{id:guid}/lines/{lineId:guid}/reallocate-schedules", (PurchaseOrderOperationsService operations,
            PurchaseSupplyService supply, Guid id, Guid lineId, ReallocateSchedulesRequest request, CancellationToken ct) =>
            Execute(() => operations.ReallocateSchedules(id, lineId, request, supply, ct)));

        var receipts = endpoints.MapGroup("/api/purchase-receipts").WithTags("Purchase Receipts");
        receipts.MapGet("/", (PurchaseOrderOperationsService service, Guid? purchaseOrderId, string? status,
            int? page, int? pageSize, CancellationToken ct) => Execute(() =>
                service.ListReceipts(purchaseOrderId, status, page ?? 1, pageSize ?? 25, ct)));
        receipts.MapGet("/{id:guid}", async (PurchaseOrderOperationsService service, Guid id, CancellationToken ct) =>
        {
            try { return await service.GetReceipt(id, ct) is { } value ? Results.Ok(value) : Results.NotFound(); }
            catch (Exception exception) { return Problem(exception); }
        });
        receipts.MapPost("/", (PurchaseOrderOperationsService service, CreatePurchaseReceiptRequest request,
            CancellationToken ct) => ExecuteCreated(() => service.CreateReceipt(request, ct), x => $"/api/purchase-receipts/{x.Id}"));
        receipts.MapPost("/{id:guid}/post", (PurchaseOrderOperationsService service, Guid id,
            PostPurchaseReceiptRequest request, CancellationToken ct) => Execute(() => service.PostReceipt(id, request, ct)));
        receipts.MapPost("/{id:guid}/reverse", (PurchaseOrderOperationsService service, Guid id,
            ReversePurchaseReceiptRequest request, CancellationToken ct) => Execute(() => service.ReverseReceipt(id, request, ct)));
        return endpoints;
    }

    private static DateOnly RequireDate(DateOnly? value) => value ?? throw new PurchaseSupplyValidationException(
        new Dictionary<string, string[]> { ["evaluationDate"] = ["تاریخ ارزیابی باید صریح ارسال شود."] });

    private static async Task<IResult> Execute<T>(Func<Task<T>> action)
    {
        try { return Results.Ok(await action()); }
        catch (Exception exception) { return Problem(exception); }
    }

    private static async Task<IResult> ExecuteCreated<T>(Func<Task<T>> action, Func<T, string> location)
    {
        try
        {
            var response = await action();
            return Results.Created(location(response), response);
        }
        catch (Exception exception) { return Problem(exception); }
    }

    private static IResult Problem(Exception exception) => exception switch
    {
        PurchaseSupplyValidationException validation => Results.ValidationProblem(validation.Errors),
        PurchaseSupplyConflictException conflict => Results.Problem(statusCode: 409,
            title: "Purchase supply conflict", detail: conflict.Message,
            extensions: new Dictionary<string, object?> { ["code"] = conflict.Code }),
        InventoryConflictException conflict => Results.Problem(statusCode: 409,
            title: "Inventory conflict", detail: conflict.Message,
            extensions: new Dictionary<string, object?> { ["code"] = conflict.Code }),
        KeyNotFoundException => Results.NotFound(),
        _ => throw exception
    };
}
