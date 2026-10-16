namespace HabitFlow.Domain.Habits;

/// <summary>
/// A span of local days during which the habit is not expected (it was archived). Paused days are "not scheduled":
/// they neither break nor extend streaks. <see cref="EndDate"/> is inclusive; null while the pause is open.
/// </summary>
public sealed class HabitPause
{
    private HabitPause()
    {
    }

    internal HabitPause(Guid habitId, DateOnly startDate)
    {
        HabitId = habitId;
        StartDate = startDate;
    }

    public Guid Id { get; private set; } = Guid.CreateVersion7();
    public Guid HabitId { get; private set; }
    public DateOnly StartDate { get; private set; }
    public DateOnly? EndDate { get; private set; }

    public bool Covers(DateOnly date) => date >= StartDate && (EndDate is null || date <= EndDate);

    internal void Close(DateOnly endDate) => EndDate = endDate;
}
