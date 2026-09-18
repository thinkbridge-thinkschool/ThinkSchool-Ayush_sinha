using FluentAssertions;
using MaintainXpert.Maintenance.Domain;
using MaintainXpert.Maintenance.Domain.Events;
using MaintainXpert.SharedKernel;

namespace MaintainXpert.Maintenance.Tests;

public class WorkOrderTests
{
    private static readonly AssetId AnAssetId = AssetId.New();
    private static readonly TechnicianId ATechnicianId = TechnicianId.New();
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    [Fact]
    public void Create_raises_WorkOrderCreated_event()
    {
        var workOrder = WorkOrder.Create(AnAssetId, "Replace worn belt", WorkOrderPriority.Medium, Now);

        workOrder.Status.Should().Be(WorkOrderStatus.Open);
        workOrder.DomainEvents.Should().ContainSingle(e => e is WorkOrderCreated);
    }

    [Fact]
    public void Create_without_an_asset_is_rejected()
    {
        var act = () => WorkOrder.Create(default, "Replace worn belt", WorkOrderPriority.Medium, Now);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Valid_lifecycle_transition_succeeds()
    {
        var workOrder = WorkOrder.Create(AnAssetId, "Replace worn belt", WorkOrderPriority.Medium, Now);

        workOrder.AssignTechnician(ATechnicianId);
        workOrder.Start(Now.AddHours(1));
        workOrder.Complete(Now.AddHours(2));

        workOrder.Status.Should().Be(WorkOrderStatus.Completed);
        workOrder.AssignedTechnicianId.Should().Be(ATechnicianId);
        workOrder.DomainEvents.Should().Contain(e => e is WorkOrderCompleted);
    }

    [Fact]
    public void Start_raises_WorkOrderStarted_event()
    {
        var workOrder = WorkOrder.Create(AnAssetId, "Replace worn belt", WorkOrderPriority.Medium, Now);
        workOrder.AssignTechnician(ATechnicianId);

        var startedAt = Now.AddHours(1);
        workOrder.Start(startedAt);

        workOrder.Status.Should().Be(WorkOrderStatus.InProgress);
        workOrder.DomainEvents.Should().ContainSingle(e => e is WorkOrderStarted);
        var started = workOrder.DomainEvents.OfType<WorkOrderStarted>().Single();
        started.WorkOrderId.Should().Be(workOrder.Id);
        started.AssetId.Should().Be(AnAssetId);
        started.OccurredAtUtc.Should().Be(startedAt);
    }

    [Fact]
    public void Completing_without_a_technician_fails()
    {
        var workOrder = WorkOrder.Create(AnAssetId, "Replace worn belt", WorkOrderPriority.Medium, Now);

        var act = () => workOrder.Complete(Now.AddHours(1));

        act.Should().Throw<InvalidWorkOrderTransitionException>()
            .WithMessage("*without a technician*");
    }

    [Fact]
    public void Completed_work_order_cannot_be_reassigned()
    {
        var workOrder = WorkOrder.Create(AnAssetId, "Replace worn belt", WorkOrderPriority.Medium, Now);
        workOrder.AssignTechnician(ATechnicianId);
        workOrder.Start(Now.AddHours(1));
        workOrder.Complete(Now.AddHours(2));

        var act = () => workOrder.AssignTechnician(TechnicianId.New());

        act.Should().Throw<InvalidWorkOrderTransitionException>()
            .WithMessage("*cannot be reassigned*");
    }

    [Fact]
    public void Starting_before_assignment_is_rejected()
    {
        var workOrder = WorkOrder.Create(AnAssetId, "Replace worn belt", WorkOrderPriority.Medium, Now);

        var act = () => workOrder.Start(Now);

        act.Should().Throw<InvalidWorkOrderTransitionException>();
    }

    [Fact]
    public void Completing_before_starting_is_rejected()
    {
        var workOrder = WorkOrder.Create(AnAssetId, "Replace worn belt", WorkOrderPriority.Medium, Now);
        workOrder.AssignTechnician(ATechnicianId);

        var act = () => workOrder.Complete(Now.AddHours(1));

        act.Should().Throw<InvalidWorkOrderTransitionException>();
    }
}
