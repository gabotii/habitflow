using HabitFlow.Domain.Notifications;
using HabitFlow.Domain.Reminders;
using HabitFlow.Infrastructure.Email;
using HabitFlow.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HabitFlow.Infrastructure.Reminders;

public sealed class ReminderOptions
{
    public const string SectionName = "Reminders";

    /// <summary>Runs the background worker. Tests disable it and call the dispatcher directly.</summary>
    public bool Enabled { get; set; } = true;

    public int PollSeconds { get; set; } = 30;

    /// <summary>Occurrences older than this (e.g. after downtime) are skipped instead of firing late.</summary>
    public int MaxDelayMinutes { get; set; } = 120;
}

/// <summary>
/// Turns due reminders into persisted notifications (docs/ARCHITECTURE.md §3.8). Safe to run on several instances:
/// due rows are claimed with <c>FOR UPDATE SKIP LOCKED</c>, and the unique (ReminderId, OccurrenceAt) index makes a
/// retried occurrence a no-op. Runs without a user context, so queries opt out of the ownership filters explicitly.
/// </summary>
public sealed class ReminderDispatcher(
    AppDbContext db,
    TimeProvider timeProvider,
    IEmailSender emailSender,
    IOptions<ReminderOptions> options,
    ILogger<ReminderDispatcher> logger)
{
    private const int BatchSize = 100;

    public async Task<int> DispatchDueAsync(CancellationToken ct)
    {
        var now = timeProvider.GetUtcNow();
        var maxDelay = TimeSpan.FromMinutes(options.Value.MaxDelayMinutes);

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var due = await db.Reminders
            .FromSqlInterpolated($"""
                SELECT * FROM "Reminders"
                WHERE "IsActive" AND "NextFireAt" <= {now}
                ORDER BY "NextFireAt"
                LIMIT {BatchSize}
                FOR UPDATE SKIP LOCKED
                """)
            .IgnoreQueryFilters()
            .ToListAsync(ct);
        if (due.Count == 0)
        {
            return 0;
        }

        var userIds = due.Select(r => r.UserId).Distinct().ToList();
        var consent = await db.Users.AsNoTracking()
            .Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.EmailRemindersEnabled, ct);
        var habitIds = due.Where(r => r.HabitId != null).Select(r => r.HabitId!.Value).ToList();
        var habitNames = await db.Habits.IgnoreQueryFilters().AsNoTracking()
            .Where(h => habitIds.Contains(h.Id))
            .ToDictionaryAsync(h => h.Id, h => h.Name, ct);

        var created = 0;
        foreach (var reminder in due)
        {
            var occurrence = reminder.NextFireAt!.Value;
            if (now - occurrence <= maxDelay &&
                !await db.Notifications.IgnoreQueryFilters()
                    .AnyAsync(n => n.ReminderId == reminder.Id && n.OccurrenceAt == occurrence, ct))
            {
                db.Notifications.Add(Notification.Create(
                    reminder.UserId,
                    NotificationKind.Reminder,
                    reminder.Title,
                    reminder.Description ?? Body(reminder, habitNames),
                    reminder.HabitId is { } habitId ? $"/habitos/{habitId}"
                        : reminder.GoalId is { } goalId ? $"/objetivos/{goalId}"
                        : "/recordatorios",
                    sendEmail: reminder.Channel == ReminderChannel.InAppAndEmail &&
                               consent.GetValueOrDefault(reminder.UserId) && emailSender.IsEnabled,
                    reminder.Id,
                    occurrence));
                created++;
            }
            else
            {
                logger.LogInformation("Skipped stale occurrence of reminder {ReminderId}", reminder.Id);
            }

            reminder.MarkFired(occurrence, now);
        }

        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return created;
    }

    /// <summary>Delivers pending reminder e-mails (up to 3 attempts each). Never logs addresses or contents.</summary>
    public async Task<int> SendPendingEmailsAsync(CancellationToken ct)
    {
        if (!emailSender.IsEnabled)
        {
            return 0;
        }

        var pending = await db.Notifications.IgnoreQueryFilters()
            .Where(n => n.EmailStatus == EmailDeliveryStatus.Pending)
            .OrderBy(n => n.CreatedAt)
            .Take(20)
            .ToListAsync(ct);
        var userIds = pending.Select(n => n.UserId).Distinct().ToList();
        var emails = await db.Users.AsNoTracking()
            .Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.Email, ct);

        var sent = 0;
        foreach (var notification in pending)
        {
            var success = false;
            if (emails.GetValueOrDefault(notification.UserId) is { } address)
            {
                try
                {
                    await emailSender.SendAsync(address, $"Recordatorio: {notification.Title}",
                        $"{notification.Body}\n\nAbre HabitFlow para registrar tu progreso.", ct);
                    success = true;
                    sent++;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogWarning(ex, "Reminder e-mail for notification {NotificationId} failed", notification.Id);
                }
            }

            notification.RecordEmailAttempt(success);
        }

        await db.SaveChangesAsync(ct);
        return sent;
    }

    private static string Body(Reminder reminder, IReadOnlyDictionary<Guid, string> habitNames) =>
        reminder.HabitId is { } id && habitNames.TryGetValue(id, out var name)
            ? $"Es momento de: {name}."
            : $"Recordatorio de las {reminder.LocalTime:HH\\:mm}.";
}

/// <summary>Polls the dispatcher. State lives in the database, so restarts lose nothing.</summary>
internal sealed class ReminderWorker(
    IServiceScopeFactory scopes,
    IOptions<ReminderOptions> options,
    ILogger<ReminderWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled)
        {
            return;
        }

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Max(5, options.Value.PollSeconds)));
        do
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var dispatcher = scope.ServiceProvider.GetRequiredService<ReminderDispatcher>();
                var created = await dispatcher.DispatchDueAsync(stoppingToken);
                var sent = await dispatcher.SendPendingEmailsAsync(stoppingToken);
                if (created + sent > 0)
                {
                    logger.LogInformation("Reminders: {Created} notifications created, {Sent} e-mails sent", created, sent);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Reminder dispatch failed; retrying on the next tick");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
