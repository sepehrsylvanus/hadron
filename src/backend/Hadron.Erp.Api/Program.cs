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
using Hadron.Erp.Api.Modules.PurchaseSupply.Api;
using Hadron.Erp.Api.Modules.PurchaseSupply.Application;
using Hadron.Erp.Api.Modules.Mrp.Api;
using Hadron.Erp.Api.Modules.Mrp.Application;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddProblemDetails();

var databaseConnectionString = DatabaseConnectionString.Create(builder.Configuration);
builder.Services.AddDbContextFactory<HadronDbContext>(options =>
    options.UseNpgsql(databaseConnectionString));
builder.Services.AddScoped<ReferenceDataService>();
builder.Services.AddScoped<MaterialItemService>();
builder.Services.AddScoped<ProductService>();
builder.Services.AddScoped<SupplierService>();
builder.Services.AddScoped<SourcingService>();
builder.Services.AddScoped<BomService>();
builder.Services.AddScoped<IBomOperationalReadinessQuery, BomOperationalReadinessQuery>();
builder.Services.AddScoped<PlanningService>();
builder.Services.AddScoped<InventoryService>();
builder.Services.AddScoped<PurchaseSupplyService>();
builder.Services.AddScoped<MrpService>();
builder.Services.AddScoped<MaterialPlanningService>();
builder.Services.AddSingleton<MrpCalculator>();
builder.Services.AddScoped<IItemBaseUnitChangeGuard, InventoryHistoryItemGuard>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<IPlanningCalendar, PlanningCalendar>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton<IAuditEventSink, NoOpAuditEventSink>();

builder.Services
    .AddHealthChecks()
    .AddCheck<PostgresHealthCheck>("postgresql", tags: ["ready"]);

if (builder.Environment.IsDevelopment())
{
    var frontendOrigin = builder.Configuration["FRONTEND_ORIGIN"] ?? "http://localhost:5173";
    builder.Services.AddCors(options =>
        options.AddPolicy("DevelopmentFrontend", policy =>
            policy.WithOrigins(frontendOrigin).AllowAnyHeader().AllowAnyMethod()));
}

var app = builder.Build();

app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.UseCors("DevelopmentFrontend");
}

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

app.MapReferenceDataEndpoints();
app.MapMaterialItemEndpoints();
app.MapProductEndpoints();
app.MapSupplierEndpoints();
app.MapSourcingEndpoints();
app.MapBomEndpoints();
app.MapPlanningEndpoints();
app.MapInventoryEndpoints();
app.MapPurchaseSupplyEndpoints();
app.MapMrpEndpoints();
app.MapMaterialPlanningEndpoints();

app.Run();

public partial class Program;
