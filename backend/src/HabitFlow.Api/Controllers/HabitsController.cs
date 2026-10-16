using HabitFlow.Application.Categories;
using HabitFlow.Application.Common;
using HabitFlow.Application.Habits;
using HabitFlow.Application.Tracking;
using Microsoft.AspNetCore.Mvc;

namespace HabitFlow.Api.Controllers;

[ApiController]
[Route("api/v1/categories")]
[Produces("application/json")]
public sealed class CategoriesController(CategoryService categories) : ControllerBase
{
    /// <summary>Built-in categories plus the caller's own.</summary>
    [HttpGet]
    public Task<IReadOnlyList<CategoryDto>> List(CancellationToken ct) => categories.ListAsync(ct);

    [HttpPost]
    [ProducesResponseType<CategoryDto>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Create(SaveCategoryRequest request, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, await categories.CreateAsync(request, ct));

    [HttpPut("{id:guid}")]
    public Task<CategoryDto> Update(Guid id, SaveCategoryRequest request, CancellationToken ct) =>
        categories.UpdateAsync(id, request, ct);

    /// <summary>Only custom categories without habits can be deleted (409 otherwise).</summary>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await categories.DeleteAsync(id, ct);
        return NoContent();
    }
}

[ApiController]
[Route("api/v1/habits")]
[Produces("application/json")]
public sealed class HabitsController(HabitService habits, HabitLogService logs) : ControllerBase
{
    private const string DateRoute = "{date:regex(^\\d{{4}}-\\d{{2}}-\\d{{2}}$)}";

    /// <summary>Habits with today's state, streak and weekly progress. Paginated and sorted by name.</summary>
    [HttpGet]
    public Task<PagedResult<HabitListItemDto>> List([FromQuery] HabitListQuery query, CancellationToken ct) =>
        habits.ListAsync(query, ct);

    [HttpGet("{id:guid}")]
    [ProducesResponseType<HabitDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<HabitDto> Get(Guid id, CancellationToken ct) => habits.GetAsync(id, ct);

    [HttpPost]
    [ProducesResponseType<HabitDto>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Create(CreateHabitRequest request, CancellationToken ct)
    {
        var habit = await habits.CreateAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = habit.Id }, habit);
    }

    /// <summary>
    /// Updates details and rules. Frequency/target changes apply from today (the habit's local date); past days keep
    /// the rules they had. Send the <c>version</c> you read: a stale one returns 409.
    /// </summary>
    [HttpPut("{id:guid}")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public Task<HabitDto> Update(Guid id, UpdateHabitRequest request, CancellationToken ct) =>
        habits.UpdateAsync(id, request, ct);

    /// <summary>Archives from today. History is kept; archived days are not counted as missed.</summary>
    [HttpPost("{id:guid}/archive")]
    public Task<HabitDto> Archive(Guid id, CancellationToken ct) => habits.ArchiveAsync(id, ct);

    [HttpPost("{id:guid}/restore")]
    public Task<HabitDto> Restore(Guid id, CancellationToken ct) => habits.RestoreAsync(id, ct);

    /// <summary>Permanently deletes an archived habit and its whole history.</summary>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await habits.DeleteAsync(id, ct);
        return NoContent();
    }

    /// <summary>Logs in a date range (default: last 90 days, at most 366).</summary>
    [HttpGet("{id:guid}/logs")]
    public Task<IReadOnlyList<HabitLogDto>> Logs(Guid id, [FromQuery] DateRangeQuery range, CancellationToken ct) =>
        logs.ListAsync(id, range, ct);

    /// <summary>
    /// Creates or replaces the single log of a local date (idempotent). Binary habits send <c>completed</c>;
    /// quantity habits send the day's total in <c>quantity</c>.
    /// </summary>
    [HttpPut("{id:guid}/logs/" + DateRoute)]
    [ProducesResponseType<LogResultDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public Task<LogResultDto> PutLog(Guid id, DateOnly date, UpsertLogRequest request, CancellationToken ct) =>
        logs.UpsertAsync(id, date, request, ct);

    /// <summary>Removes the log of a date. Idempotent.</summary>
    [HttpDelete("{id:guid}/logs/" + DateRoute)]
    public Task<LogResultDto> DeleteLog(Guid id, DateOnly date, CancellationToken ct) => logs.DeleteAsync(id, date, ct);

    /// <summary>Audit trail of log changes, newest first.</summary>
    [HttpGet("{id:guid}/logs/changes")]
    public Task<PagedResult<LogChangeDto>> Changes(Guid id, [FromQuery] LogChangesQuery query, CancellationToken ct) =>
        logs.ChangesAsync(id, query, ct);

    [HttpGet("{id:guid}/streak")]
    public Task<StreakDto> Streak(Guid id, CancellationToken ct) => logs.StreakAsync(id, ct);
}
