# Day 30 — Build day 2: feature completeness

## Task

Hit feature completeness and prepare the implementation for code review.

## Repository

https://github.com/thinkbridge-thinkschool/ThinkSchool-Ayush_sinha/tree/main/Capstone-project/day-30

## What I implemented

Starting from the Day 29 foundation (create → assign → start → complete
work order lifecycle, verified against the running API), I closed two
gaps rather than bolting on unrelated features:

1. **A persistence bug.** `SqlWorkOrderRepository`/`SqlAssetRepository`
   only called `SaveChangesAsync` when creating a record. Every state
   change after that — assign, start, complete, and the asset updates
   triggered by the domain-event handlers — was never saved against the
   real SQL-backed repositories; only the in-memory fallback happened to
   work, because it holds live object references. Added `UpdateAsync` to
   both repository interfaces, wired it into `WorkOrderService` and the
   `Assets` domain-event handlers, and proved it with a test that opens a
   fresh `AppDbContext` per step (mirroring separate HTTP requests) so a
   missing save shows up as a stale read.
2. **An incomplete asset lifecycle.** `AssetStatus` already declared
   `UnderMaintenance` and `Decommissioned`, but nothing ever set them.
   Added `Asset.BeginMaintenance()`/`Decommission()`, a new
   `WorkOrderStarted` domain event that marks the asset `UnderMaintenance`
   when its work order starts (mirroring the existing
   `WorkOrderCompleted` → back to `Operational` flow), and
   `POST /api/v1/assets/{id}/decommission`.
3. **A missing business rule.** `WorkOrderService.CreateAsync` used to
   accept a work order against any non-default `AssetId`, including ones
   that didn't exist. It now checks the referenced asset through a new
   `IAssetLookup` port — owned by `Maintenance`, implemented only by the
   API host — and rejects a missing asset (404) or a decommissioned one
   (409).

## Happy path

`POST /api/v1/assets` (201, Operational) → `POST /api/v1/work-orders`
(201, Open; 404/409 if the asset is missing/decommissioned) →
`POST /{id}/assign` (200, Assigned) → `POST /{id}/start` (200, InProgress;
asset flips to UnderMaintenance) → `POST /{id}/complete` (200, Completed;
asset returns to Operational, `LastMaintenanceCompletedAt` set) →
`GET /api/v1/assets/{id}` confirms the final state at each step.
`POST /api/v1/assets/{id}/decommission` exposes the terminal state
directly. Full transcript: [`evidence/happy-path-verification.txt`](evidence/happy-path-verification.txt).

## Verification

- `dotnet build`: 0 Warnings, 0 Errors.
- `dotnet test`: **32/32 passed, 0 failed** (11 `MaintainXpert.Maintenance.Tests`,
  10 `MaintainXpert.Assets.Tests`, 1 `MaintainXpert.Notifications.Tests`,
  10 `MaintainXpert.Api.Tests` — up from Day 29's 17/17). Full output:
  [`evidence/build-and-test-output.txt`](evidence/build-and-test-output.txt).
- Ran the API locally (`dotnet run`, in-memory fallback — this machine
  can't reach the Azure SQL instance behind the Day 27 private endpoint)
  and drove the full lifecycle plus the new 404/409/200 cases with real
  HTTP requests, including a manual before/after check that reverting
  the persistence fix makes the new regression test fail. Full
  transcript: [`evidence/happy-path-verification.txt`](evidence/happy-path-verification.txt);
  how the persistence bug was found and proven fixed:
  [`evidence/persistence-fix-verification.txt`](evidence/persistence-fix-verification.txt).

## Pull Request

PR URL: pending manual creation. The branch is pushed to the company
repository and GitHub returned the create-PR link, but the GitHub CLI
(`gh`) is not installed on this machine, so the PR itself has to be
opened by hand from:

https://github.com/thinkbridge-thinkschool/ThinkSchool-Ayush_sinha/pull/new/feature/day-30-feature-completeness

Base branch: `main`. Head branch: `feature/day-30-feature-completeness`
(pushed to `origin`, i.e. `thinkbridge-thinkschool/ThinkSchool-Ayush_sinha`).
Title: `Day 30: feature completeness`.

## Review feedback thread

Reviewer feedback is pending; no review comment was fabricated. The PR
itself has not been created yet (see above — that's the manual action
still required), so there is no review thread to report yet. Once the PR
is open and a reviewer comments, this section will be updated with the
actual thread: the comment, the response, and whichever of "changed" or
"defended, with reasoning" applies — not a rewritten history.

## What I changed

- Added `IWorkOrderRepository.UpdateAsync`/`IAssetRepository.UpdateAsync`
  and wired them into `WorkOrderService` and the `Assets` domain-event
  handlers, so SQL-backed state changes after creation are actually
  durable.
- Added `Asset.BeginMaintenance()`/`Decommission()`, the `WorkOrderStarted`
  domain event, `Assets.WorkOrderStartedHandler`, and
  `POST /api/v1/assets/{id}/decommission`.
- Added `Maintenance.Application.IAssetLookup` and `AssetLookupAdapter`
  (API host), and made `WorkOrderService.CreateAsync` reject a work order
  against a missing or decommissioned asset.
- Added `AssetNotFoundException`, `AssetDecommissionedException`,
  `InvalidAssetOperationException`, mapped to `ProblemDetails` in
  `GlobalExceptionHandler`.
- Added 15 tests across all four test projects.
- Updated `docs/architecture.md` to describe the new flow and the
  `IAssetLookup` port.

## What I defended

No review discussion has happened yet (see "Review feedback thread"
above), so there's nothing to report here as an actual defended point.
The one design call worth flagging for the reviewer, in case it comes
up: `IAssetLookup` is a new port that `Maintenance` declares for itself
and only the API host implements, rather than having `Maintenance`
reference `Assets` directly. That's slightly more indirection for one
method, but it's the same dependency-inversion shape the repository
interfaces already use, and it's what keeps ADR-001's one-directional
module boundary (`Assets`/`Notifications` depend on `Maintenance`'s
contracts, never the reverse) actually true instead of just documented.

## What did I learn this session?

The most useful finding wasn't a missing feature, it was a missing test
angle: the in-memory repositories are reference-type dictionaries, so a
handler that mutates an aggregate and forgets to "save" it still looks
correct in every test that only exercises that path, because the
mutation is already visible through the shared reference. The SQL
repositories don't have that property — nothing was durable there past
the initial `AddAsync` until this session added `UpdateAsync` and called
it. Proving it required a test that opens a fresh `DbContext` per step,
the same shape as separate HTTP requests, rather than one that reuses a
single context across the whole scenario. That's a general lesson, not
specific to this repo: an in-memory test double built on shared
references can hide a missing persistence call that a real
request-scoped context would expose immediately.

Completing `Asset`'s status lifecycle was more about reading the existing
code carefully than designing something new — `UnderMaintenance` and
`Decommissioned` were already sitting in the enum from Day 22, unused.
Treating "why does this exist but never get set" as a real question,
rather than assuming it was intentionally deferred, is what turned into
the decommission endpoint and the `WorkOrderStarted` event.

## What would break this?

- A concurrent request racing the decommission endpoint against
  `WorkOrderService.CreateAsync`'s asset-lookup check (check-then-act,
  not a transaction) could still let a work order through against an
  asset that gets decommissioned a moment later. Low likelihood for a
  single-operator capstone slice, but real.
- `IAssetLookup`'s only implementation goes through `IAssetRepository`
  under the same request scope as everything else; if `Assets` ever
  moves to genuinely separate storage or a separate service, this port
  is the seam to change, but today it's still an in-process call, not a
  network one — the ADR-001 critique about synchronous in-process
  coupling applies here too, not just to the event dispatcher.
- The already-documented ADR-001 gap (a handler exception turns a
  successful `Maintenance` write into a generic 500, no idempotency key)
  is unchanged by this session's work — `WorkOrderStartedHandler` and the
  decommission endpoint inherit the exact same risk `WorkOrderCompletedHandler`
  already had.
- `SqlRepositoryPersistenceTests` uses EF Core's InMemory provider, which
  is good enough to prove "does this call SaveChanges" but doesn't
  exercise real SQL Server behavior (concurrency tokens, transaction
  isolation) — the real Azure SQL path still hasn't been re-verified
  end-to-end since Day 27, for the same private-endpoint-reachability
  reason Day 29 noted.

## Commit log

```
d08fd4f9 docs(capstone): update architecture for day 30 asset lifecycle
56b46d2d test(capstone): cover day 30 asset lifecycle and validation rules
8b1e23ff feat(capstone): complete work order and asset feature workflow
18c79d88 fix(capstone): persist repository state changes after creation
```

Branch: `feature/day-30-feature-completeness`, pushed to `origin`
(`thinkbridge-thinkschool/ThinkSchool-Ayush_sinha`), base `main`.
