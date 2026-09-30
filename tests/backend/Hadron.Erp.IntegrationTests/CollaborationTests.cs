using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Hadron.Erp.Api.Infrastructure;
using Hadron.Erp.Api.Modules.Collaboration.Api;
using Hadron.Erp.Api.Modules.Collaboration.Application;
using Hadron.Erp.Api.Modules.Identity.Domain;
using Hadron.Erp.Api.Platform.Security;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;

namespace Hadron.Erp.IntegrationTests;

[Collection("ReferenceData")]
public sealed class CollaborationTests : IClassFixture<FoundationApiFactory>, IAsyncLifetime
{
    private static readonly Guid Primary = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid Recipient = Guid.Parse("00000000-0000-0000-0000-000000000002");
    private static readonly Guid Outsider = Guid.Parse("00000000-0000-0000-0000-000000000003");
    private readonly FoundationApiFactory factory;
    private readonly HttpClient client;

    public CollaborationTests(FoundationApiFactory factory)
    { this.factory = factory; client = factory.CreateClient(); }

    [Fact]
    public void Hub_group_methods_require_only_the_conversation_id_from_the_client()
    {
        foreach (var methodName in new[] { nameof(CollaborationHub.JoinConversation), nameof(CollaborationHub.LeaveConversation) })
        {
            var method = typeof(CollaborationHub).GetMethod(methodName);
            Assert.NotNull(method);
            var parameter = Assert.Single(method!.GetParameters());
            Assert.Equal(typeof(Guid), parameter.ParameterType);
        }
    }

    public async Task InitializeAsync()
    {
        await using var db = await Db();
        await db.Database.MigrateAsync();
        await db.Database.ExecuteSqlRawAsync("TRUNCATE collaboration.conversations RESTART IDENTITY CASCADE");
        await SeedUsers(db);
        factory.AuditSink.Clear();
        factory.MeetingMediaGateway.Reset();
    }
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Twenty_message_flow_supports_edit_reaction_file_delete_privacy_and_audit()
    {
        var channel = await Post<ConversationResponse>(client, "/api/collaboration/conversations/channels", new
        { name = "تیم برنامه‌ریزی تست", description = "سناریوی ارتباطات", isPrivate = true,
            memberUserIds = new[] { Recipient } }, HttpStatusCode.Created);
        Assert.Equal(2, channel.Members.Count);
        channel = await Put<ConversationResponse>(client, $"/api/collaboration/conversations/{channel.Id}", new
        {
            name = "تیم برنامه‌ریزی و تأمین تست", description = "سناریوی ویرایش امن اعضای کانال",
            isPrivate = true, memberUserIds = new[] { Recipient }, expectedVersion = channel.Version
        });
        Assert.Equal("تیم برنامه‌ریزی و تأمین تست", channel.Name);
        Assert.Equal(2, channel.Members.Count);

        var created = new List<MessageResponse>();
        for (var index = 1; index <= 20; index++)
            created.Add(await Post<MessageResponse>(client,
                $"/api/collaboration/conversations/{channel.Id}/messages",
                new { type = "TEXT", body = $"پیام سناریوی انسانی {index:00}", stickerCode = (string?)null,
                    replyToMessageId = (Guid?)null }, HttpStatusCode.Created));

        var page = await client.GetFromJsonAsync<CollaborationPage<MessageResponse>>(
            $"/api/collaboration/conversations/{channel.Id}/messages?pageSize=100");
        Assert.Equal(20, page!.TotalCount);
        Assert.Equal("پیام سناریوی انسانی 01", page.Items.First().Body);
        Assert.Equal("پیام سناریوی انسانی 20", page.Items.Last().Body);

        var edited = await Put<MessageResponse>(client,
            $"/api/collaboration/conversations/{channel.Id}/messages/{created[4].Id}",
            new { body = "پیام شماره پنج پس از اصلاح", expectedVersion = created[4].Version });
        Assert.Equal("پیام شماره پنج پس از اصلاح", edited.Body);
        Assert.NotNull(edited.EditedAtUtc);

        var reactions = await Post<List<ReactionResponse>>(client,
            $"/api/collaboration/conversations/{channel.Id}/messages/{created[4].Id}/reactions",
            new { code = "CHECK" }, HttpStatusCode.OK);
        Assert.Collection(reactions, reaction => { Assert.Equal("CHECK", reaction.Code); Assert.Equal(1, reaction.Count); Assert.True(reaction.ReactedByCurrentUser); });

        var png = new byte[] { 0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a, 1, 2, 3, 4 };
        using (var form = new MultipartFormDataContent())
        {
            var content = new ByteArrayContent(png); content.Headers.ContentType = new("image/png");
            form.Add(content, "file", "human-flow.png");
            var upload = await client.PostAsync($"/api/collaboration/conversations/{channel.Id}/messages/{created[5].Id}/attachments", form);
            Assert.Equal(HttpStatusCode.OK, upload.StatusCode);
            var attachment = (await upload.Content.ReadFromJsonAsync<AttachmentResponse>())!;
            Assert.Equal("TYPE_VALIDATED", attachment.ValidationStatus);
            Assert.Equal(png, await client.GetByteArrayAsync(attachment.DownloadUrl));
        }

        var deleted = await client.PostAsJsonAsync(
            $"/api/collaboration/conversations/{channel.Id}/messages/{created[6].Id}/delete",
            new { expectedVersion = created[6].Version });
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);

        using var outsider = ClientFor(Outsider, "کاربر خارج از کانال");
        Assert.Equal(HttpStatusCode.Forbidden,
            (await outsider.GetAsync($"/api/collaboration/conversations/{channel.Id}")).StatusCode);
        using var denied = ClientFor(Outsider, "بدون مجوز");
        denied.DefaultRequestHeaders.Add("X-Hadron-Test-Denied-Permissions", Permissions.CollaborationWorkspaceView);
        Assert.Equal(HttpStatusCode.Forbidden, (await denied.GetAsync("/api/collaboration/conversations")).StatusCode);

        var audits = factory.AuditSink.Snapshot().Where(x => x.EntityType == "Collaboration").ToArray();
        Assert.Contains(audits, x => x.Action == "Created");
        Assert.Contains(audits, x => x.Action == "Edited");
        Assert.Contains(audits, x => x.Action == "Uploaded");
        Assert.Contains(audits, x => x.Action == "Deleted");
    }

    [Fact]
    public async Task Official_letter_is_editable_only_as_draft_then_immutable_and_event_driven()
    {
        var resolvedDate = await Post<JsonElement>(client, "/api/collaboration/date/resolve-jalali",
            new { jalaliDate = "14050624" }, HttpStatusCode.OK);
        Assert.Equal("2026-09-15", resolvedDate.GetProperty("gregorianDate").GetString());
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(
            "/api/collaboration/date/resolve-jalali", new { jalaliDate = "14051301" })).StatusCode);

        var letter = await Post<OfficialLetterResponse>(client, "/api/collaboration/letters", new
        { subject = "ابلاغ برنامه تست", body = "این نامه یک سند رسمی آزمایشی است.", classification = "CONFIDENTIAL",
            dueDate = "2026-09-15", recipients = new[] { new { userId = Recipient, type = "TO" } } },
            HttpStatusCode.Created);
        Assert.StartsWith("HT-", letter.Number); Assert.Equal("DRAFT", letter.Status);

        var updated = await Put<OfficialLetterResponse>(client, $"/api/collaboration/letters/{letter.Id}", new
        { subject = "ابلاغ اصلاح‌شده برنامه تست", body = "متن بازبینی‌شده پیش از صدور.", classification = "CONFIDENTIAL",
            dueDate = "2026-09-16", expectedVersion = letter.Version,
            recipients = new[] { new { userId = Recipient, type = "TO" } } });
        var issued = await Post<OfficialLetterResponse>(client, $"/api/collaboration/letters/{letter.Id}/issue",
            new { expectedVersion = updated.Version, note = "صدور پس از بازبینی" }, HttpStatusCode.OK);
        Assert.Equal("ISSUED", issued.Status);

        var immutable = await client.PutAsJsonAsync($"/api/collaboration/letters/{letter.Id}", new
        { subject = "تغییر غیرمجاز", body = "نباید ذخیره شود", classification = "NORMAL", dueDate = (string?)null,
            expectedVersion = issued.Version, recipients = new[] { new { userId = Recipient, type = "TO" } } });
        Assert.Equal(HttpStatusCode.Conflict, immutable.StatusCode);
        Assert.Contains("ISSUED_LETTER_IMMUTABLE", await immutable.Content.ReadAsStringAsync());

        using var recipient = ClientFor(Recipient, "گیرنده تست");
        var acknowledged = await Post<OfficialLetterResponse>(recipient,
            $"/api/collaboration/letters/{letter.Id}/acknowledge",
            new { expectedVersion = issued.Version, note = "دریافت شد" }, HttpStatusCode.OK);
        var closed = await Post<OfficialLetterResponse>(client, $"/api/collaboration/letters/{letter.Id}/close",
            new { expectedVersion = acknowledged.Version, note = "فرایند خاتمه یافت" }, HttpStatusCode.OK);
        Assert.Equal("CLOSED", closed.Status);
        Assert.Equal(new[] { "CREATED", "UPDATED", "ISSUED", "ACKNOWLEDGED", "CLOSED" },
            closed.Events.OrderBy(x => x.OccurredAtUtc).Select(x => x.EventType));
    }

    [Fact]
    public async Task Global_notification_summary_is_user_scoped_actionable_and_clears_after_read_or_transition()
    {
        var direct = await Post<ConversationResponse>(client, "/api/collaboration/conversations/direct",
            new { otherUserId = Recipient }, HttpStatusCode.Created);
        await Post<MessageResponse>(client, $"/api/collaboration/conversations/{direct.Id}/messages",
            new { type = "TEXT", body = "پیام تازه برای مرکز ارتباطات", stickerCode = (string?)null,
                replyToMessageId = (Guid?)null }, HttpStatusCode.Created);

        using var recipient = ClientFor(Recipient, "گیرنده تست");
        var recipientSummary = await recipient.GetFromJsonAsync<CollaborationNotificationSummaryResponse>(
            "/api/collaboration/notifications?limit=12");
        Assert.NotNull(recipientSummary);
        Assert.Equal(1, recipientSummary.UnreadMessageCount);
        Assert.Equal("MESSAGE", Assert.Single(recipientSummary.Items).Kind);
        Assert.Equal("Hadron Test User", recipientSummary.Items[0].Title);
        Assert.Equal($"/collaboration/messages/{direct.Id}", recipientSummary.Items[0].TargetUrl);

        var senderSummary = await client.GetFromJsonAsync<CollaborationNotificationSummaryResponse>(
            "/api/collaboration/notifications?limit=12");
        Assert.Equal(0, senderSummary!.UnreadMessageCount);

        var letter = await Post<OfficialLetterResponse>(client, "/api/collaboration/letters", new
        {
            subject = "نامه اقدام‌پذیر مرکز ارتباطات", body = "دریافت این نامه باید اعلام شود.",
            classification = "NORMAL", dueDate = (string?)null,
            recipients = new[] { new { userId = Recipient, type = "TO" } }
        }, HttpStatusCode.Created);
        var issued = await Post<OfficialLetterResponse>(client, $"/api/collaboration/letters/{letter.Id}/issue",
            new { expectedVersion = letter.Version, note = (string?)null }, HttpStatusCode.OK);

        recipientSummary = await recipient.GetFromJsonAsync<CollaborationNotificationSummaryResponse>(
            "/api/collaboration/notifications?limit=12");
        Assert.Equal(1, recipientSummary!.UnreadMessageCount);
        Assert.Equal(1, recipientSummary.PendingLetterCount);
        Assert.Equal(2, recipientSummary.TotalCount);
        Assert.Contains(recipientSummary.Items, item => item.Kind == "LETTER" && item.EntityId == letter.Id &&
            item.RequiresAction);

        Assert.Equal(HttpStatusCode.NoContent,
            (await recipient.PostAsync($"/api/collaboration/conversations/{direct.Id}/read", null)).StatusCode);
        var acknowledged = await Post<OfficialLetterResponse>(recipient,
            $"/api/collaboration/letters/{letter.Id}/acknowledge",
            new { expectedVersion = issued.Version, note = (string?)null }, HttpStatusCode.OK);

        recipientSummary = await recipient.GetFromJsonAsync<CollaborationNotificationSummaryResponse>(
            "/api/collaboration/notifications?limit=12");
        Assert.Equal(0, recipientSummary!.TotalCount);
        senderSummary = await client.GetFromJsonAsync<CollaborationNotificationSummaryResponse>(
            "/api/collaboration/notifications?limit=12");
        Assert.Equal(1, senderSummary!.PendingLetterCount);
        Assert.Contains(senderSummary.Items, item => item.EntityId == acknowledged.Id && item.Kind == "LETTER");
    }

    [Fact]
    public async Task Invalid_files_and_unconfigured_calls_fail_safely_without_breaking_messages()
    {
        var channel = await Post<ConversationResponse>(client, "/api/collaboration/conversations/channels",
            new { name = "پایداری", description = (string?)null, isPrivate = false, memberUserIds = Array.Empty<Guid>() },
            HttpStatusCode.Created);
        var message = await Post<MessageResponse>(client, $"/api/collaboration/conversations/{channel.Id}/messages",
            new { type = "TEXT", body = "پیام سالم باقی می‌ماند", stickerCode = (string?)null,
                replyToMessageId = (Guid?)null }, HttpStatusCode.Created);
        using (var form = new MultipartFormDataContent())
        {
            var content = new ByteArrayContent(Encoding.UTF8.GetBytes("not-a-png"));
            content.Headers.ContentType = new("image/png"); form.Add(content, "file", "fake.png");
            var response = await client.PostAsync($"/api/collaboration/conversations/{channel.Id}/messages/{message.Id}/attachments", form);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Contains("مطابقت ندارد", await response.Content.ReadAsStringAsync());
        }
        using var unconfiguredFactory = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<LiveKitOptions>();
            services.AddSingleton(new LiveKitOptions());
        }));
        using var unconfiguredClient = unconfiguredFactory.CreateClient();
        Assert.False((await unconfiguredClient.GetFromJsonAsync<CallConfigurationResponse>(
            "/api/collaboration/calls/configuration"))!.Available);
        Assert.Equal(HttpStatusCode.Conflict, (await unconfiguredClient.PostAsJsonAsync(
            $"/api/collaboration/conversations/{channel.Id}/calls", new { videoEnabled = true })).StatusCode);
        var page = await client.GetFromJsonAsync<CollaborationPage<MessageResponse>>(
            $"/api/collaboration/conversations/{channel.Id}/messages?pageSize=20");
        Assert.Single(page!.Items); Assert.Equal("پیام سالم باقی می‌ماند", page.Items[0].Body);
    }

    [Fact]
    public async Task Concurrent_call_starts_return_one_database_backed_active_session()
    {
        using var configuredFactory = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<LiveKitOptions>();
            services.AddSingleton(new LiveKitOptions
            {
                ServerUrl = "wss://calls.hadron.test",
                ApiKey = "hadron-test-key",
                ApiSecret = "collaboration-test-secret-with-sufficient-entropy"
            });
        }));
        using var configuredClient = configuredFactory.CreateClient();
        var channel = await Post<ConversationResponse>(configuredClient, "/api/collaboration/conversations/channels", new
        {
            name = "همزمانی تماس",
            description = "تضمین یک تماس فعال",
            isPrivate = true,
            memberUserIds = new[] { Recipient }
        }, HttpStatusCode.Created);

        var starts = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ =>
            configuredClient.PostAsJsonAsync($"/api/collaboration/conversations/{channel.Id}/calls",
                new { videoEnabled = true })));
        Assert.All(starts, response => Assert.Equal(HttpStatusCode.Created, response.StatusCode));
        var joins = await Task.WhenAll(starts.Select(response => response.Content.ReadFromJsonAsync<CallJoinResponse>()));
        var callId = Assert.Single(joins.Select(result => result!.Call.Id).Distinct());
        Assert.Single(joins, result => result!.CreatedNew);

        await using var db = await Db();
        Assert.Equal(1, await db.CollaborationCallSessions.CountAsync(x =>
            x.ConversationId == channel.Id && x.Status == "ACTIVE"));
        Assert.Equal(callId, await db.CollaborationCallSessions
            .Where(x => x.ConversationId == channel.Id && x.Status == "ACTIVE")
            .Select(x => x.Id).SingleAsync());
        Assert.Equal(1, await db.CollaborationMessages.CountAsync(x =>
            x.ConversationId == channel.Id && x.Type == "CALL_EVENT" && x.Body == "تماس تصویری آغاز شد."));
        Assert.Single(factory.AuditSink.Snapshot(), x =>
            x.EntityType == "Collaboration" && x.EntityId == callId && x.Action == "Started");
    }

    [Fact]
    public async Task Ending_an_instant_call_terminates_the_provider_room_before_persisting_ended_state()
    {
        using var configuredFactory = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<LiveKitOptions>();
            services.AddSingleton(new LiveKitOptions
            {
                ServerUrl = "wss://calls.hadron.test",
                ApiKey = "hadron-test-key",
                ApiSecret = "collaboration-test-secret-with-sufficient-entropy"
            });
        }));
        using var configuredClient = configuredFactory.CreateClient();
        var channel = await Post<ConversationResponse>(configuredClient,
            "/api/collaboration/conversations/channels", new
            {
                name = "پایان قطعی تماس",
                description = "اتاق رسانه باید پیش از ثبت پایان بسته شود",
                isPrivate = false,
                memberUserIds = Array.Empty<Guid>()
            }, HttpStatusCode.Created);
        var started = await Post<CallJoinResponse>(configuredClient,
            $"/api/collaboration/conversations/{channel.Id}/calls", new { videoEnabled = true },
            HttpStatusCode.Created);

        factory.MeetingMediaGateway.EndResult = MeetingRoomEndResult.Failed("PROVIDER_TIMEOUT");
        var failed = await configuredClient.PostAsJsonAsync(
            $"/api/collaboration/calls/{started.Call.Id}/end",
            new { expectedVersion = started.Call.Version });
        Assert.Equal(HttpStatusCode.Conflict, failed.StatusCode);
        Assert.Contains("CALL_PROVIDER_END_FAILED", await failed.Content.ReadAsStringAsync());
        await using (var failedDb = await Db())
            Assert.Equal("ACTIVE", (await failedDb.CollaborationCallSessions
                .SingleAsync(x => x.Id == started.Call.Id)).Status);

        factory.MeetingMediaGateway.EndResult = MeetingRoomEndResult.Ended();
        var ended = await configuredClient.PostAsJsonAsync(
            $"/api/collaboration/calls/{started.Call.Id}/end",
            new { expectedVersion = started.Call.Version });
        Assert.Equal(HttpStatusCode.NoContent, ended.StatusCode);
        Assert.Equal(new[] { started.Call.RoomName, started.Call.RoomName },
            factory.MeetingMediaGateway.EndedRooms);
        await using var db = await Db();
        Assert.Equal("ENDED", (await db.CollaborationCallSessions
            .SingleAsync(x => x.Id == started.Call.Id)).Status);
    }

    [Fact]
    public async Task Database_allows_only_one_active_call_per_conversation_but_preserves_ended_history()
    {
        var channel = await Post<ConversationResponse>(client, "/api/collaboration/conversations/channels", new
        {
            name = "قید تماس فعال",
            description = (string?)null,
            isPrivate = false,
            memberUserIds = Array.Empty<Guid>()
        }, HttpStatusCode.Created);

        var first = new Hadron.Erp.Api.Modules.Collaboration.Domain.CallSession(
            channel.Id, $"constraint-{Guid.NewGuid():N}", Primary, "Hadron Test User", false);
        await using (var firstDb = await Db())
        {
            firstDb.CollaborationCallSessions.Add(first);
            await firstDb.SaveChangesAsync();
        }

        await using (var duplicateDb = await Db())
        {
            duplicateDb.CollaborationCallSessions.Add(new Hadron.Erp.Api.Modules.Collaboration.Domain.CallSession(
                channel.Id, $"constraint-{Guid.NewGuid():N}", Primary, "Hadron Test User", true));
            var failure = await Assert.ThrowsAsync<DbUpdateException>(() => duplicateDb.SaveChangesAsync());
            var postgres = failure.InnerException as PostgresException ?? failure.GetBaseException() as PostgresException;
            Assert.NotNull(postgres);
            Assert.Equal(PostgresErrorCodes.UniqueViolation, postgres.SqlState);
            Assert.Equal("ux_call_sessions_one_active_per_conversation", postgres.ConstraintName);
        }

        await using (var endDb = await Db())
        {
            var active = await endDb.CollaborationCallSessions.SingleAsync(x => x.Id == first.Id);
            active.End(active.Version);
            await endDb.SaveChangesAsync();
        }
        await using (var replacementDb = await Db())
        {
            replacementDb.CollaborationCallSessions.Add(new Hadron.Erp.Api.Modules.Collaboration.Domain.CallSession(
                channel.Id, $"constraint-{Guid.NewGuid():N}", Primary, "Hadron Test User", true));
            await replacementDb.SaveChangesAsync();
        }

        await using var verify = await Db();
        Assert.Equal(2, await verify.CollaborationCallSessions.CountAsync(x => x.ConversationId == channel.Id));
        Assert.Equal(1, await verify.CollaborationCallSessions.CountAsync(x =>
            x.ConversationId == channel.Id && x.Status == "ACTIVE"));
        Assert.Equal(1, await verify.CollaborationCallSessions.CountAsync(x =>
            x.ConversationId == channel.Id && x.Status == "ENDED"));
    }

    [Fact]
    public void Every_collaboration_endpoint_is_permission_protected_or_an_explicit_rate_limited_guest_route()
    {
        var endpoints = factory.Services.GetServices<EndpointDataSource>().SelectMany(x => x.Endpoints)
            .OfType<RouteEndpoint>().Where(x => x.RoutePattern.RawText?.StartsWith("/api/collaboration") == true).ToArray();
        Assert.NotEmpty(endpoints);
        Assert.All(endpoints, endpoint =>
        {
            if (endpoint.Metadata.GetMetadata<IAllowAnonymous>() is not null)
            {
                Assert.StartsWith("/api/collaboration/meeting-guests/", endpoint.RoutePattern.RawText,
                    StringComparison.Ordinal);
                Assert.NotNull(endpoint.Metadata.GetMetadata<EnableRateLimitingAttribute>());
                Assert.NotEmpty(endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods ?? []);
                return;
            }
            var permission = endpoint.Metadata.GetMetadata<RequiredPermissionMetadata>();
            Assert.NotNull(permission); Assert.Contains(permission!.Code, PermissionCatalog.Codes);
            Assert.NotEmpty(endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods ?? []);
        });
    }

    private HttpClient ClientFor(Guid id, string name)
    {
        var result = factory.CreateClient(); result.DefaultRequestHeaders.Add("X-Hadron-Test-User-Id", id.ToString());
        result.DefaultRequestHeaders.Add("X-Hadron-Test-Full-Name", name); return result;
    }
    private async Task<HadronDbContext> Db() => await factory.Services
        .GetRequiredService<IDbContextFactory<HadronDbContext>>().CreateDbContextAsync();
    private static async Task SeedUsers(HadronDbContext db)
    {
        var ids = new[] { Primary, Recipient, Outsider };
        if (await db.Users.CountAsync(x => ids.Contains(x.Id)) == ids.Length) return;
        var unit = new OrganizationalUnit("واحد ارتباطات تست", "Collaboration test"); db.OrganizationalUnits.Add(unit);
        var now = DateTime.UtcNow;
        db.Users.AddRange(User(Primary, "Hadron", "Test User", "collaboration-primary", unit.Id, now),
            User(Recipient, "گیرنده", "تست", "collaboration-recipient", unit.Id, now),
            User(Outsider, "خارج", "کانال", "collaboration-outsider", unit.Id, now));
        await db.SaveChangesAsync();
    }
    private static ApplicationUser User(Guid id, string first, string last, string name, Guid unitId, DateTime now) => new()
    {
        Id = id, UserName = name, NormalizedUserName = name.ToUpperInvariant(), Email = $"{name}@example.test",
        NormalizedEmail = $"{name}@example.test".ToUpperInvariant(), EmailConfirmed = true, FirstName = first,
        LastName = last, PhoneNumber = "+980000000000", PhoneNumberConfirmed = true, OrganizationalUnitId = unitId,
        IsActive = true, MustChangePassword = false, AccessVersion = 1, ProfilePhotoObjectKey = $"tests/{name}.png",
        ProfilePhotoContentType = "image/png", ProfilePhotoSize = 1, ProfilePhotoSha256 = new string('0', 64),
        SecurityStamp = Guid.NewGuid().ToString("N"), ConcurrencyStamp = Guid.NewGuid().ToString("N"),
        CreatedAtUtc = now, UpdatedAtUtc = now
    };
    private static async Task<T> Post<T>(HttpClient source, string url, object payload, HttpStatusCode expected)
    {
        var response = await source.PostAsJsonAsync(url, payload);
        Assert.True(response.StatusCode == expected, $"Expected {expected}, got {response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }
    private static async Task<T> Put<T>(HttpClient source, string url, object payload)
    {
        var response = await source.PutAsJsonAsync(url, payload);
        Assert.True(response.IsSuccessStatusCode, $"PUT failed {response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }
}
