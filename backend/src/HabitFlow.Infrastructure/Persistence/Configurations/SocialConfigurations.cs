using HabitFlow.Domain.Goals;
using HabitFlow.Domain.Habits;
using HabitFlow.Domain.Social;
using HabitFlow.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HabitFlow.Infrastructure.Persistence.Configurations;

internal sealed class ChallengeConfiguration : IEntityTypeConfiguration<Challenge>
{
    public void Configure(EntityTypeBuilder<Challenge> builder)
    {
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).ValueGeneratedNever();
        builder.Property(c => c.Name).HasMaxLength(Challenge.NameMaxLength).IsRequired();
        builder.Property(c => c.Description).HasMaxLength(Challenge.DescriptionMaxLength);
        builder.Property(c => c.Rule).AsText();
        builder.Property(c => c.TargetValue).HasColumnType(MappingExtensions.BigAmount);
        builder.Property(c => c.Unit).HasMaxLength(Habit.UnitMaxLength);
        builder.Property(c => c.JoinCode).HasMaxLength(8).IsFixedLength().IsRequired();
        builder.Ignore(c => c.Days);

        // The challenge survives its creator's account deletion for the other participants.
        builder.HasOne<AppUser>().WithMany().HasForeignKey(c => c.CreatorId).OnDelete(DeleteBehavior.SetNull);
        builder.HasMany(c => c.Participants).WithOne().HasForeignKey(p => p.ChallengeId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(c => c.Participants).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasIndex(c => c.JoinCode).IsUnique();
        builder.HasIndex(c => c.CreatorId);
        builder.ToTable(t =>
        {
            t.HasCheckConstraint("CK_Challenges_Dates", "\"EndDate\" >= \"StartDate\"");
            t.HasCheckConstraint("CK_Challenges_Target", "\"TargetValue\" > 0");
        });
    }
}

internal sealed class ChallengeParticipantConfiguration : IEntityTypeConfiguration<ChallengeParticipant>
{
    public void Configure(EntityTypeBuilder<ChallengeParticipant> builder)
    {
        builder.ToTable("ChallengeParticipants");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).ValueGeneratedNever();
        builder.HasOne<AppUser>().WithMany().HasForeignKey(p => p.UserId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Habit>().WithMany().HasForeignKey(p => p.HabitId).OnDelete(DeleteBehavior.SetNull);
        builder.HasIndex(p => new { p.ChallengeId, p.UserId }).IsUnique();
        builder.HasIndex(p => p.UserId);
    }
}

internal sealed class SharedGoalConfiguration : IEntityTypeConfiguration<SharedGoal>
{
    public void Configure(EntityTypeBuilder<SharedGoal> builder)
    {
        builder.HasKey(g => g.Id);
        builder.Property(g => g.Id).ValueGeneratedNever();
        builder.Property(g => g.Name).HasMaxLength(Goal.NameMaxLength).IsRequired();
        builder.Property(g => g.Description).HasMaxLength(Goal.DescriptionMaxLength);
        builder.Property(g => g.Unit).HasMaxLength(Goal.UnitMaxLength).IsRequired();
        builder.Property(g => g.CollectiveTarget).HasColumnType(MappingExtensions.BigAmount);
        builder.Property(g => g.IndividualTarget).HasColumnType(MappingExtensions.BigAmount);
        builder.Property(g => g.Status).AsText();
        builder.Property(g => g.Version).IsRowVersion();
        builder.HasMany(g => g.Participants).WithOne().HasForeignKey(p => p.SharedGoalId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(g => g.Participants).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.ToTable(t =>
        {
            t.HasCheckConstraint("CK_SharedGoals_Target", "\"CollectiveTarget\" > 0");
            t.HasCheckConstraint("CK_SharedGoals_Deadline", "\"Deadline\" IS NULL OR \"Deadline\" >= \"StartDate\"");
        });
    }
}

internal sealed class SharedGoalParticipantConfiguration : IEntityTypeConfiguration<SharedGoalParticipant>
{
    public void Configure(EntityTypeBuilder<SharedGoalParticipant> builder)
    {
        builder.ToTable("SharedGoalParticipants");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).ValueGeneratedNever();
        builder.Property(p => p.Role).AsText();
        builder.HasOne<AppUser>().WithMany().HasForeignKey(p => p.UserId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(p => new { p.SharedGoalId, p.UserId }).IsUnique();
        builder.HasIndex(p => p.UserId);
    }
}

internal sealed class SharedGoalInvitationConfiguration : IEntityTypeConfiguration<SharedGoalInvitation>
{
    public void Configure(EntityTypeBuilder<SharedGoalInvitation> builder)
    {
        builder.HasKey(i => i.Id);
        builder.Property(i => i.Id).ValueGeneratedNever();
        builder.Property(i => i.NormalizedEmail).HasMaxLength(256).IsRequired();
        builder.Property(i => i.Status).AsText();
        builder.HasOne<SharedGoal>().WithMany().HasForeignKey(i => i.SharedGoalId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<AppUser>().WithMany().HasForeignKey(i => i.InvitedByUserId).OnDelete(DeleteBehavior.Cascade);

        // One pending invitation per goal and e-mail.
        builder.HasIndex(i => new { i.SharedGoalId, i.NormalizedEmail }).IsUnique().HasFilter("\"Status\" = 'Pending'");
        builder.HasIndex(i => i.NormalizedEmail);
    }
}

internal sealed class SharedGoalContributionConfiguration : IEntityTypeConfiguration<SharedGoalContribution>
{
    public void Configure(EntityTypeBuilder<SharedGoalContribution> builder)
    {
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).ValueGeneratedNever();
        builder.Property(c => c.Amount).HasColumnType(MappingExtensions.Quantity);
        builder.Property(c => c.Note).HasMaxLength(SharedGoalContribution.NoteMaxLength);
        builder.HasOne<SharedGoal>().WithMany().HasForeignKey(c => c.SharedGoalId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<AppUser>().WithMany().HasForeignKey(c => c.UserId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(c => new { c.SharedGoalId, c.UserId });
        builder.ToTable(t => t.HasCheckConstraint("CK_SharedGoalContributions_Amount", "\"Amount\" > 0"));
    }
}
