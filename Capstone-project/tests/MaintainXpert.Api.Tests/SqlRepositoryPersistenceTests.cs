using FluentAssertions;
using MaintainXpert.Api.Infrastructure;
using MaintainXpert.Assets.Domain;
using MaintainXpert.Maintenance.Domain;
using MaintainXpert.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace MaintainXpert.Api.Tests;

// Each step opens its own AppDbContext, mirroring separate HTTP requests, so these tests only pass
// if the repository actually calls SaveChanges - a shared context would mask a missing save.
public class SqlRepositoryPersistenceTests
{
    private static AppDbContext CreateContext(string databaseName)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName)
            .Options;

        return new AppDbContext(options);
    }

    [Fact]
    public async Task WorkOrder_transitions_persist_across_separate_requests()
    {
        var databaseName = Guid.NewGuid().ToString();
        var assetId = AssetId.New();
        WorkOrderId workOrderId;

        await using (var db = CreateContext(databaseName))
        {
            var repository = new SqlWorkOrderRepository(db);
            var workOrder = WorkOrder.Create(assetId, "Replace worn belt", WorkOrderPriority.Medium, DateTimeOffset.UtcNow);
            workOrderId = workOrder.Id;
            await repository.AddAsync(workOrder);
        }

        var technicianId = TechnicianId.New();

        await using (var db = CreateContext(databaseName))
        {
            var repository = new SqlWorkOrderRepository(db);
            var workOrder = await repository.GetByIdAsync(workOrderId);
            workOrder!.AssignTechnician(technicianId);
            await repository.UpdateAsync(workOrder);
        }

        await using (var db = CreateContext(databaseName))
        {
            var repository = new SqlWorkOrderRepository(db);
            var workOrder = await repository.GetByIdAsync(workOrderId);
            workOrder!.Status.Should().Be(WorkOrderStatus.Assigned);
            workOrder.AssignedTechnicianId.Should().Be(technicianId);
        }
    }

    [Fact]
    public async Task Asset_mutations_persist_across_separate_requests()
    {
        var databaseName = Guid.NewGuid().ToString();
        AssetId assetId;

        await using (var db = CreateContext(databaseName))
        {
            var repository = new SqlAssetRepository(db);
            var asset = Asset.Register("HVAC Unit 4");
            assetId = asset.Id;
            await repository.AddAsync(asset);
        }

        var completedAt = DateTimeOffset.UtcNow;

        await using (var db = CreateContext(databaseName))
        {
            var repository = new SqlAssetRepository(db);
            var asset = await repository.GetByIdAsync(assetId);
            asset!.RecordMaintenanceCompleted(completedAt);
            await repository.UpdateAsync(asset);
        }

        await using (var db = CreateContext(databaseName))
        {
            var repository = new SqlAssetRepository(db);
            var asset = await repository.GetByIdAsync(assetId);
            asset!.LastMaintenanceCompletedAt.Should().Be(completedAt);
            asset.Status.Should().Be(AssetStatus.Operational);
        }
    }
}
