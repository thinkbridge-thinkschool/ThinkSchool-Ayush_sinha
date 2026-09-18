using MaintainXpert.Api.Endpoints;
using MaintainXpert.Api.Infrastructure;
using MaintainXpert.Assets.Application;
using MaintainXpert.Assets.Infrastructure;
using MaintainXpert.Maintenance.Application;
using MaintainXpert.Maintenance.Domain.Events;
using MaintainXpert.Maintenance.Infrastructure;
using MaintainXpert.Notifications.Application;
using MaintainXpert.Notifications.Infrastructure;
using MaintainXpert.SharedKernel;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = ApiHardeningExtensions.MaxRequestBodyBytes;
});

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

builder.Services.AddSingleton(TimeProvider.System);

var azureSqlConnectionString = builder.Configuration.GetConnectionString("AzureSql");
var useSqlServer = !string.IsNullOrWhiteSpace(azureSqlConnectionString);

if (useSqlServer)
{
    builder.Services.AddDbContext<AppDbContext>(options => options.UseSqlServer(azureSqlConnectionString));
    builder.Services.AddScoped<IWorkOrderRepository, SqlWorkOrderRepository>();
    builder.Services.AddScoped<IAssetRepository, SqlAssetRepository>();
}
else
{
    builder.Services.AddSingleton<IWorkOrderRepository, InMemoryWorkOrderRepository>();
    builder.Services.AddSingleton<IAssetRepository, InMemoryAssetRepository>();
}

builder.Services.AddSingleton<INotificationSink, ConsoleNotificationSink>();

builder.Services.AddScoped<IAssetLookup, AssetLookupAdapter>();
builder.Services.AddScoped<IDomainEventDispatcher, InProcessDomainEventDispatcher>();
builder.Services.AddScoped<WorkOrderService>();

builder.Services.AddScoped<IDomainEventHandler<WorkOrderCreated>, WorkOrderCreatedNotificationHandler>();
builder.Services.AddScoped<IDomainEventHandler<WorkOrderStarted>, WorkOrderStartedHandler>();
builder.Services.AddScoped<IDomainEventHandler<WorkOrderCompleted>, WorkOrderCompletedHandler>();

builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddProblemDetails();

builder.Services.AddApiJwtAuthentication(builder.Configuration);
builder.Services.AddApiHardening();

// Day 27's threat model flagged /auth/token as unbounded against credential-stuffing
// (accepted risk at the time, "flagged as a follow-up"). This closes that gap: a caller
// is capped at a fixed number of token attempts per window, tracked per remote IP.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.AddFixedWindowLimiter(RateLimiterPolicies.AuthToken, limiterOptions =>
    {
        limiterOptions.PermitLimit = 10;
        limiterOptions.Window = TimeSpan.FromMinutes(1);
        limiterOptions.QueueLimit = 0;
    });
});

builder.Services.AddHealthChecks();

var app = builder.Build();

app.UseExceptionHandler();

var isDevelopment = app.Environment.IsDevelopment();

app.Use(async (context, next) =>
{
    context.Response.OnStarting(() =>
    {
        context.Response.Headers.CacheControl = "no-store, no-cache";
        context.Response.Headers.Pragma = "no-cache";
        context.Response.Headers.XContentTypeOptions = "nosniff";

        if (!isDevelopment)
        {
            context.Response.Headers.StrictTransportSecurity = "max-age=31536000; includeSubDomains";
        }

        return Task.CompletedTask;
    });

    await next();
});

if (useSqlServer)
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.EnsureCreatedAsync();
}

app.UseRateLimiter();

app.UseAuthentication();
app.UseAuthorization();

app.MapHealthChecks("/health");

app.MapOpenApi();

app.MapGroup("/auth").MapAuthEndpoints();

app.MapAssetEndpoints();
app.MapWorkOrderEndpoints();

app.Run();

public partial class Program
{
}
