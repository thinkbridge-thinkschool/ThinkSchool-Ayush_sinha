using MaintainXpert.SharedKernel;

namespace MaintainXpert.Maintenance.Domain.Events;

public sealed record WorkOrderStarted(
    WorkOrderId WorkOrderId,
    AssetId AssetId,
    DateTimeOffset OccurredAtUtc) : IDomainEvent;
