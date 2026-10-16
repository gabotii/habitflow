using HabitFlow.Application.Abstractions;
using HabitFlow.Application.Common;
using HabitFlow.Application.Tracking;
using HabitFlow.Domain.Common;
using HabitFlow.Domain.Habits;
using HabitFlow.Domain.Time;
using Microsoft.EntityFrameworkCore;

namespace HabitFlow.Application.Habits;

public sealed class HabitService(IAppDbContext db, ICurrentUser currentUser, IUserClock clock, HabitTracker tracker)
{
    public const int MaxActiveHabits = 300;

    /// <summary>How far back a new habit may start (to backfill recent history) or forward (to plan ahead).</summary>
    public const int MaxStartDateOffsetDays = 366;

    public async Task<PagedResult<HabitListItemDto>> ListAsync(HabitListQuery query, CancellationToken ct)
    {
        IQueryable<Habit> habits = db.Habits.AsNoTracking();
        habits = query.Status switch
        {
            "archived" => habits.Where(h => h.ArchivedAt != null),
            "all" => habits,
            _ => habits.Where(h => h.ArchivedAt == null),
        };

        if (query.CategoryId is { } categoryId)
        {
            habits = habits.Where(h => h.CategoryId == categoryId);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            // EF Core escapes the Contains argument, so % and _ typed by the user are literal.
            var search = query.Search.Trim().ToLowerInvariant();
            habits = habits.Where(h => h.Name.ToLower().Contains(search));
        }

        var page = await habits
            .OrderBy(h => h.Name).ThenBy(h => h.Id)
            .Select(h => h.Id)
            .ToPagedResultAsync(query, ct);

        var ids = page.Items.ToList();
        var tracked = await tracker.LoadAsync(q => q.Where(h => ids.Contains(h.Id)), null, ct);
        var categories = await CategoryNamesAsync(ct);
        var items = tracked
            .OrderBy(t => ids.IndexOf(t.Habit.Id))
            .Select(t => ToListItem(t, categories))
            .ToList();
        return new PagedResult<HabitListItemDto>(items, page.Page, page.PageSize, page.TotalCount);
    }

    public async Task<HabitDto> GetAsync(Guid id, CancellationToken ct)
    {
        var tracked = await tracker.LoadOneAsync(id, null, ct);
        var categories = await CategoryNamesAsync(ct);
        var hasLogs = await db.HabitLogs.AnyAsync(l => l.HabitId == id, ct);
        return ToDto(tracked, categories, hasLogs);
    }

    public async Task<HabitDto> CreateAsync(CreateHabitRequest request, CancellationToken ct)
    {
        if (await db.Habits.CountAsync(h => h.ArchivedAt == null, ct) >= MaxActiveHabits)
        {
            throw new ConflictException("habit.limit",
                $"Puedes tener como máximo {MaxActiveHabits} hábitos activos. Archiva alguno para crear otro.");
        }

        await EnsureCategoryVisibleAsync(request.CategoryId, ct);
        var today = await TodayInAsync(request.TimeZoneId, ct);
        var startDate = request.StartDate ?? today;
        EnsureStartDateInRange(startDate, today);

        var habit = Habit.Create(
            currentUser.UserId,
            new HabitDetails(request.Name, request.Description, request.CategoryId, request.Color, request.Icon,
                request.Unit, request.TimeZoneId),
            request.Type,
            startDate,
            request.Schedule.ToSpec(request.Type),
            clock.UtcNow);

        db.Habits.Add(habit);
        await db.SaveChangesAsync(ct);
        return await GetAsync(habit.Id, ct);
    }

    /// <summary>
    /// Details apply immediately. A frequency or target change creates a schedule version from today (see
    /// <see cref="Habit.ChangeSchedule"/>); today's log, if any, is re-evaluated against the new target.
    /// </summary>
    public async Task<HabitDto> UpdateAsync(Guid id, UpdateHabitRequest request, CancellationToken ct)
    {
        var habit = await LoadTrackedAsync(id, ct);
        if (habit.Version != request.Version)
        {
            throw ConflictException.Concurrency();
        }

        if (habit.CategoryId != request.CategoryId)
        {
            await EnsureCategoryVisibleAsync(request.CategoryId, ct);
        }

        habit.Update(new HabitDetails(request.Name, request.Description, request.CategoryId, request.Color,
            request.Icon, request.Unit, request.TimeZoneId));

        var (today, _) = await tracker.TodayForAsync(habit, ct);
        var now = clock.UtcNow;
        if (request.StartDate != habit.StartDate)
        {
            EnsureStartDateInRange(request.StartDate, today);
            var hasLogs = await db.HabitLogs.AnyAsync(l => l.HabitId == id, ct);
            habit.ChangeStartDate(request.StartDate, hasLogs, today, now);
        }

        if (habit.ChangeSchedule(request.Schedule.ToSpec(habit.Type), today, now))
        {
            var effectiveFrom = today < habit.StartDate ? habit.StartDate : today;
            var affected = await db.HabitLogs.Where(l => l.HabitId == id && l.LocalDate >= effectiveFrom).ToListAsync(ct);
            foreach (var log in affected)
            {
                log.Reevaluate(habit);
            }
        }

        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task<HabitDto> ArchiveAsync(Guid id, CancellationToken ct)
    {
        var habit = await LoadTrackedAsync(id, ct);
        var (today, _) = await tracker.TodayForAsync(habit, ct);
        habit.Archive(today, clock.UtcNow);
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task<HabitDto> RestoreAsync(Guid id, CancellationToken ct)
    {
        var habit = await LoadTrackedAsync(id, ct);
        if (await db.Habits.CountAsync(h => h.ArchivedAt == null, ct) >= MaxActiveHabits)
        {
            throw new ConflictException("habit.limit", $"Puedes tener como máximo {MaxActiveHabits} hábitos activos.");
        }

        var (today, _) = await tracker.TodayForAsync(habit, ct);
        habit.Restore(today);
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    /// <summary>Permanent deletion (with all its history) is only allowed for archived habits.</summary>
    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        var habit = await db.Habits.SingleOrDefaultAsync(h => h.Id == id, ct) ?? throw new NotFoundException("Hábito");
        if (!habit.IsArchived)
        {
            throw new DomainException("habit.delete_requires_archive",
                "Archiva el hábito antes de eliminarlo definitivamente.");
        }

        db.Habits.Remove(habit);
        await db.SaveChangesAsync(ct);
    }

    internal static HabitListItemDto ToListItem(TrackedHabit t, IReadOnlyDictionary<Guid, string> categories)
    {
        var h = t.Habit;
        return new HabitListItemDto(
            h.Id,
            h.Name,
            h.Description,
            new CategoryRefDto(h.CategoryId, categories.GetValueOrDefault(h.CategoryId, "")),
            h.Color,
            h.Icon,
            h.Type,
            h.Unit,
            h.SpecOn(t.Today).ToDto(),
            h.IsArchived,
            t.Timeline.Day(t.Today),
            t.Timeline.Brief(),
            t.Timeline.CurrentWeek().ToDto());
    }

    internal async Task<IReadOnlyDictionary<Guid, string>> CategoryNamesAsync(CancellationToken ct) =>
        await db.Categories.AsNoTracking().ToDictionaryAsync(c => c.Id, c => c.Name, ct);

    private static HabitDto ToDto(TrackedHabit t, IReadOnlyDictionary<Guid, string> categories, bool hasLogs)
    {
        var h = t.Habit;
        return new HabitDto(
            h.Id,
            h.Name,
            h.Description,
            new CategoryRefDto(h.CategoryId, categories.GetValueOrDefault(h.CategoryId, "")),
            h.Color,
            h.Icon,
            h.Type,
            h.Unit,
            h.StartDate,
            h.TimeZoneId,
            t.TimeZoneId,
            h.IsArchived,
            h.ArchivedAt,
            h.SpecOn(t.Today).ToDto(),
            h.Schedules.OrderBy(s => s.EffectiveFrom)
                .Select(s => new ScheduleVersionDto(s.EffectiveFrom, s.Spec.ToDto())).ToList(),
            h.Pauses.OrderBy(p => p.StartDate).Select(p => new PauseDto(p.StartDate, p.EndDate)).ToList(),
            hasLogs,
            t.Timeline.Day(t.Today),
            t.Timeline.ToStreakDto(),
            h.Version,
            h.CreatedAt,
            h.UpdatedAt);
    }

    private async Task<Habit> LoadTrackedAsync(Guid id, CancellationToken ct) =>
        await db.Habits.Include(h => h.Schedules).Include(h => h.Pauses)
            .SingleOrDefaultAsync(h => h.Id == id, ct)
        ?? throw new NotFoundException("Hábito");

    private async Task EnsureCategoryVisibleAsync(Guid categoryId, CancellationToken ct)
    {
        // The category filter only exposes built-in categories and the caller's own.
        if (!await db.Categories.AnyAsync(c => c.Id == categoryId, ct))
        {
            throw new DomainException("habit.invalid_category", "La categoría no existe.");
        }
    }

    private async Task<DateOnly> TodayInAsync(string? habitZone, CancellationToken ct)
    {
        var zone = TimeZoneIds.TryResolve(habitZone, out var own) ? own : await clock.GetTimeZoneAsync(ct);
        return ZonedTime.LocalDate(clock.UtcNow, zone);
    }

    private static void EnsureStartDateInRange(DateOnly startDate, DateOnly today)
    {
        if (Math.Abs(startDate.DayNumber - today.DayNumber) > MaxStartDateOffsetDays)
        {
            throw new DomainException("habit.start_date_out_of_range",
                "La fecha de inicio debe estar como máximo a un año de hoy.");
        }
    }
}
