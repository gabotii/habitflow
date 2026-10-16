using HabitFlow.Application.Abstractions;
using HabitFlow.Application.Common;
using HabitFlow.Domain.Common;
using HabitFlow.Domain.Goals;
using HabitFlow.Domain.Habits;
using HabitFlow.Domain.Notifications;
using HabitFlow.Domain.Reminders;
using HabitFlow.Domain.Social;
using HabitFlow.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace HabitFlow.Infrastructure.Persistence;

public sealed class AppDbContext(
    DbContextOptions<AppDbContext> options,
    ICurrentUser currentUser,
    TimeProvider timeProvider)
    : IdentityDbContext<AppUser, IdentityRole<Guid>, Guid>(options), IAppDbContext
{
    /// <summary>Unique index names mapped to user-facing conflicts (see <see cref="SaveChangesAsync(CancellationToken)"/>).</summary>
    private static readonly Dictionary<string, (string Code, string Message)> UniqueConflicts = new()
    {
        ["IX_HabitLogs_HabitId_LocalDate"] =
            ("log.concurrent_write", "Este día se registró a la vez desde otra pestaña o dispositivo. Vuelve a intentarlo."),
        ["IX_Categories_UserId_NormalizedName"] =
            ("category.duplicate_name", "Ya existe una categoría con ese nombre."),
        ["IX_HabitSchedules_HabitId_EffectiveFrom"] =
            ("concurrency_conflict", "Otro cambio se guardó a la vez. Recarga los datos e inténtalo de nuevo."),
        ["IX_ChallengeParticipants_ChallengeId_UserId"] =
            ("challenge.already_joined", "Ya participas (o participaste) en este reto."),
        ["IX_SharedGoalInvitations_SharedGoalId_NormalizedEmail"] =
            ("invitation.duplicate", "Ya hay una invitación pendiente para ese correo."),
    };

    public DbSet<HabitCategory> Categories => Set<HabitCategory>();
    public DbSet<Habit> Habits => Set<Habit>();
    public DbSet<HabitLog> HabitLogs => Set<HabitLog>();
    public DbSet<HabitLogChange> HabitLogChanges => Set<HabitLogChange>();
    public DbSet<Goal> Goals => Set<Goal>();
    public DbSet<GoalProgressEntry> GoalEntries => Set<GoalProgressEntry>();
    public DbSet<Reminder> Reminders => Set<Reminder>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<Challenge> Challenges => Set<Challenge>();
    public DbSet<SharedGoal> SharedGoals => Set<SharedGoal>();
    public DbSet<SharedGoalInvitation> SharedGoalInvitations => Set<SharedGoalInvitation>();
    public DbSet<SharedGoalContribution> SharedGoalContributions => Set<SharedGoalContribution>();

    /// <summary>
    /// Evaluated per query by EF Core's global filters. Anonymous contexts (and background jobs) get
    /// <see cref="Guid.Empty"/>, so every owned set is empty unless the query explicitly opts out (default deny).
    /// </summary>
    private Guid CurrentUserId => currentUser.UserId;

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        StampTimestamps();
        try
        {
            return await base.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw ConflictException.Concurrency();
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException
                                           {
                                               SqlState: PostgresErrorCodes.UniqueViolation,
                                           } pg &&
                                           pg.ConstraintName is not null &&
                                           UniqueConflicts.TryGetValue(pg.ConstraintName, out var conflict))
        {
            throw new ConflictException(conflict.Code, conflict.Message);
        }
    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);

        // Row-level ownership (ADR-02). Child rows of an aggregate (schedules, pauses, goal links) are only ever
        // loaded through their filtered root.
        builder.Entity<HabitCategory>().HasQueryFilter(c => c.UserId == null || c.UserId == CurrentUserId);
        builder.Entity<Habit>().HasQueryFilter(e => e.UserId == CurrentUserId);
        builder.Entity<HabitLog>().HasQueryFilter(e => e.UserId == CurrentUserId);
        builder.Entity<HabitLogChange>().HasQueryFilter(e => e.UserId == CurrentUserId);
        builder.Entity<Goal>().HasQueryFilter(e => e.UserId == CurrentUserId);
        builder.Entity<GoalProgressEntry>().HasQueryFilter(e => e.UserId == CurrentUserId);
        builder.Entity<Reminder>().HasQueryFilter(e => e.UserId == CurrentUserId);
        builder.Entity<Notification>().HasQueryFilter(e => e.UserId == CurrentUserId);
    }

    private void StampTimestamps()
    {
        var now = timeProvider.GetUtcNow();
        foreach (var entry in ChangeTracker.Entries<Entity>())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    entry.Entity.CreatedAt = now;
                    entry.Entity.UpdatedAt = now;
                    break;
                case EntityState.Modified:
                    entry.Entity.UpdatedAt = now;
                    break;
            }
        }

        foreach (var entry in ChangeTracker.Entries<AppUser>().Where(e => e.State == EntityState.Added))
        {
            entry.Entity.CreatedAt = now;
        }
    }
}
