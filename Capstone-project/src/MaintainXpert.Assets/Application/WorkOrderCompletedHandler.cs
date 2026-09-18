using MaintainXpert.Maintenance.Domain.Events;
using MaintainXpert.SharedKernel;

namespace MaintainXpert.Assets.Application;

public sealed class WorkOrderCompletedHandler : IDomainEventHandler<WorkOrderCompleted>
{
    private readonly IAssetRepository _assetRepository;

    public WorkOrderCompletedHandler(IAssetRepository assetRepository)
    {
        _assetRepository = assetRepository;
    }

    public async Task HandleAsync(WorkOrderCompleted domainEvent, CancellationToken cancellationToken = default)
    {
        var asset = await _assetRepository.GetByIdAsync(domainEvent.AssetId, cancellationToken);

        if (asset is null)
        {
            return;
        }

        asset.RecordMaintenanceCompleted(domainEvent.OccurredAtUtc);
        await _assetRepository.UpdateAsync(asset, cancellationToken);
    }
}
