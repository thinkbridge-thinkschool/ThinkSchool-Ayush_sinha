using FluentAssertions;
using MaintainXpert.Maintenance.Application;
using MaintainXpert.Maintenance.Domain;
using MaintainXpert.Maintenance.Infrastructure;
using MaintainXpert.SharedKernel;

namespace MaintainXpert.Maintenance.Tests;

public class WorkOrderServiceTests
{
    private sealed class FakeAssetLookup : IAssetLookup
    {
        private readonly AssetLookupResult? _result;

        public FakeAssetLookup(AssetLookupResult? result)
        {
            _result = result;
        }

        public Task<AssetLookupResult?> FindAsync(AssetId assetId, CancellationToken cancellationToken = default)
            => Task.FromResult(_result);
    }

    private sealed class NoOpDomainEventDispatcher : IDomainEventDispatcher
    {
        public Task DispatchAsync(IEnumerable<IDomainEvent> domainEvents, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private static WorkOrderService CreateService(IAssetLookup assetLookup)
        => new(new InMemoryWorkOrderRepository(), assetLookup, new NoOpDomainEventDispatcher(), TimeProvider.System);

    [Fact]
    public async Task CreateAsync_rejects_a_work_order_for_an_asset_that_does_not_exist()
    {
        var service = CreateService(new FakeAssetLookup(result: null));

        var act = async () => await service.CreateAsync(AssetId.New(), "Replace worn belt", WorkOrderPriority.Medium);

        await act.Should().ThrowAsync<AssetNotFoundException>();
    }

    [Fact]
    public async Task CreateAsync_rejects_a_work_order_for_a_decommissioned_asset()
    {
        var service = CreateService(new FakeAssetLookup(new AssetLookupResult(IsDecommissioned: true)));

        var act = async () => await service.CreateAsync(AssetId.New(), "Replace worn belt", WorkOrderPriority.Medium);

        await act.Should().ThrowAsync<AssetDecommissionedException>();
    }

    [Fact]
    public async Task CreateAsync_succeeds_for_an_existing_operational_asset()
    {
        var service = CreateService(new FakeAssetLookup(new AssetLookupResult(IsDecommissioned: false)));

        var workOrder = await service.CreateAsync(AssetId.New(), "Replace worn belt", WorkOrderPriority.Medium);

        workOrder.Status.Should().Be(WorkOrderStatus.Open);
    }
}
