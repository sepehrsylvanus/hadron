using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Hadron.Erp.Api.Infrastructure;
using Hadron.Erp.Api.Modules.Identity.Domain;
using Hadron.Erp.Api.Platform.Security;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Hadron.Erp.IntegrationTests;

public sealed class IdentitySecurityTests
{
    [Fact]
    public async Task Bootstrap_login_forced_password_change_and_dynamic_system_admin_permissions_work()
    {
        var factory = await NewFactory();
        try
        {
            using var client = factory.CreateClient();
            var boot = await Bootstrap(client, "root-admin");
            Assert.True(boot.StatusCode == HttpStatusCode.Created, await boot.Content.ReadAsStringAsync());

            var login = await Login(client, "root-admin", "TempPass!234");
            Assert.Equal(HttpStatusCode.OK, login.StatusCode);
            var loginBody = await login.Content.ReadFromJsonAsync<CurrentUserContract>();
            Assert.True(loginBody!.MustChangePassword);
            Assert.True(loginBody.IsSystemAdministrator);
            Assert.Equal(PermissionCatalog.Codes.Count, loginBody.Permissions.Count);
            Assert.Collection(loginBody.DataScopes, scope =>
            {
                Assert.Equal(DataScopeTypes.All, scope.ScopeType);
                Assert.Null(scope.ScopeValue);
            });

            var blocked = await client.GetAsync("/api/products?page=1&pageSize=1");
            Assert.Equal(HttpStatusCode.Forbidden, blocked.StatusCode);
            Assert.Contains("PASSWORD_CHANGE_REQUIRED", await blocked.Content.ReadAsStringAsync());

            var changed = await SendJson(client, HttpMethod.Post, "/api/auth/change-password",
                new { currentPassword = "TempPass!234", newPassword = "NewSecure!234" });
            Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
            var relogin = await Login(client, "root-admin", "NewSecure!234");
            Assert.Equal(HttpStatusCode.OK, relogin.StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/admin/permissions")).StatusCode);

            var lastAdmin = await SendJson(client, HttpMethod.Post, $"/api/admin/users/{loginBody.Id}/deactivate", new { });
            Assert.Equal(HttpStatusCode.Conflict, lastAdmin.StatusCode);
            Assert.Contains("LAST_SYSTEM_ADMINISTRATOR", await lastAdmin.Content.ReadAsStringAsync());
        }
        finally { await ((IAsyncLifetime)factory).DisposeAsync(); }
    }

    [Fact]
    public async Task Create_user_with_missing_profile_fields_returns_actionable_validation_problem()
    {
        var factory = await NewFactory();
        try
        {
            using var client = factory.CreateClient();
            Assert.Equal(HttpStatusCode.Created, (await Bootstrap(client, "validation-admin")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await Login(client, "validation-admin", "TempPass!234")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await SendJson(client, HttpMethod.Post, "/api/auth/change-password",
                new { currentPassword = "TempPass!234", newPassword = "Validation!234" })).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await Login(client, "validation-admin", "Validation!234")).StatusCode);

            using var form = new MultipartFormDataContent();
            form.Add(new StringContent("true"), "isActive");
            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/admin/users") { Content = form };
            request.Headers.Add("X-CSRF-TOKEN", await Csrf(client));

            var response = await client.SendAsync(request);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            var problem = await response.Content.ReadFromJsonAsync<ValidationProblemContract>();
            Assert.NotNull(problem);
            Assert.Equal("IDENTITY_VALIDATION_FAILED", problem!.Code);
            Assert.Equal("نام کاربری: این فیلد الزامی است.", problem.Title);
            Assert.Collection(problem.Errors["userName"], message => Assert.Equal("این فیلد الزامی است.", message));

            var users = await client.GetFromJsonAsync<PagedUsersContract>("/api/admin/users?page=1&pageSize=25");
            Assert.Single(users!.Items);
        }
        finally { await ((IAsyncLifetime)factory).DisposeAsync(); }
    }

    [Fact]
    public async Task Security_stamp_refresh_preserves_the_active_server_session()
    {
        var factory = await NewFactory();
        try
        {
            using var client = factory.CreateClient();
            Assert.Equal(HttpStatusCode.Created, (await Bootstrap(client, "session-refresh-admin")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await Login(client, "session-refresh-admin", "TempPass!234")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await SendJson(client, HttpMethod.Post, "/api/auth/change-password",
                new { currentPassword = "TempPass!234", newPassword = "SessionRefresh!234" })).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await Login(client, "session-refresh-admin", "SessionRefresh!234")).StatusCode);

            for (var request = 0; request < 3; request++)
            {
                var current = await client.GetAsync("/api/auth/me");
                Assert.True(current.StatusCode == HttpStatusCode.OK,
                    $"Security-stamp refresh dropped the server session on request {request + 1}: {await current.Content.ReadAsStringAsync()}");
            }
        }
        finally { await ((IAsyncLifetime)factory).DisposeAsync(); }
    }

    [Fact]
    public async Task Role_grant_direct_deny_and_session_invalidation_are_enforced_by_backend()
    {
        var factory = await NewFactory();
        try
        {
            using var admin = factory.CreateClient();
            var bootstrap = await Bootstrap(admin, "root-admin");
            Assert.True(bootstrap.StatusCode == HttpStatusCode.Created, await bootstrap.Content.ReadAsStringAsync());
            Assert.Equal(HttpStatusCode.OK, (await Login(admin, "root-admin", "TempPass!234")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await SendJson(admin, HttpMethod.Post, "/api/auth/change-password", new { currentPassword = "TempPass!234", newPassword = "NewSecure!234" })).StatusCode);
            await Login(admin, "root-admin", "NewSecure!234");
            var administrator = (await admin.GetFromJsonAsync<CurrentUserContract>("/api/auth/me"))!;
            var roleResponse = await SendJson(admin, HttpMethod.Post, "/api/admin/roles",
                new { name = "ReferenceReader", descriptionFa = "مشاهده اطلاعات مرجع", descriptionEn = "Reference reader" });
            Assert.Equal(HttpStatusCode.Created, roleResponse.StatusCode);
            var role = await roleResponse.Content.ReadFromJsonAsync<RoleContract>();
            Assert.NotNull(role);
            Assert.Equal(HttpStatusCode.NoContent, (await SendJson(admin, HttpMethod.Put,
                $"/api/admin/roles/{role!.Id}/permissions", new { permissionCodes = new[] { Permissions.ReferenceDataView } })).StatusCode);

            var userResponse = await CreateUser(admin, "reference-user");
            Assert.Equal(HttpStatusCode.Created, userResponse.StatusCode);
            var detail = await userResponse.Content.ReadFromJsonAsync<UserDetailContract>();
            Assert.NotNull(detail);
            Assert.Empty(detail!.DataScopes);
            Assert.Equal(HttpStatusCode.NoContent, (await SendJson(admin, HttpMethod.Put,
                $"/api/admin/users/{detail.User.Id}/roles", new { roleIds = new[] { role.Id } })).StatusCode);

            using var user = factory.CreateClient();
            Assert.Equal(HttpStatusCode.OK, (await Login(user, "reference-user", "TempPass!234")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await SendJson(user, HttpMethod.Post, "/api/auth/change-password",
                new { currentPassword = "TempPass!234", newPassword = "ReaderPass!234" })).StatusCode);
            var readerLogin = await Login(user, "reference-user", "ReaderPass!234");
            Assert.Equal(HttpStatusCode.OK, readerLogin.StatusCode);
            var reader = (await readerLogin.Content.ReadFromJsonAsync<CurrentUserContract>())!;
            Assert.Contains(Permissions.ReferenceDataView, reader.Permissions);
            Assert.Equal(HttpStatusCode.OK, (await user.GetAsync($"/api/auth/users/{reader.Id}/photo")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await user.GetAsync($"/api/auth/users/{administrator.Id}/photo")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await user.GetAsync("/api/reference-data/types")).StatusCode);
            var mutation = await SendJson(user, HttpMethod.Post, "/api/reference-data/types/COUNTRY/values",
                new { nameFa = "آزمایشی", nameEn = "Test", sortOrder = 1 });
            Assert.Equal(HttpStatusCode.Forbidden, mutation.StatusCode);

            Assert.Equal(HttpStatusCode.NoContent, (await SendJson(admin, HttpMethod.Put,
                $"/api/admin/users/{detail.User.Id}/permission-overrides",
                new
                {
                    overrides = new[]
                    {
                        new { permissionCode = Permissions.ReferenceDataView, effect = "DENY" },
                        new { permissionCode = Permissions.IdentityProfileViewOwn, effect = "DENY" }
                    }
                })).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await user.GetAsync("/api/reference-data/types")).StatusCode);
            var deniedLogin = await Login(user, "reference-user", "ReaderPass!234");
            Assert.Equal(HttpStatusCode.OK, deniedLogin.StatusCode);
            Assert.DoesNotContain(Permissions.ReferenceDataView, (await deniedLogin.Content.ReadFromJsonAsync<CurrentUserContract>())!.Permissions);
            Assert.Equal(HttpStatusCode.Forbidden, (await user.GetAsync("/api/reference-data/types")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await user.GetAsync("/api/auth/me")).StatusCode);
            Assert.Equal(HttpStatusCode.NoContent,
                (await SendJson(user, HttpMethod.Post, "/api/auth/logout", new { })).StatusCode);
        }
        finally { await ((IAsyncLifetime)factory).DisposeAsync(); }
    }

    [Fact]
    public async Task Custom_role_with_the_complete_catalog_exposes_every_effective_permission_to_its_user()
    {
        var factory = await NewFactory();
        try
        {
            using var admin = factory.CreateClient();
            Assert.Equal(HttpStatusCode.Created, (await Bootstrap(admin, "catalog-admin")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await Login(admin, "catalog-admin", "TempPass!234")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await SendJson(admin, HttpMethod.Post, "/api/auth/change-password",
                new { currentPassword = "TempPass!234", newPassword = "CatalogAdmin!234" })).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await Login(admin, "catalog-admin", "CatalogAdmin!234")).StatusCode);

            var expected = PermissionCatalog.Codes.Order(StringComparer.Ordinal).ToArray();
            var roleResponse = await SendJson(admin, HttpMethod.Post, "/api/admin/roles", new
            {
                name = "CompleteCatalogAccess",
                descriptionFa = "تمام مجوزهای ثبت شده",
                descriptionEn = "Every registered permission",
                permissionCodes = expected
            });
            Assert.True(roleResponse.StatusCode == HttpStatusCode.Created,
                await roleResponse.Content.ReadAsStringAsync());
            var role = (await roleResponse.Content.ReadFromJsonAsync<RoleContract>())!;

            var definitionResponse = await SendJson(admin, HttpMethod.Put,
                $"/api/admin/roles/{role.Id}/definition", new
                {
                    name = "CompleteCatalogAccess",
                    descriptionFa = "تمام مجوزهای ثبت شده و بازبینی شده",
                    descriptionEn = "Every registered permission, reviewed",
                    permissionCodes = expected
                });
            Assert.True(definitionResponse.StatusCode == HttpStatusCode.OK,
                await definitionResponse.Content.ReadAsStringAsync());

            var permissionsResponse = await SendJson(admin, HttpMethod.Put,
                $"/api/admin/roles/{role.Id}/permissions", new { permissionCodes = expected });
            Assert.True(permissionsResponse.StatusCode == HttpStatusCode.NoContent,
                await permissionsResponse.Content.ReadAsStringAsync());

            var audit = await admin.GetFromJsonAsync<PagedSecurityAuditContract>(
                "/api/admin/security-audit?search=ROLE_&page=1&pageSize=100");
            foreach (var action in new[]
                     {
                         "ROLE_CREATED",
                         "ROLE_DEFINITION_UPDATED",
                         "ROLE_PERMISSIONS_REPLACED"
                     })
            {
                var auditEvent = Assert.Single(audit!.Items, item => item.Action == action);
                Assert.NotNull(auditEvent.Detail);
                Assert.InRange(auditEvent.Detail!.Length, 1, 4000);
                using var auditDetail = JsonDocument.Parse(auditEvent.Detail);
                var permissions = auditDetail.RootElement.GetProperty("Permissions");
                Assert.Equal(expected.Length, permissions.GetProperty("Count").GetInt32());
                Assert.Equal(64, permissions.GetProperty("Sha256").GetString()!.Length);
                Assert.Equal(10, permissions.GetProperty("Sample").GetArrayLength());
                Assert.True(permissions.GetProperty("IsSampleTruncated").GetBoolean());
            }

            var userResponse = await CreateUser(admin, "complete-catalog-user", [role.Id]);
            Assert.Equal(HttpStatusCode.Created, userResponse.StatusCode);
            var created = (await userResponse.Content.ReadFromJsonAsync<UserDetailContract>())!;

            var detail = await admin.GetFromJsonAsync<UserDetailWithAccessContract>(
                $"/api/admin/users/{created.User.Id}");
            Assert.Equal(expected.Length, detail!.EffectiveAccess.Count);
            Assert.All(detail.EffectiveAccess, decision =>
            {
                Assert.True(decision.IsGranted, decision.Code);
                Assert.Equal("ROLE", decision.Resolution);
                Assert.Contains("CompleteCatalogAccess", decision.Sources);
            });

            using var user = factory.CreateClient();
            var login = await Login(user, "complete-catalog-user", "TempPass!234");
            Assert.Equal(HttpStatusCode.OK, login.StatusCode);
            var current = (await login.Content.ReadFromJsonAsync<CurrentUserContract>())!;
            Assert.False(current.IsSystemAdministrator);
            Assert.Equal(expected, current.Permissions.Order(StringComparer.Ordinal));
        }
        finally { await ((IAsyncLifetime)factory).DisposeAsync(); }
    }

    [Fact]
    public void Meeting_v1_permissions_are_individually_registered_with_stable_codes()
    {
        var expected = new[]
        {
            Permissions.CollaborationMeetingView,
            Permissions.CollaborationMeetingSchedule,
            Permissions.CollaborationMeetingStart,
            Permissions.CollaborationMeetingJoin,
            Permissions.CollaborationMeetingManageParticipants,
            Permissions.CollaborationMeetingEnd,
            Permissions.CollaborationMeetingInviteGuest,
            Permissions.CollaborationMeetingReport
        };
        var stableCodes = new[]
        {
            "collaboration.meeting.view",
            "collaboration.meeting.schedule",
            "collaboration.meeting.start",
            "collaboration.meeting.join",
            "collaboration.meeting.manage_participants",
            "collaboration.meeting.end",
            "collaboration.meeting.invite_guest",
            "collaboration.meeting.report"
        };

        Assert.Equal(stableCodes, expected);
        var definitions = PermissionCatalog.All
            .Where(permission => expected.Contains(permission.Code, StringComparer.Ordinal))
            .ToArray();
        Assert.Equal(expected.Length, definitions.Length);
        Assert.All(definitions, permission =>
        {
            Assert.Equal("COLLABORATION", permission.Area);
            Assert.False(string.IsNullOrWhiteSpace(permission.NameFa));
            Assert.False(string.IsNullOrWhiteSpace(permission.NameEn));
        });
    }

    [Fact]
    public async Task Every_business_api_endpoint_is_permission_registered_and_catalog_codes_are_unique()
    {
        var factory = await NewFactory();
        try
        {
            using var client = factory.CreateClient();
            Assert.Equal(PermissionCatalog.All.Count, PermissionCatalog.Codes.Count);
            Assert.Equal(PermissionCatalog.All.Count,
                PermissionCatalog.All.Select(permission => permission.Code).Distinct(StringComparer.Ordinal).Count());
            var endpoints = factory.Services.GetServices<EndpointDataSource>().SelectMany(x => x.Endpoints)
                .OfType<RouteEndpoint>().Where(x => x.RoutePattern.RawText?.StartsWith("/api", StringComparison.Ordinal) == true)
                .ToList();
            Assert.NotEmpty(endpoints);
            var explicitPermissionExceptions = new HashSet<string>(StringComparer.Ordinal)
            {
                "/api/auth/csrf",
                "/api/auth/bootstrap-status",
                "/api/auth/bootstrap",
                "/api/auth/login",
                "/api/auth/logout",
                "/api/collaboration/meeting-guests/preview",
                "/api/collaboration/meeting-guests/redeem",
                "/api/collaboration/meeting-guests/status",
                "/api/collaboration/meeting-guests/join",
                "/api/collaboration/meeting-guests/connected",
                "/api/collaboration/meeting-guests/disconnected"
            };
            foreach (var endpoint in endpoints)
            {
                var route = endpoint.RoutePattern.RawText!;
                if (explicitPermissionExceptions.Contains(route)) continue;

                var permissions = endpoint.Metadata.GetOrderedMetadata<RequiredPermissionMetadata>();
                Assert.True(permissions.Count > 0, $"Missing explicit permission metadata: {route}");
                Assert.All(permissions, permission => Assert.Contains(permission.Code, PermissionCatalog.Codes));
                Assert.Equal(permissions.Count,
                    permissions.Select(permission => permission.Code).Distinct(StringComparer.Ordinal).Count());
            }

            static RouteEndpoint FindEndpoint(IEnumerable<RouteEndpoint> candidates, string route, string method) =>
                candidates.Single(endpoint =>
                    string.Equals(endpoint.RoutePattern.RawText?.TrimEnd('/'), route.TrimEnd('/'),
                        StringComparison.Ordinal) &&
                    endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods.Contains(method,
                        StringComparer.OrdinalIgnoreCase) == true);

            var createUserPermissions = FindEndpoint(endpoints, "/api/admin/users", HttpMethods.Post)
                .Metadata.GetOrderedMetadata<RequiredPermissionMetadata>()
                .Select(metadata => metadata.Code).ToHashSet(StringComparer.Ordinal);
            Assert.Contains(Permissions.IdentityUserManage, createUserPermissions);
            Assert.DoesNotContain(Permissions.IdentityPermissionAssign, createUserPermissions);

            var createRolePermissions = FindEndpoint(endpoints, "/api/admin/roles", HttpMethods.Post)
                .Metadata.GetOrderedMetadata<RequiredPermissionMetadata>()
                .Select(metadata => metadata.Code).ToHashSet(StringComparer.Ordinal);
            Assert.Contains(Permissions.IdentityRoleManage, createRolePermissions);
            Assert.DoesNotContain(Permissions.IdentityPermissionAssign, createRolePermissions);

            Assert.All(explicitPermissionExceptions, route => Assert.Contains(endpoints,
                endpoint => string.Equals(endpoint.RoutePattern.RawText, route, StringComparison.Ordinal)));

            var nextWasCalled = false;
            var middleware = new ApiPermissionMiddleware(_ =>
            {
                nextWasCalled = true;
                return Task.CompletedTask;
            });
            var context = new DefaultHttpContext();
            context.Request.Path = "/api/permission-contract-probe";
            context.Response.Body = new MemoryStream();
            context.SetEndpoint(new Endpoint(_ => Task.CompletedTask,
                EndpointMetadataCollection.Empty, "permission contract probe"));

            await middleware.InvokeAsync(context);

            Assert.Equal(StatusCodes.Status403Forbidden, context.Response.StatusCode);
            Assert.False(nextWasCalled);
            context.Response.Body.Position = 0;
            using var reader = new StreamReader(context.Response.Body);
            Assert.Contains("UNREGISTERED_API_PERMISSION", await reader.ReadToEndAsync());
        }
        finally { await ((IAsyncLifetime)factory).DisposeAsync(); }
    }

    [Fact]
    public async Task System_administrator_can_delegate_full_authority_and_last_active_admin_remains_protected()
    {
        var factory = await NewFactory();
        try
        {
            using var first = factory.CreateClient();
            Assert.Equal(HttpStatusCode.Created, (await Bootstrap(first, "first-admin")).StatusCode);
            await Login(first, "first-admin", "TempPass!234");
            Assert.Equal(HttpStatusCode.OK, (await SendJson(first, HttpMethod.Post, "/api/auth/change-password", new { currentPassword = "TempPass!234", newPassword = "FirstAdmin!234" })).StatusCode);
            await Login(first, "first-admin", "FirstAdmin!234");
            var systemRole = (await first.GetFromJsonAsync<List<RoleFullContract>>("/api/admin/roles"))!.Single(x => x.IsSystemRole);
            var secondResponse = await CreateUser(first, "second-admin");
            var secondDetail = (await secondResponse.Content.ReadFromJsonAsync<UserDetailContract>())!;
            Assert.Empty(secondDetail.DataScopes);
            var second = secondDetail.User;
            Assert.Equal(HttpStatusCode.NoContent, (await SendJson(first, HttpMethod.Put,
                $"/api/admin/users/{second.Id}/permission-overrides", new
                {
                    overrides = new[]
                    {
                        new { permissionCode = Permissions.ReferenceDataView, effect = PermissionEffects.Deny }
                    }
                })).StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, (await SendJson(first, HttpMethod.Put, $"/api/admin/users/{second.Id}/roles", new { roleIds = new[] { systemRole.Id } })).StatusCode);
            var promoted = await first.GetFromJsonAsync<UserPermissionOverridesContract>(
                $"/api/admin/users/{second.Id}");
            Assert.Empty(promoted!.PermissionOverrides);
            var promotionAudit = await first.GetFromJsonAsync<PagedSecurityAuditContract>(
                $"/api/admin/security-audit?targetUserId={second.Id}&search=USER_ROLES_REPLACED&page=1&pageSize=10");
            var promotionEvent = Assert.Single(promotionAudit!.Items);
            using (var detail = JsonDocument.Parse(promotionEvent.Detail!))
                Assert.Equal(1, detail.RootElement.GetProperty("ClearedPermissionOverrideCount").GetInt32());
            var narrowAdministrator = await SendJson(first, HttpMethod.Put,
                $"/api/admin/users/{second.Id}/permission-overrides", new
                {
                    overrides = new[]
                    {
                        new { permissionCode = Permissions.ReferenceDataView, effect = PermissionEffects.Deny }
                    }
                });
            Assert.Equal(HttpStatusCode.Conflict, narrowAdministrator.StatusCode);
            Assert.Contains("SYSTEM_ADMINISTRATOR_NOT_NARROWABLE",
                await narrowAdministrator.Content.ReadAsStringAsync());

            using var secondClient = factory.CreateClient();
            await Login(secondClient, "second-admin", "TempPass!234");
            Assert.Equal(HttpStatusCode.OK, (await SendJson(secondClient, HttpMethod.Post, "/api/auth/change-password", new { currentPassword = "TempPass!234", newPassword = "SecondAdmin!234" })).StatusCode);
            var secondLogin = await Login(secondClient, "second-admin", "SecondAdmin!234");
            var current = await secondLogin.Content.ReadFromJsonAsync<CurrentUserContract>();
            Assert.True(current!.IsSystemAdministrator);
            Assert.Equal(PermissionCatalog.Codes.Count, current.Permissions.Count);
            Assert.Collection(current.DataScopes, scope =>
            {
                Assert.Equal(DataScopeTypes.All, scope.ScopeType);
                Assert.Null(scope.ScopeValue);
            });
            var firstId = (await first.GetFromJsonAsync<PagedUsersContract>("/api/admin/users?search=first-admin&includeInactive=true&page=1&pageSize=10"))!.Items.Single().Id;
            Assert.Equal(HttpStatusCode.NoContent, (await SendJson(secondClient, HttpMethod.Post, $"/api/admin/users/{firstId}/deactivate", new { })).StatusCode);
            var selfDeactivate = await SendJson(secondClient, HttpMethod.Post, $"/api/admin/users/{second.Id}/deactivate", new { });
            Assert.Equal(HttpStatusCode.Conflict, selfDeactivate.StatusCode);
            Assert.Contains("LAST_SYSTEM_ADMINISTRATOR", await selfDeactivate.Content.ReadAsStringAsync());

            var directRoleRemoval = await factory.ExpectDirectSqlFailure(
                "DELETE FROM identity.user_roles WHERE \"UserId\" = @target", second.Id);
            Assert.Equal(PostgresErrorCodes.CheckViolation, directRoleRemoval.SqlState);
            Assert.Contains("LAST_SYSTEM_ADMINISTRATOR", directRoleRemoval.MessageText);
            var directAuditDelete = await factory.ExpectDirectSqlFailure(
                "DELETE FROM identity.security_audit_events WHERE \"TargetUserId\" = @target", second.Id);
            Assert.Equal(PostgresErrorCodes.CheckViolation, directAuditDelete.SqlState);
            Assert.Contains("SECURITY_AUDIT_IMMUTABLE", directAuditDelete.MessageText);
        }
        finally { await ((IAsyncLifetime)factory).DisposeAsync(); }
    }

    [Fact]
    public async Task Permission_assign_authority_cannot_escalate_itself_to_the_system_administrator_role()
    {
        var factory = await NewFactory();
        try
        {
            using var administrator = factory.CreateClient();
            Assert.Equal(HttpStatusCode.Created, (await Bootstrap(administrator, "escalation-admin")).StatusCode);
            await Login(administrator, "escalation-admin", "TempPass!234");
            Assert.Equal(HttpStatusCode.OK, (await SendJson(administrator, HttpMethod.Post,
                "/api/auth/change-password", new
                {
                    currentPassword = "TempPass!234",
                    newPassword = "EscalationAdmin!234"
                })).StatusCode);
            await Login(administrator, "escalation-admin", "EscalationAdmin!234");

            var systemRole = (await administrator.GetFromJsonAsync<List<RoleFullContract>>(
                "/api/admin/roles"))!.Single(role => role.IsSystemRole);
            var accessRoleResponse = await SendJson(administrator, HttpMethod.Post, "/api/admin/roles", new
            {
                name = "DelegatedAccessAdministrator",
                descriptionFa = "مدیر واگذارشده دسترسی",
                descriptionEn = "Delegated access administrator",
                permissionCodes = new[]
                {
                    Permissions.IdentityUserView,
                    Permissions.IdentityUserManage,
                    Permissions.IdentityPermissionAssign,
                    Permissions.OrganizationUnitView
                }
            });
            Assert.Equal(HttpStatusCode.Created, accessRoleResponse.StatusCode);
            var accessRole = (await accessRoleResponse.Content.ReadFromJsonAsync<RoleContract>())!;

            var delegatedResponse = await CreateUser(administrator, "delegated-access-admin", [accessRole.Id]);
            Assert.Equal(HttpStatusCode.Created, delegatedResponse.StatusCode);
            var delegatedId = (await delegatedResponse.Content.ReadFromJsonAsync<UserDetailContract>())!.User.Id;

            using var delegated = factory.CreateClient();
            await Login(delegated, "delegated-access-admin", "TempPass!234");
            Assert.Equal(HttpStatusCode.OK, (await SendJson(delegated, HttpMethod.Post,
                "/api/auth/change-password", new
                {
                    currentPassword = "TempPass!234",
                    newPassword = "DelegatedAccess!234"
                })).StatusCode);
            await Login(delegated, "delegated-access-admin", "DelegatedAccess!234");

            var createEscalation = await CreateUser(delegated, "delegated-created-admin", [systemRole.Id]);
            Assert.Equal(HttpStatusCode.Forbidden, createEscalation.StatusCode);
            Assert.Contains("SYSTEM_ADMINISTRATOR_ASSIGNMENT_REQUIRES_SYSTEM_ADMINISTRATOR",
                await createEscalation.Content.ReadAsStringAsync());

            var escalation = await SendJson(delegated, HttpMethod.Put,
                $"/api/admin/users/{delegatedId}/roles", new { roleIds = new[] { systemRole.Id } });

            Assert.Equal(HttpStatusCode.Forbidden, escalation.StatusCode);
            Assert.Contains("SYSTEM_ADMINISTRATOR_ASSIGNMENT_REQUIRES_SYSTEM_ADMINISTRATOR",
                await escalation.Content.ReadAsStringAsync());
            var current = await delegated.GetFromJsonAsync<CurrentUserContract>("/api/auth/me");
            Assert.False(current!.IsSystemAdministrator);
        }
        finally { await ((IAsyncLifetime)factory).DisposeAsync(); }
    }

    [Fact]
    public async Task Management_permissions_without_permission_assign_can_create_empty_records_but_cannot_assign_authority()
    {
        var factory = await NewFactory();
        try
        {
            using var administrator = factory.CreateClient();
            Assert.Equal(HttpStatusCode.Created, (await Bootstrap(administrator, "conditional-admin")).StatusCode);
            await Login(administrator, "conditional-admin", "TempPass!234");
            Assert.Equal(HttpStatusCode.OK, (await SendJson(administrator, HttpMethod.Post,
                "/api/auth/change-password", new
                {
                    currentPassword = "TempPass!234",
                    newPassword = "ConditionalAdmin!234"
                })).StatusCode);
            await Login(administrator, "conditional-admin", "ConditionalAdmin!234");

            var emptyRoleResponse = await SendJson(administrator, HttpMethod.Post, "/api/admin/roles", new
            {
                name = "EmptyAssignableRole",
                descriptionFa = "نقش بدون دسترسی برای آزمون",
                descriptionEn = "Empty role for authorization testing",
                permissionCodes = Array.Empty<string>()
            });
            Assert.Equal(HttpStatusCode.Created, emptyRoleResponse.StatusCode);
            var emptyRole = (await emptyRoleResponse.Content.ReadFromJsonAsync<RoleContract>())!;

            var managerRoleResponse = await SendJson(administrator, HttpMethod.Post, "/api/admin/roles", new
            {
                name = "IdentityRecordManager",
                descriptionFa = "مدیریت رکورد هویت بدون تخصیص دسترسی",
                descriptionEn = "Manage identity records without assigning authority",
                permissionCodes = new[]
                {
                    Permissions.IdentityUserManage,
                    Permissions.IdentityRoleManage,
                    Permissions.OrganizationUnitView,
                    Permissions.IdentityProfileViewOwn,
                    Permissions.IdentityPasswordChangeOwn
                }
            });
            Assert.Equal(HttpStatusCode.Created, managerRoleResponse.StatusCode);
            var managerRole = (await managerRoleResponse.Content.ReadFromJsonAsync<RoleContract>())!;

            var managerResponse = await CreateUser(administrator, "record-manager", [managerRole.Id]);
            Assert.Equal(HttpStatusCode.Created, managerResponse.StatusCode);
            using var manager = factory.CreateClient();
            await Login(manager, "record-manager", "TempPass!234");
            Assert.Equal(HttpStatusCode.OK, (await SendJson(manager, HttpMethod.Post,
                "/api/auth/change-password", new
                {
                    currentPassword = "TempPass!234",
                    newPassword = "RecordManager!234"
                })).StatusCode);
            await Login(manager, "record-manager", "RecordManager!234");

            var rolelessUser = await CreateUser(manager, "roleless-by-manager");
            Assert.Equal(HttpStatusCode.Created, rolelessUser.StatusCode);

            var ordinaryRoleAssignment = await CreateUser(manager, "assigned-by-manager", [emptyRole.Id]);
            Assert.Equal(HttpStatusCode.Forbidden, ordinaryRoleAssignment.StatusCode);
            Assert.Contains("PERMISSION_ASSIGN_REQUIRED", await ordinaryRoleAssignment.Content.ReadAsStringAsync());

            var systemRole = (await administrator.GetFromJsonAsync<List<RoleFullContract>>(
                "/api/admin/roles"))!.Single(role => role.IsSystemRole);
            var systemRoleAssignment = await CreateUser(manager, "admin-by-manager", [systemRole.Id]);
            Assert.Equal(HttpStatusCode.Forbidden, systemRoleAssignment.StatusCode);
            Assert.Contains("SYSTEM_ADMINISTRATOR_ASSIGNMENT_REQUIRES_SYSTEM_ADMINISTRATOR",
                await systemRoleAssignment.Content.ReadAsStringAsync());

            var emptyManagedRole = await SendJson(manager, HttpMethod.Post, "/api/admin/roles", new
            {
                name = "EmptyRoleByManager",
                descriptionFa = "نقش خالی مجاز",
                descriptionEn = "Allowed empty role",
                permissionCodes = Array.Empty<string>()
            });
            Assert.Equal(HttpStatusCode.Created, emptyManagedRole.StatusCode);

            var populatedManagedRole = await SendJson(manager, HttpMethod.Post, "/api/admin/roles", new
            {
                name = "EscalatingRoleByManager",
                descriptionFa = "نقش دارای دسترسی غیرمجاز",
                descriptionEn = "Unauthorized populated role",
                permissionCodes = new[] { Permissions.ReferenceDataView }
            });
            Assert.Equal(HttpStatusCode.Forbidden, populatedManagedRole.StatusCode);
            Assert.Contains("PERMISSION_ASSIGN_REQUIRED", await populatedManagedRole.Content.ReadAsStringAsync());
        }
        finally { await ((IAsyncLifetime)factory).DisposeAsync(); }
    }

    [Fact]
    public async Task Administration_worklists_filter_server_side_and_effective_access_explains_provenance()
    {
        var factory = await NewFactory();
        try
        {
            using var admin = factory.CreateClient();
            Assert.Equal(HttpStatusCode.Created, (await Bootstrap(admin, "worklist-admin")).StatusCode);
            var initialLogin = await Login(admin, "worklist-admin", "TempPass!234");
            Assert.True(initialLogin.StatusCode == HttpStatusCode.OK, await initialLogin.Content.ReadAsStringAsync());
            Assert.Contains(initialLogin.Headers, header => header.Key == "Set-Cookie" &&
                header.Value.Any(value => value.StartsWith("hadron.erp.session=", StringComparison.Ordinal)));
            var changed = await SendJson(admin, HttpMethod.Post, "/api/auth/change-password",
                new { currentPassword = "TempPass!234", newPassword = "DeltaSecure!847" });
            Assert.True(changed.StatusCode == HttpStatusCode.OK, await changed.Content.ReadAsStringAsync());
            await Login(admin, "worklist-admin", "DeltaSecure!847");

            var roleResponse = await SendJson(admin, HttpMethod.Post, "/api/admin/roles", new
            {
                name = "MasterDataReader",
                descriptionFa = "مشاهده اطلاعات پایه",
                descriptionEn = "Master data reader",
                permissionCodes = new[] { Permissions.ReferenceDataView, Permissions.ProductView, Permissions.ComponentView }
            });
            Assert.Equal(HttpStatusCode.Created, roleResponse.StatusCode);
            var role = (await roleResponse.Content.ReadFromJsonAsync<RoleContract>())!;

            const string obsoletePermission = "procurement.sourcing_price.manage";
            using (var scope = factory.Services.CreateScope())
            {
                var database = scope.ServiceProvider.GetRequiredService<HadronDbContext>();
                database.Set<IdentityRoleClaim<Guid>>().Add(new IdentityRoleClaim<Guid>
                {
                    RoleId = role.Id,
                    ClaimType = SecurityClaimTypes.Permission,
                    ClaimValue = obsoletePermission
                });
                await database.SaveChangesAsync();
            }
            var roleDetail = (await admin.GetFromJsonAsync<List<RolePermissionsContract>>(
                "/api/admin/roles"))!.Single(item => item.Id == role.Id);
            Assert.Equal(3, roleDetail.Permissions.Count);
            Assert.DoesNotContain(obsoletePermission, roleDetail.Permissions);

            var units = await admin.GetFromJsonAsync<List<UnitContract>>("/api/organization/units?includeInactive=true");
            var unitId = units!.Single().Id;
            var created = await CreateUser(admin, "worklist-reader", [role.Id]);
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            var reader = (await created.Content.ReadFromJsonAsync<UserDetailContract>())!;
            Assert.Contains(reader.Roles, assigned => assigned.Id == role.Id);

            Assert.Equal(HttpStatusCode.NoContent, (await SendJson(admin, HttpMethod.Put,
                $"/api/admin/users/{reader.User.Id}/permission-overrides", new
                {
                    overrides = new object[]
                    {
                        new { permissionCode = Permissions.ComponentView, effect = "DENY" },
                        new { permissionCode = Permissions.AnalyticsView, effect = "GRANT" }
                    }
                })).StatusCode);

            var filtered = await admin.GetFromJsonAsync<PagedUsersDetailedContract>(
                $"/api/admin/users?search=worklist&organizationalUnitId={unitId}&roleId={role.Id}&isActive=true&mustChangePassword=true&sort=username&page=1&pageSize=10");
            var summary = Assert.Single(filtered!.Items);
            Assert.Equal("worklist-reader", summary.UserName);
            Assert.Contains("MasterDataReader", summary.RoleNames);
            Assert.Equal(unitId, summary.OrganizationalUnit.Id);

            var roleWorklist = await admin.GetFromJsonAsync<PagedRolesContract>(
                "/api/admin/roles/worklist?search=MasterDataReader&page=1&pageSize=10");
            var roleRow = Assert.Single(roleWorklist!.Items);
            Assert.Equal(3, roleRow.PermissionCount);
            Assert.Equal(1, roleRow.UserCount);

            var unitWorklist = await admin.GetFromJsonAsync<PagedUnitsContract>(
                "/api/organization/units/worklist?includeInactive=true&search=Test&page=1&pageSize=10");
            var unitRow = Assert.Single(unitWorklist!.Items);
            Assert.Equal(2, unitRow.AssignedUserCount);
            Assert.Equal(2, unitRow.ActiveUserCount);

            var detail = await admin.GetFromJsonAsync<UserDetailWithAccessContract>($"/api/admin/users/{reader.User.Id}");
            var fromRole = detail!.EffectiveAccess.Single(item => item.Code == Permissions.ProductView);
            Assert.True(fromRole.IsGranted);
            Assert.Equal("ROLE", fromRole.Resolution);
            Assert.Contains("MasterDataReader", fromRole.Sources);
            var denied = detail.EffectiveAccess.Single(item => item.Code == Permissions.ComponentView);
            Assert.False(denied.IsGranted);
            Assert.Equal("DIRECT_DENY", denied.Resolution);
            var granted = detail.EffectiveAccess.Single(item => item.Code == Permissions.AnalyticsView);
            Assert.True(granted.IsGranted);
            Assert.Equal("DIRECT_GRANT", granted.Resolution);
            var absent = detail.EffectiveAccess.Single(item => item.Code == Permissions.BomApprove);
            Assert.False(absent.IsGranted);
            Assert.Equal("NONE", absent.Resolution);
        }
        finally { await ((IAsyncLifetime)factory).DisposeAsync(); }
    }

    [Fact]
    public async Task Warehouse_data_scope_filters_inventory_reads_and_denies_out_of_scope_objects()
    {
        var factory = await NewFactory();
        try
        {
            using var admin = factory.CreateClient();
            Assert.Equal(HttpStatusCode.Created, (await Bootstrap(admin, "warehouse-scope-admin")).StatusCode);
            await Login(admin, "warehouse-scope-admin", "TempPass!234");
            Assert.Equal(HttpStatusCode.OK, (await SendJson(admin, HttpMethod.Post,
                "/api/auth/change-password", new
                { currentPassword = "TempPass!234", newPassword = "WarehouseAdmin!234" })).StatusCode);
            var adminLogin = await Login(admin, "warehouse-scope-admin", "WarehouseAdmin!234");
            var administrator = (await adminLogin.Content.ReadFromJsonAsync<CurrentUserContract>())!;

            async Task<WarehouseContract> Warehouse(string suffix)
            {
                var response = await SendJson(admin, HttpMethod.Post, "/api/inventory/warehouses", new
                {
                    nameFa = $"انبار محدوده {suffix}",
                    nameEn = $"Scoped warehouse {suffix}",
                    addressFa = $"موقعیت {suffix}",
                    addressEn = $"Location {suffix}",
                    primaryResponsibleUserId = administrator.Id,
                    notes = (string?)null
                });
                Assert.Equal(HttpStatusCode.Created, response.StatusCode);
                return (await response.Content.ReadFromJsonAsync<WarehouseContract>())!;
            }

            var allowed = await Warehouse("A");
            var denied = await Warehouse("B");
            var roleResponse = await SendJson(admin, HttpMethod.Post, "/api/admin/roles", new
            {
                name = "WarehouseScopedReader",
                descriptionFa = "مشاهده انبار محدود",
                descriptionEn = "Scoped warehouse reader",
                permissionCodes = new[] { Permissions.InventoryView }
            });
            Assert.Equal(HttpStatusCode.Created, roleResponse.StatusCode);
            var role = (await roleResponse.Content.ReadFromJsonAsync<RoleContract>())!;
            var userResponse = await CreateUser(admin, "warehouse-scoped-user", [role.Id]);
            var userDetail = (await userResponse.Content.ReadFromJsonAsync<UserDetailContract>())!;
            var scopeResponse = await SendJson(admin, HttpMethod.Put,
                $"/api/admin/users/{userDetail.User.Id}/data-scopes", new
                { scopes = new[] { new { scopeType = DataScopeTypes.Warehouse, scopeValue = allowed.Id.ToString() } } });
            Assert.Equal(HttpStatusCode.NoContent, scopeResponse.StatusCode);

            using var scoped = factory.CreateClient();
            await Login(scoped, "warehouse-scoped-user", "TempPass!234");
            Assert.Equal(HttpStatusCode.OK, (await SendJson(scoped, HttpMethod.Post,
                "/api/auth/change-password", new
                { currentPassword = "TempPass!234", newPassword = "WarehouseUser!234" })).StatusCode);
            await Login(scoped, "warehouse-scoped-user", "WarehouseUser!234");
            var list = await scoped.GetFromJsonAsync<PagedWarehousesContract>(
                "/api/inventory/warehouses?includeInactive=true&page=1&pageSize=100");
            var only = Assert.Single(list!.Items);
            Assert.Equal(allowed.Id, only.Id);
            Assert.Equal(HttpStatusCode.OK,
                (await scoped.GetAsync($"/api/inventory/warehouses/{allowed.Id}")).StatusCode);
            var forbidden = await scoped.GetAsync($"/api/inventory/warehouses/{denied.Id}");
            Assert.Equal(HttpStatusCode.Conflict, forbidden.StatusCode);
            Assert.Contains("WAREHOUSE_SCOPE_DENIED", await forbidden.Content.ReadAsStringAsync());
        }
        finally { await ((IAsyncLifetime)factory).DisposeAsync(); }
    }

    private static async Task<IdentityApiFactory> NewFactory()
    {
        var factory = new IdentityApiFactory(); await ((IAsyncLifetime)factory).InitializeAsync(); return factory;
    }

    private async Task<HttpResponseMessage> Bootstrap(HttpClient client, string userName)
    {
        using var form = ProfileForm(userName, includeUnitId: false);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/bootstrap") { Content = form };
        request.Headers.Add("X-Hadron-Bootstrap-Token", IdentityApiFactory.BootstrapToken);
        return await client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> Login(HttpClient client, string userName, string password) =>
        await SendJson(client, HttpMethod.Post, "/api/auth/login", new { userName, password });

    private async Task<HttpResponseMessage> CreateUser(HttpClient client, string userName, IReadOnlyList<Guid>? roleIds = null)
    {
        var units = await client.GetFromJsonAsync<List<UnitContract>>("/api/organization/units?includeInactive=true");
        using var form = ProfileForm(userName, includeUnitId: true, units!.Single().Id);
        foreach (var roleId in roleIds ?? []) form.Add(new StringContent(roleId.ToString()), "roleIds");
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/admin/users") { Content = form };
        request.Headers.Add("X-CSRF-TOKEN", await Csrf(client));
        return await client.SendAsync(request);
    }

    private static MultipartFormDataContent ProfileForm(string userName, bool includeUnitId, Guid unitId = default)
    {
        var form = new MultipartFormDataContent();
        form.Add(new StringContent(userName), "userName"); form.Add(new StringContent("TempPass!234"), "temporaryPassword");
        form.Add(new StringContent("Test"), "firstName"); form.Add(new StringContent("User"), "lastName");
        form.Add(new StringContent($"{userName}@example.test"), "emailAddress"); form.Add(new StringContent("09120000000"), "cellphoneNumber");
        form.Add(new StringContent("true"), "isActive");
        if (includeUnitId) form.Add(new StringContent(unitId.ToString()), "organizationalUnitId");
        else { form.Add(new StringContent("واحد آزمون"), "organizationalUnitNameFa"); form.Add(new StringContent("Test Unit"), "organizationalUnitNameEn"); }
        var photo = new ByteArrayContent([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]);
        photo.Headers.ContentType = new("image/png"); form.Add(photo, "photo", "person.png"); return form;
    }

    private static async Task<HttpResponseMessage> SendJson(HttpClient client, HttpMethod method, string path, object value)
    {
        using var request = new HttpRequestMessage(method, path) { Content = JsonContent.Create(value) };
        request.Headers.Add("X-CSRF-TOKEN", await Csrf(client)); return await client.SendAsync(request);
    }
    private static async Task<string> Csrf(HttpClient client) =>
        (await (await client.GetAsync("/api/auth/csrf")).Content.ReadFromJsonAsync<CsrfContract>())!.Token;

    private sealed record CsrfContract(string Token);
    private sealed record ValidationProblemContract(string Title, string Code, Dictionary<string, string[]> Errors);
    private sealed record CurrentUserContract(Guid Id, bool MustChangePassword, bool IsSystemAdministrator, List<DataScopeContract> DataScopes, List<string> Permissions);
    private sealed record DataScopeContract(string ScopeType, string? ScopeValue);
    private sealed record RoleContract(Guid Id, string Name);
    private sealed record RolePermissionsContract(Guid Id, List<string> Permissions);
    private sealed record WarehouseContract(Guid Id, string Code);
    private sealed record PagedWarehousesContract(List<WarehouseContract> Items, int TotalCount);
    private sealed record RoleFullContract(Guid Id, bool IsSystemRole);
    private sealed record UnitContract(Guid Id);
    private sealed record UserContract(Guid Id);
    private sealed record UserDetailContract(UserContract User, List<DataScopeContract> DataScopes, List<RoleContract> Roles);
    private sealed record PagedUsersContract(List<UserContract> Items);
    private sealed record UnitSummaryContract(Guid Id);
    private sealed record UserDetailedContract(Guid Id, string UserName, List<string> RoleNames, UnitSummaryContract OrganizationalUnit);
    private sealed record PagedUsersDetailedContract(List<UserDetailedContract> Items);
    private sealed record RoleWorklistContract(Guid Id, int PermissionCount, int UserCount);
    private sealed record PagedRolesContract(List<RoleWorklistContract> Items);
    private sealed record UnitWorklistContract(Guid Id, int AssignedUserCount, int ActiveUserCount);
    private sealed record PagedUnitsContract(List<UnitWorklistContract> Items);
    private sealed record EffectiveAccessContract(string Code, bool IsGranted, string Resolution, List<string> Sources);
    private sealed record UserDetailWithAccessContract(List<EffectiveAccessContract> EffectiveAccess);
    private sealed record PermissionOverrideContract(string PermissionCode, string Effect);
    private sealed record UserPermissionOverridesContract(List<PermissionOverrideContract> PermissionOverrides);
    private sealed record SecurityAuditContract(string Action, string? Detail);
    private sealed record PagedSecurityAuditContract(List<SecurityAuditContract> Items);
}

public sealed class IdentityApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string BootstrapToken = "identity-bootstrap-test-token-2026";
    private readonly string _database = $"hadron_erp_test_{Guid.NewGuid():N}";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("IdentityTesting");
        builder.ConfigureLogging(logging => logging.ClearProviders());
        builder.ConfigureServices(services =>
        {
            services.AddDataProtection().UseEphemeralDataProtectionProvider();
            services.Configure<SecurityStampValidatorOptions>(options => options.ValidationInterval = TimeSpan.Zero);
        });
        builder.UseSetting("POSTGRES_DB", _database);
        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["POSTGRES_DB"] = _database,
            ["IDENTITY_BOOTSTRAP_TOKEN"] = BootstrapToken,
            ["Identity:UseInMemoryPhotoStore"] = "true",
            ["FRONTEND_ORIGIN"] = "http://localhost:5173"
        }));
    }

    async Task IAsyncLifetime.InitializeAsync()
    {
        TestDatabaseSafety.EnsureSafeDatabaseName(_database);
        await using var connection = new NpgsqlConnection(Connection("postgres")); await connection.OpenAsync();
        await new NpgsqlCommand($"CREATE DATABASE \"{_database}\"", connection).ExecuteNonQueryAsync();
        using var scope = Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<HadronDbContext>().Database.MigrateAsync();
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        Dispose(); NpgsqlConnection.ClearAllPools(); TestDatabaseSafety.EnsureSafeDatabaseName(_database);
        await using var connection = new NpgsqlConnection(Connection("postgres")); await connection.OpenAsync();
        await new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{_database}\" WITH (FORCE)", connection).ExecuteNonQueryAsync();
    }

    public async Task<PostgresException> ExpectDirectSqlFailure(string sql, Guid target)
    {
        await using var connection = new NpgsqlConnection(Connection(_database));
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("target", target);
        return await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync());
    }

    private string Connection(string database) => new NpgsqlConnectionStringBuilder
    {
        Host = Environment.GetEnvironmentVariable("POSTGRES_HOST") ?? "localhost",
        Port = int.Parse(Environment.GetEnvironmentVariable("POSTGRES_PORT") ?? "5432"),
        Database = database,
        Username = Environment.GetEnvironmentVariable("POSTGRES_USER") ?? "hadron_app",
        Password = Environment.GetEnvironmentVariable("POSTGRES_PASSWORD") ?? throw new InvalidOperationException("POSTGRES_PASSWORD is required.")
    }.ConnectionString;
}
