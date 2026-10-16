using System.Net;
using HabitFlow.Application.Auth;
using HabitFlow.Application.Common;
using HabitFlow.Application.Reminders;
using HabitFlow.Domain.Notifications;
using HabitFlow.Domain.Reminders;
using HabitFlow.Domain.Time;
using HabitFlow.Infrastructure.Reminders;
using Microsoft.Extensions.DependencyInjection;

namespace HabitFlow.Tests.Integration;

[Collection(ApiCollection.Name)]
public sealed class ReminderApiTests(ApiFactory factory)
{
    private static readonly DayOfWeek[] EveryDay =
        [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday];

    private async Task<int> DispatchAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<ReminderDispatcher>();
        var created = await dispatcher.DispatchDueAsync(CancellationToken.None);
        await dispatcher.SendPendingEmailsAsync(CancellationToken.None);
        return created;
    }

    [Fact]
    public async Task Reminder_next_fire_respects_the_zone_and_validation()
    {
        var user = await factory.RegisterUserAsync("America/Bogota");
        var habit = await user.Client.CreateHabitAsync(TestHelpers.BinaryHabit(factory.Today()));

        var response = await user.Client.PostJsonAsync("/api/v1/reminders", new SaveReminderRequest(
            "Meditar", null, "21:30", EveryDay, null, ReminderChannel.InApp, habit.Id, null));
        await response.EnsureStatusAsync(HttpStatusCode.Created);
        var reminder = await response.ReadAsync<ReminderDto>();

        Assert.Equal("America/Bogota", reminder.TimeZoneId);
        Assert.Equal(habit.Id, reminder.Habit!.Id);
        var local = TimeZoneInfo.ConvertTime(reminder.NextFireAt!.Value, TimeZoneIds.Resolve("America/Bogota"));
        Assert.Equal((21, 30), (local.Hour, local.Minute));
        Assert.True(reminder.NextFireAt > factory.Clock.GetUtcNow());

        var invalid = await user.Client.PostJsonAsync("/api/v1/reminders", new SaveReminderRequest(
            "", null, "25:00", [], "Mars/Base", ReminderChannel.InApp, null, null));
        var errors = (await invalid.ReadJsonAsync()).GetProperty("errors");
        Assert.True(errors.TryGetProperty("title", out _));
        Assert.True(errors.TryGetProperty("localTime", out _));
        Assert.True(errors.TryGetProperty("weekdays", out _));
        Assert.True(errors.TryGetProperty("timeZoneId", out _));
    }

    [Fact]
    public async Task Due_reminders_create_one_notification_per_occurrence()
    {
        var user = await factory.RegisterUserAsync("UTC");
        var now = factory.Clock.GetUtcNow();
        var at = now.AddMinutes(10);
        var reminder = await (await user.Client.PostJsonAsync("/api/v1/reminders", new SaveReminderRequest(
            "Beber agua", "Un vaso grande", $"{at:HH:mm}", EveryDay, "UTC", ReminderChannel.InApp, null, null)))
            .ReadAsync<ReminderDto>();

        Assert.Equal(0, await DispatchAsync()); // not due yet

        factory.Clock.Advance(TimeSpan.FromMinutes(11));
        try
        {
            Assert.Equal(1, await DispatchAsync());
            Assert.Equal(0, await DispatchAsync()); // already fired: rescheduled for tomorrow

            var notifications = await user.Client.GetAsync("/api/v1/notifications").OkAsync<PagedResult<NotificationDto>>();
            var notification = Assert.Single(notifications.Items);
            Assert.Equal(("Beber agua", "Un vaso grande", NotificationKind.Reminder), (notification.Title, notification.Body, notification.Kind));

            var updated = await user.Client.GetAsync($"/api/v1/reminders/{reminder.Id}").OkAsync<ReminderDto>();
            Assert.Equal(reminder.NextFireAt!.Value.AddDays(1), updated.NextFireAt);

            await (await user.Client.PostAsync($"/api/v1/notifications/{notification.Id}/read", null))
                .EnsureStatusAsync(HttpStatusCode.NoContent);
            var unread = await user.Client.GetAsync("/api/v1/notifications?unreadOnly=true").OkAsync<PagedResult<NotificationDto>>();
            Assert.Empty(unread.Items);
        }
        finally
        {
            await DeactivateAsync(user, reminder.Id);
        }
    }

    [Fact]
    public async Task Stale_occurrences_after_downtime_are_skipped()
    {
        var user = await factory.RegisterUserAsync("UTC");
        var at = factory.Clock.GetUtcNow().AddMinutes(5);
        var reminder = await (await user.Client.PostJsonAsync("/api/v1/reminders", new SaveReminderRequest(
            "Viejo", null, $"{at:HH:mm}", EveryDay, "UTC", ReminderChannel.InApp, null, null))).ReadAsync<ReminderDto>();

        factory.Clock.Advance(TimeSpan.FromHours(5));
        try
        {
            await DispatchAsync();
            var notifications = await user.Client.GetAsync("/api/v1/notifications").OkAsync<PagedResult<NotificationDto>>();
            Assert.Empty(notifications.Items);
            var updated = await user.Client.GetAsync($"/api/v1/reminders/{reminder.Id}").OkAsync<ReminderDto>();
            Assert.True(updated.NextFireAt > factory.Clock.GetUtcNow());
        }
        finally
        {
            await DeactivateAsync(user, reminder.Id);
        }
    }

    [Fact]
    public async Task Email_channel_sends_only_with_user_consent()
    {
        var user = await factory.RegisterUserAsync("UTC");
        await user.Client.PutJsonAsync("/api/v1/users/me", new UpdateProfileRequest("Test User", "UTC", true)).OkAsync<UserDto>();
        var at = factory.Clock.GetUtcNow().AddMinutes(3);
        var reminder = await (await user.Client.PostJsonAsync("/api/v1/reminders", new SaveReminderRequest(
            "Con correo", null, $"{at:HH:mm}", EveryDay, "UTC", ReminderChannel.InAppAndEmail, null, null))).ReadAsync<ReminderDto>();

        factory.Clock.Advance(TimeSpan.FromMinutes(4));
        try
        {
            await DispatchAsync();
            var notification = Assert.Single((await user.Client.GetAsync("/api/v1/notifications")
                .OkAsync<PagedResult<NotificationDto>>()).Items);
            Assert.Equal(EmailDeliveryStatus.Sent, notification.EmailStatus);
            Assert.Contains(Directory.GetFiles(factory.MailDirectory, "*.eml").Select(File.ReadAllText),
                m => m.Contains(user.Profile.Email, StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            await DeactivateAsync(user, reminder.Id);
        }
    }

    [Fact]
    public async Task Deactivated_reminders_do_not_fire_and_others_cannot_touch_them()
    {
        var user = await factory.RegisterUserAsync("UTC");
        var other = await factory.RegisterUserAsync("UTC");
        var reminder = await (await user.Client.PostJsonAsync("/api/v1/reminders", new SaveReminderRequest(
            "Pausado", null, "07:00", EveryDay, null, ReminderChannel.InApp, null, null))).ReadAsync<ReminderDto>();

        var off = await user.Client.PostJsonAsync($"/api/v1/reminders/{reminder.Id}/active", new SetReminderActiveRequest(false))
            .OkAsync<ReminderDto>();
        Assert.Null(off.NextFireAt);

        Assert.Equal(HttpStatusCode.NotFound, (await other.Client.GetAsync($"/api/v1/reminders/{reminder.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.Client.DeleteAsync($"/api/v1/reminders/{reminder.Id}")).StatusCode);
        Assert.Empty(await other.Client.GetAsync("/api/v1/reminders").OkAsync<List<ReminderDto>>());

        var foreignHabit = await user.Client.CreateHabitAsync(TestHelpers.BinaryHabit(factory.Today()));
        var link = await other.Client.PostJsonAsync("/api/v1/reminders", new SaveReminderRequest(
            "x", null, "07:00", EveryDay, null, ReminderChannel.InApp, foreignHabit.Id, null));
        Assert.Equal("reminder.invalid_habit", await link.ProblemCodeAsync());

        await (await user.Client.DeleteAsync($"/api/v1/reminders/{reminder.Id}")).EnsureStatusAsync(HttpStatusCode.NoContent);
    }

    private static async Task DeactivateAsync(TestUser user, Guid id) =>
        await user.Client.PostJsonAsync($"/api/v1/reminders/{id}/active", new SetReminderActiveRequest(false));
}
