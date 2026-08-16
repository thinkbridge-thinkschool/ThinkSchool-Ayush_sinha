using Azure.Monitor.OpenTelemetry.AspNetCore;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using QuotesApi.Authentication;
using QuotesApi.Authorization;
using QuotesApi.Data;
using QuotesApi.Middleware;
using QuotesApi.Repositories;
using QuotesApi.Services;

namespace QuotesApi.Extensions;

public static class InfrastructureExtensions
{
    /// <summary>
    /// Configuration key (and, as a fallback, environment variable) that supplies the
    /// Azure Application Insights connection string. It is intentionally read from
    /// configuration only - never hard-coded - and is treated as optional: when it is
    /// absent, Azure Monitor export is simply not attached, and the app starts and runs
    /// normally with no Azure dependency.
    /// </summary>
    private const string AppInsightsConnectionStringKey = "ApplicationInsights:ConnectionString";

    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddControllers();

        services.AddDbContext<AppDbContext>(options =>
            options.UseSqlite(
                configuration.GetConnectionString("DefaultConnection")
                ?? "Data Source=quotes.db"));

        services.AddScoped<IQuoteRepository, QuoteRepository>();
        services.AddScoped<ICollectionRepository, CollectionRepository>();
        services.AddScoped<ICollectionService, CollectionService>();
        services.AddScoped<IRefreshTokenService, RefreshTokenService>();

        services.AddSingleton<IClock, SystemClock>();
        services.AddTransient<QuoteFormatter>();
        services.AddSingleton<JwtTokenService>();

        services.AddExceptionHandler<GlobalExceptionHandler>();
        services.AddProblemDetails();

        services.AddDualJwtAuthentication(configuration);

        services.AddAuthorization(options =>
        {
            options.AddPolicy(
                PermissionClaims.CanEditQuotes,
                policy => policy.RequireClaim(
                    PermissionClaims.ClaimType,
                    PermissionClaims.CanEditQuotes));
        });

        services.AddScoped<
            IAuthorizationHandler,
            CollectionOwnershipAuthorizationHandler>();

        // Backs the /health endpoint mapped in Program.cs. Checks the real
        // dependency (can we reach the database?) rather than always
        // returning healthy - a DB outage should show up here, not just as
        // 500s on the quote endpoints.
        services.AddHealthChecks()
            .AddDbContextCheck<AppDbContext>();

        services.AddObservability(configuration);

        return services;
    }

    /// <summary>
    /// Wires up the OpenTelemetry tracing/metrics pipeline (ASP.NET Core + HttpClient
    /// instrumentation) that feeds both the existing Serilog TraceId correlation and,
    /// when configured, Azure Application Insights.
    ///
    /// Azure Monitor export is attached only when a connection string is actually
    /// present in configuration/environment. With no connection string:
    ///   - the OpenTelemetry pipeline still runs (so Activity/TraceId correlation with
    ///     Serilog keeps working locally and in CI),
    ///   - no Azure Monitor exporter is registered, so nothing attempts to reach Azure,
    ///   - startup, console logging, and existing behavior are unaffected.
    /// </summary>
    private static IServiceCollection AddObservability(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = ResolveAppInsightsConnectionString(configuration);

        var openTelemetry = services
            .AddOpenTelemetry()
            .WithTracing(tracing => tracing
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation())
            .WithMetrics(metrics => metrics
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation());

        if (!string.IsNullOrWhiteSpace(connectionString))
        {
            openTelemetry.UseAzureMonitor(options =>
                options.ConnectionString = connectionString);
        }

        return services;
    }

    /// <summary>
    /// Resolves the Application Insights connection string from configuration first
    /// (so it can be sourced from Key Vault or any other configuration provider wired
    /// into IConfiguration), then falls back to the conventional
    /// APPLICATIONINSIGHTS_CONNECTION_STRING environment variable that Azure App
    /// Service / Azure Monitor tooling sets automatically. Returns null/empty when
    /// unset - callers must treat that as "Azure Monitor disabled", never as an error.
    /// </summary>
    private static string? ResolveAppInsightsConnectionString(IConfiguration configuration) =>
        configuration[AppInsightsConnectionStringKey]
        ?? configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"]
        ?? Environment.GetEnvironmentVariable("APPLICATIONINSIGHTS_CONNECTION_STRING");
}
