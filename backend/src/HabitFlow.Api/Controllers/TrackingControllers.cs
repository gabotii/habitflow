using HabitFlow.Application.Calendar;
using HabitFlow.Application.Common;
using HabitFlow.Application.Dashboard;
using HabitFlow.Application.Goals;
using HabitFlow.Application.Reminders;
using HabitFlow.Application.Statistics;
using Microsoft.AspNetCore.Mvc;

namespace HabitFlow.Api.Controllers;

[ApiController]
[Route("api/v1/dashboard")]
[Produces("application/json")]
public sealed class DashboardController(DashboardService dashboard) : ControllerBase
{
    /// <summary>Everything the home screen needs, computed from persisted data in the user's time zone.</summary>
    [HttpGet]
    public Task<DashboardDto> Get(CancellationToken ct) => dashboard.GetAsync(ct);
}

[ApiController]
[Route("api/v1/calendar")]
[Produces("application/json")]
public sealed class CalendarController(CalendarService calendar) : ControllerBase
{
    /// <summary>Per-day summary for a grid of at most 42 days (a month view).</summary>
    [HttpGet]
    public Task<CalendarRangeDto> Range([FromQuery] CalendarQuery query, CancellationToken ct) =>
        calendar.RangeAsync(query, ct);

    /// <summary>The ISO week (Monday–Sunday) containing <c>date</c>, as a habit × day matrix.</summary>
    [HttpGet("week/{date:regex(^\\d{{4}}-\\d{{2}}-\\d{{2}}$)}")]
    public Task<CalendarWeekDto> Week(DateOnly date, CancellationToken ct) => calendar.WeekAsync(date, ct);

    /// <summary>Habits of one day with their status, so the user can log or correct them.</summary>
    [HttpGet("{date:regex(^\\d{{4}}-\\d{{2}}-\\d{{2}}$)}")]
    public Task<CalendarDayDetailDto> Day(DateOnly date, CancellationToken ct) => calendar.DayAsync(date, ct);
}

[ApiController]
[Route("api/v1/statistics")]
[Produces("application/json")]
public sealed class StatisticsController(StatisticsService statistics) : ControllerBase
{
    /// <summary>Aggregated statistics for a range (default: last 30 days, at most 366).</summary>
    [HttpGet]
    public Task<StatisticsDto> Overview([FromQuery] DateRangeQuery range, CancellationToken ct) =>
        statistics.OverviewAsync(range, ct);

    [HttpGet("/api/v1/habits/{id:guid}/statistics")]
    public Task<HabitStatisticsDto> Habit(Guid id, [FromQuery] DateRangeQuery range, CancellationToken ct) =>
        statistics.HabitAsync(id, range, ct);
}

[ApiController]
[Route("api/v1/goals")]
[Produces("application/json")]
public sealed class GoalsController(GoalService goals) : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyList<GoalDto>> List([FromQuery] GoalListQuery query, CancellationToken ct) =>
        goals.ListAsync(query, ct);

    [HttpGet("{id:guid}")]
    public Task<GoalDto> Get(Guid id, CancellationToken ct) => goals.GetAsync(id, ct);

    [HttpPost]
    [ProducesResponseType<GoalDto>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Create(CreateGoalRequest request, CancellationToken ct)
    {
        var goal = await goals.CreateAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = goal.Id }, goal);
    }

    /// <summary>Name, description, target, unit (manual goals) and deadline. Metric and habits are fixed.</summary>
    [HttpPut("{id:guid}")]
    public Task<GoalDto> Update(Guid id, UpdateGoalRequest request, CancellationToken ct) =>
        goals.UpdateAsync(id, request, ct);

    /// <summary>Pause, resume, complete, reopen or cancel (cancelled is final).</summary>
    [HttpPost("{id:guid}/status")]
    public Task<GoalDto> ChangeStatus(Guid id, ChangeGoalStatusRequest request, CancellationToken ct) =>
        goals.ChangeStatusAsync(id, request, ct);

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await goals.DeleteAsync(id, ct);
        return NoContent();
    }

    [HttpGet("{id:guid}/entries")]
    public Task<IReadOnlyList<GoalEntryDto>> Entries(Guid id, CancellationToken ct) => goals.EntriesAsync(id, ct);

    /// <summary>Adds manual progress (manual goals only, while active).</summary>
    [HttpPost("{id:guid}/entries")]
    public Task<GoalDto> AddEntry(Guid id, AddGoalEntryRequest request, CancellationToken ct) =>
        goals.AddEntryAsync(id, request, ct);

    [HttpDelete("{id:guid}/entries/{entryId:guid}")]
    public Task<GoalDto> DeleteEntry(Guid id, Guid entryId, CancellationToken ct) =>
        goals.DeleteEntryAsync(id, entryId, ct);

    /// <summary>Cumulative progress per day (streak goals: running streak length).</summary>
    [HttpGet("{id:guid}/history")]
    public Task<IReadOnlyList<GoalHistoryPointDto>> History(Guid id, CancellationToken ct) => goals.HistoryAsync(id, ct);
}

[ApiController]
[Route("api/v1/reminders")]
[Produces("application/json")]
public sealed class RemindersController(ReminderService reminders) : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyList<ReminderDto>> List(CancellationToken ct) => reminders.ListAsync(ct);

    [HttpGet("{id:guid}")]
    public Task<ReminderDto> Get(Guid id, CancellationToken ct) => reminders.GetAsync(id, ct);

    [HttpPost]
    [ProducesResponseType<ReminderDto>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Create(SaveReminderRequest request, CancellationToken ct)
    {
        var reminder = await reminders.CreateAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = reminder.Id }, reminder);
    }

    [HttpPut("{id:guid}")]
    public Task<ReminderDto> Update(Guid id, SaveReminderRequest request, CancellationToken ct) =>
        reminders.UpdateAsync(id, request, ct);

    [HttpPost("{id:guid}/active")]
    public Task<ReminderDto> SetActive(Guid id, SetReminderActiveRequest request, CancellationToken ct) =>
        reminders.SetActiveAsync(id, request, ct);

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await reminders.DeleteAsync(id, ct);
        return NoContent();
    }
}

[ApiController]
[Route("api/v1/notifications")]
[Produces("application/json")]
public sealed class NotificationsController(NotificationService notifications) : ControllerBase
{
    [HttpGet]
    public Task<PagedResult<NotificationDto>> List([FromQuery] NotificationQuery query, CancellationToken ct) =>
        notifications.ListAsync(query, ct);

    [HttpGet("unread-count")]
    public async Task<object> UnreadCount(CancellationToken ct) => new { count = await notifications.UnreadCountAsync(ct) };

    [HttpPost("{id:guid}/read")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> MarkRead(Guid id, CancellationToken ct)
    {
        await notifications.MarkReadAsync(id, ct);
        return NoContent();
    }

    [HttpPost("read-all")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> MarkAllRead(CancellationToken ct)
    {
        await notifications.MarkAllReadAsync(ct);
        return NoContent();
    }
}
