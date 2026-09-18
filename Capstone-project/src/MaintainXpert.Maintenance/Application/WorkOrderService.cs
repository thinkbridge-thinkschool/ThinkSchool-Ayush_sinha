using MaintainXpert.Maintenance.Domain;
using MaintainXpert.SharedKernel;

namespace MaintainXpert.Maintenance.Application;

public sealed class WorkOrderService
{
    private readonly IWorkOrderRepository _repository;
    private readonly IAssetLookup _assetLookup;
    private readonly IDomainEventDispatcher _dispatcher;
    private readonly TimeProvider _timeProvider;

    public WorkOrderService(
        IWorkOrderRepository repository,
        IAssetLookup assetLookup,
        IDomainEventDispatcher dispatcher,
        TimeProvider timeProvider)
    {
        _repository = repository;
        _assetLookup = assetLookup;
        _dispatcher = dispatcher;
        _timeProvider = timeProvider;
    }

    public async Task<WorkOrder> CreateAsync(
        AssetId assetId,
        string description,
        WorkOrderPriority priority,
        CancellationToken cancellationToken = default)
    {
        var asset = await _assetLookup.FindAsync(assetId, cancellationToken) ?? throw new AssetNotFoundException(assetId);

        if (asset.IsDecommissioned)
        {
            throw new AssetDecommissionedException(assetId);
        }

        var workOrder = WorkOrder.Create(assetId, description, priority, _timeProvider.GetUtcNow());
        await _repository.AddAsync(workOrder, cancellationToken);
        await DispatchAndClearAsync(workOrder, cancellationToken);
        return workOrder;
    }

    public async Task<WorkOrder> AssignTechnicianAsync(
        WorkOrderId id,
        TechnicianId technicianId,
        CancellationToken cancellationToken = default)
    {
        var workOrder = await GetOrThrowAsync(id, cancellationToken);
        workOrder.AssignTechnician(technicianId);
        await _repository.UpdateAsync(workOrder, cancellationToken);
        await DispatchAndClearAsync(workOrder, cancellationToken);
        return workOrder;
    }

    public async Task<WorkOrder> StartAsync(WorkOrderId id, CancellationToken cancellationToken = default)
    {
        var workOrder = await GetOrThrowAsync(id, cancellationToken);
        workOrder.Start(_timeProvider.GetUtcNow());
        await _repository.UpdateAsync(workOrder, cancellationToken);
        await DispatchAndClearAsync(workOrder, cancellationToken);
        return workOrder;
    }

    public async Task<WorkOrder> CompleteAsync(WorkOrderId id, CancellationToken cancellationToken = default)
    {
        var workOrder = await GetOrThrowAsync(id, cancellationToken);
        workOrder.Complete(_timeProvider.GetUtcNow());
        await _repository.UpdateAsync(workOrder, cancellationToken);
        await DispatchAndClearAsync(workOrder, cancellationToken);
        return workOrder;
    }

    private async Task<WorkOrder> GetOrThrowAsync(WorkOrderId id, CancellationToken cancellationToken)
    {
        return await _repository.GetByIdAsync(id, cancellationToken) ?? throw new WorkOrderNotFoundException(id);
    }

    private async Task DispatchAndClearAsync(WorkOrder workOrder, CancellationToken cancellationToken)
    {
        await _dispatcher.DispatchAsync(workOrder.DomainEvents, cancellationToken);
        workOrder.ClearDomainEvents();
    }
}
