using MaintainXpert.SharedKernel;

namespace MaintainXpert.Maintenance.Application;

public sealed class AssetNotFoundException : Exception
{
    public AssetNotFoundException(AssetId id) : base($"Asset '{id}' was not found.")
    {
    }
}
