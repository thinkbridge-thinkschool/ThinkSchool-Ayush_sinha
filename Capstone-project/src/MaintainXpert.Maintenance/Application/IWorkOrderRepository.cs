using MaintainXpert.Maintenance.Domain;

namespace MaintainXpert.Maintenance.Application;

public interface IWorkOrderRepository
{
    Task AddAsync(WorkOrder workOrder, CancellationToken cancellationToken = default);

    Task<WorkOrder?> GetByIdAsync(WorkOrderId id, CancellationToken cancellationToken = default);

    Task UpdateAsync(WorkOrder workOrder, CancellationToken cancellationToken = default);
}
