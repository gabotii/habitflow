using System.Globalization;
using FluentValidation;
using HabitFlow.Application.Abstractions;
using HabitFlow.Application.Common;
using HabitFlow.Domain.Common;
using HabitFlow.Domain.Notifications;
using HabitFlow.Domain.Reminders;
using HabitFlow.Domain.Time;
using Microsoft.EntityFrameworkCore;

namespace HabitFlow.Application.Reminders;

/// <summary><see cref="LocalTime"/> is a wall-clock time "HH:mm" in <see cref="TimeZoneId"/> (default: the user's zone).</summary>
public sealed record SaveReminderRequest(
    string Title,
    string? Description,
    string LocalTime,
    IReadOnlyList<DayOfWeek> Weekdays,
    string? TimeZoneId,
    ReminderChannel Channel,
    Guid? HabitId,
    Guid? GoalId);

public sealed record SetReminderActiveRequest(bool IsActive);

public sealed record ReminderTargetDto(Guid Id, string Name);

public sealed record ReminderDto(
    Guid Id,
    string Title,
    string? Description,
    string LocalTime,
    IReadOnlyList<DayOfWeek> Weekdays,
    string TimeZoneId,
    bool IsActive,
    ReminderChannel Channel,
    ReminderTargetDto? Habit,
    ReminderTargetDto? Goal,
    DateTimeOffset? NextFireAt,
    DateTimeOffset? LastFiredAt,
    DateTimeOffset CreatedAt);

public sealed class SaveReminderRequestValidator : AbstractValidator<SaveReminderRequest>
{
    public SaveReminderRequestValidator()
    {
        RuleFor(r => r.Title).NotEmpty().MaximumLength(Reminder.TitleMaxLength);
        RuleFor(r => r.Description).MaximumLength(Reminder.DescriptionMaxLength);
        RuleFor(r => r.LocalTime).Must(t => ReminderService.TryParseTime(t, out _))
            .WithMessage("Indica la hora con el formato HH:mm (por ejemplo, 08:30).");
        RuleFor(r => r.Weekdays).NotEmpty().WithMessage("Elige al menos un día de la semana.")
            .Must(d => d.All(Enum.IsDefined)).WithMessage("Día de la semana no válido.");
        RuleFor(r => r.TimeZoneId).IanaTimeZone().When(r => !string.IsNullOrWhiteSpace(r.TimeZoneId));
        RuleFor(r => r.Channel).IsInEnum();
        RuleFor(r => r).Must(r => r.HabitId is null || r.GoalId is null).WithName("goalId")
            .WithMessage("Un recordatorio puede vincularse a un hábito o a un objetivo, no a ambos.");
    }
}

public sealed class ReminderService(IAppDbContext db, ICurrentUser currentUser, IUserClock clock)
{
    public const int MaxReminders = 100;

    public async Task<IReadOnlyList<ReminderDto>> ListAsync(CancellationToken ct) =>
        await Project(db.Reminders.AsNoTracking().OrderBy(r => r.LocalTime).ThenBy(r => r.Title)).ToListAsync(ct);

    public async Task<ReminderDto> GetAsync(Guid id, CancellationToken ct) =>
        await Project(db.Reminders.AsNoTracking().Where(r => r.Id == id)).SingleOrDefaultAsync(ct)
        ?? throw new NotFoundException("Recordatorio");

    public async Task<ReminderDto> CreateAsync(SaveReminderRequest request, CancellationToken ct)
    {
        if (await db.Reminders.CountAsync(ct) >= MaxReminders)
        {
            throw new ConflictException("reminder.limit", $"Puedes tener como máximo {MaxReminders} recordatorios.");
        }

        await EnsureTargetsAsync(request, ct);
        var zone = request.TimeZoneId ?? (await clock.GetTimeZoneAsync(ct)).Id;
        var reminder = Reminder.Create(currentUser.UserId, request.Title, request.Description, Time(request),
            Mask(request), zone, request.Channel, request.HabitId, request.GoalId, clock.UtcNow);
        db.Reminders.Add(reminder);
        await db.SaveChangesAsync(ct);
        return await GetAsync(reminder.Id, ct);
    }

    public async Task<ReminderDto> UpdateAsync(Guid id, SaveReminderRequest request, CancellationToken ct)
    {
        var reminder = await LoadAsync(id, ct);
        await EnsureTargetsAsync(request, ct);
        reminder.Update(request.Title, request.Description, Time(request), Mask(request),
            request.TimeZoneId ?? reminder.TimeZoneId, request.Channel, request.HabitId, request.GoalId, clock.UtcNow);
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task<ReminderDto> SetActiveAsync(Guid id, SetReminderActiveRequest request, CancellationToken ct)
    {
        var reminder = await LoadAsync(id, ct);
        reminder.SetActive(request.IsActive, clock.UtcNow);
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        db.Reminders.Remove(await LoadAsync(id, ct));
        await db.SaveChangesAsync(ct);
    }

    public static bool TryParseTime(string? value, out TimeOnly time) =>
        TimeOnly.TryParseExact(value, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out time);

    private static TimeOnly Time(SaveReminderRequest request) =>
        TryParseTime(request.LocalTime, out var time) ? time : throw new DomainException("reminder.invalid_time", "Hora no válida.");

    private static int Mask(SaveReminderRequest request) => Weekdays.FromDays(request.Weekdays);

    private IQueryable<ReminderDto> Project(IQueryable<Reminder> query) => query.Select(r => new ReminderDto(
        r.Id,
        r.Title,
        r.Description,
        r.LocalTime.ToString("HH:mm"),
        Weekdays.ToDays(r.DaysMask),
        r.TimeZoneId,
        r.IsActive,
        r.Channel,
        db.Habits.Where(h => h.Id == r.HabitId).Select(h => new ReminderTargetDto(h.Id, h.Name)).FirstOrDefault(),
        db.Goals.Where(g => g.Id == r.GoalId).Select(g => new ReminderTargetDto(g.Id, g.Name)).FirstOrDefault(),
        r.NextFireAt,
        r.LastFiredAt,
        r.CreatedAt));

    private async Task EnsureTargetsAsync(SaveReminderRequest request, CancellationToken ct)
    {
        if (request.HabitId is { } habitId && !await db.Habits.AnyAsync(h => h.Id == habitId, ct))
        {
            throw new DomainException("reminder.invalid_habit", "El hábito no existe.");
        }

        if (request.GoalId is { } goalId && !await db.Goals.AnyAsync(g => g.Id == goalId, ct))
        {
            throw new DomainException("reminder.invalid_goal", "El objetivo no existe.");
        }
    }

    private async Task<Reminder> LoadAsync(Guid id, CancellationToken ct) =>
        await db.Reminders.SingleOrDefaultAsync(r => r.Id == id, ct) ?? throw new NotFoundException("Recordatorio");
}

public sealed record NotificationDto(
    Guid Id,
    NotificationKind Kind,
    string Title,
    string? Body,
    string? Link,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ReadAt,
    EmailDeliveryStatus EmailStatus);

public sealed class NotificationQuery : PagedQuery
{
    public bool UnreadOnly { get; init; }
}

public sealed class NotificationQueryValidator : AbstractValidator<NotificationQuery>
{
    public NotificationQueryValidator() => this.ApplyPaging();
}

public sealed class NotificationService(IAppDbContext db, IUserClock clock)
{
    public async Task<PagedResult<NotificationDto>> ListAsync(NotificationQuery query, CancellationToken ct)
    {
        var notifications = db.Notifications.AsNoTracking();
        if (query.UnreadOnly)
        {
            notifications = notifications.Where(n => n.ReadAt == null);
        }

        return await notifications
            .OrderByDescending(n => n.CreatedAt).ThenByDescending(n => n.Id)
            .Select(n => new NotificationDto(n.Id, n.Kind, n.Title, n.Body, n.Link, n.CreatedAt, n.ReadAt, n.EmailStatus))
            .ToPagedResultAsync(query, ct);
    }

    public Task<int> UnreadCountAsync(CancellationToken ct) => db.Notifications.CountAsync(n => n.ReadAt == null, ct);

    public async Task MarkReadAsync(Guid id, CancellationToken ct)
    {
        var notification = await db.Notifications.SingleOrDefaultAsync(n => n.Id == id, ct)
                           ?? throw new NotFoundException("Notificación");
        notification.MarkRead(clock.UtcNow);
        await db.SaveChangesAsync(ct);
    }

    public async Task<int> MarkAllReadAsync(CancellationToken ct)
    {
        var now = clock.UtcNow;
        return await db.Notifications.Where(n => n.ReadAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.ReadAt, now), ct);
    }
}
