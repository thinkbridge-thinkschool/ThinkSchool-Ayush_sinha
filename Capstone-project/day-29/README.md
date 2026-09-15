# Day 29 — Build Day 1: Foundation + Happy Path

## Repository

- **Repo:** https://github.com/thinkbridge-thinkschool/ThinkSchool-Ayush_sinha
- **Folder:** `Capstone-project/` (capstone root); today's changes live in
  `Capstone-project/tests/MaintainXpert.Assets.Tests/`,
  `Capstone-project/tests/MaintainXpert.Notifications.Tests/`,
  `Capstone-project/tests/MaintainXpert.Api.Tests/`, and
  `Capstone-project/day-29/` (this write-up).
- **Branch:** `main`.
- **Commit log for today:** none yet — per this session's explicit
  instructions, nothing was committed. See "Proposed Commits" below for
  the breakdown that's ready to go once approved.

## Exercise

> Build the foundation and the happy path end to end. Small, reviewable
> commits with clean messages; the main flow works against real infra by
> EOD.

## Project

**MaintainXpert** — `Capstone-project`. Foundation (modular-monolith
scaffold, `WorkOrder`/`Asset` domain model, EF Core + Azure SQL
persistence with an in-memory local/dev fallback, JWT auth, API
endpoints) and the create → assign → start → complete happy path were
already built and verified against real Azure SQL at Day 27
(`day-27/evidence/final-verification.txt`), and reviewed in the Day 28
ADR. Day 28's own build plan named the next open item as Build Day 10:
closing the test-coverage gap for `Assets`/`Notifications` plus a
dispatch-flow integration test — that is today's foundation-and-happy-path
work.

## What Day 29 Did

1. **Re-verified the happy path for real**, against a locally running
   instance of the actual API (not a re-assertion of Day 27's claim):
   register an asset → create a work order against it → assign a
   technician → start → complete → confirm the asset's
   `lastMaintenanceCompletedAt` was updated by the real
   `WorkOrderCompleted` → `Assets.WorkOrderCompletedHandler` dispatch, plus
   a validation-failure (400) and two not-found (404) cases. Full
   transcript: [`evidence/happy-path-verification.txt`](evidence/happy-path-verification.txt).
2. **Closed the documented test-coverage gap** (Build Day 10): added
   `MaintainXpert.Assets.Tests` (asset invariants + the
   `WorkOrderCompletedHandler`, including its not-found case),
   `MaintainXpert.Notifications.Tests` (`WorkOrderCreatedNotificationHandler`),
   and `MaintainXpert.Api.Tests` (a `WebApplicationFactory`-based
   dispatch-flow integration test exercising the same happy path
   end-to-end through the real minimal-API endpoints and the real
   in-process event dispatcher).

No production code changed — this was foundation validation and
test-coverage, not new features. Full detail, exact build/test output,
and the files touched: [`result.md`](result.md).

## Results

- `dotnet build`: 0 Warnings, 0 Errors.
- `dotnet test`: **17/17 passed** across four test projects (7
  pre-existing `Maintenance` tests + 10 new).
- Full happy path verified with real HTTP requests against the running
  API (local in-memory persistence — this dev machine cannot reach the
  Azure SQL instance, which sits behind a private endpoint per Day 27).

## What Was Learned

Overriding JWT configuration through `WebApplicationFactory`'s
`ConfigureWebHost` runs too late for `Program.cs` code that reads
`Jwt:Key` synchronously before `builder.Build()` — the fix is
environment variables, set before the factory first builds the host, per
ASP.NET Core's default configuration source order. See `result.md` for
the full explanation and the failure it caused before the fix.

## What Would Break This Design

The already-documented ADR-001/Day 28 critique still stands and wasn't
addressed today (that's Build Day 11, out of Day 29 scope): a failing
event handler can still turn a successful `Maintenance` write into a
generic 500 for the caller. See `result.md`, "What Would Break This
Design", for the full list, including the scope of what today's
in-memory-only verification does and doesn't cover.

## Proposed Commits

Not created yet — this session was told not to commit without explicit
sign-off. Once approved, the intended breakdown (small, reviewable,
matching what was actually touched) is:

1. `test(assets): cover Asset invariants and WorkOrderCompletedHandler`
2. `test(notifications): cover WorkOrderCreatedNotificationHandler`
3. `test(api): add happy-path dispatch-flow integration tests`
4. `chore(solution): register new test projects in MaintainXpert.slnx`
5. `docs(day-29): record foundation + happy-path verification`

## Evidence

- Full write-up: [`result.md`](result.md)
- HTTP transcript: [`evidence/happy-path-verification.txt`](evidence/happy-path-verification.txt)
- Raw build/test console output: [`evidence/build-and-test-output.txt`](evidence/build-and-test-output.txt)
