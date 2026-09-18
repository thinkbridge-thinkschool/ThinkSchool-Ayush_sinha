# Day 31 — Polish: tests, perf, security

## Objective

Day 29 built the foundation and happy path; Day 30 closed the remaining
feature gaps (persistence, asset lifecycle, the asset-existence/
decommission rule) and opened a PR for review. Day 31 does not add
product features. It polishes what already exists: fill in the testing
pyramid properly (unit, integration, one genuine E2E), find and fix a
real bottleneck on the hottest write path with a before/after
measurement, re-check security against what Day 27's threat model left
open, and get all of it through a real, green CI gate.

## Testing pyramid

```
        /\
       /E2E\        1 test  - real out-of-process Kestrel host, full HTTP flow
      /------\
     /  Integ. \    17 tests - WebApplicationFactory, full app pipeline
    /------------\
   /   Unit tests  \ 22 tests - domain, application, and infra-adjacent logic
  /------------------\
```

Total: **40/40 tests passing, 0 failed** across five test projects. See
[`evidence/build-and-test-output.txt`](evidence/build-and-test-output.txt)
for the actual `dotnet build`/`dotnet test`/`dotnet list package
--vulnerable` output this session captured.

### Unit tests

Existing unit coverage (Day 29/30) already covered `WorkOrder`/`Asset`
invariants and the domain-event handlers. The one hot-path piece that had
no direct unit test was `ValidationExtensions.Validate<T>` - the
reflection-based validator every write endpoint calls - so Day 31 adds
[`tests/MaintainXpert.Api.Tests/ValidationExtensionsTests.cs`](../tests/MaintainXpert.Api.Tests/ValidationExtensionsTests.cs):
6 tests covering the pass case, blank-required-field, too-long strings,
multiple simultaneous violations, a type with no validation attributes,
and that repeated calls against the (now cached) per-type metadata don't
leak state between instances. These tests pin the *observable behavior*,
so they'd catch a regression in the caching change without caring how
the caching is implemented.

### Integration tests (WebApplicationFactory)

`tests/MaintainXpert.Api.Tests/` already runs the real application
pipeline through `WebApplicationFactory<Program>` - DI, middleware,
routing, JWT authentication/authorization, and the in-memory-backed
`IWorkOrderRepository`/`IAssetRepository` - not isolated classes. Day 31
adds one more integration test in that project:
[`AuthTokenRateLimitingTests.cs`](../tests/MaintainXpert.Api.Tests/AuthTokenRateLimitingTests.cs),
which drives a real sequence of `POST /auth/token` calls through the
actual rate-limiter middleware and asserts both that a legitimate
request within the window still gets a 200 and that the limit produces a
real 429 once exceeded. `MaintainXpert.Api.Tests` now has 17 tests total:
the 10 existing WebApplicationFactory/persistence tests from Day 29/30,
plus the 6 new `ValidationExtensionsTests` unit tests and this 1 new
rate-limiting integration test.

### One E2E test

[`tests/MaintainXpert.E2E.Tests/WorkOrderLifecycleEndToEndTests.cs`](../tests/MaintainXpert.E2E.Tests/WorkOrderLifecycleEndToEndTests.cs)
launches the actual `MaintainXpert.Api` binary as a **separate `dotnet`
process** bound to a real Kestrel socket on loopback, waits for `/health`
to report healthy, then drives the complete business flow purely over
HTTP against that external process: get a token, register an asset,
raise a work order, assign a technician, start it (asset flips to
`UnderMaintenance`), complete it (asset returns to `Operational`,
`LastMaintenanceCompletedAt` set). This is deliberately not the same
transport as the WebApplicationFactory integration tests - it is a real
process boundary and a real socket, which is the one part of "genuine
end-to-end" a WebApplicationFactory test cannot claim on its own. The
capstone is API-only (no frontend), so this is the smallest correct
mechanism for an externally observable entry point.

## Performance: hot-path p99

### Choosing the hot path

The four write endpoints (`POST /assets`, `POST /work-orders`, `/assign`,
`/decommission`) all call `ValidationExtensions.Validate<T>` before
touching the domain or the repository. Reading that method turned up a
real, avoidable cost: it called `type.GetConstructors()`,
`type.GetProperties()`, and `GetCustomAttributes<ValidationAttribute>()`
**on every single request**, even though that reflection result is a
pure function of the request type and never changes after the first
lookup. `POST /api/v1/work-orders` was picked as the benchmark target: it
is the core domain action (work-order creation), it goes through
validation + an asset-existence lookup + a repository write + domain-event
dispatch, and it is the endpoint the existing test suite already exercises
most heavily.

### Fix

Cache the per-type validator metadata (which properties have which
`ValidationAttribute`s) in a `ConcurrentDictionary<Type, PropertyValidator[]>`,
computed once per type instead of once per request. See
[`ValidationExtensions.cs`](../src/MaintainXpert.Api/Infrastructure/ValidationExtensions.cs).
The observable behavior (which requests pass/fail, and the exact error
messages) is unchanged - pinned by `ValidationExtensionsTests`.

### Measurement method

[`day-31/perf/HotPathBenchmark`](perf/HotPathBenchmark) is a standalone
console tool using the same methodology as the E2E test: it launches the
real `MaintainXpert.Api` process (Release build, in-memory repository
fallback - the same environment every prior day's evidence was captured
against, since this workstation cannot reach the Day 27 Azure SQL private
endpoint) on a real socket, authenticates, registers one asset, runs 20
unmeasured warm-up requests against `POST /api/v1/work-orders` (to get
past JIT/startup effects), then times 300 further requests with
`Stopwatch`, sorts the latencies, and reports p50/p95/p99/min/max/mean.

Run with:

```bash
cd Capstone-project/day-31/perf/HotPathBenchmark
dotnet run --configuration Release
```

The **same tool, same workload (300 measured + 20 warm-up requests
against `POST /api/v1/work-orders`), same machine, same session** was run
against the code before the fix (checked out via `git stash` back to the
original `ValidationExtensions.cs`) and after it, back to back, nothing
else changed in between.

### Results (actual, measured)

| | Before (`git stash` to pre-fix code) | After (fix applied) |
|---|---|---|
| min | 1.738 ms | 1.432–1.906 ms |
| p50 | 5.291 ms | 1.957–2.622 ms |
| p95 | 26.617 ms | 5.303–7.742 ms |
| **p99** | **52.169 ms** | **8.165–11.441 ms** |
| max | 143.496 ms | 19.354–21.913 ms |
| mean | 10.044 ms | 2.557–3.358 ms |

The "after" column shows the range across two consecutive runs (see
[`evidence/perf-before.txt`](evidence/perf-before.txt) and
[`evidence/perf-after.txt`](evidence/perf-after.txt) for the raw output
of the first before/after pair). p99 dropped by roughly 78-84% and mean
latency by roughly 66-75%, consistent with removing a reflection scan
that ran on every request.

## Security re-check

Full write-up: [`evidence/security-recheck.txt`](evidence/security-recheck.txt).
Summary: authentication/authorization, input validation, error-response
shape, secrets/configuration, and logging were all re-checked and confirmed
unchanged/still holding. One real, previously-documented gap was found and
fixed: Day 27's own threat model flagged `POST /auth/token` as unbounded
against credential-stuffing and explicitly left it as an accepted risk
("flagged as a follow-up"). Day 31 closes it with ASP.NET Core's built-in
rate limiter (`Microsoft.AspNetCore.RateLimiting`, part of the shared
framework, no new package) - a 10-requests/minute fixed window per
caller, `429` once exceeded - with a regression test
(`AuthTokenRateLimitingTests`). Dependency check:
`dotnet list Capstone-project/MaintainXpert.slnx package --vulnerable
--include-transitive` reports zero vulnerable packages across every
project; this check now also runs in CI and fails the build if that
changes.

## CI verification

The existing `.github/workflows/ci.yml` only ever built/tested
`day-1/QuotesApi` - the capstone had no CI gate at all. Day 31 adds a
second job, `capstone`, that restores and builds
`Capstone-project/MaintainXpert.slnx` (Release), runs each unit test
project, the WebApplicationFactory integration project, and the E2E
project (each with `--collect:"XPlat Code Coverage"` where coverage is
meaningful), then runs `dotnet list package --vulnerable` and fails the
build if any vulnerable package is reported. It leaves the existing
day-1 job untouched.

Actual, verified result: [run 35323618791](https://github.com/thinkbridge-thinkschool/ThinkSchool-Ayush_sinha/actions/runs/35323618791),
`conclusion: success` on both jobs, for the current branch-tip commit.
Full detail: [`result.md`](result.md#ci-run).

## Relevant commands

```bash
cd Capstone-project
dotnet build MaintainXpert.slnx
dotnet test MaintainXpert.slnx                                    # unit + integration + E2E
dotnet list MaintainXpert.slnx package --vulnerable --include-transitive
cd day-31/perf/HotPathBenchmark && dotnet run --configuration Release
```
