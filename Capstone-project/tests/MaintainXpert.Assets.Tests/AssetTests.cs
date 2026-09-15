using FluentAssertions;
using MaintainXpert.Assets.Domain;

namespace MaintainXpert.Assets.Tests;

public class AssetTests
{
    [Fact]
    public void Register_creates_an_operational_asset_with_no_maintenance_history()
    {
        var asset = Asset.Register("HVAC Unit 4");

        asset.Name.Should().Be("HVAC Unit 4");
        asset.Status.Should().Be(AssetStatus.Operational);
        asset.LastMaintenanceCompletedAt.Should().BeNull();
    }

    [Fact]
    public void Register_rejects_a_blank_name()
    {
        var act = () => Asset.Register("   ");

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void RecordMaintenanceCompleted_sets_the_completion_timestamp()
    {
        var asset = Asset.Register("HVAC Unit 4");
        var completedAt = DateTimeOffset.UtcNow;

        asset.RecordMaintenanceCompleted(completedAt);

        asset.LastMaintenanceCompletedAt.Should().Be(completedAt);
        asset.Status.Should().Be(AssetStatus.Operational);
    }
}
