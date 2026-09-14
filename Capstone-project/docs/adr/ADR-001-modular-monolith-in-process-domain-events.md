# ADR-001 — Modular Monolith with In-Process Domain Events

## Status

Accepted

## Context

MaintainXpert is one deployable ASP.NET Core host, `MaintainXpert.Api`,
composed from four internal modules: `MaintainXpert.Maintenance` (the
`WorkOrder` aggregate and its lifecycle), `MaintainXpert.Assets` (the
`Asset` aggregate), `MaintainXpert.Notifications` (reacts to maintenance
events, currently a console sink), and `MaintainXpert.SharedKernel` (the
`IDomainEvent`/`IDomainEventHandler`/`IDomainEventDispatcher` contracts and
the `AssetId` identity type). This split was chosen at the Day 22 kickoff
(`docs/architecture.md`) and has carried through the Day 27 security pass
unchanged.

The problem this ADR addresses: **how do `Maintenance`, `Assets`, and
`Notifications` stay decoupled bounded contexts without paying for
distributed-systems infrastructure the project does not yet need?**
Concretely:

- Creating a work order (`WorkOrderService.CreateAsync`) must eventually
  notify someone (`Notifications`), and completing one
  (`WorkOrderService.CompleteAsync`) must eventually update the
  referenced asset's maintenance state (`Assets`) — but `Maintenance`
  must not reference `Assets` or `Notifications` internals to do it.
- The project is a solo capstone slice with one asset type, one
  work-order lifecycle, and one integration client (`day-27/threat-model/stride-lite.md`
  models exactly one client-credentials caller). There is no evidence of
  divergent load or failure profiles between modules yet.
- The Azure subscription used for deployment (`day-27/evidence/architecture.txt`)
  has a **verified, hard, subscription-wide limit of one Container Apps
  environment** (`MaxNumberOfGlobalEnvironmentsInSubExceeded`, returned
  when a second, VNet-integrated environment was attempted). Any design
  that assumes several independently deployed services is not currently
  provisionable on this subscription without a quota change.
- What could become a problem later: if `Notifications` grows a real
  channel (email/SMS) with its own latency and failure modes, or if
  `Assets` needs to scale independently of `Maintenance`, today's
  in-process, synchronous event dispatch would tie `Maintenance`'s write
  path directly to those modules' reliability (see the Design Review
  Critique below, and `docs/architecture.md` §8-9, which already
  flagged the dispatcher as "the seam where a real broker/outbox would
  be substituted later").

## Decision

MaintainXpert stays **one deployable modular monolith**: a single
`MaintainXpert.Api` host referencing `Maintenance`, `Assets`,
`Notifications`, and `SharedKernel` as project references
(`MaintainXpert.slnx`), with module boundaries enforced by project
structure and explicit contracts, not network boundaries. Cross-module
reaction to a change (`WorkOrderCreated`, `WorkOrderCompleted`, both
defined in `MaintainXpert.Maintenance.Domain.Events` and dispatched
through the `SharedKernel` `IDomainEvent`/`IDomainEventHandler` contracts)
is dispatched **in-process and synchronously** by
`MaintainXpert.Api.Infrastructure.InProcessDomainEventDispatcher`, which
resolves `IDomainEventHandler<T>` instances from DI and awaits each one
inline, right after the triggering aggregate is persisted
(`WorkOrderService.DispatchAndClearAsync`). `Assets` and `Notifications`
depend on `Maintenance`'s published event *contracts*, never its
implementation types, and the dependency is one-directional (verified via
`dotnet list reference`, recorded in `day-22`'s result).

## Alternatives Considered

### Alternative 1 — Microservices (Maintenance / Assets / Notifications as independently deployable services)

- **Advantages:** independent scaling and deployment per module; real
  failure isolation (a `Notifications` outage cannot fail a work-order
  write); each service could evolve its own technology/release cadence.
- **Disadvantages:** `Maintenance` → `Assets`/`Notifications`
  communication needs a network call or a message broker instead of a
  plain in-process interface call; eventual consistency and partial
  failure have to be handled explicitly at every integration point, not
  just at one seam.
- **Operational complexity:** three (or more) independently deployed
  Container Apps, service discovery, per-service CI/CD, more Bicep
  modules — and this subscription is hard-capped at **one** Container
  Apps environment (verified, `day-27/evidence/architecture.txt`), so a
  genuine microservice split is not provisionable today without a quota
  increase.
- **Development impact:** cross-service contracts need versioning
  discipline from day one; local development needs three running (or
  mocked) services instead of one `dotnet run`.
- **Scalability impact:** only pays off if a module's load or failure
  profile genuinely diverges from the others — not the case yet; all
  three modules react to the same work-order lifecycle at the same
  volume.
- **Testing impact:** integration tests need real network calls or
  consumer-driven contract tooling instead of one in-process test host.
- **Security impact:** each service needs its own authentication/
  authorization boundary and adds its own attack surface, multiplying
  what Day 27's STRIDE-lite model would need to cover.

### Alternative 2 — Modular monolith, but with a real async broker/outbox now (e.g. Azure Service Bus or Storage Queue + transactional outbox)

- **Advantages:** keeps one deployable, while decoupling handler
  failure/latency from the triggering write immediately; durable,
  at-least-once delivery from day one; closer to how `Notifications`
  should behave once it has a real external channel.
- **Disadvantages:** needs an outbox table plus a background
  relay/dispatcher, and handlers must become idempotent (a message can
  be delivered more than once) — meaningfully more moving parts for
  handlers that today only look up one asset or print a console line.
- **Operational complexity:** one more Azure resource to provision,
  monitor, and pay for; an outbox table adds a schema migration and a
  hosted background service.
- **Development impact:** both `WorkOrderService` and the
  `IDomainEventDispatcher` implementation change; every handler must be
  rewritten to tolerate redelivery.
- **Scalability impact:** a real benefit once `Notifications` gains a
  genuine external channel with its own latency/failure profile — not
  yet the case.
- **Testing impact:** needs tests for the outbox relay and
  redelivery/idempotency, on top of the aggregate tests that already
  exist — more surface for the same three flows this slice has today.

### Chosen — Modular monolith with in-process synchronous domain events

- **Advantages:** one deployable Container App (matches the
  subscription's actual one-environment ceiling), one build, one
  `dotnet test` run, no broker to provision or pay for; module
  boundaries are still enforced by project references and event
  contracts, not by network boundaries, so `Assets`/`Notifications`
  still cannot reach into `Maintenance`'s internals; `IDomainEventDispatcher`
  is an explicit interface seam in `SharedKernel`, so swapping in
  Alternative 2 later changes only its implementation and the DI
  registration in `Program.cs`, not the aggregate or the handlers.
- **Disadvantages:** no failure isolation between the aggregate write
  and its handlers today (see the critique below); no durability — an
  event is lost if the process crashes between persistence and
  dispatch; no independent scaling of `Maintenance` vs. `Assets` vs.
  `Notifications`.
- **Operational complexity:** lowest of the three — matches what Day 27
  already deployed onto the shared, quota-constrained `thinkschool-env`.
- **Development impact:** lowest — one host (`src/MaintainXpert.Api`),
  one DI composition root, one test project to extend.
- **Scalability impact:** acceptable for the current single-tenant,
  low-volume work-order slice; would need revisiting once a module's
  load or failure profile diverges from the others.
- **Testing impact:** `WorkOrderTests.cs` exercises the aggregate
  directly and confirms `WorkOrderCreated` is raised on creation; the
  dispatcher and handlers are simple enough to unit test in-process
  without network mocking, though they are not covered by tests yet
  (see the build plan, Build Day 10).
- **Security impact:** one trust boundary, already threat-modeled
  end-to-end in `day-27/threat-model/stride-lite.md`; splitting later
  would require re-modeling per-service trust boundaries.

## Trade-offs

### Benefits

- Single build, single deployable, single test run — matches both the
  project's current size (one core aggregate, one consuming module per
  side) and the subscription's hard one-Container-Apps-environment
  limit.
- Module boundaries are real today, not aspirational: `Assets` and
  `Notifications` depend only on `Maintenance`'s published event records
  and `SharedKernel`, never on `Maintenance`'s internal types — verified
  by the project reference graph.
- The dispatch mechanism is isolated behind one interface
  (`IDomainEventDispatcher`), so the decision is revisable at the
  infrastructure layer without touching the `WorkOrder`/`Asset`
  aggregates or the existing handlers.

### Costs

- Handler execution is not isolated from the triggering write's HTTP
  response: an exception thrown inside `WorkOrderCreatedNotificationHandler`
  or `WorkOrderCompletedHandler` propagates through
  `InProcessDomainEventDispatcher.DispatchAsync` →
  `WorkOrderService.DispatchAndClearAsync` → the minimal API endpoint →
  `GlobalExceptionHandler`, which returns a generic 500 to the caller
  even when the triggering aggregate write already succeeded.
- No durability: an event raised on the aggregate exists only in memory
  between persistence and dispatch; a process crash in that window loses
  it silently, with no retry.
- No independent scaling: `Maintenance`, `Assets`, and `Notifications`
  all run in the same process and the same Container App replica set.

### Risks

- If `Notifications` gains a real external channel (email/SMS) with
  meaningfully higher latency or a higher failure rate than an in-memory
  lookup, that latency/failure becomes `Maintenance`'s problem too,
  because dispatch is awaited inline.
- Without idempotency on the write endpoints, a caller that sees a 500
  caused by a handler failure (not an aggregate failure) may retry an
  operation that already succeeded, risking duplicate work orders.

### When this decision may need to change

- If any one module's request volume, latency, or failure profile
  starts to diverge materially from the others (e.g., `Notifications`
  adds a slow or unreliable external provider).
- If the Azure subscription's one-Container-Apps-environment limit is
  lifted or a different hosting model is adopted, removing the
  operational blocker to independent deployability.
- If event loss on a process crash becomes unacceptable for the
  business (e.g., a missed completion notification has real
  consequences) — at that point, Alternative 2 (outbox/broker) becomes
  the right next step, using the existing `IDomainEventDispatcher` seam.

## Why This Decision

- **Current project size:** one aggregate family (`WorkOrder`, `Asset`),
  one integration client, one console-sink notification channel — not
  enough surface to justify per-module deployability.
- **Team/development complexity:** a solo capstone build benefits from
  one host to run and debug (`dotnet run` in `src/MaintainXpert.Api`)
  rather than coordinating several services locally.
- **Deployment complexity:** the deployment target itself (the shared
  `thinkschool-env` Container Apps environment) cannot host more than
  one environment on this subscription, which rules out a genuine
  microservice topology today regardless of code-level readiness.
- **Reliability and security:** Day 27 already threat-modeled and
  ZAP-scanned this exact topology (one host, one trust boundary) with a
  clean result (zero Low/Medium/High findings); splitting the topology
  would invalidate that model and require re-verification per service.
- **Operational cost:** no broker, no outbox, no extra Azure resource to
  provision or pay for on a cost-conscious training subscription.

## Consequences

**Positive consequences**

- The system ships as one artifact that Day 27's Bicep IaC already
  builds and deploys successfully.
- Module boundaries are enforced by the compiler (project references)
  today, not left to documentation discipline alone.
- The path to Alternative 2 is already seamed off behind
  `IDomainEventDispatcher`, so it is an infrastructure change, not a
  domain rewrite, when it becomes necessary.

**Negative consequences**

- A failing or slow event handler in `Assets` or `Notifications` can
  make a `Maintenance` write endpoint report failure even though the
  aggregate write itself succeeded (see Design Review Critique below).
- There is no durable record of a domain event once it has been
  dispatched and cleared from the aggregate (`ClearDomainEvents`); a
  crash between persistence and dispatch loses it.

**Future consequences**

- If `Notifications` or `Assets` grow real external dependencies,
  today's synchronous coupling will need to be revisited (Alternative 2
  or a scoped microservice extraction for that one module only).
- `Assets` and `Notifications` currently reference `Maintenance`
  directly for its event contracts (`WorkOrderCreated`,
  `WorkOrderCompleted`); as already noted in the Day 22 result, a larger
  system would likely extract those contracts into a separate
  `MaintainXpert.Contracts` project so consumers stop pulling in the
  producer's whole implementation graph (see the build plan, Build Day
  14).

## Rejected Alternatives

- **Microservices** — rejected for now: the current slice's load and
  team size don't justify the distributed-systems tax, and the
  deployment target cannot even provision more than one Container Apps
  environment on this subscription.
- **Modular monolith + real broker/outbox today** — rejected for now:
  the actual handlers in this slice (one asset lookup, one console
  write) don't yet have the latency or reliability profile that would
  justify an outbox table, a background relay, and idempotent handlers;
  the interface seam to add it later already exists.

## Validation

- **Tests:** `tests/MaintainXpert.Maintenance.Tests/WorkOrderTests.cs`
  (7 tests) verifies the `WorkOrder` aggregate's invariants and that
  `WorkOrderCreated` is raised on creation — the domain side of this
  decision's contract. The dispatcher and the `Assets`/`Notifications`
  handlers are not yet covered by tests; closing that gap is Build Day
  10 in the plan below.
- **Architecture boundaries:** the project reference graph
  (`MaintainXpert.slnx`, verified with `dotnet list reference` at Day
  22) shows `Assets` and `Notifications` depending on `Maintenance` and
  `SharedKernel` only — never the reverse — which is the boundary this
  ADR relies on.
- **Deployment behavior:** Day 27 deployed this exact one-host topology
  to Azure Container Apps and verified it healthy at
  `RunningAtMaxScale` (`day-27/evidence/final-verification.txt`),
  confirming the single-deployable assumption holds in practice, not
  just on paper.
- **Security checks:** `day-27/threat-model/stride-lite.md` models the
  in-process dispatcher explicitly as a component, and the ZAP baseline
  scan against the deployed single host returned zero Low/Medium/High
  findings (`day-27/evidence/zap-fixed-findings.txt`).
- **Observability:** not yet instrumented — MaintainXpert has no
  Application Insights or structured telemetry today (verified: no such
  package reference in `src/MaintainXpert.Api/MaintainXpert.Api.csproj`).
  This is a real gap, not validated, and is called out as planned work
  in the build plan rather than claimed as done.
