using FluentAssertions;
using MaintainXpert.Maintenance.Domain;
using MaintainXpert.Maintenance.Domain.Events;
using MaintainXpert.Notifications.Application;
using MaintainXpert.Notifications.Domain;
using MaintainXpert.SharedKernel;

namespace MaintainXpert.Notifications.Tests;

public class WorkOrderCreatedNotificationHandlerTests
{
    private sealed class RecordingNotificationSink : INotificationSink
    {
        public NotificationMessage? SentMessage { get; private set; }

        public Task SendAsync(NotificationMessage message, CancellationToken cancellationToken = default)
        {
            SentMessage = message;
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task HandleAsync_sends_a_notification_naming_the_work_order_and_asset()
    {
        var sink = new RecordingNotificationSink();
        var handler = new WorkOrderCreatedNotificationHandler(sink);
        var occurredAt = DateTimeOffset.UtcNow;
        var domainEvent = new WorkOrderCreated(WorkOrderId.New(), AssetId.New(), occurredAt);

        await handler.HandleAsync(domainEvent);

        sink.SentMessage.Should().NotBeNull();
        sink.SentMessage!.Subject.Should().Be("New maintenance work order");
        sink.SentMessage.Body.Should().Contain(domainEvent.WorkOrderId.ToString());
        sink.SentMessage.Body.Should().Contain(domainEvent.AssetId.ToString());
        sink.SentMessage.CreatedAt.Should().Be(occurredAt);
    }
}
