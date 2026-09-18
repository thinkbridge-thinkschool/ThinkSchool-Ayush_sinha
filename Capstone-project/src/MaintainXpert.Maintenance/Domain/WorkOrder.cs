using MaintainXpert.Maintenance.Domain.Events;
using MaintainXpert.SharedKernel;

namespace MaintainXpert.Maintenance.Domain;

public sealed class WorkOrder
{
    private readonly List<IDomainEvent> _domainEvents = new();

    public WorkOrderId Id { get; }
    public AssetId AssetId { get; }
    public string Description { get; }
    public WorkOrderPriority Priority { get; }
    public WorkOrderStatus Status { get; private set; }
    public TechnicianId? AssignedTechnicianId { get; private set; }
    public DateTimeOffset CreatedAt { get; }

    public IReadOnlyCollection<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    private WorkOrder(
        WorkOrderId id,
        AssetId assetId,
        string description,
        WorkOrderPriority priority,
        DateTimeOffset createdAt)
    {
        Id = id;
        AssetId = assetId;
        Description = description;
        Priority = priority;
        Status = WorkOrderStatus.Open;
        CreatedAt = createdAt;
    }

    public static WorkOrder Create(
        AssetId assetId,
        string description,
        WorkOrderPriority priority,
        DateTimeOffset createdAt)
    {
        if (assetId == default)
        {
            throw new ArgumentException("A work order must reference an asset.", nameof(assetId));
        }

        if (string.IsNullOrWhiteSpace(description))
        {
            throw new ArgumentException("A work order must have a description.", nameof(description));
        }

        var workOrder = new WorkOrder(WorkOrderId.New(), assetId, description, priority, createdAt);
        workOrder._domainEvents.Add(new WorkOrderCreated(workOrder.Id, workOrder.AssetId, createdAt));
        return workOrder;
    }

    public void AssignTechnician(TechnicianId technicianId)
    {
        if (Status == WorkOrderStatus.Completed)
        {
            throw new InvalidWorkOrderTransitionException("A completed work order cannot be reassigned.");
        }

        AssignedTechnicianId = technicianId;
        Status = WorkOrderStatus.Assigned;
    }

    public void Start(DateTimeOffset startedAt)
    {
        if (Status != WorkOrderStatus.Assigned)
        {
            throw new InvalidWorkOrderTransitionException(
                $"Cannot start a work order from status '{Status}'. A technician must be assigned first.");
        }

        Status = WorkOrderStatus.InProgress;
        _domainEvents.Add(new WorkOrderStarted(Id, AssetId, startedAt));
    }

    public void Complete(DateTimeOffset completedAt)
    {
        if (AssignedTechnicianId is null)
        {
            throw new InvalidWorkOrderTransitionException("A work order cannot be completed without a technician.");
        }

        if (Status != WorkOrderStatus.InProgress)
        {
            throw new InvalidWorkOrderTransitionException(
                $"Cannot complete a work order from status '{Status}'.");
        }

        Status = WorkOrderStatus.Completed;
        _domainEvents.Add(new WorkOrderCompleted(Id, AssetId, AssignedTechnicianId.Value, completedAt));
    }

    public void ClearDomainEvents() => _domainEvents.Clear();
}
