using System.Collections.Concurrent;
using MaintainXpert.Maintenance.Application;
using MaintainXpert.Maintenance.Domain;

namespace MaintainXpert.Maintenance.Infrastructure;

public sealed class InMemoryWorkOrderRepository : IWorkOrderRepository
{
    private readonly ConcurrentDictionary<WorkOrderId, WorkOrder> _workOrders = new();

    public Task AddAsync(WorkOrder workOrder, CancellationToken cancellationToken = default)
    {
        _workOrders[workOrder.Id] = workOrder;
        return Task.CompletedTask;
    }

    public Task<WorkOrder?> GetByIdAsync(WorkOrderId id, CancellationToken cancellationToken = default)
    {
        _workOrders.TryGetValue(id, out var workOrder);
        return Task.FromResult(workOrder);
    }

    public Task UpdateAsync(WorkOrder workOrder, CancellationToken cancellationToken = default)
    {
        _workOrders[workOrder.Id] = workOrder;
        return Task.CompletedTask;
    }
}
