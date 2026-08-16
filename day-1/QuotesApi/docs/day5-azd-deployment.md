# Day 5 — Deploy via azd CLI

## Azure environment

Subscription:
Azure for Students

Subscription ID:
708f56eb-d40f-4658-adde-d6f5866dad34

Location:
centralindia

Resource group:
thinkschool-rg

Container Apps environment:
thinkschool-env

## azure.yaml

The service `quotes-api` is defined once, pointing at the actual project
(`project: .`, i.e. this `day-1/QuotesApi` directory), hosted as an Azure
Container App, built from its `.NET` container tooling (no Dockerfile):

```yaml
name: quotes-api
services:
    quotes-api:
        project: .
        host: containerapp
        language: dotnet
resources:
    quotes-api:
        type: host.containerapp
        port: 8080
```

The generated `infra/` (Bicep) does **not** create a new resource group or a
new Container Apps environment — both are referenced as `existing`
(`thinkschool-rg`, `thinkschool-env`), since they were already provisioned
outside of azd (see `docs/day5-azure-container-apps.md`). It creates a new
Azure Container Registry, Log Analytics workspace, and Application Insights
instance (none of those existed beforehand), plus the `quotes-api` Container
App and a user-assigned managed identity scoped only to `AcrPull` on the
registry.

## Deployment

Command:

```
azd up
```

The first `azd up` attempt failed and was fixed through three separate
diagnose-fix-redeploy cycles, each confirmed with real Azure CLI/container
log evidence (not guessed):

1. **`ImagePullBackOff`** — `QuotesApi.csproj` set the obsolete
   `ContainerImageName` MSBuild property, which (per
   `Microsoft.NET.Build.Containers.targets`) unconditionally overwrites
   `ContainerRepository` even when azd supplies it via
   `-p:ContainerRepository=<service>/<service>-<env>` on the command line.
   The image was actually pushed to `quotes-api:<tag>` in the registry while
   the Container App was configured to pull
   `quotes-api/quotes-api-quotesapi-thinkschool:<tag>`. Fixed by removing the
   obsolete property from the `.csproj`.
2. **`CrashLoopBackOff` (`DllNotFoundException: e_sqlite3`)** — the project
   pinned `ContainerRuntimeIdentifier=linux-musl-x64` for its Alpine base
   image, but azd's own `dotnet publish` invocation for this service
   unconditionally passes `-r linux-x64` regardless of that setting, which
   governs the actual native asset (SQLite) resolved at publish time — so a
   glibc SQLite binary was always embedded into the musl/Alpine image
   regardless. Fixed by aligning `ContainerBaseImage` and `RuntimeIdentifier`
   to glibc/`linux-x64` (what azd actually deploys) instead of fighting it.
3. **`CrashLoopBackOff` (`SqliteException: SQLite Error 14, unable to open
   database file`)** — the default connection string
   (`Data Source=quotes.db`) resolves relative to the container's working
   directory, which the deployed container's non-root user cannot write to.
   Fixed by setting `ConnectionStrings__DefaultConnection` to
   `Data Source=/tmp/quotes.db` as a Container App environment variable
   (`/tmp` is writable regardless of the running user) — the database engine
   itself (SQLite) was not changed.

The final successful `azd up` run:

```
Provisioning and deploying (azd up)
  (✓) Done: Log Analytics workspace: log-2i2oapij4zsrc
  (✓) Done: Container Registry: cr2i2oapij4zsrc
  (✓) Done: Application Insights: appi-2i2oapij4zsrc
  (✓) Done: Portal dashboard: dash-2i2oapij4zsrc
  (✓) Done: Container App: quotes-api
  quotes-api: Publishing (Publishing container image)
  quotes-api: Deploying (Updating container app revision) [39s]
  quotes-api: Deploying (Waiting for container revision (15s)) [54s]
  quotes-api: Deploying (Fetching endpoints for service) [57s]
  quotes-api: Done [57s]
  - Endpoint: https://quotes-api.politeocean-3efec37e.centralindia.azurecontainerapps.io/

SUCCESS: Your application was provisioned and deployed to Azure in 2 minutes 51 seconds.
  Provisioning: 1 minute 48 seconds
  Deploying:    57 seconds
```

- **Container Registry**: `cr2i2oapij4zsrc.azurecr.io`
- **Container App**: `quotes-api` (resource group `thinkschool-rg`)
- **Image**: `cr2i2oapij4zsrc.azurecr.io/quotes-api/quotes-api-quotesapi-thinkschool:azd-deploy-1786721696`
- **Revision**: `quotes-api--azd-1786721736` (100% traffic, `HealthState: Healthy`, replica `runningState: Running`, `restartCount: 0`)
- **Provisioning status**: `Succeeded`

## Live URL

```
https://quotes-api.politeocean-3efec37e.centralindia.azurecontainerapps.io/
```

Confirmed via `az containerapp show`:

```json
{
  "external": true,
  "fqdn": "quotes-api.politeocean-3efec37e.centralindia.azurecontainerapps.io",
  "targetPort": 8080,
  "latestRevisionName": "quotes-api--azd-1786721736",
  "provisioningState": "Succeeded"
}
```

## Health verification

Command:

```
curl -i https://quotes-api.politeocean-3efec37e.centralindia.azurecontainerapps.io/health
```

Actual response:

```
HTTP/2 404
content-length: 0
date: Fri, 14 Aug 2026 15:36:33 GMT
server: Kestrel
```

**This is not HTTP 200.** QuotesApi does not define a `/health` route
anywhere in its source (confirmed by exhaustive search of `Program.cs`,
`Endpoints/`, and `Extensions/` during configuration review — see
`docs/day5-azure-container-apps.md`). No fake/placeholder health endpoint
was added to make this return 200, per this exercise's explicit constraint.

The deployment itself is confirmed live and functioning correctly against
the application's actual routes:

```
curl -i https://quotes-api.politeocean-3efec37e.centralindia.azurecontainerapps.io/api/quotes
```

```
HTTP/2 200
content-type: application/json; charset=utf-8
date: Fri, 14 Aug 2026 15:36:59 GMT
server: Kestrel

{"page":1,"size":10,"total":0,"items":[]}
```

## Result

QuotesApi was successfully deployed to Azure Container Apps through the
Azure Developer CLI (`azd up`): the Container App is provisioned
(`Succeeded`), its latest revision is `Healthy` and serving 100% of traffic,
and a real application route (`GET /api/quotes`) returns HTTP 200 with valid
JSON from the live endpoint.

The specific `/health` endpoint referenced by this exercise does **not**
exist in the application, so `curl .../health` genuinely returns HTTP 404,
not 200. This is a pre-existing gap in the application (not introduced by
this deployment work) and is called out here rather than worked around.
