using FluentValidation;
using HabitFlow.Domain.Time;

namespace HabitFlow.Application.Common;

/// <summary>The resource does not exist or does not belong to the caller (both map to 404 to avoid leaking existence).</summary>
public sealed class NotFoundException(string resource)
    : Exception($"{resource} no encontrado.")
{
    public string Resource { get; } = resource;
}

/// <summary>The request conflicts with the current state, e.g. a duplicate or a concurrent modification.</summary>
public sealed class ConflictException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;

    public static ConflictException Concurrency() => new("concurrency_conflict",
        "Otro cambio se guardó mientras editabas. Recarga los datos e inténtalo de nuevo.");
}

/// <summary>Invalid credentials or session. Always reported with the same generic message to avoid user enumeration.</summary>
public sealed class AuthenticationFailedException(string message = "Credenciales no válidas.") : Exception(message);

/// <summary>A feature that depends on external configuration (e.g. SMTP) is not available in this deployment.</summary>
public sealed class FeatureUnavailableException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

/// <summary>An inclusive range of local dates, validated by <see cref="DateRangeQueryValidator"/>.</summary>
public class DateRangeQuery
{
    public DateOnly? From { get; init; }
    public DateOnly? To { get; init; }

    /// <summary>Alternative to <see cref="From"/>: the last N days ending at <see cref="To"/> (default: the user's today).</summary>
    public int? Days { get; init; }
}

public static class DateRanges
{
    public const int MaxDays = 366;

    /// <summary>Resolves an optional range against today, defaulting to the last <paramref name="defaultDays"/> days.</summary>
    public static (DateOnly From, DateOnly To) Resolve(DateRangeQuery query, DateOnly today, int defaultDays)
    {
        var to = query.To ?? today;
        var from = query.From ?? to.AddDays(-((query.Days ?? defaultDays) - 1));
        return (from, to);
    }

    public static IEnumerable<DateOnly> Days(DateOnly from, DateOnly to)
    {
        for (var day = from; day <= to; day = day.AddDays(1))
        {
            yield return day;
        }
    }
}

public sealed class DateRangeQueryValidator : AbstractValidator<DateRangeQuery>
{
    public DateRangeQueryValidator()
    {
        RuleFor(q => q.Days).InclusiveBetween(1, DateRanges.MaxDays).When(q => q.Days is not null);
        RuleFor(q => q.To)
            .GreaterThanOrEqualTo(q => q.From)
            .When(q => q.From is not null && q.To is not null)
            .WithMessage("La fecha final no puede ser anterior a la inicial.");
        RuleFor(q => q)
            .Must(q => q.From is null || q.To is null || q.To.Value.DayNumber - q.From.Value.DayNumber < DateRanges.MaxDays)
            .WithName("to")
            .WithMessage($"El rango no puede superar {DateRanges.MaxDays} días.");
    }
}

public static class Percent
{
    /// <summary>Ratio (0–1) → percentage rounded to one decimal, or null when there is nothing to measure.</summary>
    public static double? Of(double? ratio) => ratio is null ? null : Math.Round(ratio.Value * 100, 1);

    public static double? Of(int numerator, int denominator) =>
        denominator == 0 ? null : Math.Round(numerator * 100.0 / denominator, 1);
}

public static class ValidationExtensions
{
    public static IRuleBuilderOptions<T, string?> IanaTimeZone<T>(this IRuleBuilder<T, string?> rule) =>
        rule
            .Must(value => TimeZoneIds.TryResolve(value, out _))
            .WithMessage("Zona horaria desconocida. Usa un identificador IANA, por ejemplo \"Europe/Madrid\".");

    public static void ApplyPaging<T>(this AbstractValidator<T> validator)
        where T : PagedQuery
    {
        validator.RuleFor(q => q.Page).GreaterThanOrEqualTo(1);
        validator.RuleFor(q => q.PageSize).InclusiveBetween(1, PagedQuery.MaxPageSize);
    }

    /// <summary>Positive, at most <paramref name="max"/>, with at most two decimals.</summary>
    public static IRuleBuilderOptions<T, decimal> Amount<T>(this IRuleBuilder<T, decimal> rule, decimal max) =>
        rule
            .GreaterThan(0).WithMessage("Debe ser mayor que 0.")
            .LessThanOrEqualTo(max).WithMessage($"No puede superar {max:N0}.")
            .Must(v => decimal.Round(v, 2) == v).WithMessage("Admite como máximo 2 decimales.");
}
