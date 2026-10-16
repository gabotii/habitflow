using System.Text;
using FluentValidation;
using HabitFlow.Application.Auth;
using HabitFlow.Application.Common;
using HabitFlow.Application.Social;
using HabitFlow.Domain.Time;
using HabitFlow.Infrastructure.Email;
using HabitFlow.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ValidationFailure = FluentValidation.Results.ValidationFailure;

namespace HabitFlow.Infrastructure.Auth;

/// <summary>
/// Account management on top of ASP.NET Core Identity (hashing, lockout, security stamps). Issuing the session
/// cookie is an HTTP concern and happens in the API layer with <see cref="SignInManager{TUser}"/>.
/// </summary>
public sealed class AccountService(
    UserManager<AppUser> userManager,
    SignInManager<AppUser> signInManager,
    SharedGoalService sharedGoals,
    IEmailSender emailSender,
    IOptions<EmailOptions> emailOptions,
    ILogger<AccountService> logger)
{
    private static readonly string DummyPasswordHash =
        new PasswordHasher<AppUser>().HashPassword(new AppUser(), "not-a-real-password-1A");

    public AuthCapabilitiesDto Capabilities => new(emailSender.IsEnabled, emailSender.IsEnabled);

    public async Task<AppUser> RegisterAsync(RegisterRequest request)
    {
        var email = request.Email.Trim();
        var user = new AppUser
        {
            Id = Guid.CreateVersion7(),
            UserName = email,
            Email = email,
            DisplayName = request.DisplayName.Trim(),
            TimeZoneId = request.TimeZoneId ?? TimeZoneIds.Default,
        };

        var result = await userManager.CreateAsync(user, request.Password);
        if (!result.Succeeded)
        {
            if (result.Errors.Any(e => e.Code is "DuplicateEmail" or "DuplicateUserName"))
            {
                throw new ConflictException("auth.email_taken", "Ya existe una cuenta con ese correo electrónico.");
            }

            throw new ValidationException(result.Errors.Select(e => new ValidationFailure("Password", e.Description)));
        }

        logger.LogInformation("User {UserId} registered", user.Id);
        return user;
    }

    /// <summary>Checks credentials with lockout (5 failures → 15 minutes). Unknown e-mails take the same time.</summary>
    public async Task<AppUser> ValidateCredentialsAsync(LoginRequest request)
    {
        var user = await userManager.FindByEmailAsync(request.Email.Trim());
        if (user is null)
        {
            new PasswordHasher<AppUser>().VerifyHashedPassword(new AppUser(), DummyPasswordHash, request.Password);
            throw new AuthenticationFailedException();
        }

        var result = await signInManager.CheckPasswordSignInAsync(user, request.Password, lockoutOnFailure: true);
        if (result.IsLockedOut)
        {
            logger.LogWarning("Login attempt for locked out user {UserId}", user.Id);
            throw new AuthenticationFailedException(
                "La cuenta está bloqueada temporalmente por demasiados intentos fallidos. Inténtalo más tarde.");
        }

        if (!result.Succeeded)
        {
            logger.LogWarning("Failed login for user {UserId}", user.Id);
            throw new AuthenticationFailedException();
        }

        return user;
    }

    public async Task<AppUser> GetUserAsync(Guid userId) =>
        await userManager.FindByIdAsync(userId.ToString()) ?? throw new AuthenticationFailedException("Sesión no válida.");

    public async Task<UserDto> UpdateProfileAsync(Guid userId, UpdateProfileRequest request)
    {
        var user = await GetUserAsync(userId);
        user.DisplayName = request.DisplayName.Trim();
        user.TimeZoneId = request.TimeZoneId;
        user.EmailRemindersEnabled = request.EmailRemindersEnabled;
        await userManager.UpdateAsync(user);
        return ToDto(user);
    }

    /// <summary>
    /// Changes the password. Identity rotates the security stamp, which invalidates every other session at its next
    /// validation; the API re-issues the current device's cookie.
    /// </summary>
    public async Task<AppUser> ChangePasswordAsync(Guid userId, ChangePasswordRequest request)
    {
        var user = await GetUserAsync(userId);
        var result = await userManager.ChangePasswordAsync(user, request.CurrentPassword, request.NewPassword);
        if (!result.Succeeded)
        {
            if (result.Errors.Any(e => e.Code == "PasswordMismatch"))
            {
                throw new ValidationException(
                    [new ValidationFailure("CurrentPassword", "La contraseña actual no es correcta.")]);
            }

            throw new ValidationException(result.Errors.Select(e => new ValidationFailure("NewPassword", e.Description)));
        }

        logger.LogInformation("User {UserId} changed their password", userId);
        return user;
    }

    /// <summary>
    /// Sends a reset link if the account exists. The response never reveals whether it does (no enumeration).
    /// </summary>
    public async Task ForgotPasswordAsync(ForgotPasswordRequest request, CancellationToken ct)
    {
        EnsureEmailEnabled();
        var user = await userManager.FindByEmailAsync(request.Email.Trim());
        if (user is null)
        {
            return;
        }

        var token = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(
            await userManager.GeneratePasswordResetTokenAsync(user)));
        var link = $"{emailOptions.Value.PublicBaseUrl.TrimEnd('/')}/restablecer-contrasena" +
                   $"?email={Uri.EscapeDataString(user.Email!)}&token={token}";
        await emailSender.SendAsync(
            user.Email!,
            "Restablece tu contraseña de HabitFlow",
            $"Hola, {user.DisplayName}:\n\nPara elegir una nueva contraseña abre este enlace (caduca en 1 hora):\n{link}\n\n" +
            "Si no lo has pedido tú, ignora este mensaje: tu contraseña no cambiará.",
            ct);
        logger.LogInformation("Password reset requested for user {UserId}", user.Id);
    }

    public async Task ResetPasswordAsync(ResetPasswordRequest request)
    {
        EnsureEmailEnabled();
        var user = await userManager.FindByEmailAsync(request.Email.Trim());
        string token;
        try
        {
            token = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(request.Token));
        }
        catch (FormatException)
        {
            throw InvalidResetToken();
        }

        if (user is null)
        {
            throw InvalidResetToken();
        }

        var result = await userManager.ResetPasswordAsync(user, token, request.NewPassword);
        if (!result.Succeeded)
        {
            if (result.Errors.Any(e => e.Code == "InvalidToken"))
            {
                throw InvalidResetToken();
            }

            throw new ValidationException(result.Errors.Select(e => new ValidationFailure("NewPassword", e.Description)));
        }

        await userManager.ResetAccessFailedCountAsync(user);
        await userManager.SetLockoutEndDateAsync(user, null);
        logger.LogInformation("User {UserId} reset their password", user.Id);
    }

    /// <summary>
    /// Deletes the account and, by cascade, every habit, log, goal, reminder and notification it owns. Shared goals it
    /// owns pass to another member first; both changes are saved in the same <c>SaveChanges</c> (same DbContext).
    /// </summary>
    public async Task DeleteAccountAsync(Guid userId, DeleteAccountRequest request)
    {
        var user = await GetUserAsync(userId);
        if (!await userManager.CheckPasswordAsync(user, request.Password))
        {
            throw new ValidationException([new ValidationFailure("Password", "La contraseña no es correcta.")]);
        }

        await sharedGoals.PrepareAccountDeletionAsync(userId, CancellationToken.None);
        var result = await userManager.DeleteAsync(user);
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(
                $"Account deletion failed: {string.Join(", ", result.Errors.Select(e => e.Code))}");
        }

        logger.LogInformation("User {UserId} deleted their account", userId);
    }

    public static UserDto ToDto(AppUser user) => new(
        user.Id, user.Email!, user.DisplayName, user.TimeZoneId, user.EmailRemindersEnabled, user.CreatedAt);

    private void EnsureEmailEnabled()
    {
        if (!emailSender.IsEnabled)
        {
            throw new FeatureUnavailableException("email.not_configured",
                "La recuperación de contraseña no está disponible: el servidor no tiene correo configurado.");
        }
    }

    private static ValidationException InvalidResetToken() =>
        new([new ValidationFailure("Token", "El enlace no es válido o ha caducado. Solicita uno nuevo.")]);
}
