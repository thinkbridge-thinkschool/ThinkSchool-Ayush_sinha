# Day 31 — Exercise Result

## CI Run

Repository: `thinkbridge-thinkschool/ThinkSchool-Ayush_sinha`
Branch: `feature/day-31-polish` (base: `feature/day-30-feature-completeness`)
Workflow: `.github/workflows/ci.yml`, job `capstone` (new for Day 31; the
existing `test` job, covering `day-1/QuotesApi`, is untouched and also
green).

**Run for the current branch-tip commit `b501b0f4`** (verified via the
GitHub Actions API, `conclusion: success`, both jobs, every step):
https://github.com/thinkbridge-thinkschool/ThinkSchool-Ayush_sinha/actions/runs/35323618791

- `test` (day-1/QuotesApi): Checkout, Setup .NET SDK, restore/build/test
  Tests.Domain and Tests.Integration, upload results/coverage, enforce
  >=70% coverage — all steps `success`.
- `capstone` (Capstone-project, new this session): Checkout, Setup .NET
  SDK, restore, build (Release), unit tests (Maintenance/Assets/
  Notifications), integration tests (Api, WebApplicationFactory), E2E
  test (real out-of-process host), vulnerable-package check, upload
  test/coverage artifacts — all steps `success`.

The identical code was also independently confirmed green one commit
earlier (`75d1854b`, run
https://github.com/thinkbridge-thinkschool/ThinkSchool-Ayush_sinha/actions/runs/35321840712)
before an attribution trailer that shouldn't have been in the commit
messages was stripped and the branch rewritten (`git diff` between the
two trees returns zero output — file content never changed, only commit
messages).

## Test Coverage

### Unit

- `MaintainXpert.Maintenance.Tests`: 11/11 passed. Line coverage 109/158
  = **68.99%**.
- `MaintainXpert.Assets.Tests`: 10/10 passed. Line coverage 78/227 =
  **34.36%**.
- `MaintainXpert.Notifications.Tests`: 1/1 passed. Line coverage 22/174 =
  **12.64%**.

Method: `dotnet test <project> --collect:"XPlat Code Coverage"` per
project, cobertura XML aggregated by (source file, line) with a small
script (see [`evidence/coverage-summary.txt`](evidence/coverage-summary.txt)
for the full explanation, including why Assets/Notifications read lower —
their Infrastructure-layer code is exercised by the integration suite,
not their own unit tests).

### Integration

`MaintainXpert.Api.Tests` (WebApplicationFactory, full application
pipeline — DI, middleware, routing, JWT auth, in-memory-backed
repositories): **17/17 passed**. Line coverage across the *entire
application* (every module gets loaded into this one process): 708/1217
= **58.18%**.

### E2E

`MaintainXpert.E2E.Tests` (`WorkOrderLifecycleEndToEndTests`): **1/1
passed** — real `dotnet` child process, real Kestrel socket, full
asset + work-order lifecycle driven purely over HTTP. Coverage is not
reported for this project: coverlet instruments the test process, not a
separate child process communicating over a real socket, so a line
percentage here would be meaningless rather than just low.

**Total: 40/40 tests passed, 0 failed**, across 5 projects. Full raw
output: [`evidence/build-and-test-output.txt`](evidence/build-and-test-output.txt).

## Hot-Path Performance

Endpoint: `POST /api/v1/work-orders` (validation + asset-existence
lookup + repository write + domain-event dispatch — the busiest write
endpoint, and the one every write request's reflection-based validation
cost showed up on).

### Before Polish

p99 = **52.169 ms** (min 1.738 ms, p50 5.291 ms, p95 26.617 ms, max
143.496 ms, mean 10.044 ms). Measured against the original
`ValidationExtensions.Validate<T>` (recomputes reflection metadata on
every call), checked out via `git stash` before the fix was applied.
Raw output: [`evidence/perf-before.txt`](evidence/perf-before.txt).

### After Polish

p99 = **8.165 ms** on the first run immediately after the fix, **11.441
ms** on a second consecutive run (min 1.432–1.906 ms, p50 1.957–2.622 ms,
p95 5.303–7.742 ms, max 19.354–21.913 ms, mean 2.557–3.358 ms). Both runs
used the exact same tool and workload as the "before" run. Raw output
(first run): [`evidence/perf-after.txt`](evidence/perf-after.txt).

**p99 improvement: roughly 78–84%** (52.169 ms → 8.165–11.441 ms),
consistent with removing a `GetConstructors`/`GetProperties`/
`GetCustomAttributes` scan that previously ran on every request instead
of once per type.

### Measurement Method

Tool: [`day-31/perf/HotPathBenchmark`](perf/HotPathBenchmark) (standalone
console app, not part of the CI gate — perf numbers on a shared CI
runner are noisy; this was run locally, twice, for the numbers above).

- Launches the real `MaintainXpert.Api` process (`dotnet run --project
  ... --configuration Release`), bound to a real loopback socket, using
  the in-memory repository fallback — the same environment every prior
  day's evidence in this capstone was captured against, since this
  workstation cannot reach the Day 27 Azure SQL private endpoint.
- Waits for `/health` to report healthy, authenticates, registers one
  asset.
- Runs 20 unmeasured warm-up requests against `POST /api/v1/work-orders`
  (JIT/startup effects), then times **300 further requests** with
  `Stopwatch`, one at a time, sorts the latencies, and reports
  p50/p95/p99/min/max/mean.
- Identical workload and tool run before and after the fix, back to
  back, on the same machine, nothing else changed in between.

## Security Re-check

Full write-up: [`evidence/security-recheck.txt`](evidence/security-recheck.txt).

Checked: authentication/authorization on every write endpoint (still
enforced, `workorders.write` scope), the (by-design, unauthenticated)
read endpoints re-confirmed against Day 27's own threat model rather
than assumed, input validation (behavior-preserving through the perf
fix, pinned by tests), error responses (`GlobalExceptionHandler` still
returns generic `ProblemDetails`, no stack traces/exception types),
secrets/configuration (no secrets in any committed `appsettings*.json`),
logging (client IDs logged, never secrets or tokens), and dependency
vulnerabilities.

Command: `dotnet list Capstone-project/MaintainXpert.slnx package
--vulnerable --include-transitive`
Result: every one of the 10 projects (5 source, 5 test) reports "has no
vulnerable packages given the current sources." This check now also
runs in CI and fails the build if that ever changes.

**Fix applied**: Day 27's threat model explicitly flagged `POST
/auth/token` as unbounded against credential stuffing and left it
unmitigated ("flagged as a follow-up", "accepted risk"). Closed with
ASP.NET Core's built-in fixed-window rate limiter (10 requests/minute per
caller, part of the shared framework — no new package), returning `429`
once exceeded while leaving legitimate requests within the window
unaffected. Regression test:
`AuthTokenRateLimitingTests.Auth_token_serves_valid_requests_within_the_limit_then_rejects_once_it_is_exceeded`,
passing as part of the 17/17 in `MaintainXpert.Api.Tests`.

## GitHub Link

- Repo: https://github.com/thinkbridge-thinkschool/ThinkSchool-Ayush_sinha
- Folder: https://github.com/thinkbridge-thinkschool/ThinkSchool-Ayush_sinha/tree/feature/day-31-polish/Capstone-project/day-31
- Branch: `feature/day-31-polish`
- CI: see [CI Run](#ci-run) above.
- PR: pending — GitHub CLI is installed but not yet authenticated in this
  environment (`gh auth login` required). The branch is pushed and ready;
  open the PR from
  https://github.com/thinkbridge-thinkschool/ThinkSchool-Ayush_sinha/pull/new/feature/day-31-polish
  (base `feature/day-30-feature-completeness`) once authenticated.

## What I Learned

The clearest signal that a "performance polish" target is real, not
invented, was reading the code path every write endpoint shares
(`ValidationExtensions.Validate<T>`) and noticing it re-derived the same
reflection metadata on every request — a pure function of the request
type, computed anew each time. Measuring it before assuming it mattered
turned a 52 ms p99 into an 8–11 ms one with a one-method, behavior-
preserving change; the measurement is what made the fix worth doing
rather than a guess dressed up as one.

## What Would Break This

The rate limiter closes the specific gap Day 27 flagged (unbounded
guesses against one caller), but it's in-memory and per-process: a
distributed attacker spread across many source addresses, or a future
deployment scaled to multiple replicas without a shared limiter store,
would not be fully covered by this fix — a real fix for that shape of
attack needs a distributed limiter (e.g. Redis-backed), which is out of
scope for this polish pass and not claimed as done. Separately, the E2E
test and the benchmark both run against the in-memory repository
fallback, the same limitation every prior day in this capstone has
carried since Day 27 - the real Azure SQL path (behind a private
endpoint) still hasn't been re-verified end-to-end from this workstation.
