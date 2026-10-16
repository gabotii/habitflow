using HabitFlow.Application.Abstractions;
using HabitFlow.Infrastructure.Auth;
using HabitFlow.Infrastructure.Email;
using HabitFlow.Infrastructure.Identity;
using HabitFlow.Infrastructure.Persistence;
using HabitFlow.Infrastructure.Reminders;
using HabitFlow.Infrastructure.Users;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace HabitFlow.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.TryAddSingleton(TimeProvider.System);

        // Resolved lazily so configuration added after service registration (hosting, tests) is honoured.
        services.AddDbContext<AppDbContext>((sp, options) =>
        {
            var connectionString = sp.GetRequiredService<IConfiguration>().GetConnectionString("Default");
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                throw new InvalidOperationException(
                    "Missing connection string 'ConnectionStrings:Default' (env var ConnectionStrings__Default).");
            }

            // Split queries: aggregates load several collections (schedules, pauses) without a cartesian product.
            options.UseNpgsql(connectionString, o => o.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery));

            // Aggregate children (schedules, pauses, goal links) are only loaded through their filtered root.
            options.ConfigureWarnings(w =>
                w.Ignore(CoreEventId.PossibleIncorrectRequiredNavigationWithQueryFilterInteractionWarning));
        });
        services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<AppDbContext>());
        services.AddScoped<IUserClock, UserClock>();
        services.AddScoped<IUserDirectory, UserDirectory>();

        services
            .AddIdentityCore<AppUser>(options =>
            {
                options.User.RequireUniqueEmail = true;
                options.Password.RequiredLength = 8;
                options.Password.RequireDigit = true;
                options.Password.RequireLowercase = true;
                options.Password.RequireUppercase = true;
                options.Password.RequireNonAlphanumeric = false;
                options.Lockout.AllowedForNewUsers = true;
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
            })
            .AddSignInManager()
            .AddEntityFrameworkStores<AppDbContext>()
            .AddDefaultTokenProviders();

        services.Configure<DataProtectionTokenProviderOptions>(o => o.TokenLifespan = TimeSpan.FromHours(1));

        // Other sessions notice a password change (security stamp) within this interval.
        services.Configure<SecurityStampValidatorOptions>(o => o.ValidationInterval = TimeSpan.FromMinutes(5));

        // Session cookies are encrypted with Data Protection keys; they must survive restarts and be shared by every
        // instance, so production persists them to a mounted folder.
        var dataProtection = services.AddDataProtection().SetApplicationName("HabitFlow");
        var keysPath = configuration["DataProtection:KeysPath"];
        if (!string.IsNullOrWhiteSpace(keysPath))
        {
            dataProtection.PersistKeysToFileSystem(new DirectoryInfo(keysPath));
        }

        services.Configure<EmailOptions>(configuration.GetSection(EmailOptions.SectionName));
        services.AddSingleton<IEmailSender, EmailSender>();
        services.AddScoped<AccountService>();

        services.Configure<ReminderOptions>(configuration.GetSection(ReminderOptions.SectionName));
        services.AddScoped<ReminderDispatcher>();
        services.AddHostedService<ReminderWorker>();

        return services;
    }
}
