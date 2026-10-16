using HabitFlow.Application.Abstractions;
using HabitFlow.Application.Common;
using HabitFlow.Domain.Common;
using HabitFlow.Domain.Goals;
using HabitFlow.Domain.Habits;
using Microsoft.EntityFrameworkCore;

namespace HabitFlow.Application.Goals;

public sealed class GoalService(
    IAppDbContext db,
    ICurrentUser currentUser,
    IUserClock clock,
    GoalProgressCalculator calculator)
{
    public const int MaxGoals = 200;

    public async Task<IReadOnlyList<GoalDto>> ListAsync(GoalListQuery query, CancellationToken ct)
    {
        IQueryable<Goal> goals = db.Goals.AsNoTracking().Include(g => g.Habits);
        goals = query.Status switch
        {
            "active" => goals.Where(g => g.Status == GoalStatus.Active),
            "paused" => goals.Where(g => g.Status == GoalStatus.Paused),
            "completed" => goals.Where(g => g.Status == GoalStatus.Completed),
            "cancelled" => goals.Where(g => g.Status == GoalStatus.Cancelled),
            "all" => goals,
            _ => goals.Where(g => g.Status == GoalStatus.Active || g.Status == GoalStatus.Paused),
        };

        var list = await goals.OrderBy(g => g.Deadline == null).ThenBy(g => g.Deadline).ThenBy(g => g.Name)
            .ToListAsync(ct);
        return await ToDtosAsync(list, ct);
    }

    public async Task<GoalDto> GetAsync(Guid id, CancellationToken ct)
    {
        var goal = await db.Goals.AsNoTracking().Include(g => g.Habits).SingleOrDefaultAsync(g => g.Id == id, ct)
                   ?? throw new NotFoundException("Objetivo");
        return (await ToDtosAsync([goal], ct))[0];
    }

    public async Task<GoalDto> CreateAsync(CreateGoalRequest request, CancellationToken ct)
    {
        if (await db.Goals.CountAsync(ct) >= MaxGoals)
        {
            throw new ConflictException("goal.limit", $"Puedes tener como máximo {MaxGoals} objetivos.");
        }

        var today = await clock.GetTodayAsync(ct);
        var startDate = request.StartDate ?? today;
        var habitIds = request.HabitIds?.Distinct().ToList() ?? [];
        var unit = request.Metric == GoalMetric.Manual
            ? request.Unit ?? string.Empty
            : await HabitUnitAsync(request.Metric, habitIds, today, ct);

        var goal = Goal.Create(currentUser.UserId, request.Name, request.Description, request.Metric,
            request.TargetValue, unit, startDate, request.Deadline, habitIds);
        db.Goals.Add(goal);
        await db.SaveChangesAsync(ct);
        return await GetAsync(goal.Id, ct);
    }

    public async Task<GoalDto> UpdateAsync(Guid id, UpdateGoalRequest request, CancellationToken ct)
    {
        var goal = await LoadAsync(id, ct);
        if (goal.Version != request.Version)
        {
            throw ConflictException.Concurrency();
        }

        goal.Update(request.Name, request.Description, request.TargetValue, request.Unit ?? goal.Unit, request.Deadline);
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task<GoalDto> ChangeStatusAsync(Guid id, ChangeGoalStatusRequest request, CancellationToken ct)
    {
        var goal = await LoadAsync(id, ct);
        goal.ChangeStatus(request.Status, clock.UtcNow);
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        var goal = await LoadAsync(id, ct);
        db.Goals.Remove(goal);
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<GoalEntryDto>> EntriesAsync(Guid goalId, CancellationToken ct)
    {
        await EnsureExistsAsync(goalId, ct);
        return await db.GoalEntries.AsNoTracking()
            .Where(e => e.GoalId == goalId)
            .OrderByDescending(e => e.LocalDate).ThenByDescending(e => e.CreatedAt)
            .Take(500)
            .Select(e => new GoalEntryDto(e.Id, e.LocalDate, e.Amount, e.Note, e.CreatedAt))
            .ToListAsync(ct);
    }

    public async Task<GoalDto> AddEntryAsync(Guid goalId, AddGoalEntryRequest request, CancellationToken ct)
    {
        var goal = await LoadAsync(goalId, ct);
        var today = await clock.GetTodayAsync(ct);
        var date = request.Date ?? today;
        goal.EnsureAcceptsEntries(date, today);
        db.GoalEntries.Add(GoalProgressEntry.Create(goal, date, request.Amount, request.Note));
        await db.SaveChangesAsync(ct);
        return await GetAsync(goalId, ct);
    }

    public async Task<GoalDto> DeleteEntryAsync(Guid goalId, Guid entryId, CancellationToken ct)
    {
        var goal = await LoadAsync(goalId, ct);
        if (goal.Status is GoalStatus.Cancelled)
        {
            throw new DomainException("goal.cancelled", "Un objetivo cancelado no se puede modificar.");
        }

        var entry = await db.GoalEntries.SingleOrDefaultAsync(e => e.Id == entryId && e.GoalId == goalId, ct)
                    ?? throw new NotFoundException("Registro de progreso");
        db.GoalEntries.Remove(entry);
        await db.SaveChangesAsync(ct);
        return await GetAsync(goalId, ct);
    }

    public async Task<IReadOnlyList<GoalHistoryPointDto>> HistoryAsync(Guid goalId, CancellationToken ct)
    {
        var goal = await db.Goals.AsNoTracking().Include(g => g.Habits).SingleOrDefaultAsync(g => g.Id == goalId, ct)
                   ?? throw new NotFoundException("Objetivo");
        return await calculator.HistoryAsync(goal, ct);
    }

    internal async Task<IReadOnlyList<GoalDto>> ToDtosAsync(IReadOnlyList<Goal> goals, CancellationToken ct)
    {
        if (goals.Count == 0)
        {
            return [];
        }

        var today = await clock.GetTodayAsync(ct);
        var values = await calculator.CurrentValuesAsync(goals, ct);
        var habitIds = goals.SelectMany(g => g.Habits.Select(h => h.HabitId)).Distinct().ToList();
        var habits = await db.Habits.AsNoTracking()
            .Where(h => habitIds.Contains(h.Id))
            .Select(h => new GoalHabitDto(h.Id, h.Name, h.Icon, h.Color, h.Unit, h.ArchivedAt != null))
            .ToDictionaryAsync(h => h.Id, ct);

        return goals.Select(g =>
        {
            var progress = GoalProgress.Compute(values.GetValueOrDefault(g.Id), g.TargetValue);
            var open = g.Status is GoalStatus.Active or GoalStatus.Paused;
            return new GoalDto(
                g.Id, g.Name, g.Description, g.Metric, g.TargetValue, g.Unit, g.StartDate, g.Deadline, g.Status,
                g.CompletedAt,
                g.Habits.Select(h => habits.GetValueOrDefault(h.HabitId)).OfType<GoalHabitDto>().ToList(),
                new GoalProgressDto(progress.Current, progress.Target, progress.Percent, progress.ExceededBy,
                    progress.TargetReached),
                open && g.Deadline < today && !progress.TargetReached,
                open && g.Deadline is { } deadline && deadline >= today ? deadline.DayNumber - today.DayNumber : null,
                g.Version,
                g.CreatedAt,
                g.UpdatedAt);
        }).ToList();
    }

    private async Task<string> HabitUnitAsync(GoalMetric metric, IReadOnlyList<Guid> habitIds, DateOnly today,
        CancellationToken ct)
    {
        var habits = await db.Habits.AsNoTracking().Include(h => h.Schedules)
            .Where(h => habitIds.Contains(h.Id))
            .ToListAsync(ct);
        if (habits.Count != habitIds.Count)
        {
            throw new DomainException("goal.invalid_habit", "Alguno de los hábitos no existe.");
        }

        if (habits.Any(h => h.IsArchived))
        {
            throw new DomainException("goal.archived_habit", "No puedes vincular hábitos archivados.");
        }

        switch (metric)
        {
            case GoalMetric.HabitQuantity:
                if (habits.Any(h => h.Type != HabitType.Quantity))
                {
                    throw new DomainException("goal.quantity_habits_only",
                        "Las metas de cantidad solo admiten hábitos cuantificables.");
                }

                var units = habits.Select(h => h.Unit!.Trim().ToLowerInvariant()).Distinct().ToList();
                if (units.Count != 1)
                {
                    throw new DomainException("goal.mixed_units", "Todos los hábitos deben usar la misma unidad.");
                }

                return habits[0].Unit!;
            case GoalMetric.HabitStreak:
                return habits[0].SpecOn(today).Unit == PeriodUnit.Week ? "semanas" : "días";
            default:
                return "veces";
        }
    }

    private async Task<Goal> LoadAsync(Guid id, CancellationToken ct) =>
        await db.Goals.Include(g => g.Habits).SingleOrDefaultAsync(g => g.Id == id, ct)
        ?? throw new NotFoundException("Objetivo");

    private async Task EnsureExistsAsync(Guid id, CancellationToken ct)
    {
        if (!await db.Goals.AnyAsync(g => g.Id == id, ct))
        {
            throw new NotFoundException("Objetivo");
        }
    }
}
