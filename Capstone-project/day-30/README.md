# Day 30 — Build day 2: feature completeness

## Goal

Day 29 verified the foundation and the happy path: register an asset,
raise a work order against it, assign a technician, start and complete
the work, and have that completion update the asset through the
`WorkOrderCompleted` event. Day 30 completes the feature set on top of
that foundation — closing gaps in the existing design rather than adding
unrelated functionality — and prepares the change for review.

Two kinds of gaps were closed. The first is a correctness gap: work
orders could be raised against assets that didn't exist, and once a work
order moved past its initial creation, none of its state changes (or the
asset changes they trigger) were actually durable against the real SQL
repositories. The second is a design gap: `AssetStatus` already declared
`UnderMaintenance` and `Decommissioned`, but nothing in the codebase ever
set them — the asset lifecycle the enum implied was never wired up.

## What was implemented

**Persistence fix.** `IWorkOrderRepository`/`IAssetRepository` gained an
`UpdateAsync` method. The SQL implementations now call
`SaveChangesAsync` on every state change, not just on create; the
in-memory implementations got the same method for interface symmetry.
`WorkOrderService` and the `Assets` domain-event handlers call it after
every mutation (assign, start, complete, decommission, and the
asset-side effects of the work-order lifecycle). Previously, none of
those transitions would have survived past the HTTP response when
running against Azure SQL — only creation was ever saved. See
[`evidence/persistence-fix-verification.txt`](evidence/persistence-fix-verification.txt)
for how this was found and how the fix is proven.

**Asset lifecycle completion.** `Asset` gained `BeginMaintenance()` and
`Decommission()`. A new `WorkOrderStarted` domain event (raised by
`WorkOrder.Start`) is dispatched to a new `Assets.WorkOrderStartedHandler`,
which marks the referenced asset `UnderMaintenance` — mirroring the
existing `WorkOrderCompleted` → `Assets.WorkOrderCompletedHandler` flow
that already returns it to `Operational`. `POST /api/v1/assets/{id}/decommission`
exposes the terminal `Decommissioned` state explicitly.

**Cross-module business rule.** `WorkOrderService.CreateAsync` now
rejects a work order raised against an asset that doesn't exist (404) or
that has been decommissioned (409). Because `Maintenance` must never
depend on `Assets` directly (see Architecture below), this is done
through a small port — `Maintenance.Application.IAssetLookup` — that
`Maintenance` declares and only the API host implements
(`AssetLookupAdapter`), the same dependency-inversion shape already used
for the repository interfaces.

**Consistent error responses.** `GlobalExceptionHandler` maps the two new
exceptions (`AssetNotFoundException` → 404, `AssetDecommissionedException`
→ 409) and the new `InvalidAssetOperationException` (re-decommissioning →
409) to `ProblemDetails`, the same pattern already used for
`WorkOrderNotFoundException`/`InvalidWorkOrderTransitionException`.

## Architecture

Unchanged: one deployable app (`MaintainXpert.Api`) over four modules
(`Maintenance`, `Assets`, `Notifications`, `SharedKernel`), each with its
own `Domain`/`Application`/`Infrastructure` folders. The one addition is
`IAssetLookup`: `Maintenance` owns and declares the interface it needs
from `Assets`, and only the API composition root — which already
references every module — wires up the concrete adapter. `Maintenance`'s
own project still never references `Assets`, so the one-directional
module boundary from ADR-001 holds.

```
API layer            Endpoints, request/response DTOs, ProblemDetails mapping
     |
Application layer    WorkOrderService, domain-event handlers, ports (IAssetLookup,
     |                IWorkOrderRepository, IAssetRepository)
     |
Domain layer          WorkOrder, Asset - lifecycle rules and invariants, no
     |                infrastructure dependencies
     |
Infrastructure        SqlWorkOrderRepository/SqlAssetRepository (Azure SQL),
                       InMemory* (local/dev fallback), AssetLookupAdapter
```

Full detail: [`../docs/architecture.md`](../docs/architecture.md).

## Main workflow

```
POST /api/v1/assets                          -> 201, asset Operational
POST /api/v1/work-orders                     -> 404/409 if the asset is missing/decommissioned
                                                 201, work order Open (otherwise)
                                                 -> WorkOrderCreated -> Notifications
POST /api/v1/work-orders/{id}/assign         -> 200, Assigned
POST /api/v1/work-orders/{id}/start          -> 200, InProgress
                                                 -> WorkOrderStarted -> asset UnderMaintenance
POST /api/v1/work-orders/{id}/complete       -> 200, Completed
                                                 -> WorkOrderCompleted -> asset Operational,
                                                    LastMaintenanceCompletedAt set
GET  /api/v1/assets/{id}                     -> reflects the current asset state at each step
POST /api/v1/assets/{id}/decommission        -> 200, Decommissioned (terminal)
```

## API

All endpoints are unchanged from Day 29 except the one addition below.

| Method | Route | Notes |
|---|---|---|
| POST | `/auth/token` | unchanged |
| POST | `/api/v1/assets` | unchanged |
| GET | `/api/v1/assets/{id}` | unchanged |
| POST | `/api/v1/assets/{id}/decommission` | **new** — 200 with the updated asset, 404 if missing, 409 if already decommissioned |
| POST | `/api/v1/work-orders` | now 404 if the asset doesn't exist, 409 if it's decommissioned |
| GET | `/api/v1/work-orders/{id}` | unchanged |
| POST | `/api/v1/work-orders/{id}/assign` | unchanged |
| POST | `/api/v1/work-orders/{id}/start` | unchanged response shape; now also dispatches `WorkOrderStarted` |
| POST | `/api/v1/work-orders/{id}/complete` | unchanged |

## Persistence

Same approach as Day 27/29: EF Core over Azure SQL when
`ConnectionStrings:AzureSql` is configured, an in-memory
`ConcurrentDictionary`-backed fallback otherwise (this dev machine only
exercises the fallback — Azure SQL sits behind a private endpoint). The
Day 30 change is that every repository now has an explicit `UpdateAsync`
that the SQL implementation backs with `SaveChangesAsync`, so state
changes after the initial create are actually durable against the real
database, not just reflected in the in-memory object graph returned to
the caller. See [`evidence/persistence-fix-verification.txt`](evidence/persistence-fix-verification.txt).

## Validation and failure handling

- Missing/blank `Name`/`Description` → 400 (`ValidationExtensions`, unchanged).
- Referencing a nonexistent asset when creating a work order → 404.
- Referencing a decommissioned asset when creating a work order → 409.
- Completing/assigning/starting a nonexistent work order → 404 (unchanged).
- Invalid work-order lifecycle transitions → 409 (unchanged).
- Decommissioning a nonexistent asset → 404; decommissioning an
  already-decommissioned asset → 409.
- Missing/invalid bearer token on a write endpoint → 401 (unchanged).

## Testing

`dotnet test` (full solution): **32/32 passed, 0 failed** — 11
`MaintainXpert.Maintenance.Tests`, 10 `MaintainXpert.Assets.Tests`, 1
`MaintainXpert.Notifications.Tests`, 10 `MaintainXpert.Api.Tests` (7 +
15 new since Day 29's 17/17). `dotnet build`: 0 warnings, 0 errors. Full
output: [`evidence/build-and-test-output.txt`](evidence/build-and-test-output.txt).

New coverage: `Asset.BeginMaintenance`/`Decommission` and the
already-decommissioned failure case; `WorkOrderStartedHandler` (including
the missing-asset no-op, mirroring the existing `WorkOrderCompletedHandler`
test); `WorkOrderService`'s asset-existence/decommission rule with a fake
`IAssetLookup`; API-level 404/409/200 coverage for the new endpoint and
rule; and `SqlRepositoryPersistenceTests`, which opens a fresh
`AppDbContext` per step (mirroring separate HTTP requests) against EF
Core's InMemory provider to prove the SQL repositories actually persist
state changes, not just create.

Full manual HTTP verification against the running API:
[`evidence/happy-path-verification.txt`](evidence/happy-path-verification.txt).

## Review

Day 30's exercise is code review etiquette: hit feature completeness,
open a PR for review, and respond to comments the way a team would —
address them or push back with reasoning, not silent force-pushes. The
exercise submission is the PR URL plus a thread where feedback was
responded to, stating what changed and what was defended.

This work is submitted as a pull request from
`feature/day-30-feature-completeness` into `main` in
`thinkbridge-thinkschool/ThinkSchool-Ayush_sinha`, covering
`Capstone-project/day-30`.

- **PR URL:** see [`result.md`](result.md#pull-request)
- **Review thread:** see [`result.md`](result.md#review-feedback-thread) —
  filled in once a reviewer comments; not fabricated in the meantime
- **What changed / what was defended:** see [`result.md`](result.md#what-i-changed)
  and [`result.md`](result.md#what-i-defended)

## What changed from Day 29

- Fixed: SQL repositories now persist state changes after creation, not
  just at creation.
- Added: `Asset.BeginMaintenance()`/`Decommission()`, completing the
  `AssetStatus` lifecycle that Day 22/27/29 had declared but never used.
- Added: `WorkOrderStarted` domain event and `Assets.WorkOrderStartedHandler`.
- Added: `POST /api/v1/assets/{id}/decommission`.
- Added: the asset-existence/decommission business rule on work-order
  creation, via a new `IAssetLookup` port owned by `Maintenance`.
- Added: 15 new tests across all four test projects; all 17 existing
  Day 29 tests still pass unmodified in behavior (three call sites
  updated for `WorkOrder.Start`'s new `DateTimeOffset` parameter, which
  the aggregate needs to timestamp `WorkOrderStarted` the same way
  `Create`/`Complete` already timestamp their own events).
