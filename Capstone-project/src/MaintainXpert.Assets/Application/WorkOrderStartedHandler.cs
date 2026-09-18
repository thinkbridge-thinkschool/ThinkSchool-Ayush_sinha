using MaintainXpert.Maintenance.Domain.Events;
using MaintainXpert.SharedKernel;

namespace MaintainXpert.Assets.Application;

public sealed class WorkOrderStartedHandler : IDomainEventHandler<WorkOrderStarted>
{
    private readonly IAssetRepository _assetRepository;

    public WorkOrderStartedHandler(IAssetRepository assetRepository)
    {
        _assetRepository = assetRepository;
    }

    public async Task HandleAsync(WorkOrderStarted domainEvent, CancellationToken cancellationToken = default)
    {
        var asset = await _assetRepository.GetByIdAsync(domainEvent.AssetId, cancellationToken);

        if (asset is null)
        {
            return;
        }

        asset.BeginMaintenance();
        await _assetRepository.UpdateAsync(asset, cancellationToken);
    }
}
