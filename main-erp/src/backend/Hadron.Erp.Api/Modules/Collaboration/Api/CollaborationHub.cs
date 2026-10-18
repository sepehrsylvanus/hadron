using System.Security.Claims;
using Hadron.Erp.Api.Modules.Collaboration.Application;
using Hadron.Erp.Api.Platform.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Hadron.Erp.Api.Modules.Collaboration.Api;

[Authorize(Policy = Permissions.CollaborationWorkspaceView)]
public sealed class CollaborationHub(CollaborationService service) : Hub
{
    public override async Task OnConnectedAsync()
    {
        var actor = CollaborationActorFactory.From(Context.User ?? new ClaimsPrincipal());
        await Groups.AddToGroupAsync(Context.ConnectionId, CollaborationService.UserGroup(actor.UserId),
            Context.ConnectionAborted);
        await base.OnConnectedAsync();
    }

    public async Task JoinConversation(Guid conversationId)
    {
        var actor = CollaborationActorFactory.From(Context.User ?? new ClaimsPrincipal());
        await service.EnsureConversationAccess(conversationId, actor, Context.ConnectionAborted);
        await Groups.AddToGroupAsync(
            Context.ConnectionId,
            CollaborationService.Group(conversationId),
            Context.ConnectionAborted);
    }

    public Task LeaveConversation(Guid conversationId) =>
        Groups.RemoveFromGroupAsync(
            Context.ConnectionId,
            CollaborationService.Group(conversationId),
            Context.ConnectionAborted);
}

internal static class CollaborationActorFactory
{
    public static CollaborationActor From(ClaimsPrincipal principal)
    {
        var raw = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(raw, out var userId) || userId == Guid.Empty)
            throw new CollaborationActorInvalidException();

        var displayName = principal.FindFirstValue(SecurityClaimTypes.FullName) ?? principal.Identity?.Name ?? "کاربر Hadron";
        return new(userId, displayName, principal.IsInRole(SystemRoleNames.SystemAdministrator));
    }
}

internal sealed class CollaborationActorInvalidException : Exception
{
}
