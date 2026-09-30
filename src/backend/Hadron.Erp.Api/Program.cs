using Hadron.Erp.Api.Infrastructure;
using Hadron.Erp.Api.Modules.Catalog.Api;
using Hadron.Erp.Api.Modules.Catalog.Application;
using Hadron.Erp.Api.Modules.ReferenceData.Api;
using Hadron.Erp.Api.Modules.ReferenceData.Application;
using Hadron.Erp.Api.Modules.Products.Api;
using Hadron.Erp.Api.Modules.Products.Application;
using Hadron.Erp.Api.Platform.Audit;
using Hadron.Erp.Api.Modules.Suppliers.Api;
using Hadron.Erp.Api.Modules.Suppliers.Application;
using Hadron.Erp.Api.Modules.Sourcing.Api;
using Hadron.Erp.Api.Modules.Sourcing.Application;
using Hadron.Erp.Api.Modules.Bom.Api;
using Hadron.Erp.Api.Modules.Bom.Application;
using Hadron.Erp.Api.Modules.Planning.Api;
using Hadron.Erp.Api.Modules.Planning.Application;
using Hadron.Erp.Api.Modules.Inventory.Api;
using Hadron.Erp.Api.Modules.Inventory.Application;
using Hadron.Erp.Api.Modules.Production.Api;
using Hadron.Erp.Api.Modules.Production.Application;
using Hadron.Erp.Api.Modules.Trading.Api;
using Hadron.Erp.Api.Modules.Trading.Application;
using Hadron.Erp.Api.Modules.PurchaseSupply.Api;
using Hadron.Erp.Api.Modules.PurchaseSupply.Application;
using Hadron.Erp.Api.Modules.Mrp.Api;
using Hadron.Erp.Api.Modules.Mrp.Application;
using Hadron.Erp.Api.Modules.OrderPlanning.Api;
using Hadron.Erp.Api.Modules.OrderPlanning.Application;
using Hadron.Erp.Api.Modules.Analytics.Api;
using Hadron.Erp.Api.Modules.Analytics.Application;
using Hadron.Erp.Api.Modules.CashFlow.Api;
using Hadron.Erp.Api.Modules.CashFlow.Application;
using Hadron.Erp.Api.Modules.Identity.Api;
using Hadron.Erp.Api.Modules.Identity.Application;
using Hadron.Erp.Api.Modules.Identity.Domain;
using Hadron.Erp.Api.Modules.Collaboration.Api;
using Hadron.Erp.Api.Modules.Collaboration.Application;
using Hadron.Erp.Api.Modules.Collaboration.Infrastructure;
using Hadron.Erp.Api.Platform.Security;
using Hadron.Erp.Api.Platform.Storage;
using Hadron.Erp.Api.Platform.Errors;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

if (builder.Environment.IsDevelopment())
{
    builder.Logging.ClearProviders();
    builder.Logging.AddConsole();
    builder.Logging.AddDebug();
}

builder.Services.AddHadronDataProtection(builder.Configuration, builder.Environment);

builder.Services.AddProblemDetails();
builder.Services.AddSignalR(options =>
{
    options.EnableDetailedErrors = builder.Environment.IsDevelopment();
    options.MaximumReceiveMessageSize = 64 * 1024;
});
builder.Services.AddExceptionHandler<HadronExceptionHandler>();
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = async (context, cancellationToken) =>
    {
        var retryAfterSeconds = 60;
        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
            retryAfterSeconds = Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds));
        context.HttpContext.Response.Headers.RetryAfter = retryAfterSeconds.ToString();
        context.HttpContext.Response.ContentType = "application/problem+json";
        var persian = context.HttpContext.Request.Headers.AcceptLanguage.Any(value =>
            value?.StartsWith("fa", StringComparison.OrdinalIgnoreCase) == true);
        await context.HttpContext.Response.WriteAsJsonAsync(new
        {
            title = persian ? "تعداد تلاش‌ها بیش از حد مجاز است." : "Too many attempts.",
            detail = persian
                ? "برای حفاظت از حساب، لطفاً کمی بعد دوباره تلاش کنید."
                : "For account protection, wait before trying again.",
            status = StatusCodes.Status429TooManyRequests,
            code = "RATE_LIMITED",
            traceId = context.HttpContext.TraceIdentifier
        }, cancellationToken);
    };
    options.AddPolicy("authentication-login", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(ClientPartition(httpContext), _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 10,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
            AutoReplenishment = true
        }));
    options.AddPolicy("authentication-bootstrap", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(ClientPartition(httpContext), _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 5,
            Window = TimeSpan.FromMinutes(15),
            QueueLimit = 0,
            AutoReplenishment = true
        }));
    options.AddPolicy("meeting-guest-invitation", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(ClientPartition(httpContext), _ => new FixedWindowRateLimiterOptions
        {
            // A 100-guest arrival wave performs preview + redeem (200 requests).
            // Keep 50% burst headroom while retaining a strict per-address boundary.
            PermitLimit = 300,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
            AutoReplenishment = true
        }));
    // One corporate/NAT address may legitimately represent 100 guests. At a ten-second
    // status cadence that is 600 requests/minute; 1,200 leaves deterministic headroom
    // for join/connected/disconnected bursts without weakening invite-token throttling.
    options.AddPolicy("meeting-guest-session", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(ClientPartition(httpContext), _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 1_200,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
            AutoReplenishment = true
        }));
});

var databaseConnectionString = DatabaseConnectionString.Create(builder.Configuration);
builder.Services.Configure<AutomaticMrpRefreshOptions>(
    builder.Configuration.GetSection("MrpAutomation"));
builder.Services.AddSingleton<MrpInputChangeInterceptor>();
builder.Services.AddDbContextFactory<HadronDbContext>((services, options) =>
    options.UseNpgsql(databaseConnectionString)
        .AddInterceptors(services.GetRequiredService<MrpInputChangeInterceptor>()));
builder.Services.AddIdentity<ApplicationUser, ApplicationRole>(options =>
    {
        options.User.RequireUniqueEmail = true;
        options.Password.RequiredLength = 10;
        options.Password.RequireDigit = true;
        options.Password.RequireLowercase = true;
        options.Password.RequireUppercase = true;
        options.Password.RequireNonAlphanumeric = true;
        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
    })
    .AddEntityFrameworkStores<HadronDbContext>()
    .AddClaimsPrincipalFactory<HadronClaimsPrincipalFactory>()
    .AddDefaultTokenProviders();
builder.Services.ConfigureApplicationCookie(options =>
{
    options.Cookie.Name = "hadron.erp.session";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = builder.Environment.IsDevelopment() || builder.Environment.IsEnvironment("IdentityTesting")
        ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
    options.SlidingExpiration = true;
    options.ExpireTimeSpan = TimeSpan.FromHours(8);
    options.Events.OnRedirectToLogin = context =>
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        return context.Response.WriteAsJsonAsync(new { title = "Authentication required", status = 401, code = "AUTHENTICATION_REQUIRED" });
    };
    options.Events.OnRedirectToAccessDenied = context =>
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        return context.Response.WriteAsJsonAsync(new { title = "Permission denied", status = 403, code = "PERMISSION_DENIED" });
    };
});
builder.Services.Configure<SecurityStampValidatorOptions>(options =>
{
    options.OnRefreshingPrincipal = context =>
    {
        var sessionClaim = context.CurrentPrincipal?.FindFirst(SecurityClaimTypes.SessionId);
        if (sessionClaim is not null && context.NewPrincipal?.Identity is ClaimsIdentity identity &&
            !identity.HasClaim(SecurityClaimTypes.SessionId, sessionClaim.Value))
        {
            identity.AddClaim(new Claim(SecurityClaimTypes.SessionId, sessionClaim.Value));
        }

        return Task.CompletedTask;
    };
});
if (builder.Environment.IsEnvironment("Testing"))
{
    builder.Services.AddAuthentication(options =>
    {
        options.DefaultAuthenticateScheme = "HadronTesting";
        options.DefaultChallengeScheme = "HadronTesting";
        options.DefaultScheme = "HadronTesting";
    }).AddScheme<AuthenticationSchemeOptions, TestingAuthenticationHandler>("HadronTesting", _ => { });
}
builder.Services.AddAuthorization(options =>
{
    foreach (var permission in PermissionCatalog.Codes)
    {
        var isAuthenticatedSelfService = permission is Permissions.IdentityProfileViewOwn or
            Permissions.IdentityPasswordChangeOwn;
        options.AddPolicy(permission, policy => policy.RequireAuthenticatedUser().RequireAssertion(context =>
            (isAuthenticatedSelfService ||
             context.User.IsInRole(SystemRoleNames.SystemAdministrator) ||
             context.User.HasClaim(SecurityClaimTypes.Permission, permission)) &&
            !context.User.HasClaim(SecurityClaimTypes.PermissionDeny, permission)));
    }
});
builder.Services.AddAntiforgery(options =>
{
    options.Cookie.Name = "hadron.erp.csrf";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = builder.Environment.IsDevelopment() || builder.Environment.IsEnvironment("IdentityTesting")
        ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
    options.HeaderName = "X-CSRF-TOKEN";
});
var useInMemoryProfilePhotoStore = builder.Environment.IsEnvironment("Testing") ||
    builder.Environment.IsEnvironment("IdentityTesting") ||
    builder.Configuration.GetValue<bool>("Identity:UseInMemoryPhotoStore");
var profilePhotoStorageOptions = useInMemoryProfilePhotoStore
    ? null
    : ProfilePhotoStorageOptions.From(builder.Configuration);
builder.Services.AddScoped<IdentityService>();
builder.Services.AddSingleton<IProfilePhotoStore>(_ => useInMemoryProfilePhotoStore
    ? new InMemoryProfilePhotoStore()
    : new MinioProfilePhotoStore(profilePhotoStorageOptions!));
builder.Services.AddSingleton<IProfilePhotoStoreHealthProbe>(services =>
    (IProfilePhotoStoreHealthProbe)services.GetRequiredService<IProfilePhotoStore>());
builder.Services.AddScoped<ReferenceDataService>();
builder.Services.AddScoped<MaterialItemService>();
builder.Services.AddScoped<ProductService>();
builder.Services.AddScoped<SupplierService>();
builder.Services.AddScoped<SourcingService>();
builder.Services.AddScoped<BomService>();
builder.Services.AddScoped<BomStructureService>();
builder.Services.AddScoped<BomApplicabilityService>();
builder.Services.AddScoped<IBomOperationalReadinessQuery, BomOperationalReadinessQuery>();
builder.Services.AddScoped<PlanningService>();
builder.Services.AddScoped<InventoryService>();
builder.Services.AddScoped<ProductionService>();
builder.Services.AddScoped<ProductionRoutingService>();
builder.Services.AddScoped<PurchaseSupplyService>();
builder.Services.AddScoped<PurchaseOrderOperationsService>();
builder.Services.AddScoped<PurchaseReceiptInventoryCoordinator>();
builder.Services.AddSingleton<IPurchaseReceiptFaultInjector, NoOpPurchaseReceiptFaultInjector>();
builder.Services.AddScoped<MrpService>();
builder.Services.AddScoped<MrpPlanLifecycleService>();
builder.Services.AddScoped<MaterialPlanningService>();
builder.Services.AddScoped<PlanningCollaborationService>();
builder.Services.AddScoped<AutomaticMrpRefreshProcessor>();
builder.Services.AddScoped<OrderPlanningService>();
builder.Services.AddScoped<AnalyticsService>();
builder.Services.AddScoped<AnalyticsExportService>();
builder.Services.AddScoped<CashFlowQueryService>();
builder.Services.AddScoped<ReportingFxRateService>();
builder.Services.AddScoped<MaterialCostBookService>();
builder.Services.AddScoped<PurchasePriceHistoryService>();
builder.Services.AddScoped<TradingService>();
builder.Services.AddScoped<CollaborationService>();
builder.Services.AddScoped<MeetingService>();
builder.Services.AddScoped<MeetingGuestService>();
builder.Services.AddScoped<MeetingDateTimeResolver>();
builder.Services.AddSingleton(LiveKitOptions.From(
    builder.Configuration,
    requireSecureWebSocket: builder.Environment.IsProduction()));
builder.Services.AddSingleton<LiveKitTokenService>();
builder.Services.AddHttpClient<IMeetingMediaGateway, LiveKitMeetingMediaGateway>(client =>
    client.Timeout = TimeSpan.FromSeconds(10));
var useInMemoryCollaborationStore = builder.Environment.IsEnvironment("Testing") ||
    builder.Environment.IsEnvironment("IdentityTesting") ||
    builder.Configuration.GetValue<bool>("Collaboration:UseInMemoryFileStore");
var collaborationStorageOptions = useInMemoryCollaborationStore
    ? null
    : CollaborationStorageOptions.From(builder.Configuration);
builder.Services.AddSingleton<ICollaborationFileStore>(_ => useInMemoryCollaborationStore
    ? new InMemoryCollaborationFileStore()
    : new MinioCollaborationFileStore(collaborationStorageOptions!));
builder.Services.AddSingleton<IOrderPlanningGenerationFaultInjector, NoOpOrderPlanningGenerationFaultInjector>();
builder.Services.AddSingleton<OrderPlanningCalculator>();
builder.Services.AddSingleton<MrpCalculator>();
builder.Services.AddScoped<IItemBaseUnitChangeGuard, OperationalItemBaseUnitChangeGuard>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<IPlanningCalendar, PlanningCalendar>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton<IAuditEventSink, PostgresAuditEventSink>();
builder.Services.AddScoped<OperationalAuditQuery>();
if (!builder.Environment.IsEnvironment("Testing") &&
    !builder.Environment.IsEnvironment("IdentityTesting"))
    builder.Services.AddHostedService<AutomaticMrpRefreshWorker>();

builder.Services
    .AddHealthChecks()
    .AddCheck<PostgresHealthCheck>("postgresql", tags: ["ready"])
    .AddCheck<ProfilePhotoStoreHealthCheck>("profile-photo-storage", tags: ["ready"]);

if (builder.Environment.IsDevelopment())
{
    var frontendOrigins = (builder.Configuration["FRONTEND_ORIGINS"] ??
        builder.Configuration["FRONTEND_ORIGIN"] ??
        "http://localhost:5173,http://127.0.0.1:5173")
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    builder.Services.AddCors(options =>
        options.AddPolicy("DevelopmentFrontend", policy =>
            policy.WithOrigins(frontendOrigins).AllowAnyHeader().AllowAnyMethod().AllowCredentials()));
}

var app = builder.Build();

app.UseExceptionHandler();
app.UseRateLimiter();

if (app.Environment.IsDevelopment())
{
    app.UseCors("DevelopmentFrontend");
}
app.UseAuthentication();
app.UseMiddleware<IdentitySessionMiddleware>();
app.UseMiddleware<ApiAntiforgeryMiddleware>();
app.UseMiddleware<ApiPermissionMiddleware>();
app.UseAuthorization();

app.MapGet("/", () => Results.Ok(new
{
    service = "Hadron ERP API",
    status = "running"
}));

app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = _ => false
});

app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = registration => registration.Tags.Contains("ready")
});

app.MapIdentityEndpoints();
app.MapOperationalAuditEndpoints();
app.MapReferenceDataEndpoints();
app.MapMaterialItemEndpoints();
app.MapProductEndpoints();
app.MapSupplierEndpoints();
app.MapSourcingEndpoints();
app.MapBomEndpoints();
app.MapPlanningEndpoints();
app.MapInventoryEndpoints();
app.MapProductionEndpoints();
app.MapProductionRoutingEndpoints();
app.MapPurchaseSupplyEndpoints();
app.MapMrpEndpoints();
app.MapMaterialPlanningEndpoints();
app.MapOrderPlanningEndpoints();
app.MapAnalyticsEndpoints();
app.MapCashFlowEndpoints();
app.MapReportingFxEndpoints();
app.MapMaterialCostBookEndpoints();
app.MapPurchasePriceHistoryEndpoints();
app.MapTradingEndpoints();
app.MapCollaborationEndpoints();
app.MapMeetingEndpoints();
app.MapHub<CollaborationHub>("/hubs/collaboration")
    .RequirePermission(Permissions.CollaborationWorkspaceView);

app.Run();

static string ClientPartition(HttpContext context)
{
    // TestServer has no remote socket address. A validated, Testing-only header keeps
    // independent rate-limit scenarios deterministic without trusting proxy headers
    // or weakening the Production partition key.
    var environment = context.RequestServices.GetRequiredService<IHostEnvironment>();
    if (environment.IsEnvironment("Testing") &&
        context.Request.Headers.TryGetValue("X-Hadron-Test-Client-Ip", out var testAddress) &&
        System.Net.IPAddress.TryParse(testAddress.ToString(), out var parsedTestAddress))
        return $"test:{parsedTestAddress}";

    return context.Connection.RemoteIpAddress?.ToString() ?? "unknown-client";
}

public partial class Program;
