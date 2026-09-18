using MaintainXpert.Assets.Domain;
using MaintainXpert.SharedKernel;

namespace MaintainXpert.Assets.Application;

public interface IAssetRepository
{
    Task AddAsync(Asset asset, CancellationToken cancellationToken = default);

    Task<Asset?> GetByIdAsync(AssetId id, CancellationToken cancellationToken = default);

    Task UpdateAsync(Asset asset, CancellationToken cancellationToken = default);
}
