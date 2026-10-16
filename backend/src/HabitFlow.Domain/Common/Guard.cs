namespace HabitFlow.Domain.Common;

internal static class Guard
{
    public static string Required(string? value, int maxLength, string field)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            throw new DomainException("validation.required", $"El campo «{field}» es obligatorio.");
        }

        if (trimmed.Length > maxLength)
        {
            throw new DomainException("validation.too_long", $"El campo «{field}» admite como máximo {maxLength} caracteres.");
        }

        return trimmed;
    }

    public static string? Optional(string? value, int maxLength, string field)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return null;
        }

        return trimmed.Length > maxLength
            ? throw new DomainException("validation.too_long", $"El campo «{field}» admite como máximo {maxLength} caracteres.")
            : trimmed;
    }
}
