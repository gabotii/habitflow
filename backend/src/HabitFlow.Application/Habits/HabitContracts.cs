using FluentValidation;
using HabitFlow.Application.Common;
using HabitFlow.Application.Tracking;
using HabitFlow.Domain.Habits;
using HabitFlow.Domain.Time;

namespace HabitFlow.Application.Habits;

public sealed record ScheduleInput(
    ScheduleKind Kind,
    IReadOnlyList<DayOfWeek>? Weekdays = null,
    int? TimesPerWeek = null,
    int? IntervalDays = null,
    decimal? TargetValue = null)
{
    public ScheduleSpec ToSpec(HabitType type) => ScheduleSpec.Create(
        type,
        Kind,
        Weekdays is { Count: > 0 } days ? Domain.Time.Weekdays.FromDays(days) : null,
        TimesPerWeek,
        IntervalDays,
        TargetValue);
}

public sealed record CreateHabitRequest(
    string Name,
    string? Description,
    Guid CategoryId,
    HabitColor Color,
    string Icon,
    HabitType Type,
    string? Unit,
    DateOnly? StartDate,
    string? TimeZoneId,
    ScheduleInput Schedule);

/// <summary>The type cannot change after creation. <see cref="Version"/> must match the stored one (optimistic lock).</summary>
public sealed record UpdateHabitRequest(
    string Name,
    string? Description,
    Guid CategoryId,
    HabitColor Color,
    string Icon,
    string? Unit,
    DateOnly StartDate,
    string? TimeZoneId,
    ScheduleInput Schedule,
    uint Version);

public sealed class HabitListQuery : PagedQuery
{
    /// <summary>active (default), archived or all.</summary>
    public string Status { get; init; } = "active";

    public Guid? CategoryId { get; init; }
    public string? Search { get; init; }
}

public sealed record CategoryRefDto(Guid Id, string Name);

public sealed record ScheduleVersionDto(DateOnly EffectiveFrom, ScheduleDto Schedule);

public sealed record PauseDto(DateOnly Start, DateOnly? End);

public sealed record HabitListItemDto(
    Guid Id,
    string Name,
    string? Description,
    CategoryRefDto Category,
    HabitColor Color,
    string Icon,
    HabitType Type,
    string? Unit,
    ScheduleDto Schedule,
    bool IsArchived,
    HabitDayDto Today,
    StreakBriefDto Streak,
    WeekProgressDto? Week);

public sealed record HabitDto(
    Guid Id,
    string Name,
    string? Description,
    CategoryRefDto Category,
    HabitColor Color,
    string Icon,
    HabitType Type,
    string? Unit,
    DateOnly StartDate,
    string? TimeZoneId,
    string EffectiveTimeZoneId,
    bool IsArchived,
    DateTimeOffset? ArchivedAt,
    ScheduleDto Schedule,
    IReadOnlyList<ScheduleVersionDto> ScheduleHistory,
    IReadOnlyList<PauseDto> Pauses,
    bool HasLogs,
    HabitDayDto Today,
    StreakDto Streak,
    uint Version,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed class ScheduleInputValidator : AbstractValidator<ScheduleInput>
{
    public ScheduleInputValidator()
    {
        RuleFor(s => s.Kind).IsInEnum();
        RuleFor(s => s.Weekdays)
            .NotEmpty().WithMessage("Elige al menos un día de la semana.")
            .Must(d => d!.All(Enum.IsDefined)).WithMessage("Día de la semana no válido.")
            .When(s => s.Kind == ScheduleKind.Weekdays);
        RuleFor(s => s.TimesPerWeek)
            .NotNull().InclusiveBetween(1, 7).WithMessage("Las veces por semana deben estar entre 1 y 7.")
            .When(s => s.Kind == ScheduleKind.TimesPerWeek);
        RuleFor(s => s.IntervalDays)
            .NotNull().InclusiveBetween(2, 365).WithMessage("El intervalo debe estar entre 2 y 365 días.")
            .When(s => s.Kind == ScheduleKind.EveryNDays);
        RuleFor(s => s.TargetValue!.Value)
            .Amount(ScheduleSpec.MaxTarget)
            .OverridePropertyName("targetValue")
            .When(s => s.TargetValue is not null);
    }
}

internal static class HabitRules
{
    public static void ApplyCommon<T>(
        AbstractValidator<T> v,
        Func<T, string> name,
        Func<T, string?> description,
        Func<T, HabitColor> color,
        Func<T, string> icon,
        Func<T, string?> timeZone,
        Func<T, ScheduleInput> schedule)
    {
        v.RuleFor(r => name(r)).NotEmpty().MaximumLength(Habit.NameMaxLength).OverridePropertyName("name");
        v.RuleFor(r => description(r)).MaximumLength(Habit.DescriptionMaxLength).OverridePropertyName("description");
        v.RuleFor(r => color(r)).IsInEnum().OverridePropertyName("color");
        v.RuleFor(r => icon(r)).Must(HabitIcons.All.Contains).WithMessage("Icono no válido.").OverridePropertyName("icon");
        v.RuleFor(r => timeZone(r)).IanaTimeZone().When(r => !string.IsNullOrWhiteSpace(timeZone(r)))
            .OverridePropertyName("timeZoneId");
        v.RuleFor(r => schedule(r)).NotNull().SetValidator(new ScheduleInputValidator()).OverridePropertyName("schedule");
    }
}

public sealed class CreateHabitRequestValidator : AbstractValidator<CreateHabitRequest>
{
    public CreateHabitRequestValidator()
    {
        HabitRules.ApplyCommon(this, r => r.Name, r => r.Description, r => r.Color, r => r.Icon, r => r.TimeZoneId,
            r => r.Schedule);
        RuleFor(r => r.Type).IsInEnum();
        RuleFor(r => r.Unit).NotEmpty().WithMessage("Indica la unidad de medida (por ejemplo, minutos o páginas).")
            .MaximumLength(Habit.UnitMaxLength).When(r => r.Type == HabitType.Quantity);
        RuleFor(r => r.Schedule.TargetValue).Null()
            .WithMessage("Los hábitos de sí/no no tienen meta diaria numérica.")
            .OverridePropertyName("schedule.targetValue")
            .When(r => r.Type == HabitType.Binary && r.Schedule is not null);
    }
}

public sealed class UpdateHabitRequestValidator : AbstractValidator<UpdateHabitRequest>
{
    public UpdateHabitRequestValidator()
    {
        HabitRules.ApplyCommon(this, r => r.Name, r => r.Description, r => r.Color, r => r.Icon, r => r.TimeZoneId,
            r => r.Schedule);
        RuleFor(r => r.Unit).MaximumLength(Habit.UnitMaxLength);
    }
}

public sealed class HabitListQueryValidator : AbstractValidator<HabitListQuery>
{
    public HabitListQueryValidator()
    {
        this.ApplyPaging();
        RuleFor(q => q.Status).Must(s => s is "active" or "archived" or "all")
            .WithMessage("Valores permitidos: active, archived, all.");
        RuleFor(q => q.Search).MaximumLength(80);
    }
}
