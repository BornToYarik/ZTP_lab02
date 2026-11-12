using MailKit.Net.Smtp;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using MimeKit;
using Polly;
using ZTP_lab02.Contracts;

namespace ZTP_lab02.EmailService;

public class EmailConsumer : IConsumer<Notification>
{
    private readonly AppDbContext _db;
    private readonly IConfiguration _config;
    private readonly ILogger<EmailConsumer> _logger;

    public EmailConsumer(
        AppDbContext db,
        IConfiguration config,
        ILogger<EmailConsumer> logger)
    {
        _db = db;
        _config = config;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<Notification> context)
    {
        var incoming = context.Message;

        // Fetch current state from PostgreSQL to guarantee exactly-once delivery
        var notif = await _db.Notifications.FirstOrDefaultAsync(n => n.Id == incoming.Id);
        if (notif == null)
        {
            _logger.LogWarning("Email notification {Id} not found in database.", incoming.Id);
            return;
        }

        if (notif.Sent || notif.IsCancelled)
        {
            _logger.LogInformation(
                "Skipping Email notification {Id} (Sent={Sent}, IsCancelled={IsCancelled}).",
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
                        "Email delivery attempt {Attempt}/3 failed for notification {Id}. Retrying in {Delay}s...",
                        attempt, notif.Id, delay.TotalSeconds);
                });

        await retryPolicy.ExecuteAsync(async () =>
        {
            await SendEmailViaSmtpAsync(notif.Recipient, notif.Content, notif.Priority);
        });

        // Mark notification as sent to prevent duplicate deliveries
        notif.Sent = true;
        await _db.SaveChangesAsync();

        _logger.LogInformation(
            "Email notification {Id} successfully delivered to {Recipient} (Priority: {Priority}).",
            notif.Id, notif.Recipient, notif.Priority);
    }

    private async Task SendEmailViaSmtpAsync(string recipient, string content, NotificationPriority priority)
    {
        // Simulate failure for testing retry mechanism if content contains "[FAIL]"
        if (content.Contains("[FAIL]", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Simulated SMTP failure triggered by '[FAIL]' tag in content.");
        }

        var smtpHost = _config["Smtp:Host"] ?? "localhost";
        var smtpPort = int.Parse(_config["Smtp:Port"] ?? "1025");
        var fromAddress = _config["Smtp:FromAddress"] ?? "noreply@ztp-lab02.local";
        var fromName = _config["Smtp:FromName"] ?? "ZTP Notification System";

        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(fromName, fromAddress));
        message.To.Add(MailboxAddress.Parse(recipient));
        message.Subject = $"[{priority}] Notification from ZTP Lab02";
        message.Body = new TextPart("plain")
        {
            Text = content
        };

        using var client = new SmtpClient();
        await client.ConnectAsync(smtpHost, smtpPort, MailKit.Security.SecureSocketOptions.None);
        await client.SendAsync(message);
        await client.DisconnectAsync(true);
    }
}