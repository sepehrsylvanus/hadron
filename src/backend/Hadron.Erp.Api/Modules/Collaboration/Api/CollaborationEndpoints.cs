using System.Security.Claims;
using Hadron.Erp.Api.Infrastructure;
using Hadron.Erp.Api.Modules.Collaboration.Application;
using Hadron.Erp.Api.Modules.Collaboration.Domain;
using Hadron.Erp.Api.Modules.Collaboration.Infrastructure;
using Hadron.Erp.Api.Modules.Planning.Application;
using Hadron.Erp.Api.Platform.Security;
using Hadron.Erp.Api.Platform.Storage;
using Microsoft.EntityFrameworkCore;

namespace Hadron.Erp.Api.Modules.Collaboration.Api;

public static class CollaborationEndpoints
{
    public static IEndpointRouteBuilder MapCollaborationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var root = endpoints.MapGroup("/api/collaboration").WithTags("Collaboration").RequireAuthorization();

        root.MapGet("/directory/users", (string? search, int? page, int? pageSize, CollaborationService service,
                CancellationToken ct) => Run(() => service.Directory(search, page ?? 1, pageSize ?? 25, ct)))
            .RequirePermission(Permissions.CollaborationWorkspaceView);
        root.MapGet("/directory/users/{id:guid}/photo", DirectoryPhoto)
            .RequirePermission(Permissions.CollaborationWorkspaceView);
        root.MapPost("/date/resolve-jalali", ResolveJalaliDate)
            .RequirePermission(Permissions.CorrespondenceLetterManage);

        root.MapGet("/notifications", (int? limit, ClaimsPrincipal user, CollaborationService service,
                CancellationToken ct) => Run(() => service.NotificationSummary(
                    CollaborationActorFactory.From(user), limit ?? 12,
                    HasPermission(user, Permissions.CorrespondenceLetterView),
                    HasPermission(user, Permissions.CollaborationCallJoin),
                    HasPermission(user, Permissions.CollaborationMeetingView), ct)))
            .RequirePermission(Permissions.CollaborationWorkspaceView);

        root.MapGet("/conversations", (string? search, int? page, int? pageSize, ClaimsPrincipal user,
                CollaborationService service, CancellationToken ct) => Run(() => service.Conversations(
                    CollaborationActorFactory.From(user), search, page ?? 1, pageSize ?? 50, ct)))
            .RequirePermission(Permissions.CollaborationWorkspaceView);
        root.MapGet("/conversations/{id:guid}", (Guid id, ClaimsPrincipal user, CollaborationService service,
                CancellationToken ct) => Run(() => service.Conversation(id, CollaborationActorFactory.From(user), ct)))
            .RequirePermission(Permissions.CollaborationWorkspaceView);
        root.MapGet("/conversations/{id:guid}/messages", (Guid id, DateTime? beforeUtc, int? pageSize,
                ClaimsPrincipal user, CollaborationService service, CancellationToken ct) => Run(() => service.Messages(
                    id, CollaborationActorFactory.From(user), beforeUtc, pageSize ?? 50, ct)))
            .RequirePermission(Permissions.CollaborationWorkspaceView);
        root.MapPost("/conversations/channels", (CreateChannelRequest request, ClaimsPrincipal user,
                CollaborationService service, CancellationToken ct) => Created(() => service.CreateChannel(
                    request, CollaborationActorFactory.From(user), ct), "/api/collaboration/conversations"))
            .RequirePermission(Permissions.CollaborationChannelManage);
        root.MapPost("/conversations/direct", (CreateDirectConversationRequest request, ClaimsPrincipal user,
                CollaborationService service, CancellationToken ct) => Created(() => service.CreateDirect(
                    request, CollaborationActorFactory.From(user), ct), "/api/collaboration/conversations"))
            .RequirePermission(Permissions.CollaborationMessageSend);
        root.MapPut("/conversations/{id:guid}", (Guid id, UpdateConversationRequest request, ClaimsPrincipal user,
                CollaborationService service, CancellationToken ct) => Run(() => service.UpdateConversation(
                    id, request, CollaborationActorFactory.From(user), ct)))
            .RequirePermission(Permissions.CollaborationChannelManage);
        root.MapPost("/conversations/{id:guid}/archive", (Guid id, ArchiveConversationRequest request,
                ClaimsPrincipal user, CollaborationService service, CancellationToken ct) => NoContent(() =>
                    service.ArchiveConversation(id, request, CollaborationActorFactory.From(user), ct)))
            .RequirePermission(Permissions.CollaborationChannelManage);
        root.MapPost("/conversations/{id:guid}/read", (Guid id, ClaimsPrincipal user,
                CollaborationService service, CancellationToken ct) => NoContent(() => service.MarkRead(
                    id, CollaborationActorFactory.From(user), ct)))
            .RequirePermission(Permissions.CollaborationWorkspaceView);

        root.MapPost("/conversations/{conversationId:guid}/messages", (Guid conversationId, SendMessageRequest request,
                ClaimsPrincipal user, CollaborationService service, CancellationToken ct) => Created(() =>
                    service.SendMessage(conversationId, request, CollaborationActorFactory.From(user), ct),
                    $"/api/collaboration/conversations/{conversationId}/messages"))
            .RequirePermission(Permissions.CollaborationMessageSend);
        root.MapPut("/conversations/{conversationId:guid}/messages/{messageId:guid}", (Guid conversationId,
                Guid messageId, EditMessageRequest request, ClaimsPrincipal user, CollaborationService service,
                CancellationToken ct) => Run(() => service.EditMessage(conversationId, messageId, request,
                    CollaborationActorFactory.From(user), CanModerate(user), ct)))
            .RequirePermission(Permissions.CollaborationMessageSend);
        root.MapPost("/conversations/{conversationId:guid}/messages/{messageId:guid}/delete", (Guid conversationId,
                Guid messageId, DeleteMessageRequest request, ClaimsPrincipal user, CollaborationService service,
                CancellationToken ct) => NoContent(() => service.DeleteMessage(conversationId, messageId, request,
                    CollaborationActorFactory.From(user), CanModerate(user), ct)))
            .RequirePermission(Permissions.CollaborationMessageSend);
        root.MapPost("/conversations/{conversationId:guid}/messages/{messageId:guid}/reactions", (Guid conversationId,
                Guid messageId, ToggleReactionRequest request, ClaimsPrincipal user, CollaborationService service,
                CancellationToken ct) => Run(() => service.ToggleReaction(conversationId, messageId, request,
                    CollaborationActorFactory.From(user), ct)))
            .RequirePermission(Permissions.CollaborationMessageSend);
        root.MapPost("/conversations/{conversationId:guid}/messages/{messageId:guid}/attachments",
                async (Guid conversationId, Guid messageId, HttpContext context, ClaimsPrincipal user,
                    CollaborationService service, CancellationToken ct) => await Run(async () =>
                {
                    var form = await context.Request.ReadFormAsync(ct);
                    var file = form.Files.GetFile("file") ?? throw new CollaborationValidationException(
                        new Dictionary<string, string[]> { ["file"] = ["فایل الزامی است."] });
                    return await service.AttachToMessage(conversationId, messageId, file,
                        CollaborationActorFactory.From(user), ct);
                }))
            .RequirePermission(Permissions.CollaborationFileShare);
        root.MapGet("/attachments/{id:guid}", async (Guid id, ClaimsPrincipal user, CollaborationService service,
                ICollaborationFileStore store, CancellationToken ct) => await RunResult(async () =>
                {
                    var result = await service.Download(id, CollaborationActorFactory.From(user), store, ct);
                    return Results.Stream(result.Content, result.Attachment.ContentType,
                        result.Attachment.OriginalFileName, enableRangeProcessing: true);
                }))
            .RequirePermission(Permissions.CollaborationWorkspaceView);

        root.MapGet("/letters", (string? search, string? status, int? page, int? pageSize, ClaimsPrincipal user,
                CollaborationService service, CancellationToken ct) => Run(() => service.Letters(
                    CollaborationActorFactory.From(user), search, status, page ?? 1, pageSize ?? 25, ct)))
            .RequirePermission(Permissions.CorrespondenceLetterView);
        root.MapGet("/letters/{id:guid}", (Guid id, ClaimsPrincipal user, CollaborationService service,
                CancellationToken ct) => Run(() => service.Letter(id, CollaborationActorFactory.From(user), ct)))
            .RequirePermission(Permissions.CorrespondenceLetterView);
        root.MapPost("/letters", (CreateOfficialLetterRequest request, ClaimsPrincipal user,
                CollaborationService service, CancellationToken ct) => Created(() => service.CreateLetter(
                    request, CollaborationActorFactory.From(user), ct), "/api/collaboration/letters"))
            .RequirePermission(Permissions.CorrespondenceLetterManage);
        root.MapPut("/letters/{id:guid}", (Guid id, UpdateOfficialLetterRequest request, ClaimsPrincipal user,
                CollaborationService service, CancellationToken ct) => Run(() => service.UpdateLetter(
                    id, request, CollaborationActorFactory.From(user), ct)))
            .RequirePermission(Permissions.CorrespondenceLetterManage);
        root.MapPost("/letters/{id:guid}/issue", (Guid id, LetterTransitionRequest request, ClaimsPrincipal user,
                CollaborationService service, CancellationToken ct) => Run(() => service.TransitionLetter(
                    id, "ISSUED", request, CollaborationActorFactory.From(user), ct)))
            .RequirePermission(Permissions.CorrespondenceLetterIssue);
        root.MapPost("/letters/{id:guid}/acknowledge", (Guid id, LetterTransitionRequest request, ClaimsPrincipal user,
                CollaborationService service, CancellationToken ct) => Run(() => service.TransitionLetter(
                    id, "ACKNOWLEDGED", request, CollaborationActorFactory.From(user), ct)))
            .RequirePermission(Permissions.CorrespondenceLetterAcknowledge);
        root.MapPost("/letters/{id:guid}/close", (Guid id, LetterTransitionRequest request, ClaimsPrincipal user,
                CollaborationService service, CancellationToken ct) => Run(() => service.TransitionLetter(
                    id, "CLOSED", request, CollaborationActorFactory.From(user), ct)))
            .RequirePermission(Permissions.CorrespondenceLetterManage);
        root.MapPost("/letters/{id:guid}/attachments", async (Guid id, HttpContext context, ClaimsPrincipal user,
                CollaborationService service, CancellationToken ct) => await Run(async () =>
                {
                    var form = await context.Request.ReadFormAsync(ct);
                    var file = form.Files.GetFile("file") ?? throw new CollaborationValidationException(
                        new Dictionary<string, string[]> { ["file"] = ["فایل الزامی است."] });
                    return await service.AttachToLetter(id, file, CollaborationActorFactory.From(user), ct);
                }))
            .RequirePermission(Permissions.CollaborationFileShare);

        root.MapGet("/calls/configuration", (CollaborationService service) => Results.Ok(service.CallConfiguration()))
            .RequirePermission(Permissions.CollaborationCallJoin);
        root.MapPost("/conversations/{conversationId:guid}/calls", (Guid conversationId, StartCallRequest request,
                ClaimsPrincipal user, CollaborationService service, CancellationToken ct) => Created(() =>
                    service.StartCall(conversationId, request, CollaborationActorFactory.From(user), ct),
                    "/api/collaboration/calls"))
            .RequirePermission(Permissions.CollaborationCallStart)
            .RequirePermission(Permissions.CollaborationCallJoin);
        root.MapPost("/calls/{callId:guid}/join", (Guid callId, ClaimsPrincipal user, CollaborationService service,
                CancellationToken ct) => Run(() => service.JoinCall(callId, CollaborationActorFactory.From(user), ct)))
            .RequirePermission(Permissions.CollaborationCallJoin);
        root.MapPost("/calls/{callId:guid}/end", (Guid callId, EndCallRequest request, ClaimsPrincipal user,
                CollaborationService service, CancellationToken ct) => NoContent(() => service.EndCall(callId,
                    request, CollaborationActorFactory.From(user), CanModerate(user), ct)))
            .RequirePermission(Permissions.CollaborationCallStart);
        return endpoints;
    }

    private static IResult ResolveJalaliDate(ResolveCollaborationJalaliDateRequest request,
        IPlanningCalendar calendar)
    {
        var value = string.Concat((request.JalaliDate ?? string.Empty).Where(char.IsAsciiDigit));
        if (value.Length != 8 || !int.TryParse(value[..4], out var year) ||
            !int.TryParse(value[4..6], out var month) || !int.TryParse(value[6..], out var day))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["dueDate"] = ["تاریخ شمسی باید ۸ رقم و به‌صورت YYYYMMDD باشد."]
            });
        }

        try
        {
            return Results.Ok(new { jalaliDate = value, gregorianDate = calendar.GetGregorianDate(year, month, day) });
        }
        catch (Exception)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["dueDate"] = ["تاریخ شمسی معتبر نیست."]
            });
        }
    }

    private static async Task<IResult> DirectoryPhoto(Guid id, ClaimsPrincipal user, HadronDbContext db,
        IProfilePhotoStore store, CancellationToken ct)
    {
        _ = CollaborationActorFactory.From(user);
        var photo = await db.Users.AsNoTracking().Where(x => x.Id == id && x.IsActive).Select(x => new
            { x.ProfilePhotoObjectKey, x.ProfilePhotoContentType, x.ProfilePhotoSha256 }).SingleOrDefaultAsync(ct);
        if (photo?.ProfilePhotoObjectKey is null) return Results.NotFound();
        var stream = await store.Get(photo.ProfilePhotoObjectKey, ct);
        return Results.Stream(stream, photo.ProfilePhotoContentType, enableRangeProcessing: true);
    }

    private static bool CanModerate(ClaimsPrincipal user) => user.IsInRole(SystemRoleNames.SystemAdministrator) ||
        user.HasClaim(SecurityClaimTypes.Permission, Permissions.CollaborationMessageModerate);

    private static bool HasPermission(ClaimsPrincipal user, string permission) =>
        user.IsInRole(SystemRoleNames.SystemAdministrator) ||
        user.HasClaim(SecurityClaimTypes.Permission, permission);

    private static async Task<IResult> Run<T>(Func<Task<T>> action)
    {
        IResult? problem = null;
        try { return Results.Ok(await action()); }
        catch (Exception ex) when (TryProblem(ex, out problem)) { return problem!; }
    }
    private static async Task<IResult> Created<T>(Func<Task<T>> action, string path)
    {
        IResult? problem = null;
        try { var value = await action(); var id = value?.GetType().GetProperty("Id")?.GetValue(value); return Results.Created($"{path}/{id}", value); }
        catch (Exception ex) when (TryProblem(ex, out problem)) { return problem!; }
    }
    private static async Task<IResult> NoContent(Func<Task> action)
    {
        IResult? problem = null;
        try { await action(); return Results.NoContent(); }
        catch (Exception ex) when (TryProblem(ex, out problem)) { return problem!; }
    }
    private static async Task<IResult> RunResult(Func<Task<IResult>> action)
    {
        IResult? problem = null;
        try { return await action(); }
        catch (Exception ex) when (TryProblem(ex, out problem)) { return problem!; }
    }
    private static bool TryProblem(Exception exception, out IResult? problem)
    {
        switch (exception)
        {
            case CollaborationValidationException validation:
                problem = Results.ValidationProblem(validation.Errors, title: "اطلاعات ارتباطات نیاز به اصلاح دارد."); return true;
            case CollaborationConflictException conflict:
                problem = Results.Problem(statusCode: 409, title: "عملیات قابل انجام نیست", detail: conflict.Message,
                    extensions: new Dictionary<string, object?> { ["code"] = conflict.Code }); return true;
            case CollaborationNotFoundException missing:
                problem = Results.Problem(statusCode: 404, title: "اطلاعات یافت نشد", detail: missing.Message,
                    extensions: new Dictionary<string, object?> { ["code"] = missing.Code }); return true;
            case DbUpdateConcurrencyException:
                problem = Results.Problem(statusCode: 409, title: "اطلاعات هم‌زمان تغییر کرده است",
                    detail: "صفحه را تازه‌سازی و دوباره تلاش کنید.", extensions: new Dictionary<string, object?>
                    { ["code"] = "COLLABORATION_VERSION_CONFLICT" }); return true;
            case CollaborationActorInvalidException:
                problem = Results.Problem(statusCode: 401, title: "نشست کاربری معتبر نیست",
                    detail: "برای ادامه دوباره وارد سامانه شوید.", extensions: new Dictionary<string, object?>
                    { ["code"] = "COLLABORATION_ACTOR_INVALID" }); return true;
            case UnauthorizedAccessException:
                problem = Results.Problem(statusCode: 403, title: "دسترسی مجاز نیست",
                    detail: "شما مجوز انجام این عملیات را ندارید.", extensions: new Dictionary<string, object?>
                    { ["code"] = "COLLABORATION_FORBIDDEN" }); return true;
            default: problem = null; return false;
        }
    }
}
