using System.Security.Claims;
using Hadron.Erp.Api.Modules.Collaboration.Application;
using Hadron.Erp.Api.Modules.Collaboration.Domain;
using Hadron.Erp.Api.Modules.Identity.Domain;
using Hadron.Erp.Api.Platform.Security;
using Microsoft.EntityFrameworkCore;

namespace Hadron.Erp.Api.Modules.Collaboration.Api;

public static class MeetingEndpoints
{
    public static IEndpointRouteBuilder MapMeetingEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var root = endpoints.MapGroup("/api/collaboration/meetings").WithTags("Collaboration Meetings");
        root.MapPost("/resolve-jalali-datetime", (ResolveMeetingJalaliDateTimeRequest request,
                MeetingDateTimeResolver resolver) => RunSync(() => resolver.Resolve(request)))
            .RequirePermission(Permissions.CollaborationMeetingSchedule);
        root.MapGet("", (string? search, string? status, int? page, int? pageSize, ClaimsPrincipal user, MeetingService service,
                CancellationToken ct) => Run(() => service.List(CollaborationActorFactory.From(user),
                search, status, page ?? 1, pageSize ?? 25, ct)))
            .RequirePermission(Permissions.CollaborationMeetingView);
        root.MapGet("/{meetingId:guid}", (Guid meetingId, ClaimsPrincipal user, MeetingService service,
                CancellationToken ct) => Run(() => service.Get(meetingId, CollaborationActorFactory.From(user), ct)))
            .RequirePermission(Permissions.CollaborationMeetingView);
        root.MapPost("", (CreateMeetingRequest request, ClaimsPrincipal user, MeetingService service,
                CancellationToken ct) => Created(async () =>
            {
                if (!request.ScheduledStartAtUtc.HasValue && !HasPermission(user, Permissions.CollaborationMeetingStart))
                    throw new UnauthorizedAccessException("Starting an instant meeting requires start authority.");
                return await service.Create(request, CollaborationActorFactory.From(user), ct);
            }, "/api/collaboration/meetings"))
            .RequirePermission(Permissions.CollaborationMeetingSchedule);
        root.MapPut("/{meetingId:guid}", (Guid meetingId, UpdateMeetingRequest request,
                ClaimsPrincipal user, MeetingService service, CancellationToken ct) => Run(() => service.Update(
                meetingId, request, CollaborationActorFactory.From(user), ct)))
            .RequirePermission(Permissions.CollaborationMeetingSchedule);
        root.MapPost("/{meetingId:guid}/start", (Guid meetingId, MeetingVersionRequest request,
                ClaimsPrincipal user, MeetingService service, CancellationToken ct) => Run(() => service.Start(
                meetingId, request, CollaborationActorFactory.From(user), ct)))
            .RequirePermission(Permissions.CollaborationMeetingStart);
        root.MapPost("/{meetingId:guid}/cancel", (Guid meetingId, MeetingVersionRequest request,
                ClaimsPrincipal user, MeetingService service, CancellationToken ct) => Run(() => service.Cancel(
                meetingId, request, CollaborationActorFactory.From(user), ct)))
            .RequirePermission(Permissions.CollaborationMeetingSchedule);
        root.MapPost("/{meetingId:guid}/lock", (Guid meetingId, MeetingVersionRequest request,
                ClaimsPrincipal user, MeetingService service, CancellationToken ct) => Run(() => service.SetLock(
                meetingId, request, true, CollaborationActorFactory.From(user), ct)))
            .RequirePermission(Permissions.CollaborationMeetingManageParticipants);
        root.MapPost("/{meetingId:guid}/unlock", (Guid meetingId, MeetingVersionRequest request,
                ClaimsPrincipal user, MeetingService service, CancellationToken ct) => Run(() => service.SetLock(
                meetingId, request, false, CollaborationActorFactory.From(user), ct)))
            .RequirePermission(Permissions.CollaborationMeetingManageParticipants);
        root.MapPost("/{meetingId:guid}/end", (Guid meetingId, MeetingVersionRequest request,
                ClaimsPrincipal user, MeetingService service, CancellationToken ct) => Run(() => service.End(
                meetingId, request, CollaborationActorFactory.From(user), ct)))
            .RequirePermission(Permissions.CollaborationMeetingEnd);
        root.MapPost("/{meetingId:guid}/join", (Guid meetingId, ClaimsPrincipal user, MeetingService service,
                CancellationToken ct) => Run(() => service.Join(meetingId, CollaborationActorFactory.From(user), ct)))
            .RequirePermission(Permissions.CollaborationMeetingJoin);
        root.MapPost("/{meetingId:guid}/connected", (Guid meetingId, ClaimsPrincipal user,
                MeetingService service, CancellationToken ct) => Run(() => service.Connected(meetingId,
                CollaborationActorFactory.From(user), ct)))
            .RequirePermission(Permissions.CollaborationMeetingJoin);
        root.MapPost("/{meetingId:guid}/disconnected", (Guid meetingId, ClaimsPrincipal user,
                MeetingService service, CancellationToken ct) => RunNullable(() => service.Disconnected(meetingId,
                CollaborationActorFactory.From(user), ct)))
            .RequirePermission(Permissions.CollaborationMeetingJoin);
        root.MapPost("/{meetingId:guid}/participants/{participantId:guid}/role", (Guid meetingId,
                Guid participantId, MeetingParticipantRoleRequest request, ClaimsPrincipal user,
                MeetingService service, CancellationToken ct) => Run(() => service.ChangeRole(meetingId,
                participantId, request, CollaborationActorFactory.From(user), ct)))
            .RequirePermission(Permissions.CollaborationMeetingManageParticipants);
        root.MapPost("/{meetingId:guid}/participants/{participantId:guid}/admit", (Guid meetingId,
                Guid participantId, ClaimsPrincipal user, MeetingService service, CancellationToken ct) =>
                Run(() => service.Admit(meetingId, participantId, CollaborationActorFactory.From(user), ct)))
            .RequirePermission(Permissions.CollaborationMeetingManageParticipants);
        root.MapPost("/{meetingId:guid}/participants/{participantId:guid}/deny", (Guid meetingId,
                Guid participantId, ClaimsPrincipal user, MeetingService service, CancellationToken ct) =>
                Run(() => service.Deny(meetingId, participantId, CollaborationActorFactory.From(user), ct)))
            .RequirePermission(Permissions.CollaborationMeetingManageParticipants);
        root.MapPost("/{meetingId:guid}/participants/{participantId:guid}/revoke", (Guid meetingId,
                Guid participantId, ClaimsPrincipal user, MeetingService service, CancellationToken ct) =>
                Run(() => service.Revoke(meetingId, participantId, CollaborationActorFactory.From(user), ct)))
            .RequirePermission(Permissions.CollaborationMeetingManageParticipants);
        root.MapPost("/{meetingId:guid}/guest-invites", (Guid meetingId,
                CreateMeetingGuestInviteRequest request, ClaimsPrincipal user, MeetingService service,
                CancellationToken ct) => Created(() => service.CreateGuestInvite(meetingId, request,
                CollaborationActorFactory.From(user), ct), $"/api/collaboration/meetings/{meetingId}/guest-invites"))
            .RequirePermission(Permissions.CollaborationMeetingInviteGuest);
        root.MapGet("/{meetingId:guid}/report", (Guid meetingId, ClaimsPrincipal user, MeetingService service,
                CancellationToken ct) => Run(() => service.Report(meetingId, CollaborationActorFactory.From(user), ct)))
            .RequirePermission(Permissions.CollaborationMeetingReport);

        var guests = endpoints.MapGroup("/api/collaboration/meeting-guests")
            .WithTags("Collaboration Meeting Guests");
        guests.MapPost("/preview", (MeetingGuestTokenRequest request, MeetingGuestService service,
                CancellationToken ct) => RunGuest(() => service.Preview(request, ct)))
            .AllowAnonymous().RequireRateLimiting("meeting-guest-invitation");
        guests.MapPost("/redeem", (MeetingGuestTokenRequest request, MeetingGuestService service,
                CancellationToken ct) => RunGuest(() => service.Redeem(request, ct)))
            .AllowAnonymous().RequireRateLimiting("meeting-guest-invitation");
        guests.MapPost("/status", (MeetingGuestTicketRequest request, MeetingGuestService service,
                CancellationToken ct) => RunGuest(() => service.Status(request, ct)))
            .AllowAnonymous().RequireRateLimiting("meeting-guest-session");
        guests.MapPost("/join", (MeetingGuestTicketRequest request, MeetingGuestService service,
                CancellationToken ct) => RunGuest(() => service.Join(request, ct)))
            .AllowAnonymous().RequireRateLimiting("meeting-guest-session");
        guests.MapPost("/connected", (MeetingGuestTicketRequest request, MeetingGuestService service,
                CancellationToken ct) => RunGuest(() => service.Connected(request, ct)))
            .AllowAnonymous().RequireRateLimiting("meeting-guest-session");
        guests.MapPost("/disconnected", (MeetingGuestTicketRequest request, MeetingGuestService service,
                CancellationToken ct) => RunGuestNullable(() => service.Disconnected(request, ct)))
            .AllowAnonymous().RequireRateLimiting("meeting-guest-session");
        return endpoints;
    }

    private static bool HasPermission(ClaimsPrincipal user, string permission) =>
        user.IsInRole(SystemRoleNames.SystemAdministrator) ||
        user.HasClaim(SecurityClaimTypes.Permission, permission);

    private static async Task<IResult> Run<T>(Func<Task<T>> action)
    {
        try { return Results.Ok(await action()); }
        catch (Exception exception) when (Problem(exception) is { } result) { return result; }
    }

    private static IResult RunSync<T>(Func<T> action)
    {
        try { return Results.Ok(action()); }
        catch (Exception exception) when (Problem(exception) is { } result) { return result; }
    }

    private static async Task<IResult> Created<T>(Func<Task<T>> action, string path)
    {
        try
        {
            var value = await action();
            var id = value?.GetType().GetProperty("Id")?.GetValue(value);
            return Results.Created($"{path}/{id}", value);
        }
        catch (Exception exception) when (Problem(exception) is { } result) { return result; }
    }

    private static async Task<IResult> RunNullable<T>(Func<Task<T?>> action)
    {
        try
        {
            var value = await action();
            return value is null ? Results.NoContent() : Results.Ok(value);
        }
        catch (Exception exception) when (Problem(exception) is { } result) { return result; }
    }

    private static async Task<IResult> RunGuest<T>(Func<Task<T>> action)
    {
        try { return Results.Ok(await action()); }
        catch (MeetingGuestAccessException) { return GuestAccessProblem(); }
        catch (Exception exception) when (Problem(exception) is { } result) { return result; }
    }

    private static async Task<IResult> RunGuestNullable<T>(Func<Task<T?>> action)
    {
        try
        {
            var value = await action();
            return value is null ? Results.NoContent() : Results.Ok(value);
        }
        catch (MeetingGuestAccessException) { return GuestAccessProblem(); }
        catch (Exception exception) when (Problem(exception) is { } result) { return result; }
    }

    private static IResult GuestAccessProblem() => Results.Problem(statusCode: 401,
        title: "دسترسی مهمان معتبر نیست",
        detail: "دعوت‌نامه یا دسترسی مهمان نامعتبر، منقضی یا لغو شده است.",
        extensions: new Dictionary<string, object?> { ["code"] = "MEETING_GUEST_ACCESS_INVALID" });

    private static IResult? Problem(Exception exception) => exception switch
    {
        CollaborationValidationException validation => Results.ValidationProblem(validation.Errors,
            title: "اطلاعات جلسه نیاز به اصلاح دارد."),
        CollaborationConflictException conflict => Results.Problem(statusCode: 409,
            title: "عملیات جلسه قابل انجام نیست", detail: conflict.Message,
            extensions: new Dictionary<string, object?> { ["code"] = conflict.Code }),
        CollaborationNotFoundException missing => Results.Problem(statusCode: 404,
            title: "جلسه یافت نشد", detail: missing.Message,
            extensions: new Dictionary<string, object?> { ["code"] = missing.Code }),
        DbUpdateConcurrencyException => Results.Problem(statusCode: 409,
            title: "اطلاعات هم‌زمان تغییر کرده است", detail: "اطلاعات را تازه‌سازی و دوباره تلاش کنید.",
            extensions: new Dictionary<string, object?> { ["code"] = "MEETING_VERSION_CONFLICT" }),
        CollaborationActorInvalidException => Results.Problem(statusCode: 401,
            title: "نشست کاربری معتبر نیست", detail: "برای ادامه دوباره وارد سامانه شوید.",
            extensions: new Dictionary<string, object?> { ["code"] = "COLLABORATION_ACTOR_INVALID" }),
        UnauthorizedAccessException => Results.Problem(statusCode: 403,
            title: "دسترسی مجاز نیست", detail: "شما مجوز یا نقش لازم برای این عملیات را ندارید.",
            extensions: new Dictionary<string, object?> { ["code"] = "MEETING_ROLE_FORBIDDEN" }),
        MeetingGuestAccessException => GuestAccessProblem(),
        _ => null
    };
}
