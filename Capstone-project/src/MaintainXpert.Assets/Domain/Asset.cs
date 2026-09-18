using MaintainXpert.SharedKernel;

namespace MaintainXpert.Assets.Domain;

public sealed class Asset
{
    public AssetId Id { get; }
    public string Name { get; }
    public AssetStatus Status { get; private set; }
    public DateTimeOffset? LastMaintenanceCompletedAt { get; private set; }

    private Asset(AssetId id, string name)
    {
        Id = id;
        Name = name;
        Status = AssetStatus.Operational;
    }

    public static Asset Register(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("An asset must have a name.", nameof(name));
        }

        return new Asset(AssetId.New(), name);
    }

    public void RecordMaintenanceCompleted(DateTimeOffset completedAt)
    {
        LastMaintenanceCompletedAt = completedAt;
        Status = AssetStatus.Operational;
    }

    public void BeginMaintenance()
    {
        Status = AssetStatus.UnderMaintenance;
    }

    public void Decommission()
    {
        if (Status == AssetStatus.Decommissioned)
        {
            throw new InvalidAssetOperationException("Asset is already decommissioned.");
        }

        Status = AssetStatus.Decommissioned;
    }
}
