using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using QuotesApi.Data;
using QuotesApi.Endpoints;
using QuotesApi.Extensions;
using Serilog;
using Serilog.Context;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, services, configuration) =>
    configuration
        .ReadFrom.Configuration(context.Configuration)
        .Enrich.FromLogContext());

builder.Services.AddInfrastructure(builder.Configuration);

var app = builder.Build();

app.UseExceptionHandler();

app.Use(async (context, next) =>
{
    // Prefer the W3C trace ID from the current OpenTelemetry Activity - the same ID
    // that the ASP.NET Core/HttpClient instrumentation attaches to spans exported to
    // Azure Application Insights - so Serilog's "TraceId" property, this request's
    // trace, and its Application Insights telemetry all share one identifier. Falls
    // back to the ASP.NET Core request identifier if no Activity is present (e.g. no
    // listener is currently sampling), so correlation never breaks.
    var traceId = Activity.Current?.TraceId.ToString() ?? context.TraceIdentifier;

    using (LogContext.PushProperty("TraceId", traceId))
    {
        await next();
    }
});

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

    db.Database.Migrate();

    await DbSeeder.SeedAsync(db);
}

app.UseAuthentication();

app.UseAuthorization();

app.MapHealthChecks("/health");

app.MapQuoteEndpoints();

app.MapControllers();

app.Run();

public partial class Program
{
}
