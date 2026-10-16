using FluentValidation;
using HabitFlow.Application.Common;

namespace HabitFlow.Application.Auth;

public sealed record UserDto(
    Guid Id,
    string Email,
    string DisplayName,
    string TimeZoneId,
    bool EmailRemindersEnabled,
    DateTimeOffset CreatedAt);

public sealed record RegisterRequest(string Email, string Password, string DisplayName, string? TimeZoneId);

public sealed record LoginRequest(string Email, string Password, bool RememberMe = true);

public sealed record UpdateProfileRequest(string DisplayName, string TimeZoneId, bool EmailRemindersEnabled);

public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);

public sealed record ForgotPasswordRequest(string Email);

public sealed record ResetPasswordRequest(string Email, string Token, string NewPassword);

public sealed record DeleteAccountRequest(string Password);

/// <summary>Which optional, configuration-dependent features this deployment supports.</summary>
public sealed record AuthCapabilitiesDto(bool PasswordResetAvailable, bool EmailRemindersAvailable);

public static class PasswordPolicy
{
    public const int MinLength = 8;
    public const int MaxLength = 128;

    public static IRuleBuilderOptions<T, string> StrongPassword<T>(this IRuleBuilder<T, string> rule) =>
        rule
            .NotEmpty()
            .MinimumLength(MinLength)
            .MaximumLength(MaxLength)
            .Matches("[a-z]").WithMessage("La contraseña debe incluir al menos una minúscula.")
            .Matches("[A-Z]").WithMessage("La contraseña debe incluir al menos una mayúscula.")
            .Matches("[0-9]").WithMessage("La contraseña debe incluir al menos un número.");
}

public sealed class RegisterRequestValidator : AbstractValidator<RegisterRequest>
{
    public RegisterRequestValidator()
    {
        RuleFor(r => r.Email).NotEmpty().EmailAddress().MaximumLength(254);
        RuleFor(r => r.Password).StrongPassword();
        RuleFor(r => r.DisplayName).NotEmpty().MaximumLength(80);
        RuleFor(r => r.TimeZoneId).IanaTimeZone().When(r => r.TimeZoneId is not null);
    }
}

public sealed class LoginRequestValidator : AbstractValidator<LoginRequest>
{
    public LoginRequestValidator()
    {
        RuleFor(r => r.Email).NotEmpty().MaximumLength(254);
        RuleFor(r => r.Password).NotEmpty().MaximumLength(PasswordPolicy.MaxLength);
    }
}

public sealed class UpdateProfileRequestValidator : AbstractValidator<UpdateProfileRequest>
{
    public UpdateProfileRequestValidator()
    {
        RuleFor(r => r.DisplayName).NotEmpty().MaximumLength(80);
        RuleFor(r => r.TimeZoneId).IanaTimeZone();
    }
}

public sealed class ChangePasswordRequestValidator : AbstractValidator<ChangePasswordRequest>
{
    public ChangePasswordRequestValidator()
    {
        RuleFor(r => r.CurrentPassword).NotEmpty().MaximumLength(PasswordPolicy.MaxLength);
        RuleFor(r => r.NewPassword)
            .StrongPassword()
            .NotEqual(r => r.CurrentPassword)
            .WithMessage("La nueva contraseña debe ser distinta de la actual.");
    }
}

public sealed class ForgotPasswordRequestValidator : AbstractValidator<ForgotPasswordRequest>
{
    public ForgotPasswordRequestValidator() => RuleFor(r => r.Email).NotEmpty().EmailAddress().MaximumLength(254);
}

public sealed class ResetPasswordRequestValidator : AbstractValidator<ResetPasswordRequest>
{
    public ResetPasswordRequestValidator()
    {
        RuleFor(r => r.Email).NotEmpty().EmailAddress().MaximumLength(254);
        RuleFor(r => r.Token).NotEmpty().MaximumLength(2000);
        RuleFor(r => r.NewPassword).StrongPassword();
    }
}

public sealed class DeleteAccountRequestValidator : AbstractValidator<DeleteAccountRequest>
{
    public DeleteAccountRequestValidator() => RuleFor(r => r.Password).NotEmpty().MaximumLength(PasswordPolicy.MaxLength);
}
