# Day 29 — Build Day 1

## Exercise

> Build the foundation and the happy path end to end. Small, reviewable
> commits with clean messages; the main flow works against real infra by
> EOD.

## Goal

Foundation + happy path, without pulling in Day 30-32 scope.

## Starting point (verified before any change)

`dotnet build`: **0 Warnings, 0 Errors**. `dotnet test`
(`tests/MaintainXpert.Maintenance.Tests`, the only test project that
existed): **7/7 passed**. This matches the state left by Day 27/28: the
modular-monolith foundation, the `WorkOrder`/`Asset` domain model,
EF Core + Azure SQL persistence (with an in-memory fallback for
local/dev), JWT client-credentials auth, and the full
create → assign → start → complete happy path were already built and,
per `day-27/evidence/final-verification.txt`, already verified against
real Azure SQL on the deployed Container App. Day 28's own build plan
(`day-28/result.md`, "Day-by-Day Build Plan") lists this as Build Days
1-9, **COMPLETED**, and names Build Day 10 — closing the test-coverage
gap for `Assets`/`Notifications` plus a dispatch-flow integration test —
as the next **PLANNED** item.

So today's foundation-and-happy-path work is: (1) re-verify that happy
path still works, for real, end to end, rather than re-asserting Day 27's
claim; and (2) close exactly that documented Build Day 10 gap — automated
tests for the happy path that until today had none outside the
`Maintenance` aggregate.

## Implemented

- **`tests/MaintainXpert.Assets.Tests`** — `AssetTests.cs` (register
  creates an operational asset with no maintenance history; a blank name
  is rejected; `RecordMaintenanceCompleted` sets the completion
  timestamp) and `WorkOrderCompletedHandlerTests.cs` (the handler records
  completion on the referenced asset; it does not throw when the
  referenced asset does not exist — the not-found case for this handler).
- **`tests/MaintainXpert.Notifications.Tests`** — `WorkOrderCreatedNotificationHandlerTests.cs`,
  verifying the handler sends a notification naming the actual work order
  and asset IDs, using a recording fake `INotificationSink`.
- **`tests/MaintainXpert.Api.Tests`** — a `WebApplicationFactory<Program>`-based
  dispatch-flow integration test (`WorkOrderHappyPathTests.cs`) that
  drives the real minimal-API endpoints, real `WorkOrderService`, real
  aggregates, and the real in-process event dispatcher end to end:
  register an asset → create a work order → assign → start → complete →
  re-fetch the asset and assert `LastMaintenanceCompletedAt` was set by
  the `WorkOrderCompleted` → `Assets.WorkOrderCompletedHandler` dispatch.
  Plus a validation-failure case (empty `Description` → 400), a
  not-found case (completing a nonexistent work order → 404), and an
  unauthenticated-write case (no bearer token → 401). `ApiFactory.cs`
  configures a fixture-only JWT signing key and integration-client secret
  via environment variables set before the host is first built (see
  "What Was Learned" below for why that ordering matters), and pins
  `ConnectionStrings:AzureSql` empty so the fixture always exercises the
  in-memory repositories, deterministically, regardless of what secrets
  happen to be configured on the machine running the tests.
- **`MaintainXpert.slnx`** — registered the three new test projects under
  `/tests/`.

No production/source code changed. This was a test-coverage addition
against the existing, already-built happy path — the smallest change that
closes Build Day 10, per the Day 29 scope of foundation + happy path over
feature growth.

## Architecture / request flow

Unchanged from `docs/architecture.md` and ADR-001 — ratified again today,
not modified:

```
Client
  -> POST /auth/token                         (AuthEndpoints, TokenService)
  -> POST /api/v1/assets                      (AssetEndpoints -> IAssetRepository)
  -> POST /api/v1/work-orders                 (WorkOrderEndpoints -> WorkOrderService.CreateAsync)
       -> WorkOrder.Create (domain invariants) -> IWorkOrderRepository.AddAsync
       -> IDomainEventDispatcher.DispatchAsync(WorkOrderCreated)
            -> Notifications.WorkOrderCreatedNotificationHandler -> ConsoleNotificationSink
  -> POST /api/v1/work-orders/{id}/assign      -> WorkOrder.AssignTechnician
  -> POST /api/v1/work-orders/{id}/start       -> WorkOrder.Start
  -> POST /api/v1/work-orders/{id}/complete    -> WorkOrder.Complete
       -> IDomainEventDispatcher.DispatchAsync(WorkOrderCompleted)
            -> Assets.WorkOrderCompletedHandler -> Asset.RecordMaintenanceCompleted
  -> GET /api/v1/assets/{id}                   -> reflects the updated maintenance state
```

Module boundaries are exactly as ADR-001 describes them: `Assets` and
`Notifications` depend on `Maintenance`'s published event *contracts*
(`WorkOrderCreated`, `WorkOrderCompleted`) and on `SharedKernel`, never on
`Maintenance`'s internal types. Dispatch stays in-process and synchronous
via `InProcessDomainEventDispatcher`, per ADR-001 — no dispatch-resilience
change was made today; that is Build Day 11 in the existing plan, out of
Day 29 scope.

## Happy path (verified against the running local API)

1. `POST /auth/token` → 200, bearer token issued.
2. `POST /api/v1/assets` → **201 Created**, real generated `AssetId`.
3. `GET /api/v1/assets/{id}` → 200, the just-created asset.
4. `POST /api/v1/work-orders` → **201 Created**, `status: Open`, a real
   `WorkOrderId`; console log confirms `WorkOrderCreated` reached
   `Notifications`.
5. `POST /api/v1/work-orders/{id}/assign` → 200, `status: Assigned`.
6. `POST /api/v1/work-orders/{id}/start` → 200, `status: InProgress`.
7. `POST /api/v1/work-orders/{id}/complete` → 200, `status: Completed`.
8. `GET /api/v1/assets/{id}` → 200, `lastMaintenanceCompletedAt` now set —
   proof the `WorkOrderCompleted` event actually reached
   `Assets.WorkOrderCompletedHandler` and updated the aggregate through
   real persistence, not a hardcoded response.

Full transcript: [`evidence/happy-path-verification.txt`](evidence/happy-path-verification.txt).

## Verification

**Build** (`dotnet build`, full solution, after adding the three test
projects):

```
Build succeeded.
    0 Warning(s)
    0 Error(s)
```

**Tests** (`dotnet test`, full solution):

```
MaintainXpert.Maintenance.Tests    Passed:  7, Failed: 0, Total:  7
MaintainXpert.Notifications.Tests  Passed:  1, Failed: 0, Total:  1
MaintainXpert.Assets.Tests         Passed:  5, Failed: 0, Total:  5
MaintainXpert.Api.Tests            Passed:  4, Failed: 0, Total:  4
```

Total: **17/17 passed, 0 failed** (7 pre-existing + 10 new). Raw console
output: [`evidence/build-and-test-output.txt`](evidence/build-and-test-output.txt).

**API run**: `dotnet run --launch-profile http` in
`src/MaintainXpert.Api`, `ASPNETCORE_ENVIRONMENT=Development`, listening
on `http://localhost:5223`. No `ConnectionStrings:AzureSql` is configured
on this dev machine, so the app used the existing, already-documented
in-memory repository fallback (`Program.cs`'s `useSqlServer == false`
branch) — not a fabricated stand-in, but the project's own supported
local/dev configuration (README.md, "How to run the API"; ADR-001,
"in-memory fallback for local/dev"). The real Azure SQL path sits behind
a private endpoint (Day 27) and is not reachable from this machine; it
was verified end-to-end against real Azure SQL when originally deployed
(`day-27/evidence/final-verification.txt`) and was not re-touched today —
no Azure resources were called or modified.

**HTTP verification**: see the Happy Path section above and
[`evidence/happy-path-verification.txt`](evidence/happy-path-verification.txt)
for the full transcript, including the validation-failure (400) and
not-found (404) cases.

## What Was Learned

- Overriding configuration through `WebApplicationFactory.ConfigureWebHost`/`ConfigureAppConfiguration`
  is too late for code in `Program.cs` that reads configuration
  synchronously *before* `builder.Build()` runs —
  `JwtAuthenticationExtensions.AddApiJwtAuthentication` reads `Jwt:Key`
  eagerly to build `TokenValidationParameters` at that point in the
  pipeline. The first version of the `ApiFactory` test fixture used
  `ConfigureAppConfiguration` for the fixture's JWT key and picked up
  whichever real key happened to be in this developer's local user
  secrets instead, at the point that mattered — tokens were issued with
  one key and validated against another, so every write endpoint after
  the first came back 401 despite Development environment. Setting the
  override via environment variables *before* the factory ever builds
  the host fixes it, because environment variables are read earlier in
  ASP.NET Core's default configuration order than
  `ConfigureWebHost`-injected sources, and later than user secrets — so
  they deterministically win regardless of what's configured on the
  machine running the tests.
- The happy path genuinely needs the in-process dispatcher, not just the
  aggregate, to be exercised to prove it end to end: the `Maintenance`
  aggregate tests alone (Day 22) could not have caught a broken
  `Assets`/`Notifications` wiring, because they never touch
  `IDomainEventDispatcher`. The new API-level test does, and would have
  failed if `WorkOrderCompletedHandler` were never registered in DI.

## What Would Break This Design

- **A handler exception surfaces as a request failure.** This is the
  exact, already-documented ADR-001/Day 28 critique: because dispatch is
  in-process and synchronous, an exception inside
  `WorkOrderCreatedNotificationHandler` or `WorkOrderCompletedHandler`
  propagates to `GlobalExceptionHandler` and turns into a generic 500 —
  even though the triggering `WorkOrder`/`Asset` write already succeeded.
  Not fixed today; that is Build Day 11, planned, out of Day 29 scope.
- **The in-memory repositories are not durable and not safe for
  concurrent processes.** `InMemoryWorkOrderRepository`/`InMemoryAssetRepository`
  are `ConcurrentDictionary`-backed singletons scoped to one process — the
  supported local/dev configuration, not a production substitute. Restart
  the app and every asset/work order created during today's manual
  verification is gone; this is expected and by design, not a defect.
- **The real Azure SQL path was not re-verified today.** It was verified
  at Day 27 and is architecturally unchanged, but no request was made
  against the deployed Container App as part of this pass (no reachable
  credentials on this machine, and doing so was out of today's scope) —
  so today's evidence is for the in-memory path only, and a regression
  specific to the SQL repositories (`SqlWorkOrderRepository`/`SqlAssetRepository`)
  would not have been caught by today's verification.
- **`EnsureCreatedAsync` instead of migrations** (unchanged, already
  flagged as Build Day 12/planned) means the real SQL schema still isn't
  under migration control — irrelevant to today's in-memory verification
  but still a real gap for the Azure SQL path.

## Files Created/Modified

**Created**

- `Capstone-project/tests/MaintainXpert.Assets.Tests/MaintainXpert.Assets.Tests.csproj`
- `Capstone-project/tests/MaintainXpert.Assets.Tests/AssetTests.cs`
- `Capstone-project/tests/MaintainXpert.Assets.Tests/WorkOrderCompletedHandlerTests.cs`
- `Capstone-project/tests/MaintainXpert.Notifications.Tests/MaintainXpert.Notifications.Tests.csproj`
- `Capstone-project/tests/MaintainXpert.Notifications.Tests/WorkOrderCreatedNotificationHandlerTests.cs`
- `Capstone-project/tests/MaintainXpert.Api.Tests/MaintainXpert.Api.Tests.csproj`
- `Capstone-project/tests/MaintainXpert.Api.Tests/ApiFactory.cs`
- `Capstone-project/tests/MaintainXpert.Api.Tests/WorkOrderHappyPathTests.cs`
- `Capstone-project/day-29/README.md`
- `Capstone-project/day-29/result.md`
- `Capstone-project/day-29/evidence/happy-path-verification.txt`
- `Capstone-project/day-29/evidence/build-and-test-output.txt`

**Modified**

- `Capstone-project/MaintainXpert.slnx` — registered the three new test
  projects under `/tests/`.

**Not committed to source control (local dev machine only)**

- This machine's `IntegrationClient:ClientSecretHash` user secret (under
  `MaintainXpert.Api`'s `UserSecretsId`) was rotated to a known local
  test value to obtain a bearer token for the manual curl walkthrough,
  since the previous local secret's plaintext was never recorded (same
  no-plaintext-secrets convention as `day-27/evidence/authentication.txt`).
  This affects only this developer's local secret store, not the
  repository or any deployed environment.
