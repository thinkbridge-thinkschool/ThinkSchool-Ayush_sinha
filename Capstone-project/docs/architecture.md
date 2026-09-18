# MaintainXpert — Architecture

## 1. Product slice

Maintenance Work Orders: register assets, raise a work order against an
asset, assign a technician, move the work order through its lifecycle, and
publish events other parts of the system can react to asynchronously. This
is the smallest end-to-end slice of MaintainXpert that already exercises
every bounded context.

## 2. Why MaintainXpert

MaintainXpert is a maintenance and asset management platform: businesses
register machines/assets, raise maintenance requests, assign technicians,
and track work to completion. Work orders are the natural core of the
domain — everything else (assets, technicians, notifications) exists to
support the work-order lifecycle.

## 3. Bounded contexts

- **Maintenance** — owns maintenance requests, work orders, and the
  work-order lifecycle. This is the core context for the initial slice.
- **AssetManagement** — owns assets/machines and their maintenance-related
  state. Other contexts reference an asset only by its `AssetId`; they
  never reach into `Asset` directly. `Maintenance` needs to know whether a
  referenced asset exists and is decommissioned before raising a work
  order against it; it gets that through `Maintenance.Application.IAssetLookup`
  — a port `Maintenance` owns and declares, per the dependency-inversion
  shape already used for `IWorkOrderRepository`/`IAssetRepository`. Only
  the API host (composition root) implements it (`AssetLookupAdapter`),
  keeping the one-directional dependency rule intact: `Maintenance`'s own
  project never references `Assets`.
- **Notifications** — reacts to maintenance events asynchronously. Kept
  deliberately thin at this stage: no email/SMS integration, just a
  console sink standing in for a future channel.
- **SharedKernel** — the only code shared across contexts: the
  `IDomainEvent`/`IDomainEventHandler`/`IDomainEventDispatcher` contracts,
  and the `AssetId` identity type that lets Maintenance reference an asset
  without depending on the AssetManagement module.

## 4. Core aggregate

`WorkOrder` (in `MaintainXpert.Maintenance.Domain`):

- `WorkOrderId`, `AssetId`, `Priority`, `Status`, `Description`,
  `AssignedTechnicianId`, `CreatedAt`.
- Lifecycle: `Open → Assigned → InProgress → Completed`.
- All state changes go through the aggregate's own methods
  (`AssignTechnician`, `Start`, `Complete`) — nothing external sets
  `Status` directly.

## 5. Aggregate invariants

- A work order must reference an asset (`Create` rejects a default
  `AssetId`).
- A work order cannot be completed without an assigned technician.
- A completed work order cannot be reassigned.
- Lifecycle transitions are one-directional and validated: you cannot
  `Start` before `Assigned`, or `Complete` before `InProgress`.
- Beyond the aggregate itself, `WorkOrderService.CreateAsync` rejects a
  work order raised against an asset that does not exist, or one that has
  been decommissioned (`AssetNotFoundException` / `AssetDecommissionedException`,
  mapped to 404/409). This is an application-level rule, not an aggregate
  invariant, because `WorkOrder.Create` has no way to ask another context
  whether its `AssetId` is valid — that is exactly what `IAssetLookup` is
  for.

Covered by `tests/MaintainXpert.Maintenance.Tests/WorkOrderTests.cs` and
`WorkOrderServiceTests.cs`.

`Asset` (in `MaintainXpert.Assets.Domain`) has its own lifecycle:
`Operational` (default) ⇄ `UnderMaintenance` (while a work order raised
against it is in progress) → `Decommissioned` (terminal, via an explicit
`POST /assets/{id}/decommission`; decommissioning an already-decommissioned
asset is rejected). Covered by `tests/MaintainXpert.Assets.Tests/AssetTests.cs`.

## 6. Module boundaries

One deployable app (`MaintainXpert.Api`), four internal modules
(`MaintainXpert.Maintenance`, `MaintainXpert.Assets`,
`MaintainXpert.Notifications`, `MaintainXpert.SharedKernel`), each with its
own `Domain` / `Application` / `Infrastructure` folders. Only the API host
references every module; modules reference each other only through public
contracts (event records, repository interfaces), never through
implementation types. `Assets` and `Notifications` depend on
`Maintenance`'s published event contracts (`WorkOrderCreated`,
`WorkOrderCompleted`) the same way an external subscriber would — that is
the one intentional context-to-context dependency in this scaffold, and
it is one-directional (consumers depend on the producer's contracts, never
the reverse).

## 7. Synchronous flow

`POST /work-orders` → `WorkOrderService.CreateAsync` → `WorkOrder.Create`
(validates invariants) → `IWorkOrderRepository.AddAsync` → response
returned to the caller. Assignment, start, and completion follow the same
shape: HTTP endpoint → `WorkOrderService` → aggregate method → repository.

## 8. Asynchronous flows

**Flow 1 — Create Work Order**
`WorkOrderService.CreateAsync` → `WorkOrder.Create` raises `WorkOrderCreated`
→ `WorkOrderService` dispatches it through `IDomainEventDispatcher` →
`Notifications.WorkOrderCreatedNotificationHandler` sends a notification
(console sink today, a real channel later).

**Flow 2 — Start Work Order**
`WorkOrderService.StartAsync` → `WorkOrder.Start` raises `WorkOrderStarted`
→ dispatched the same way → `Assets.WorkOrderStartedHandler` looks up the
`Asset` and marks it `UnderMaintenance`.

**Flow 3 — Complete Work Order**
`WorkOrderService.CompleteAsync` → `WorkOrder.Complete` raises
`WorkOrderCompleted` → dispatched the same way →
`Assets.WorkOrderCompletedHandler` looks up the `Asset`, records
`LastMaintenanceCompletedAt`, and returns it to `Operational`.

All three events are dispatched in-process by
`MaintainXpert.Api.Infrastructure.InProcessDomainEventDispatcher`, which
resolves `IDomainEventHandler<T>` from DI and invokes them after the
aggregate is persisted. It plays the role a message broker would play
later; swapping it for a real outbox/broker is an infrastructure change,
not a domain or module-boundary change.

## 9. Why modular monolith instead of microservices for this stage

One asset (or a handful) — the maintenance slice — does not yet justify
independent deployability, network boundaries, or distributed transactions.
A modular monolith keeps the bounded contexts honest through project/
namespace boundaries and explicit contracts (events, repository
interfaces) while staying a single deployable app: one build, one process,
one set of integration tests, no distributed-systems tax. If a module
later needs to scale or deploy independently, the seams are already
there — `Assets` and `Notifications` never touch `Maintenance`'s internals,
only its published events.

## 10. Scaffolded solution layout

```
Capstone-project/
├── MaintainXpert.slnx
├── src/
│   ├── MaintainXpert.Api/               # host: DI wiring, minimal API endpoints, event dispatcher
│   ├── MaintainXpert.Maintenance/       # Domain / Application / Infrastructure
│   ├── MaintainXpert.Assets/            # Domain / Application / Infrastructure
│   ├── MaintainXpert.Notifications/     # Domain / Application / Infrastructure
│   └── MaintainXpert.SharedKernel/      # IDomainEvent contracts, AssetId
├── tests/
│   └── MaintainXpert.Maintenance.Tests/ # WorkOrder aggregate invariants
├── docs/
│   └── architecture.md
├── README.md
└── result.md
```
