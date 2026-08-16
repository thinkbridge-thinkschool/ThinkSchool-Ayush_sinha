# Azure Application Insights integration

This documents the Day 4 Azure Monitor OpenTelemetry integration for QuotesApi:
how it is wired, how it stays optional locally/in CI, and the KQL/alert
configuration to create once a real Application Insights resource exists.

No Azure resource was created as part of this work, and none of the queries
below have been run against a real Application Insights instance - there is
no connection string available in this environment.

## How it's wired

`Extensions/InfrastructureExtensions.cs` (`AddObservability`) registers a single
OpenTelemetry pipeline:

- `AddAspNetCoreInstrumentation()` and `AddHttpClientInstrumentation()` for both
  tracing and metrics - this pipeline runs unconditionally, so request tracing
  and Activity/TraceId correlation work the same locally, in CI, and in
  production, regardless of Azure Monitor.
- `UseAzureMonitor()` (from `Azure.Monitor.OpenTelemetry.AspNetCore`) is attached
  to that same pipeline **only** when a connection string is resolved. It is
  never called with an empty/missing string, so there is no attempt to reach
  Azure when one isn't configured.

This is a single pipeline, not a second parallel one: Azure Monitor is an
additional exporter attached to the same `TracerProviderBuilder` /
`MeterProviderBuilder` that also feeds Serilog correlation (see below).

## Configuration key

Resolution order (`ResolveAppInsightsConnectionString`):

1. `ApplicationInsights:ConnectionString` (IConfiguration - can be backed by
   appsettings, environment variables, Azure Key Vault, or Azure App Service
   configuration)
2. `APPLICATIONINSIGHTS_CONNECTION_STRING` as a configuration key
3. `APPLICATIONINSIGHTS_CONNECTION_STRING` as a raw environment variable
   (the conventional variable name Azure App Service / Azure Monitor tooling
   sets automatically)

`appsettings.json` only ever contains an empty placeholder:

```json
"ApplicationInsights": {
  "ConnectionString": ""
}
```

An empty string is treated as "not configured", the same as a missing key -
never a fake/placeholder credential.

### Production

Provide the real connection string via one of:

- Azure App Service **Configuration > Application settings**
  (`APPLICATIONINSIGHTS_CONNECTION_STRING`), or
- Azure Key Vault, referenced either through an App Service Key Vault
  reference (`@Microsoft.KeyVault(...)` app setting) or via
  `IConfigurationBuilder.AddAzureKeyVault(...)` in the hosting environment, or
- A managed deployment secret store (e.g. GitHub Actions/Azure DevOps secret
  injected as an environment variable at deploy time).

The secret must never be committed to `appsettings.json`, `appsettings.*.json`,
or source control in any form.

### Local development / CI

No key is set, so `ResolveAppInsightsConnectionString` returns `null`, and
`UseAzureMonitor()` is never called. The app starts normally, Serilog console
logging and the ASP.NET Core/HttpClient OpenTelemetry pipeline still run, and
no Azure credentials, subscription, or network access are required to build,
run, or test.

## Correlation

```
HTTP request
  -> ASP.NET Core OpenTelemetry instrumentation starts an Activity (W3C trace id)
  -> Program.cs middleware reads Activity.Current?.TraceId, falls back to
     HttpContext.TraceIdentifier if no Activity is active
  -> pushed into Serilog's LogContext as "TraceId" (same property/key as before -
     no second correlation ID introduced)
  -> the same Activity is exported as an OpenTelemetry span
  -> when configured, Azure Monitor exports that span with the identical trace ID
```

Console log lines and Application Insights `requests`/`dependencies`/`traces`
rows for the same request therefore share one trace ID end-to-end.

## KQL - slowest 10 requests in the last hour

```kql
requests
| where timestamp > ago(1h)
| order by duration desc
| take 10
| project timestamp, name, url, resultCode, success, duration, operation_Id, cloud_RoleName
```

- `timestamp > ago(1h)`: last hour only.
- `order by duration desc` + `take 10`: slowest 10 requests.
- `name`: the request/operation name (e.g. `POST /api/quotes`).
- `resultCode`, `success`: HTTP result code and whether App Insights marked it
  a success.
- `operation_Id`: the trace/operation identifier - the same value that
  correlates back to `traceId`/`TraceId` in logs, per the correlation flow
  above.

This query has not been run against a live resource; it is provided for use
once a real Application Insights instance is connected.

## Alert - POST /api/quotes average duration > 500ms over 5 minutes

Recommended as a scheduled query (log search) alert rule rather than a
hard-coded resource, since no Azure resource ID exists yet:

**Query:**

```kql
requests
| where name == "POST /api/quotes"
| summarize AvgDurationMs = avg(duration) by bin(timestamp, 5m)
| where AvgDurationMs > 500
```

**Alert rule settings (to configure once a resource exists):**

| Setting | Value |
|---|---|
| Signal type | Custom log search / scheduled query |
| Scope | The Application Insights resource for this app (resource ID not known yet - do not hard-code) |
| Evaluation frequency | 5 minutes |
| Aggregation window | 5 minutes |
| Alert logic | Result count > 0 |
| Severity | Sev 3 (Warning) - adjust per on-call conventions |
| Action group | Whichever notification channel the team designates when the resource is created |

No alert has been created in Azure - this is the minimal declarative
configuration needed to create one later, once a subscription/resource is
available.

## Known scope limits

- EF Core (SQLite) command spans are not separately instrumented. Adding
  `OpenTelemetry.Instrumentation.EntityFrameworkCore` was intentionally
  skipped here to avoid pulling in an extra (currently pre-release) package
  beyond what this task required. HTTP-level request/dependency tracing is
  unaffected.
- Logs written through Serilog continue to go to the Console sink exactly as
  before, unchanged. `UseAzureMonitor()` registers its own OpenTelemetry
  logging provider, but Serilog's default `Host.UseSerilog(...)` wiring here
  does **not** forward events to other `ILoggerProvider`s (that requires
  `writeToProviders: true`, and even then, Serilog log events are not
  reliably captured by the Azure Monitor OpenTelemetry logging provider - see
  [serilog/serilog-extensions-logging#285](https://github.com/serilog/serilog-extensions-logging/issues/285)).
  So today, only OpenTelemetry-instrumented **traces** (requests/dependencies,
  from the ASP.NET Core/HttpClient instrumentation) reach Application
  Insights; Serilog **log** entries stay Console-only. Piping Serilog events
  into Application Insights as well (e.g. via `Serilog.Sinks.OpenTelemetry`)
  is a follow-up, not implemented here to keep this change minimal.
