using MassTransit;
using Microsoft.EntityFrameworkCore;
using Polly;
using ZTP_lab02.Contracts;

namespace ZTP_lab02.PushService;

public class PushConsumer : IConsumer<Notification>
{
    private readonly AppDbContext _db;
    private readonly ILogger<PushConsumer> _logger;

    public PushConsumer(AppDbContext db, ILogger<PushConsumer> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<Notification> context)
    {
        var incoming = context.Message;

        // Fetch current state from PostgreSQL to guarantee exactly-once delivery
        var notif = await _db.Notifications.FirstOrDefaultAsync(n => n.Id == incoming.Id);
        if (notif == null)
        {
            _logger.LogWarning("Push notification {Id} not found in database.", incoming.Id);
            return;
        }

        if (notif.Sent || notif.IsCancelled)
        {
            _logger.LogInformation(
                "Skipping Push notification {Id} (Sent={Sent}, IsCancelled={IsCancelled}).",
                notif.Id, notif.Sent, notif.IsCancelled);
            return;
        }

        // Retry up to 3 times with a 5-second delay on failure
        var retryPolicy = Policy
            .Handle<Exception>()
            .WaitAndRetryAsync(
                retryCount: 3,
                sleepDurationProvider: attempt => TimeSpan.FromSeconds(5),
                onRetry: (exception, delay, attempt, _) =>
                {
                    _logger.LogWarning(
                        exception,
                        "Push delivery attempt {Attempt}/3 failed for notification {Id}. Retrying in {Delay}s...",
                        attempt, notif.Id, delay.TotalSeconds);
                });

        await retryPolicy.ExecuteAsync(async () =>
        {
            await SendPushNotificationAsync(notif.Recipient, notif.Content, notif.Priority);
        });

        // Mark notification as sent to prevent duplicate deliveries
        notif.Sent = true;
        await _db.SaveChangesAsync();

        _logger.LogInformation(
            "Push notification {Id} successfully delivered to device {Recipient} (Priority: {Priority}).",
            notif.Id, notif.Recipient, notif.Priority);
    }

    private async Task SendPushNotificationAsync(string deviceToken, string content, NotificationPriority priority)
    {
        // Simulate failure for testing retry mechanism if content contains "[FAIL]"
        if (content.Contains("[FAIL]", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Simulated Push gateway error triggered by '[FAIL]' tag in content.");
        }

        // Simulate external Push notification gateway latency
        await Task.Delay(300);

        _logger.LogInformation(
            ">>> [PUSH SIMULATION] Device: '{Device}' | Priority: {Priority} | Payload: '{Content}' <<<",
            deviceToken, priority, content);
    }
}