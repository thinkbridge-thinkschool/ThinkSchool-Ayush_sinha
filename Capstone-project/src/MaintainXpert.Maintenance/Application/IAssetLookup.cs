using MaintainXpert.SharedKernel;

namespace MaintainXpert.Maintenance.Application;

public interface IAssetLookup
{
    Task<AssetLookupResult?> FindAsync(AssetId assetId, CancellationToken cancellationToken = default);
}

public sealed record AssetLookupResult(bool IsDecommissioned);
