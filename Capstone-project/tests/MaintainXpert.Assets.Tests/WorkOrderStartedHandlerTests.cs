using FluentAssertions;
using MaintainXpert.Assets.Application;
using MaintainXpert.Assets.Domain;
using MaintainXpert.Assets.Infrastructure;
using MaintainXpert.Maintenance.Domain;
using MaintainXpert.Maintenance.Domain.Events;
using MaintainXpert.SharedKernel;

namespace MaintainXpert.Assets.Tests;

public class WorkOrderStartedHandlerTests
{
    [Fact]
    public async Task HandleAsync_marks_the_referenced_asset_under_maintenance()
    {
        var repository = new InMemoryAssetRepository();
        var asset = Asset.Register("HVAC Unit 4");
        await repository.AddAsync(asset);

        var handler = new WorkOrderStartedHandler(repository);
        var domainEvent = new WorkOrderStarted(WorkOrderId.New(), asset.Id, DateTimeOffset.UtcNow);

        await handler.HandleAsync(domainEvent);

        var updated = await repository.GetByIdAsync(asset.Id);
        updated!.Status.Should().Be(AssetStatus.UnderMaintenance);
    }

    [Fact]
    public async Task HandleAsync_does_not_throw_when_the_referenced_asset_does_not_exist()
    {
        var repository = new InMemoryAssetRepository();
        var handler = new WorkOrderStartedHandler(repository);
        var domainEvent = new WorkOrderStarted(WorkOrderId.New(), AssetId.New(), DateTimeOffset.UtcNow);

        var act = async () => await handler.HandleAsync(domainEvent);

        await act.Should().NotThrowAsync();
    }
}
