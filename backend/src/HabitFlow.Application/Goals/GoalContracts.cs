using FluentValidation;
using HabitFlow.Application.Common;
using HabitFlow.Domain.Goals;
using HabitFlow.Domain.Habits;

namespace HabitFlow.Application.Goals;

public sealed record CreateGoalRequest(
    string Name,
    string? Description,
    GoalMetric Metric,
    decimal TargetValue,
    string? Unit,
    DateOnly? StartDate,
    DateOnly? Deadline,
    IReadOnlyList<Guid>? HabitIds);

/// <summary>The metric and linked habits are fixed after creation; <see cref="Version"/> enables optimistic locking.</summary>
public sealed record UpdateGoalRequest(
    string Name,
    string? Description,
    decimal TargetValue,
    string? Unit,
    DateOnly? Deadline,
    uint Version);

public sealed record ChangeGoalStatusRequest(GoalStatus Status);

public sealed record AddGoalEntryRequest(DateOnly? Date, decimal Amount, string? Note);

public sealed class GoalListQuery
{
    /// <summary>open (active + paused, default), active, paused, completed, cancelled or all.</summary>
    public string Status { get; init; } = "open";
}

public sealed record GoalHabitDto(Guid Id, string Name, string Icon, HabitColor Color, string? Unit, bool IsArchived);

public sealed record GoalProgressDto(decimal Current, decimal Target, decimal Percent, decimal ExceededBy, bool TargetReached);

public sealed record GoalDto(
    Guid Id,
    string Name,
    string? Description,
    GoalMetric Metric,
    decimal TargetValue,
    string Unit,
    DateOnly StartDate,
    DateOnly? Deadline,
    GoalStatus Status,
    DateTimeOffset? CompletedAt,
    IReadOnlyList<GoalHabitDto> Habits,
    GoalProgressDto Progress,
    bool IsOverdue,
    int? DaysLeft,
    uint Version,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record GoalEntryDto(Guid Id, DateOnly Date, decimal Amount, string? Note, DateTimeOffset CreatedAt);

public sealed record GoalHistoryPointDto(DateOnly Date, decimal Value);

public sealed class CreateGoalRequestValidator : AbstractValidator<CreateGoalRequest>
{
    public CreateGoalRequestValidator()
    {
        RuleFor(r => r.Name).NotEmpty().MaximumLength(Goal.NameMaxLength);
        RuleFor(r => r.Description).MaximumLength(Goal.DescriptionMaxLength);
        RuleFor(r => r.Metric).IsInEnum();
        RuleFor(r => r.TargetValue).Amount(Goal.MaxTarget);
        RuleFor(r => r.Unit).NotEmpty().WithMessage("Indica la unidad (por ejemplo, libros u horas).")
            .MaximumLength(Goal.UnitMaxLength)
            .When(r => r.Metric == GoalMetric.Manual);
        RuleFor(r => r.TargetValue).Must(v => v == decimal.Truncate(v))
            .WithMessage("Esta meta se mide en unidades enteras.")
            .When(r => r.Metric is GoalMetric.HabitCompletions or GoalMetric.HabitStreak);
        RuleFor(r => r.Deadline).GreaterThanOrEqualTo(r => r.StartDate)
            .When(r => r.Deadline is not null && r.StartDate is not null)
            .WithMessage("La fecha límite no puede ser anterior al inicio.");
        RuleFor(r => r.HabitIds).NotEmpty().WithMessage("Vincula al menos un hábito.")
            .When(r => r.Metric != GoalMetric.Manual);
        RuleFor(r => r.HabitIds!.Count).LessThanOrEqualTo(Goal.MaxLinkedHabits).OverridePropertyName("habitIds")
            .When(r => r.HabitIds is not null);
    }
}

public sealed class UpdateGoalRequestValidator : AbstractValidator<UpdateGoalRequest>
{
    public UpdateGoalRequestValidator()
    {
        RuleFor(r => r.Name).NotEmpty().MaximumLength(Goal.NameMaxLength);
        RuleFor(r => r.Description).MaximumLength(Goal.DescriptionMaxLength);
        RuleFor(r => r.TargetValue).Amount(Goal.MaxTarget);
        RuleFor(r => r.Unit).MaximumLength(Goal.UnitMaxLength);
    }
}

public sealed class ChangeGoalStatusRequestValidator : AbstractValidator<ChangeGoalStatusRequest>
{
    public ChangeGoalStatusRequestValidator() => RuleFor(r => r.Status).IsInEnum();
}

public sealed class AddGoalEntryRequestValidator : AbstractValidator<AddGoalEntryRequest>
{
    public AddGoalEntryRequestValidator()
    {
        RuleFor(r => r.Amount).Amount(GoalProgressEntry.MaxAmount);
        RuleFor(r => r.Note).MaximumLength(GoalProgressEntry.NoteMaxLength);
    }
}

public sealed class GoalListQueryValidator : AbstractValidator<GoalListQuery>
{
    public GoalListQueryValidator() =>
        RuleFor(q => q.Status).Must(s => s is "open" or "active" or "paused" or "completed" or "cancelled" or "all")
            .WithMessage("Valores permitidos: open, active, paused, completed, cancelled, all.");
}
