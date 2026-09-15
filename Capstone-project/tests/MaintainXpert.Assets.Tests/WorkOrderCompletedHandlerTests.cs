using FluentAssertions;
using MaintainXpert.Assets.Application;
using MaintainXpert.Assets.Domain;
using MaintainXpert.Assets.Infrastructure;
using MaintainXpert.Maintenance.Domain;
using MaintainXpert.Maintenance.Domain.Events;
using MaintainXpert.SharedKernel;

namespace MaintainXpert.Assets.Tests;

public class WorkOrderCompletedHandlerTests
{
    [Fact]
    public async Task HandleAsync_records_maintenance_completed_on_the_referenced_asset()
    {
        var repository = new InMemoryAssetRepository();
        var asset = Asset.Register("HVAC Unit 4");
        await repository.AddAsync(asset);

        var handler = new WorkOrderCompletedHandler(repository);
        var completedAt = DateTimeOffset.UtcNow;
        var domainEvent = new WorkOrderCompleted(WorkOrderId.New(), asset.Id, TechnicianId.New(), completedAt);

        await handler.HandleAsync(domainEvent);

        var updated = await repository.GetByIdAsync(asset.Id);
        updated!.LastMaintenanceCompletedAt.Should().Be(completedAt);
    }

    [Fact]
    public async Task HandleAsync_does_not_throw_when_the_referenced_asset_does_not_exist()
    {
        var repository = new InMemoryAssetRepository();
        var handler = new WorkOrderCompletedHandler(repository);
        var domainEvent = new WorkOrderCompleted(WorkOrderId.New(), AssetId.New(), TechnicianId.New(), DateTimeOffset.UtcNow);

        var act = async () => await handler.HandleAsync(domainEvent);

        await act.Should().NotThrowAsync();
    }
}
