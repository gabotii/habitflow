namespace HabitFlow.Domain.Time;

public static class TimeZoneIds
{
    public const string Default = "Europe/Madrid";

    /// <summary>
    /// Resolves an IANA id (<c>Europe/Madrid</c>, <c>America/Bogota</c>, <c>UTC</c>). On Windows .NET maps IANA ids
    /// through ICU; on Linux it reads tzdata. Windows-style ids are rejected so stored values are portable.
    /// </summary>
    public static bool TryResolve(string? id, out TimeZoneInfo zone)
    {
        zone = TimeZoneInfo.Utc;
        if (string.IsNullOrWhiteSpace(id) || id.Length > 64)
        {
            return false;
        }

        if (id is "UTC" or "Etc/UTC")
        {
            return true;
        }

        if (!TimeZoneInfo.TryFindSystemTimeZoneById(id, out var found) || !found.HasIanaId)
        {
            return false;
        }

        zone = found;
        return true;
    }

    public static TimeZoneInfo Resolve(string? id) =>
        TryResolve(id, out var zone) ? zone : throw new ArgumentException($"Unknown time zone '{id}'.", nameof(id));
}
