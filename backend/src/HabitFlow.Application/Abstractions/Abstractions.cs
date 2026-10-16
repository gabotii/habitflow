using HabitFlow.Domain.Goals;
using HabitFlow.Domain.Habits;
using HabitFlow.Domain.Notifications;
using HabitFlow.Domain.Reminders;
using HabitFlow.Domain.Social;
using Microsoft.EntityFrameworkCore;

namespace HabitFlow.Application.Abstractions;

/// <summary>The authenticated caller. <see cref="Guid.Empty"/> when anonymous.</summary>
public interface ICurrentUser
{
    Guid UserId { get; }
}

/// <summary>Current time from the perspective of the authenticated user (their profile time zone).</summary>
public interface IUserClock
{
    DateTimeOffset UtcNow { get; }

    Task<TimeZoneInfo> GetTimeZoneAsync(CancellationToken cancellationToken = default);

    /// <summary>Today's calendar date in the user's time zone.</summary>
    Task<DateOnly> GetTodayAsync(CancellationToken cancellationToken = default);
}

public sealed record UserSnapshot(
    Guid Id,
    string Email,
    string DisplayName,
    string TimeZoneId,
    bool EmailRemindersEnabled);

/// <summary>Read access to user accounts (owned by ASP.NET Core Identity in the infrastructure layer).</summary>
public interface IUserDirectory
{
    Task<UserSnapshot?> FindAsync(Guid userId, CancellationToken cancellationToken = default);

    Task<IReadOnlyDictionary<Guid, string>> GetDisplayNamesAsync(
        IReadOnlyCollection<Guid> userIds,
        CancellationToken cancellationToken = default);

    /// <summary>Never expose the result to other users: it would reveal which e-mails have an account.</summary>
    Task<Guid?> FindIdByEmailAsync(string email, CancellationToken cancellationToken = default);
}

/// <summary>
/// Persistence boundary used by the use cases. EF Core's DbSet already is a repository/unit of work, so we expose it
/// directly instead of wrapping it in pass-through repositories (docs/ARCHITECTURE.md ADR-01). Every user-owned set is
/// filtered to the current user's rows by global query filters (default deny when anonymous, ADR-02).
/// </summary>
public interface IAppDbContext
{
    DbSet<HabitCategory> Categories { get; }
    DbSet<Habit> Habits { get; }
    DbSet<HabitLog> HabitLogs { get; }
    DbSet<HabitLogChange> HabitLogChanges { get; }
    DbSet<Goal> Goals { get; }
    DbSet<GoalProgressEntry> GoalEntries { get; }
    DbSet<Reminder> Reminders { get; }
    DbSet<Notification> Notifications { get; }

    // Social features: shared between users, so they have no ownership filter; every query checks membership.
    DbSet<Challenge> Challenges { get; }
    DbSet<SharedGoal> SharedGoals { get; }
    DbSet<SharedGoalInvitation> SharedGoalInvitations { get; }
    DbSet<SharedGoalContribution> SharedGoalContributions { get; }

    /// <summary>
    /// Saves changes. Known unique-constraint violations surface as <see cref="Common.ConflictException"/> and
    /// optimistic-concurrency failures as a <c>concurrency_conflict</c> conflict.
    /// </summary>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
