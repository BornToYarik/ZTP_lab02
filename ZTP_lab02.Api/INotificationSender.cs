using MassTransit;
using Microsoft.EntityFrameworkCore;
using ZTP_lab02.Contracts;

namespace ZTP_lab02.Api;

public interface INotificationSender
{
    Task SendAsync(int notificationId);
}

public class NotificationSender : INotificationSender
{
    private readonly AppDbContext _db;
    private readonly ISendEndpointProvider _sendEndpointProvider;
    private readonly ILogger<NotificationSender> _logger;

    public NotificationSender(
        AppDbContext db,
        ISendEndpointProvider sendEndpointProvider,
        ILogger<NotificationSender> logger)
    {
        _db = db;
        _sendEndpointProvider = sendEndpointProvider;
        _logger = logger;
    }

    public async Task SendAsync(int notificationId)
    {
        var notification = await _db.Notifications
            .FirstOrDefaultAsync(n => n.Id == notificationId);

        if (notification == null)
        {
            _logger.LogWarning("Notification {Id} has not found.", notificationId);
            return;
        }

        // cansel and sending verification
        if (notification.Sent || notification.IsCancelled)
        {
            _logger.LogInformation(
                "Notification {Id} missed (Sent={Sent}, IsCancelled={IsCancelled}).",
                notificationId, notification.Sent, notification.IsCancelled);
            return;
        }

        // choosing queue ("EMAIL" or "PUSH")
        var queueName = notification.Channel.ToUpperInvariant() switch
        {
            "EMAIL" => "email-queue",
            "PUSH" => "push-queue",
            _ => throw new InvalidOperationException($"unknown channel: {notification.Channel}")
        };

        var endpoint = await _sendEndpointProvider.GetSendEndpoint(new Uri($"queue:{queueName}"));

        // Sending to RabbitMQ and choosing priority (Low = 1, High = 10)
        await endpoint.Send(notification, context =>
        {
            context.SetPriority((byte)notification.Priority);
        });

        _logger.LogInformation(
            "Notification {Id} sent in queue {Queue} with priority {Priority}.",
            notification.Id, queueName, notification.Priority);
    }
}