using HabitFlow.Domain.Common;
using HabitFlow.Domain.Goals;
using HabitFlow.Domain.Habits;
using HabitFlow.Domain.Notifications;
using HabitFlow.Domain.Reminders;
using HabitFlow.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HabitFlow.Infrastructure.Persistence.Configurations;

internal static class MappingExtensions
{
    public const string Quantity = "numeric(12,2)";
    public const string BigAmount = "numeric(14,2)";

    /// <summary>
    /// Common mapping for user-owned entities: app-generated UUIDv7 key, FK to the user (deleting the account
    /// deletes all of its data) and timestamps.
    /// </summary>
    public static void ConfigureOwned<T>(this EntityTypeBuilder<T> builder)
        where T : OwnedEntity
    {
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.HasOne<AppUser>().WithMany().HasForeignKey(e => e.UserId).OnDelete(DeleteBehavior.Cascade);
        builder.Property(e => e.CreatedAt).IsRequired();
        builder.Property(e => e.UpdatedAt).IsRequired();
    }

    /// <summary>Enums are stored as text: readable in SQL and stable if members are reordered.</summary>
    public static PropertyBuilder<TEnum> AsText<TEnum>(this PropertyBuilder<TEnum> property)
        where TEnum : struct, Enum => property.HasConversion<string>().HasMaxLength(32);
}

internal sealed class AppUserConfiguration : IEntityTypeConfiguration<AppUser>
{
    public void Configure(EntityTypeBuilder<AppUser> builder)
    {
        builder.Property(u => u.DisplayName).HasMaxLength(AppUser.DisplayNameMaxLength).IsRequired();
        builder.Property(u => u.TimeZoneId).HasMaxLength(64).IsRequired();
    }
}

internal sealed class HabitCategoryConfiguration : IEntityTypeConfiguration<HabitCategory>
{
    public void Configure(EntityTypeBuilder<HabitCategory> builder)
    {
        builder.ToTable("Categories");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).ValueGeneratedNever();
        builder.HasOne<AppUser>().WithMany().HasForeignKey(c => c.UserId).OnDelete(DeleteBehavior.Cascade);
        builder.Property(c => c.Name).HasMaxLength(HabitCategory.NameMaxLength).IsRequired();
        builder.Property(c => c.NormalizedName).HasMaxLength(HabitCategory.NameMaxLength).IsRequired();
        builder.Property(c => c.Color).AsText();
        builder.Property(c => c.Icon).HasMaxLength(32).IsRequired();
        builder.Ignore(c => c.IsBuiltIn);

        // NULLS NOT DISTINCT: built-in names are unique too, and a user cannot shadow them.
        builder.HasIndex(c => new { c.UserId, c.NormalizedName }).IsUnique().AreNullsDistinct(false);
        builder.HasData(BuiltInCategories.All);
    }
}

internal sealed class HabitConfiguration : IEntityTypeConfiguration<Habit>
{
    public void Configure(EntityTypeBuilder<Habit> builder)
    {
        builder.ConfigureOwned();
        builder.Property(h => h.Name).HasMaxLength(Habit.NameMaxLength).IsRequired();
        builder.Property(h => h.Description).HasMaxLength(Habit.DescriptionMaxLength);
        builder.Property(h => h.Color).AsText();
        builder.Property(h => h.Icon).HasMaxLength(32).IsRequired();
        builder.Property(h => h.Type).AsText();
        builder.Property(h => h.Unit).HasMaxLength(Habit.UnitMaxLength);
        builder.Property(h => h.TimeZoneId).HasMaxLength(64);
        builder.Property(h => h.Version).IsRowVersion();
        builder.Ignore(h => h.IsArchived);

        builder.HasOne<HabitCategory>().WithMany().HasForeignKey(h => h.CategoryId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(h => h.Schedules).WithOne().HasForeignKey(s => s.HabitId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(h => h.Pauses).WithOne().HasForeignKey(p => p.HabitId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(h => h.Schedules).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Navigation(h => h.Pauses).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasIndex(h => new { h.UserId, h.ArchivedAt });
        builder.HasIndex(h => h.CategoryId);
        builder.ToTable(t => t.HasCheckConstraint("CK_Habits_Unit",
            "(\"Type\" = 'Quantity' AND \"Unit\" IS NOT NULL) OR (\"Type\" = 'Binary' AND \"Unit\" IS NULL)"));
    }
}

internal sealed class HabitScheduleConfiguration : IEntityTypeConfiguration<HabitSchedule>
{
    public void Configure(EntityTypeBuilder<HabitSchedule> builder)
    {
        builder.ToTable("HabitSchedules", t =>
        {
            t.HasCheckConstraint("CK_HabitSchedules_Params",
                "(\"Kind\" = 'Daily' AND \"WeekdaysMask\" IS NULL AND \"TimesPerWeek\" IS NULL AND \"IntervalDays\" IS NULL) OR " +
                "(\"Kind\" = 'Weekdays' AND \"WeekdaysMask\" BETWEEN 1 AND 127 AND \"TimesPerWeek\" IS NULL AND \"IntervalDays\" IS NULL) OR " +
                "(\"Kind\" = 'TimesPerWeek' AND \"TimesPerWeek\" BETWEEN 1 AND 7 AND \"WeekdaysMask\" IS NULL AND \"IntervalDays\" IS NULL) OR " +
                "(\"Kind\" = 'EveryNDays' AND \"IntervalDays\" BETWEEN 2 AND 365 AND \"WeekdaysMask\" IS NULL AND \"TimesPerWeek\" IS NULL)");
            t.HasCheckConstraint("CK_HabitSchedules_Target", "\"TargetValue\" IS NULL OR \"TargetValue\" > 0");
        });
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).ValueGeneratedNever();
        builder.Property(s => s.Kind).AsText();
        builder.Property(s => s.WeekdaysMask).HasColumnType("smallint");
        builder.Property(s => s.TimesPerWeek).HasColumnType("smallint");
        builder.Property(s => s.IntervalDays).HasColumnType("smallint");
        builder.Property(s => s.TargetValue).HasColumnType(MappingExtensions.Quantity);
        builder.Ignore(s => s.Spec);
        builder.HasIndex(s => new { s.HabitId, s.EffectiveFrom }).IsUnique();
    }
}

internal sealed class HabitPauseConfiguration : IEntityTypeConfiguration<HabitPause>
{
    public void Configure(EntityTypeBuilder<HabitPause> builder)
    {
        builder.ToTable("HabitPauses", t =>
            t.HasCheckConstraint("CK_HabitPauses_Range", "\"EndDate\" IS NULL OR \"EndDate\" >= \"StartDate\""));
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).ValueGeneratedNever();
        builder.HasIndex(p => p.HabitId);
    }
}

internal sealed class HabitLogConfiguration : IEntityTypeConfiguration<HabitLog>
{
    public void Configure(EntityTypeBuilder<HabitLog> builder)
    {
        builder.ConfigureOwned();
        builder.Property(l => l.Quantity).HasColumnType(MappingExtensions.Quantity);
        builder.Property(l => l.Note).HasMaxLength(HabitLog.NoteMaxLength);
        builder.HasOne<Habit>().WithMany().HasForeignKey(l => l.HabitId).OnDelete(DeleteBehavior.Cascade);

        // One effective log per habit and local day: the core anti-duplication rule.
        builder.HasIndex(l => new { l.HabitId, l.LocalDate }).IsUnique();
        builder.HasIndex(l => new { l.UserId, l.LocalDate });
        builder.ToTable(t => t.HasCheckConstraint("CK_HabitLogs_Quantity", "\"Quantity\" IS NULL OR \"Quantity\" >= 0"));
    }
}

internal sealed class HabitLogChangeConfiguration : IEntityTypeConfiguration<HabitLogChange>
{
    public void Configure(EntityTypeBuilder<HabitLogChange> builder)
    {
        builder.ConfigureOwned();
        builder.Property(c => c.Action).AsText();
        builder.Property(c => c.PreviousQuantity).HasColumnType(MappingExtensions.Quantity);
        builder.Property(c => c.NewQuantity).HasColumnType(MappingExtensions.Quantity);
        builder.HasOne<Habit>().WithMany().HasForeignKey(c => c.HabitId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(c => new { c.UserId, c.CreatedAt });
        builder.HasIndex(c => new { c.HabitId, c.CreatedAt });
    }
}

internal sealed class GoalConfiguration : IEntityTypeConfiguration<Goal>
{
    public void Configure(EntityTypeBuilder<Goal> builder)
    {
        builder.ConfigureOwned();
        builder.Property(g => g.Name).HasMaxLength(Goal.NameMaxLength).IsRequired();
        builder.Property(g => g.Description).HasMaxLength(Goal.DescriptionMaxLength);
        builder.Property(g => g.Metric).AsText();
        builder.Property(g => g.Status).AsText();
        builder.Property(g => g.Unit).HasMaxLength(Goal.UnitMaxLength).IsRequired();
        builder.Property(g => g.TargetValue).HasColumnType(MappingExtensions.BigAmount);
        builder.Property(g => g.Version).IsRowVersion();
        builder.Ignore(g => g.IsHabitBased);
        builder.HasMany(g => g.Habits).WithOne().HasForeignKey(gh => gh.GoalId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(g => g.Habits).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.HasIndex(g => new { g.UserId, g.Status });
        builder.ToTable(t =>
        {
            t.HasCheckConstraint("CK_Goals_Target", "\"TargetValue\" > 0");
            t.HasCheckConstraint("CK_Goals_Deadline", "\"Deadline\" IS NULL OR \"Deadline\" >= \"StartDate\"");
        });
    }
}

internal sealed class GoalHabitConfiguration : IEntityTypeConfiguration<GoalHabit>
{
    public void Configure(EntityTypeBuilder<GoalHabit> builder)
    {
        builder.ToTable("GoalHabits");
        builder.HasKey(gh => new { gh.GoalId, gh.HabitId });
        builder.HasOne<Habit>().WithMany().HasForeignKey(gh => gh.HabitId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(gh => gh.HabitId);
    }
}

internal sealed class GoalProgressEntryConfiguration : IEntityTypeConfiguration<GoalProgressEntry>
{
    public void Configure(EntityTypeBuilder<GoalProgressEntry> builder)
    {
        builder.ConfigureOwned();
        builder.ToTable("GoalEntries", t => t.HasCheckConstraint("CK_GoalEntries_Amount", "\"Amount\" > 0"));
        builder.Property(e => e.Amount).HasColumnType(MappingExtensions.Quantity);
        builder.Property(e => e.Note).HasMaxLength(GoalProgressEntry.NoteMaxLength);
        builder.HasOne<Goal>().WithMany().HasForeignKey(e => e.GoalId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(e => new { e.GoalId, e.LocalDate });
    }
}

internal sealed class ReminderConfiguration : IEntityTypeConfiguration<Reminder>
{
    public void Configure(EntityTypeBuilder<Reminder> builder)
    {
        builder.ConfigureOwned();
        builder.Property(r => r.Title).HasMaxLength(Reminder.TitleMaxLength).IsRequired();
        builder.Property(r => r.Description).HasMaxLength(Reminder.DescriptionMaxLength);
        builder.Property(r => r.DaysMask).HasColumnType("smallint");
        builder.Property(r => r.TimeZoneId).HasMaxLength(64).IsRequired();
        builder.Property(r => r.Channel).AsText();
        builder.HasOne<Habit>().WithMany().HasForeignKey(r => r.HabitId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Goal>().WithMany().HasForeignKey(r => r.GoalId).OnDelete(DeleteBehavior.Cascade);

        // The dispatcher's hot path: due active reminders.
        builder.HasIndex(r => r.NextFireAt).HasFilter("\"IsActive\"");
        builder.HasIndex(r => r.UserId);
        builder.ToTable(t =>
        {
            t.HasCheckConstraint("CK_Reminders_SingleTarget", "\"HabitId\" IS NULL OR \"GoalId\" IS NULL");
            t.HasCheckConstraint("CK_Reminders_Days", "\"DaysMask\" BETWEEN 1 AND 127");
        });
    }
}

internal sealed class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> builder)
    {
        builder.ConfigureOwned();
        builder.Property(n => n.Kind).AsText();
        builder.Property(n => n.Title).HasMaxLength(150).IsRequired();
        builder.Property(n => n.Body).HasMaxLength(500);
        builder.Property(n => n.Link).HasMaxLength(200);
        builder.Property(n => n.EmailStatus).AsText();
        builder.HasOne<Reminder>().WithMany().HasForeignKey(n => n.ReminderId).OnDelete(DeleteBehavior.SetNull);

        // Idempotent dispatch: one notification per reminder occurrence.
        builder.HasIndex(n => new { n.ReminderId, n.OccurrenceAt }).IsUnique();
        builder.HasIndex(n => new { n.UserId, n.CreatedAt });
        builder.HasIndex(n => n.EmailStatus).HasFilter("\"EmailStatus\" = 'Pending'");
    }
}
