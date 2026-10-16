using HabitFlow.Domain.Common;

namespace HabitFlow.Domain.Notifications;

public enum NotificationKind
{
    Reminder,
    Invitation,
    Challenge,
    SharedGoal,
}

public enum EmailDeliveryStatus
{
    NotRequested,
    Pending,
    Sent,
    Failed,
}

/// <summary>
/// A persisted in-app notification. Reminder notifications are unique per (reminder, occurrence) so a retried
/// dispatch never creates duplicates. E-mail delivery state lives here too (a minimal outbox).
/// </summary>
public sealed class Notification : OwnedEntity
{
    public const int MaxEmailAttempts = 3;

    private Notification()
    {
    }

    public NotificationKind Kind { get; private set; }
    public string Title { get; private set; } = string.Empty;
    public string? Body { get; private set; }

    /// <summary>Relative client route to open (e.g. <c>/habits/{id}</c>); never an absolute URL.</summary>
    public string? Link { get; private set; }

    public Guid? ReminderId { get; private set; }
    public DateTimeOffset? OccurrenceAt { get; private set; }
    public DateTimeOffset? ReadAt { get; private set; }
    public EmailDeliveryStatus EmailStatus { get; private set; }
    public int EmailAttempts { get; private set; }

    public static Notification Create(
        Guid userId,
        NotificationKind kind,
        string title,
        string? body,
        string? link,
        bool sendEmail,
        Guid? reminderId = null,
        DateTimeOffset? occurrenceAt = null) => new()
        {
            UserId = userId,
            Kind = kind,
            Title = title.Length > 150 ? title[..150] : title,
            Body = body is { Length: > 500 } ? body[..500] : body,
            Link = link,
            ReminderId = reminderId,
            OccurrenceAt = occurrenceAt,
            EmailStatus = sendEmail ? EmailDeliveryStatus.Pending : EmailDeliveryStatus.NotRequested,
        };

    public void MarkRead(DateTimeOffset now) => ReadAt ??= now;

    public void RecordEmailAttempt(bool success)
    {
        EmailAttempts++;
        EmailStatus = success ? EmailDeliveryStatus.Sent
            : EmailAttempts >= MaxEmailAttempts ? EmailDeliveryStatus.Failed
            : EmailDeliveryStatus.Pending;
    }
}
