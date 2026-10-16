using System.Globalization;
using FluentValidation;
using HabitFlow.Application.Calendar;
using HabitFlow.Application.Categories;
using HabitFlow.Application.Dashboard;
using HabitFlow.Application.Goals;
using HabitFlow.Application.Reminders;
using HabitFlow.Application.Social;
using HabitFlow.Application.Statistics;
using HabitFlow.Application.Habits;
using HabitFlow.Application.Tracking;
using Microsoft.Extensions.DependencyInjection;

namespace HabitFlow.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        // Built-in FluentValidation messages in Spanish (the product language).
        ValidatorOptions.Global.LanguageManager.Culture = CultureInfo.GetCultureInfo("es");
        services.AddValidatorsFromAssembly(typeof(DependencyInjection).Assembly, includeInternalTypes: true);

        services.AddScoped<HabitTracker>();
        services.AddScoped<CategoryService>();
        services.AddScoped<HabitService>();
        services.AddScoped<HabitLogService>();
        services.AddScoped<CalendarService>();
        services.AddScoped<GoalProgressCalculator>();
        services.AddScoped<GoalService>();
        services.AddScoped<StatisticsService>();
        services.AddScoped<DashboardService>();
        services.AddScoped<ReminderService>();
        services.AddScoped<NotificationService>();
        services.AddScoped<ChallengeService>();
        services.AddScoped<SharedGoalService>();
        return services;
    }
}
