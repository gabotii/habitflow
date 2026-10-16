using HabitFlow.Domain.Goals;
using HabitFlow.Domain.Habits;
using HabitFlow.Domain.Reminders;
using HabitFlow.Domain.Time;
using HabitFlow.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;

namespace HabitFlow.Infrastructure.Persistence;

/// <summary>
/// Creates one demo account with fictitious habits and ~2 months of deterministic history. It is idempotent (does
/// nothing if the demo account exists), never touches other accounts, and only runs through the explicit
/// <c>seed-demo</c> command, which refuses to run in Production. No real personal data.
/// </summary>
public sealed class DemoDataSeeder(AppDbContext db, UserManager<AppUser> userManager, TimeProvider timeProvider)
{
    public const string DemoTimeZone = "Europe/Madrid";
    private const int HistoryDays = 60;

    /// <returns>false when the demo account already existed.</returns>
    public async Task<bool> SeedAsync(string email, string password, CancellationToken ct = default)
    {
        if (await userManager.FindByEmailAsync(email) is not null)
        {
            return false;
        }

        var user = new AppUser
        {
            Id = Guid.CreateVersion7(),
            UserName = email,
            Email = email,
            DisplayName = "Alex",
            TimeZoneId = DemoTimeZone,
        };
        var created = await userManager.CreateAsync(user, password);
        if (!created.Succeeded)
        {
            throw new InvalidOperationException(
                "Could not create the demo user: " + string.Join("; ", created.Errors.Select(e => e.Description)));
        }

        var now = timeProvider.GetUtcNow();
        var today = ZonedTime.LocalDate(now, TimeZoneIds.Resolve(DemoTimeZone));
        var start = today.AddDays(-(HistoryDays - 1));
        var random = new Random(2026);

        var reading = NewHabit(user.Id, "Leer 20 páginas", "Antes de dormir, sin pantallas.", BuiltInCategories.Learning,
            HabitColor.Indigo, "book-open", HabitType.Quantity, "páginas", start,
            ScheduleSpec.Create(HabitType.Quantity, ScheduleKind.Daily, targetValue: 20), now);
        var water = NewHabit(user.Id, "Beber agua", null, BuiltInCategories.Health, HabitColor.Sky, "droplet",
            HabitType.Quantity, "litros", start, ScheduleSpec.Create(HabitType.Quantity, ScheduleKind.Daily, targetValue: 2), now);
        var training = NewHabit(user.Id, "Entrenar", "Fuerza o carrera suave.", BuiltInCategories.Exercise,
            HabitColor.Orange, "dumbbell", HabitType.Binary, null, start,
            ScheduleSpec.Create(HabitType.Binary, ScheduleKind.Weekdays,
                weekdaysMask: Weekdays.FromDays([DayOfWeek.Monday, DayOfWeek.Wednesday, DayOfWeek.Friday])), now);
        var coding = NewHabit(user.Id, "Estudiar programación", "Cursos y proyectos personales.",
            BuiltInCategories.Learning, HabitColor.Violet, "code", HabitType.Quantity, "minutos", start,
            ScheduleSpec.Create(HabitType.Quantity, ScheduleKind.TimesPerWeek, timesPerWeek: 4, targetValue: 30), now);
        var meditation = NewHabit(user.Id, "Meditar 10 minutos", null, BuiltInCategories.Mind, HabitColor.Teal,
            "brain", HabitType.Binary, null, start, ScheduleSpec.Create(HabitType.Binary, ScheduleKind.Daily), now);
        var english = NewHabit(user.Id, "Practicar inglés", "Una conversación o una lección.",
            BuiltInCategories.Learning, HabitColor.Rose, "languages", HabitType.Binary, null, today.AddDays(-20),
            ScheduleSpec.Create(HabitType.Binary, ScheduleKind.EveryNDays, intervalDays: 2), now);

        // History of a rules change: meditation went from every day to weekdays three weeks ago.
        meditation.ChangeSchedule(
            ScheduleSpec.Create(HabitType.Binary, ScheduleKind.Weekdays, weekdaysMask: 0b001_1111),
            today.AddDays(-21), now);

        var habits = new[] { reading, water, training, coding, meditation, english };
        db.Habits.AddRange(habits);

        AddHistory(reading, start, today, random, 0.85, d => Math.Round((decimal)(random.Next(8, 35)), 0));
        AddHistory(water, start, today, random, 0.75, d => Math.Round((decimal)(random.Next(10, 28) / 10.0), 1));
        AddHistory(training, start, today, random, 0.8, _ => null);
        AddHistory(coding, start, today, random, 0.55, d => random.Next(15, 90));
        AddHistory(meditation, start, today, random, 0.9, _ => null);
        AddHistory(english, today.AddDays(-20), today, random, 0.75, _ => null);

        // Goals: one manual, one fed by a habit.
        var books = Goal.Create(user.Id, "Leer 12 libros este año", "Ficción y divulgación.", GoalMetric.Manual, 12,
            "libros", new DateOnly(today.Year, 1, 1), new DateOnly(today.Year, 12, 31), []);
        db.Goals.Add(books);
        foreach (var (daysAgo, amount) in new[] { (50, 1m), (30, 2m), (12, 1m) })
        {
            db.GoalEntries.Add(GoalProgressEntry.Create(books, today.AddDays(-daysAgo), amount, null));
        }

        db.Goals.Add(Goal.Create(user.Id, "Entrenar 12 veces este mes", null, GoalMetric.HabitCompletions, 12, "veces",
            new DateOnly(today.Year, today.Month, 1), new DateOnly(today.Year, today.Month, 1).AddMonths(1).AddDays(-1),
            [training.Id]));

        db.Reminders.Add(Reminder.Create(user.Id, "Hora de leer", "Veinte páginas antes de dormir.", new TimeOnly(22, 0),
            Weekdays.All, DemoTimeZone, ReminderChannel.InApp, reading.Id, null, now));

        await db.SaveChangesAsync(ct);
        return true;
    }

    private static Habit NewHabit(Guid userId, string name, string? description, Guid categoryId, HabitColor color,
        string icon, HabitType type, string? unit, DateOnly start, ScheduleSpec spec, DateTimeOffset now) =>
        Habit.Create(userId, new HabitDetails(name, description, categoryId, color, icon, unit, null), type, start, spec, now);

    private void AddHistory(Habit habit, DateOnly start, DateOnly today, Random random, double probability,
        Func<DateOnly, decimal?> quantity)
    {
        for (var day = start; day < today; day = day.AddDays(1))
        {
            var spec = habit.SpecOn(day);
            var eligible = spec.Kind switch
            {
                ScheduleKind.Weekdays => Weekdays.Contains(spec.WeekdaysMask!.Value, day.DayOfWeek),
                ScheduleKind.EveryNDays => (day.DayNumber - start.DayNumber) % spec.IntervalDays!.Value == 0,
                _ => true,
            };
            if (!eligible || random.NextDouble() > probability)
            {
                continue;
            }

            var amount = habit.Type == HabitType.Quantity ? quantity(day) : null;
            var log = HabitLog.Create(habit, day, amount, completed: true, note: null);
            db.HabitLogs.Add(log);
            if (day >= today.AddDays(-3))
            {
                db.HabitLogChanges.Add(HabitLogChange.Record(habit.UserId, habit.Id, day, null, log));
            }
        }
    }
}
