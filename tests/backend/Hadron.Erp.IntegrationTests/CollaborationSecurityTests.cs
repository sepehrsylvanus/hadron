using System.Security.Claims;
using Hadron.Erp.Api.Modules.Collaboration.Api;
using Hadron.Erp.Api.Platform.Security;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Hadron.Erp.IntegrationTests;

public sealed class CollaborationActorFactoryTests
{
    [Fact]
    public void Actor_factory_rejects_missing_malformed_and_empty_user_identifiers()
    {
        var missing = new ClaimsPrincipal(new ClaimsIdentity(authenticationType: "test"));
        var malformed = Principal("not-a-guid");
        var empty = Principal(Guid.Empty.ToString());

        Assert.Throws<CollaborationActorInvalidException>(() => CollaborationActorFactory.From(missing));
        Assert.Throws<CollaborationActorInvalidException>(() => CollaborationActorFactory.From(malformed));
        Assert.Throws<CollaborationActorInvalidException>(() => CollaborationActorFactory.From(empty));
    }

    [Fact]
    public void Actor_factory_preserves_authenticated_identity()
    {
        var userId = Guid.NewGuid();
        var identity = new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
            new Claim(SecurityClaimTypes.FullName, "Meeting Security Test")
        }, "test");

        var actor = CollaborationActorFactory.From(new ClaimsPrincipal(identity));

        Assert.Equal(userId, actor.UserId);
        Assert.Equal("Meeting Security Test", actor.DisplayName);
    }

    private static ClaimsPrincipal Principal(string identifier) => new(new ClaimsIdentity(new[]
    {
        new Claim(ClaimTypes.NameIdentifier, identifier)
    }, "test"));
}

public sealed class CollaborationEndpointPermissionTests : IClassFixture<FoundationApiFactory>
{
    private readonly FoundationApiFactory factory;

    public CollaborationEndpointPermissionTests(FoundationApiFactory factory) => this.factory = factory;

    [Fact]
    public void Starting_an_instant_call_requires_both_start_and_join_permissions()
    {
        var endpoint = factory.Services.GetServices<EndpointDataSource>()
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Single(candidate => candidate.RoutePattern.RawText ==
                "/api/collaboration/conversations/{conversationId:guid}/calls");

        var permissions = endpoint.Metadata.GetOrderedMetadata<RequiredPermissionMetadata>()
            .Select(metadata => metadata.Code)
            .ToArray();

        Assert.Contains(Permissions.CollaborationCallStart, permissions);
        Assert.Contains(Permissions.CollaborationCallJoin, permissions);
    }
}
