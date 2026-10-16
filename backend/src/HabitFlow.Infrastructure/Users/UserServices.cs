using HabitFlow.Application.Abstractions;
using HabitFlow.Domain.Time;
using HabitFlow.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HabitFlow.Infrastructure.Users;

/// <summary>Resolves the caller's profile time zone once per request scope.</summary>
internal sealed class UserClock(AppDbContext db, ICurrentUser currentUser, TimeProvider timeProvider) : IUserClock
{
    private TimeZoneInfo? _zone;

    public DateTimeOffset UtcNow => timeProvider.GetUtcNow();

    public async Task<TimeZoneInfo> GetTimeZoneAsync(CancellationToken cancellationToken = default)
    {
        if (_zone is not null)
        {
            return _zone;
        }

        var zoneId = await db.Users
            .Where(u => u.Id == currentUser.UserId)
            .Select(u => u.TimeZoneId)
            .SingleOrDefaultAsync(cancellationToken);

        _zone = TimeZoneIds.TryResolve(zoneId, out var zone) ? zone : TimeZoneInfo.Utc;
        return _zone;
    }

    public async Task<DateOnly> GetTodayAsync(CancellationToken cancellationToken = default) =>
        ZonedTime.LocalDate(UtcNow, await GetTimeZoneAsync(cancellationToken));
}

internal sealed class UserDirectory(AppDbContext db) : IUserDirectory
{
    public async Task<UserSnapshot?> FindAsync(Guid userId, CancellationToken cancellationToken = default) =>
        await db.Users.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new UserSnapshot(u.Id, u.Email!, u.DisplayName, u.TimeZoneId, u.EmailRemindersEnabled))
            .SingleOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyDictionary<Guid, string>> GetDisplayNamesAsync(
        IReadOnlyCollection<Guid> userIds,
        CancellationToken cancellationToken = default) =>
        await db.Users.AsNoTracking()
            .Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.DisplayName, cancellationToken);

    public async Task<Guid?> FindIdByEmailAsync(string email, CancellationToken cancellationToken = default)
    {
        var normalized = email.Trim().ToUpperInvariant();
        return await db.Users.AsNoTracking()
            .Where(u => u.NormalizedEmail == normalized)
            .Select(u => (Guid?)u.Id)
            .SingleOrDefaultAsync(cancellationToken);
    }
}
