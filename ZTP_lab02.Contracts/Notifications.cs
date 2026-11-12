namespace ZTP_lab02.Contracts;

public enum NotificationPriority
{
    Low = 1,
    High = 10
}

public class Notification
{
    public int Id { get; set; }
    
    // Email address or id of device for push 
    public string Recipient { get; set; } = string.Empty;
    
    // "EMAIL" or "PUSH"
    public string Channel { get; set; } = string.Empty;
    
    // content of notification
    public string Content { get; set; } = string.Empty;
    
    // planned time of sending (UTC)
    public DateTimeOffset ScheduledTime { get; set; }
    
    // users time zone (f.e.: "Europe/Warsaw" or "America/New_York")
    public string TimeZoneId { get; set; } = "Europe/Warsaw";
    
    // priory: Low (1) or High (10)
    public NotificationPriority Priority { get; set; } = NotificationPriority.Low;
    
    // duplicate protection (1 sent only) 
    public bool Sent { get; set; } = false;
    
    // cansel flag
    public bool IsCancelled { get; set; } = false;
    
    // task ID on Hangfire (needed for cancellation or immediate sending)
    public string? HangfireJobId { get; set; }
}