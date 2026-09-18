using MaintainXpert.SharedKernel;

namespace MaintainXpert.Maintenance.Application;

public sealed class AssetDecommissionedException : Exception
{
    public AssetDecommissionedException(AssetId id)
        : base($"Asset '{id}' is decommissioned and cannot accept new work orders.")
    {
    }
}
