using MaintainXpert.Assets.Application;
using MaintainXpert.Assets.Domain;
using MaintainXpert.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace MaintainXpert.Api.Infrastructure;

public sealed class SqlAssetRepository : IAssetRepository
{
    private readonly AppDbContext _db;

    public SqlAssetRepository(AppDbContext db)
    {
        _db = db;
    }

    public async Task AddAsync(Asset asset, CancellationToken cancellationToken = default)
    {
        _db.Assets.Add(asset);
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task<Asset?> GetByIdAsync(AssetId id, CancellationToken cancellationToken = default)
    {
        var asset = await _db.Assets
            .FirstOrDefaultAsync(a => a.Id == id, cancellationToken);

        return asset;
    }

    public async Task UpdateAsync(Asset asset, CancellationToken cancellationToken = default)
    {
        await _db.SaveChangesAsync(cancellationToken);
    }
}
