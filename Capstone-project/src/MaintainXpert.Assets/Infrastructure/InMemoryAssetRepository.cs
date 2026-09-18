using System.Collections.Concurrent;
using MaintainXpert.Assets.Application;
using MaintainXpert.Assets.Domain;
using MaintainXpert.SharedKernel;

namespace MaintainXpert.Assets.Infrastructure;

public sealed class InMemoryAssetRepository : IAssetRepository
{
    private readonly ConcurrentDictionary<AssetId, Asset> _assets = new();

    public Task AddAsync(Asset asset, CancellationToken cancellationToken = default)
    {
        _assets[asset.Id] = asset;
        return Task.CompletedTask;
    }

    public Task<Asset?> GetByIdAsync(AssetId id, CancellationToken cancellationToken = default)
    {
        _assets.TryGetValue(id, out var asset);
        return Task.FromResult(asset);
    }

    public Task UpdateAsync(Asset asset, CancellationToken cancellationToken = default)
    {
        _assets[asset.Id] = asset;
        return Task.CompletedTask;
    }
}
