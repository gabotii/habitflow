namespace HabitFlow.Domain.Common;

/// <summary>Raised when an operation would break a business rule. <see cref="Code"/> is a stable, machine-readable identifier.</summary>
public sealed class DomainException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
