using FluentValidation;
using HabitFlow.Application.Abstractions;
using HabitFlow.Application.Common;
using HabitFlow.Application.Tracking;
using HabitFlow.Domain.Habits;
using Microsoft.EntityFrameworkCore;

namespace HabitFlow.Application.Habits;

/// <summary>
/// Absolute values for one day. Binary habits send <see cref="Completed"/>; quantity habits send the day's total in
/// <see cref="Quantity"/> (never an increment, so retrying the same request is harmless).
/// </summary>
public sealed record UpsertLogRequest(bool? Completed, decimal? Quantity, string? Note);

// Note semantics: null = keep the current note, "" = remove it, text = replace it.

public sealed record HabitLogDto(
    DateOnly Date,
    decimal? Quantity,
    bool Completed,
    string? Note,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>Returned after every write so the client can refresh the card without another round-trip.</summary>
public sealed record LogResultDto(HabitLogDto? Log, HabitDayDto Day, StreakBriefDto Streak, WeekProgressDto? Week);

public sealed record LogChangeDto(
    DateOnly Date,
    LogChangeAction Action,
    decimal? PreviousQuantity,
    decimal? NewQuantity,
    bool? PreviousCompleted,
    bool? NewCompleted,
    DateTimeOffset At);

public sealed class LogChangesQuery : PagedQuery;

public sealed class UpsertLogRequestValidator : AbstractValidator<UpsertLogRequest>
{
    public UpsertLogRequestValidator()
    {
        RuleFor(r => r.Quantity)
            .GreaterThanOrEqualTo(0).LessThanOrEqualTo(HabitLog.MaxQuantity)
            .Must(q => q is null || decimal.Round(q.Value, 2) == q).WithMessage("Admite como máximo 2 decimales.");
        RuleFor(r => r.Note).MaximumLength(HabitLog.NoteMaxLength);
        RuleFor(r => r)
            .Must(r => r.Completed is not null || r.Quantity is not null)
            .WithName("completed")
            .WithMessage("Indica si completaste el hábito o la cantidad registrada.");
    }
}

public sealed class LogChangesQueryValidator : AbstractValidator<LogChangesQuery>
{
    public LogChangesQueryValidator() => this.ApplyPaging();
}

public sealed class HabitLogService(IAppDbContext db, HabitTracker tracker)
{
    public async Task<IReadOnlyList<HabitLogDto>> ListAsync(Guid habitId, DateRangeQuery range, CancellationToken ct)
    {
        var habit = await db.Habits.AsNoTracking().SingleOrDefaultAsync(h => h.Id == habitId, ct)
                    ?? throw new NotFoundException("Hábito");
        var (today, _) = await tracker.TodayForAsync(habit, ct);
        var (from, to) = DateRanges.Resolve(range, today, 90);

        return await db.HabitLogs.AsNoTracking()
            .Where(l => l.HabitId == habitId && l.LocalDate >= from && l.LocalDate <= to)
            .OrderByDescending(l => l.LocalDate)
            .Select(l => new HabitLogDto(l.LocalDate, l.Quantity, l.IsCompleted, l.Note, l.CreatedAt, l.UpdatedAt))
            .ToListAsync(ct);
    }

    /// <summary>
    /// Creates or replaces the single log of that day (idempotent). Writes are validated against the habit's own
    /// "today" and start date, completion is computed with the rules in force that day, and every effective change is
    /// audited. Two concurrent first writes of the same day hit the unique index: one wins, the other gets a 409.
    /// </summary>
    public async Task<LogResultDto> UpsertAsync(Guid habitId, DateOnly date, UpsertLogRequest request, CancellationToken ct)
    {
        var habit = await LoadHabitAsync(habitId, ct);
        var (today, _) = await tracker.TodayForAsync(habit, ct);
        habit.EnsureCanLog(date, today);

        var completed = request.Completed ?? false;
        var log = await db.HabitLogs.SingleOrDefaultAsync(l => l.HabitId == habitId && l.LocalDate == date, ct);
        if (log is null)
        {
            log = HabitLog.Create(habit, date, request.Quantity, completed, request.Note);
            db.HabitLogs.Add(log);
            db.HabitLogChanges.Add(HabitLogChange.Record(habit.UserId, habitId, date, null, log));
        }
        else
        {
            var before = (log.Quantity, log.IsCompleted);
            var noteBefore = log.Note;
            // A null note keeps the stored one (one-tap check/uncheck from a card); an empty string clears it.
            log.Update(habit, request.Quantity, completed, request.Note ?? log.Note);
            if (before != (log.Quantity, log.IsCompleted))
            {
                db.HabitLogChanges.Add(HabitLogChange.Record(habit.UserId, habitId, date, before, log));
            }
            else if (noteBefore == log.Note)
            {
                // Same values: nothing to save (idempotent repeat).
                return await ResultAsync(habitId, date, log, ct);
            }
        }

        await db.SaveChangesAsync(ct);
        return await ResultAsync(habitId, date, log, ct);
    }

    /// <summary>Removes the day's log ("unmark"). Idempotent: deleting a missing log is not an error.</summary>
    public async Task<LogResultDto> DeleteAsync(Guid habitId, DateOnly date, CancellationToken ct)
    {
        var habit = await LoadHabitAsync(habitId, ct);
        var (today, _) = await tracker.TodayForAsync(habit, ct);
        habit.EnsureCanLog(date, today);

        var log = await db.HabitLogs.SingleOrDefaultAsync(l => l.HabitId == habitId && l.LocalDate == date, ct);
        if (log is not null)
        {
            db.HabitLogs.Remove(log);
            db.HabitLogChanges.Add(HabitLogChange.Record(habit.UserId, habitId, date, (log.Quantity, log.IsCompleted), null));
            await db.SaveChangesAsync(ct);
        }

        return await ResultAsync(habitId, date, null, ct);
    }

    public async Task<PagedResult<LogChangeDto>> ChangesAsync(Guid habitId, LogChangesQuery query, CancellationToken ct)
    {
        if (!await db.Habits.AnyAsync(h => h.Id == habitId, ct))
        {
            throw new NotFoundException("Hábito");
        }

        return await db.HabitLogChanges.AsNoTracking()
            .Where(c => c.HabitId == habitId)
            .OrderByDescending(c => c.CreatedAt).ThenByDescending(c => c.Id)
            .Select(c => new LogChangeDto(c.LocalDate, c.Action, c.PreviousQuantity, c.NewQuantity, c.PreviousCompleted,
                c.NewCompleted, c.CreatedAt))
            .ToPagedResultAsync(query, ct);
    }

    public async Task<StreakDto> StreakAsync(Guid habitId, CancellationToken ct) =>
        (await tracker.LoadOneAsync(habitId, null, ct)).Timeline.ToStreakDto();

    private async Task<Habit> LoadHabitAsync(Guid habitId, CancellationToken ct) =>
        await db.Habits.AsNoTracking().Include(h => h.Schedules).Include(h => h.Pauses)
            .SingleOrDefaultAsync(h => h.Id == habitId, ct)
        ?? throw new NotFoundException("Hábito");

    private async Task<LogResultDto> ResultAsync(Guid habitId, DateOnly date, HabitLog? log, CancellationToken ct)
    {
        var timeline = (await tracker.LoadOneAsync(habitId, null, ct)).Timeline;
        return new LogResultDto(
            log is null ? null : new HabitLogDto(log.LocalDate, log.Quantity, log.IsCompleted, log.Note, log.CreatedAt, log.UpdatedAt),
            timeline.Day(date, log?.Note),
            timeline.Brief(),
            timeline.CurrentWeek().ToDto());
    }
}
