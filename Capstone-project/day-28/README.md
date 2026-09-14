# Day 28 — Design Review + ADR

## Exercise

> Paste the ADR + your day-by-day build plan + the top critique you got and how it changed the design.

## Project

**MaintainXpert** — `Capstone-project`, the modular-monolith maintenance
and asset management platform built across the earlier capstone days
(Day 22 kickoff, Day 27 security pass).

## ADR

Full write-up: [`docs/adr/ADR-001-modular-monolith-in-process-domain-events.md`](../docs/adr/ADR-001-modular-monolith-in-process-domain-events.md).

- **Decision:** MaintainXpert stays one deployable modular monolith
  (`MaintainXpert.Api` host + `Maintenance`/`Assets`/`Notifications`/
  `SharedKernel` modules), with cross-module reaction to domain events
  dispatched in-process and synchronously by
  `InProcessDomainEventDispatcher`, rather than split into microservices
  or backed by a real message broker/outbox.
- **Context:** `Maintenance` must notify `Notifications` and update
  `Assets` when a work order is created/completed, without depending on
  their internals. The project is a solo, single-client capstone slice,
  and the Azure subscription used for deployment has a verified,
  hard, subscription-wide limit of **one** Container Apps environment
  (`day-27/evidence/architecture.txt`) — a real operational constraint
  against splitting into independently deployed services today.
- **Alternatives considered:** (1) microservices, with `Maintenance`/
  `Assets`/`Notifications` as independently deployable services talking
  over a network/broker; (2) staying a modular monolith but adding a
  real async broker/outbox (e.g. Azure Service Bus) for domain events
  right now, instead of in-process dispatch.
- **Trade-offs:** the chosen design keeps one build/deploy/test cycle
  and no extra broker infrastructure, but it means a failing or slow
  event handler in `Assets`/`Notifications` can make a `Maintenance`
  write endpoint report failure even after the aggregate write already
  succeeded, and an event is not durable if the process crashes between
  persistence and dispatch.
- **Why chosen:** current project size (one aggregate family, one
  integration client), the subscription's one-environment deployment
  ceiling, and Day 27's threat model/ZAP baseline already validating
  this exact one-host topology all point the same way — the
  distributed-systems tax of the alternatives isn't earned yet, and the
  dispatcher is already seamed off (`IDomainEventDispatcher`) so the
  decision is revisable later without a domain rewrite.

## Design Review Critique

No mentor or peer review artifact exists anywhere in this repository —
checked directly (see
[`evidence/design-review.txt`](evidence/design-review.txt) for what was
searched). Git history for `Capstone-project` has exactly two prior
commits ("Kick off MaintainXpert capstone", "Security pass"), neither of
which is feedback from another person. So the critique below is labeled
honestly as a **design review critique / proposed critique** from
self-review, not attributed to a real mentor or peer.

**Critique:** the ADR's chosen mechanism — in-process, synchronous,
inline-awaited domain event dispatch — couples `Maintenance`'s write-path
availability to `Assets`' and `Notifications`' handler reliability. Traced
through the actual code
(`WorkOrderService.DispatchAndClearAsync` →
`InProcessDomainEventDispatcher.DispatchAsync`, no per-handler try/catch):
a handler exception propagates all the way to `GlobalExceptionHandler`,
which returns a generic 500 — even for `CreateAsync`, where the
`WorkOrder` was already persisted via `IWorkOrderRepository.AddAsync`
*before* dispatch runs. A caller that retries after that 500 (there is no
idempotency key on `POST /work-orders`) risks creating a duplicate work
order for an operation that had already succeeded.

**Design change:** the ADR now documents an explicit contract requirement
for `IDomainEventDispatcher` that did not exist before this review —
handler execution must be isolated per handler (caught and logged) so a
handler failure does not fail the triggering HTTP request — and records
this as planned work (Build Day 11 below), not yet implemented. The ADR's
"When this decision may need to change" section and Alternative 2
(broker/outbox) now explicitly name this failure mode as the trigger
condition for revisiting the decision, which the original Day 22
architecture write-up did not call out.

**Resulting trade-off:** even after that isolation fix is built, the
decision still leaves events non-durable (an event raised between
persistence and a process crash is lost, with no outbox to replay it
from) and still ties `Assets`/`Notifications` to `Maintenance`'s process
lifetime. Closing that residual gap is exactly Alternative 2, deliberately
deferred rather than built now (see the ADR's Rejected Alternatives).

## Day-by-Day Build Plan

See [`result.md`](result.md) for the full table. Summary: Build Days 1-8
are already complete (modular monolith scaffold, `WorkOrder`/`Asset`
domain model, in-process event dispatch, EF Core + Azure SQL persistence,
API endpoints, JWT auth, threat modeling/hardening, private networking —
Day 22 and Day 27 work already in this repository). Build Day 9 is this
design review. Build Days 10-14 are planned, not yet built: test coverage
for `Assets`/`Notifications`, per-handler dispatch isolation (the critique
fix above), EF Core migrations to replace `EnsureCreatedAsync`,
observability, and extracting event contracts into a dedicated
`MaintainXpert.Contracts` project.

## What I Learned

The most useful outcome of writing this ADR was noticing that "modular
monolith" and "in-process synchronous dispatch" are actually two
separable decisions that this project had bundled together since Day 22 —
the deployment topology (one host) doesn't strictly require the dispatch
mechanism to be synchronous and un-isolated; that was a second, smaller
decision worth its own scrutiny. Tracing the critique through the actual
call stack (`WorkOrderService` → `InProcessDomainEventDispatcher` →
`GlobalExceptionHandler`) was more convincing than reasoning about it
abstractly — the failure mode is real and specific to this codebase, not
a generic "synchronous coupling is bad" observation.

## What Could Break This Design?

- **Notifications gaining a real channel.** The moment
  `ConsoleNotificationSink` is replaced by a real email/SMS provider with
  network latency and its own failure modes, every work-order write pays
  that latency and inherits that failure mode, because dispatch is
  awaited inline on the request path.
- **A retried duplicate write.** A caller that sees a 500 caused by a
  handler failure (not an aggregate failure) has no way to tell the
  difference and no idempotency key to safely retry — a realistic
  failure mode given there is no test coverage today for
  `WorkOrderCompletedHandler` or `WorkOrderCreatedNotificationHandler`
  (only the `Maintenance` aggregate is tested).
- **A process crash between persistence and dispatch.** Because domain
  events live only in memory on the aggregate until dispatched and
  cleared, a crash in that narrow window silently drops the event, with
  no outbox to recover it from.

## Evidence

- ADR: [`docs/adr/ADR-001-modular-monolith-in-process-domain-events.md`](../docs/adr/ADR-001-modular-monolith-in-process-domain-events.md)
- Self-review inspection notes: [`evidence/design-review.txt`](evidence/design-review.txt)
- Submission-ready summary: [`result.md`](result.md)
