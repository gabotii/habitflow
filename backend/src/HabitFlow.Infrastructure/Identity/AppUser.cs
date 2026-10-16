using HabitFlow.Domain.Time;
using Microsoft.AspNetCore.Identity;

namespace HabitFlow.Infrastructure.Identity;

/// <summary>Application user. Password hashing, lockout and security stamps are handled by ASP.NET Core Identity.</summary>
public sealed class AppUser : IdentityUser<Guid>
{
    public const int DisplayNameMaxLength = 80;

    public string DisplayName { get; set; } = string.Empty;

    /// <summary>IANA zone used to compute "today", weekly statistics and default reminder zones.</summary>
    public string TimeZoneId { get; set; } = TimeZoneIds.Default;

    /// <summary>Consent for reminder e-mails (only used when SMTP is configured).</summary>
    public bool EmailRemindersEnabled { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
