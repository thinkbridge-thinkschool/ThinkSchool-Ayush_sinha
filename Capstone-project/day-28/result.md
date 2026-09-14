# Day 28 — Result

## Exercise

> Paste the ADR + your day-by-day build plan + the top critique you got and how it changed the design.

## 1. ADR Decision

MaintainXpert stays a single deployable modular monolith
(`MaintainXpert.Api` host over the `Maintenance`, `Assets`,
`Notifications`, and `SharedKernel` modules), and cross-module reaction to
domain events (`WorkOrderCreated`, `WorkOrderCompleted`) is dispatched
in-process and synchronously by `InProcessDomainEventDispatcher`, rather
than split into microservices or backed by a real message broker/outbox.
Full ADR: [`docs/adr/ADR-001-modular-monolith-in-process-domain-events.md`](../docs/adr/ADR-001-modular-monolith-in-process-domain-events.md).

## 2. Context

`Maintenance` must let `Assets` and `Notifications` react to a work
order's lifecycle without depending on their internals. The project is a
solo, single-integration-client capstone slice (one aggregate family:
`WorkOrder`, `Asset`). The Azure subscription used for deployment has a
verified, hard, subscription-wide limit of **one** Container Apps
environment (`MaxNumberOfGlobalEnvironmentsInSubExceeded`,
`day-27/evidence/architecture.txt`) — splitting into independently
deployed services is not currently provisionable, regardless of code
readiness.

## 3. Alternatives

| Alternative | Core idea | Rejected because |
|---|---|---|
| Microservices | `Maintenance`/`Assets`/`Notifications` as independently deployable services over a network/broker | Distributed-systems tax (service discovery, versioned network contracts, per-service CI/CD) isn't earned by current load/team size, and the subscription cannot even provision more than one Container Apps environment today |
| Modular monolith + real broker/outbox now | Keep one deployable, but dispatch `WorkOrderCreated`/`WorkOrderCompleted` via an outbox table + Azure Service Bus/Storage Queue instead of in-process calls | Today's handlers (one asset lookup, one console write) don't yet have the latency/reliability profile that justifies an outbox, a relay, and idempotent handlers; the seam to add it later already exists |
| **Chosen: modular monolith + in-process synchronous dispatch** | One host, one build, module boundaries enforced by project references + event contracts, dispatch via `IDomainEventDispatcher` | — |

Full comparison (advantages/disadvantages/operational/dev/scalability/
testing/security impact for each): ADR §"Alternatives Considered".

## 4. Trade-offs

- **Benefits:** one build/deploy/test cycle; no broker to provision or
  pay for; module boundaries already enforced by the compiler (project
  references); the dispatch mechanism is isolated behind one interface,
  so it's revisable later without touching the aggregates.
- **Costs:** no failure isolation between an aggregate write and its
  event handlers today; no durability for an event between persistence
  and dispatch; no independent scaling across modules.
- **Risks:** if `Notifications` gains a real external channel, its
  latency/failures become `Maintenance`'s problem too; a caller retrying
  after a handler-caused 500 (no idempotency key exists) risks creating
  a duplicate work order.
- **Reconsider when:** a module's load/failure profile diverges from the
  others, the subscription's environment quota changes, or event loss on
  a crash becomes unacceptable to the business.

## 5. Why Chosen

Current project size (one aggregate family, one integration client),
the Azure subscription's one-Container-Apps-environment ceiling, and Day
27's threat model + ZAP baseline (zero Low/Medium/High findings) already
validating this exact single-host topology all point the same way: the
alternatives' distributed-systems tax isn't earned yet, and the
`IDomainEventDispatcher` seam keeps the decision revisable without a
domain rewrite.

## 6. Top Critique

**No mentor or peer review artifact exists in this repository** —
verified by searching Capstone-project for review/feedback/mentor
language and by checking git history (two prior commits, neither a
review). This is therefore a **design review critique / proposed
critique** from self-review, not a real person's feedback. See
[`evidence/design-review.txt`](evidence/design-review.txt) for the full
inspection trail.

> The chosen dispatch mechanism — in-process, synchronous, inline-awaited
> — couples `Maintenance`'s write-path availability to `Assets`' and
> `Notifications`' handler reliability. Traced through the actual code:
> `WorkOrderService.DispatchAndClearAsync` awaits
> `InProcessDomainEventDispatcher.DispatchAsync` with no per-handler
> try/catch, so a handler exception propagates to `GlobalExceptionHandler`
> and returns a generic 500 — even for `CreateAsync`, where the
> `WorkOrder` was already persisted before dispatch ran. A caller
> retrying that 500 (no idempotency key exists on `POST /work-orders`)
> risks creating a duplicate work order for a write that had already
> succeeded.

## 7. How the Critique Changed the Design

The ADR now documents an explicit requirement that was not written down
before this review: `IDomainEventDispatcher` implementations must isolate
each handler (catch and log, don't propagate) so a handler failure never
fails the triggering request. This is recorded as **planned** work (Build
Day 11 below) — not yet implemented, per the Day 28 scope of
documentation over refactor. The ADR's "When this decision may need to
change" section and its Alternative 2 (broker/outbox) now explicitly name
this exact failure mode as the trigger for revisiting the decision,
which the original Day 22 write-up did not call out. The remaining
trade-off: even with per-handler isolation, events stay non-durable
(lost on a crash between persistence and dispatch) until Alternative 2 is
actually built.

## 8. Day-by-Day Build Plan

| Build Day | Goal | Work | Main Deliverable | Status | Validation |
|---|---|---|---|---|---|
| 1 | Modular monolith foundation | Scaffold `MaintainXpert.Api` host + `Maintenance`/`Assets`/`Notifications`/`SharedKernel` modules, one-directional project references | `MaintainXpert.slnx`, 5 projects | **COMPLETED** (Day 22) | `dotnet build` succeeded, 0 warnings/errors |
| 2 | Domain model | `WorkOrder` aggregate (lifecycle `Open→Assigned→InProgress→Completed`, invariants in aggregate methods), `Asset` aggregate | `WorkOrder.cs`, `Asset.cs` | **COMPLETED** (Day 22) | 7 unit tests passing, `WorkOrderTests.cs` |
| 3 | Cross-module domain events | `WorkOrderCreated`/`WorkOrderCompleted` event records, `SharedKernel` contracts, `InProcessDomainEventDispatcher`, `Assets`/`Notifications` handlers | `IDomainEvent`/`IDomainEventHandler`/`IDomainEventDispatcher`, 2 handlers | **COMPLETED** (Day 22) | Flow documented and traced in `docs/architecture.md` §8 |
| 4 | Persistence | `AppDbContext`, `SqlWorkOrderRepository`/`SqlAssetRepository`, Azure SQL with AAD-only auth, in-memory fallback for local/dev | `AppDbContext.cs`, 2 SQL repositories | **COMPLETED** (Day 27) | Full create→assign→start→complete lifecycle verified against real Azure SQL (`day-27/evidence/final-verification.txt`) |
| 5 | API surface | Minimal API endpoints for assets/work orders, URL-segment versioning | `AssetEndpoints.cs`, `WorkOrderEndpoints.cs` | **COMPLETED** (Day 27) | v1 → 200, v2 → 404 (`day-27/evidence/versioning.txt`) |
| 6 | AuthN/AuthZ | Client-credentials JWT issuance, `workorders.write` policy on write endpoints | `AuthEndpoints.cs`, `JwtAuthenticationExtensions.cs`, `TokenService.cs` | **COMPLETED** (Day 27) | 401 → 201 flow verified (`day-27/evidence/authentication.txt`) |
| 7 | Threat model & hardening | STRIDE-lite threat model, input limits, `GlobalExceptionHandler`, OpenAPI hardening | `stride-lite.md`, `ApiHardeningExtensions.cs` | **COMPLETED** (Day 27) | ZAP baseline: 0 Low/Medium/High findings |
| 8 | Network isolation | VNet + private endpoint + private DNS zone for Azure SQL, Bicep IaC | `day-27/infra/*.bicep` | **COMPLETED** (Day 27, residual: SQL public access stays enabled — documented subscription quota reason) | Private DNS + TCP reachability verified from inside the VNet |
| 9 | Design review | This ADR, critique, build plan | `docs/adr/ADR-001-*.md`, `day-28/*` | **COMPLETED** (Day 28, this document) | Self-review trail in `evidence/design-review.txt` |
| 10 | Close the testing gap | Unit tests for `Assets` (`WorkOrderCompletedHandler`, `Asset` invariants) and `Notifications` (`WorkOrderCreatedNotificationHandler`); a dispatch-flow integration test | New test projects/files | **PLANNED** | `dotnet test` green across all modules, not just `Maintenance` |
| 11 | Dispatch resilience (critique fix) | Per-handler exception isolation + logging in `InProcessDomainEventDispatcher`; idempotency key on `POST /work-orders` | Updated dispatcher, idempotency doc | **PLANNED** | Fault-injection test: a throwing handler no longer surfaces as a 500 to the caller |
| 12 | Persistence hardening | Replace `EnsureCreatedAsync` with EF Core migrations; narrow the managed identity's DB role from `db_ddladmin` to `db_datareader`/`db_datawriter` | `Migrations/` folder, updated Bicep role assignment | **PLANNED** | Migration applies cleanly to a fresh database; role change verified via `az sql` |
| 13 | Observability | Structured logging for the dispatch and repository layers (no App Insights wired yet) | Logging additions | **PLANNED** | Manual log inspection during a full lifecycle run |
| 14 | Contracts extraction | Move `WorkOrderCreated`/`WorkOrderCompleted` into a small `MaintainXpert.Contracts` project so `Assets`/`Notifications` stop referencing `Maintenance` directly | `MaintainXpert.Contracts` project | **PLANNED** | `dotnet list reference` shows `Assets`/`Notifications` depending only on `Contracts` + `SharedKernel` |

Build Days 1-9 are complete and already exist in this repository (Day 22
kickoff, Day 27 security pass, this Day 28 review). Build Days 10-14 are
a plan, not a claim of completed work.

## 9. What Was Learned

Writing the ADR surfaced that "modular monolith" (deployment topology)
and "in-process synchronous dispatch" (integration mechanism) were
bundled together as one decision since Day 22, when they are actually
separable — the single-host topology does not, by itself, require
un-isolated synchronous dispatch. Tracing the critique through the real
call stack (`WorkOrderService` → `InProcessDomainEventDispatcher` →
`GlobalExceptionHandler`) made the weakness concrete and specific to this
codebase, rather than a generic "synchronous coupling is risky"
observation.

## 10. What Could Break This Design

- `Notifications` gaining a real channel (email/SMS) with real latency
  and failure modes would make every work-order write pay that cost,
  because dispatch is awaited inline on the request path.
- A caller retrying a request after a handler-caused 500 has no
  idempotency key to avoid creating a duplicate work order — and no test
  coverage exists yet for the handlers involved.
- A process crash between an aggregate's persistence and its event
  dispatch silently drops that event; there is no outbox to recover it
  from.

## 11. Files Created/Modified

**Created**

- `Capstone-project/docs/adr/ADR-001-modular-monolith-in-process-domain-events.md`
- `Capstone-project/day-28/README.md`
- `Capstone-project/day-28/result.md`
- `Capstone-project/day-28/evidence/design-review.txt`

**Modified**

- None. This is a documentation-only addition; no existing MaintainXpert
  source, test, or infrastructure file was changed.
