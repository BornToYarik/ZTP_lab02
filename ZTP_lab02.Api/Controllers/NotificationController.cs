using Hangfire;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ZTP_lab02.Contracts;

namespace ZTP_lab02.Api.Controllers;

public record NotificationDto(
    string Recipient,
    string Channel, // "EMAIL" or "PUSH"
    string Content,
    DateTimeOffset ScheduledTime,
    string TimeZoneId = "Europe/Warsaw",
    NotificationPriority Priority = NotificationPriority.Low
);

[ApiController]
[Route("api/notifications")]
public class NotificationController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IBackgroundJobClient _jobs;

    public NotificationController(AppDbContext db, IBackgroundJobClient jobs)
    {
        _db = db;
        _jobs = jobs;
    }

    // 1. creation and planing notification
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] NotificationDto dto)
    {
        var channel = dto.Channel.ToUpperInvariant();
        if (channel != "EMAIL" && channel != "PUSH")
        {
            return BadRequest("Channel must be 'EMAIL' or 'PUSH'.");
        }

        TimeZoneInfo userTimeZone;
        try
        {
            userTimeZone = TimeZoneInfo.FindSystemTimeZoneById(dto.TimeZoneId);
        }
        catch (TimeZoneNotFoundException)
        {
            return BadRequest($"Unknown timezone: {dto.TimeZoneId}. Example: 'Europe/Warsaw', 'America/New_York'.");
        }

        // cheking if its night hours (22:00 - 08:00)
        var adjustedScheduledTime = AdjustForNightHours(dto.ScheduledTime, userTimeZone);

        var n = new Notification
        {
            Recipient = dto.Recipient,
            Channel = channel,
            Content = dto.Content,
            ScheduledTime = adjustedScheduledTime.ToUniversalTime(),
            TimeZoneId = dto.TimeZoneId,
            Priority = dto.Priority,
            Sent = false,
            IsCancelled = false
        };

        _db.Notifications.Add(n);
        await _db.SaveChangesAsync();

        // Task planning in Hangfire on right time
        var jobId = _jobs.Schedule<INotificationSender>(
            s => s.SendAsync(n.Id),
            n.ScheduledTime
        );

        n.HangfireJobId = jobId;
        await _db.SaveChangesAsync();

        return Ok(new
        {
            Message = "Notification is planned",
            Notification = n,
            WasShiftedFromNightHours = adjustedScheduledTime != dto.ScheduledTime
        });
    }

    // 2. immediate sent of planned notification
    [HttpPost("{id:int}/send-now")]
    public async Task<IActionResult> ForceSendNow(int id)
    {
        var n = await _db.Notifications.FirstOrDefaultAsync(x => x.Id == id);
        if (n == null) return NotFound("Notification not found.");
        if (n.Sent) return BadRequest("Notification is already sent.");
        if (n.IsCancelled) return BadRequest("Notification was cancelled.");

        // delete old delayed notification Hangfire
        if (!string.IsNullOrEmpty(n.HangfireJobId))
        {
            _jobs.Delete(n.HangfireJobId);
        }

        // immediate task
        n.ScheduledTime = DateTimeOffset.UtcNow;
        n.HangfireJobId = _jobs.Enqueue<INotificationSender>(s => s.SendAsync(n.Id));
        await _db.SaveChangesAsync();

        return Ok(new { Message = $"Notification {id} is in immediate queue." });
    }

    // 3. Cancellation planned task
    [HttpPost("{id:int}/cancel")]
    public async Task<IActionResult> Cancel(int id)
    {
        var n = await _db.Notifications.FirstOrDefaultAsync(x => x.Id == id);
        if (n == null) return NotFound("Notification not found.");
        if (n.Sent) return BadRequest("Notification is already sent.");
        if (n.IsCancelled) return BadRequest("Notification was cancelled already.");

        n.IsCancelled = true;

        if (!string.IsNullOrEmpty(n.HangfireJobId))
        {
            _jobs.Delete(n.HangfireJobId);
        }

        await _db.SaveChangesAsync();

        return Ok(new { Message = $"Notification {id} was cancelled." });
    }

    // 4. list of every notification and their statuses
    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var list = await _db.Notifications
            .OrderByDescending(n => n.Id)
            .ToListAsync();
        return Ok(list);
    }

    // business logic of reschedule (22:00 - 08:00) on 08:00 AM by users timezone
    private static DateTimeOffset AdjustForNightHours(DateTimeOffset scheduledTime, TimeZoneInfo userTimeZone)
    {
        var localTime = TimeZoneInfo.ConvertTime(scheduledTime, userTimeZone);

        if (localTime.Hour >= 22)
        {
            // if time >= 22:00, reschedule on next day 08:00 AM
            var nextDayMorning = localTime.Date.AddDays(1).AddHours(8);
            return new DateTimeOffset(nextDayMorning, userTimeZone.GetUtcOffset(nextDayMorning));
        }
        
        if (localTime.Hour < 8)
        {
            // If time is from 00:00 to 07:59, reschedule on 08:00 AM
            var sameDayMorning = localTime.Date.AddHours(8);
            return new DateTimeOffset(sameDayMorning, userTimeZone.GetUtcOffset(sameDayMorning));
        }

        return scheduledTime;
    }
}