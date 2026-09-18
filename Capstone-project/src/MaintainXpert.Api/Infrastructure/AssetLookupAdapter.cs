using MaintainXpert.Assets.Application;
using MaintainXpert.Assets.Domain;
using MaintainXpert.Maintenance.Application;
using MaintainXpert.SharedKernel;

namespace MaintainXpert.Api.Infrastructure;

public sealed class AssetLookupAdapter : IAssetLookup
{
    private readonly IAssetRepository _assetRepository;

    public AssetLookupAdapter(IAssetRepository assetRepository)
    {
        _assetRepository = assetRepository;
    }

    public async Task<AssetLookupResult?> FindAsync(AssetId assetId, CancellationToken cancellationToken = default)
    {
        var asset = await _assetRepository.GetByIdAsync(assetId, cancellationToken);

        return asset is null ? null : new AssetLookupResult(asset.Status == AssetStatus.Decommissioned);
    }
}
