using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using PBRMS.Api.Live;
using PBRMS.Domain;
using static PBRMS.Tests.TestSteps;

namespace PBRMS.Tests;

/// <summary>
/// Real-time updates through a real SignalR connection: when a booking comes in, the person
/// who has to act on it hears straight away, and only they get the notification.
/// </summary>
[Collection("API")]
public class LiveUpdateTests(TestApi api)
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task A_submitted_booking_reaches_the_EP_straight_away()
    {
        await using var ep = await ConnectAsync("ep@pbrms.test");
        var arrived = Listen<LiveNotification>(ep, "notification");
        var booking = await DraftAsync();

        await api.TransitionAsync("producer@pbrms.test", booking.Id, BookingAction.Submit);

        var notification = await arrived.WaitAsync(Patience);
        Assert.Equal(booking.Id, notification.BookingId);
        Assert.Contains("needs your approval", notification.Text);
    }

    [Fact]
    public async Task A_notification_goes_only_to_the_person_it_is_for()
    {
        await using var ep = await ConnectAsync("ep@pbrms.test");
        await using var supervisor = await ConnectAsync("supervisor@pbrms.test");
        var forEp = Listen<LiveNotification>(ep, "notification");
        var forSupervisor = Listen<LiveNotification>(supervisor, "notification");
        var booking = await DraftAsync();

        await api.TransitionAsync("producer@pbrms.test", booking.Id, BookingAction.Submit);
        await forEp.WaitAsync(Patience);

        Assert.False(forSupervisor.IsCompleted);
    }

    [Fact]
    public async Task Everyone_hears_that_a_booking_changed_so_their_screen_can_refresh()
    {
        await using var bookingOfficer = await ConnectAsync("booking@pbrms.test");
        var booking = await DraftAsync();
        var changed = Listen<LiveBookingChange>(bookingOfficer, "bookingChanged", c => c.Id == booking.Id);

        await api.TransitionAsync("producer@pbrms.test", booking.Id, BookingAction.Submit);

        Assert.Equal(booking.Id, (await changed.WaitAsync(Patience)).Id);
    }

    [Fact]
    public async Task Everyone_hears_that_a_roster_changed_so_their_screen_can_refresh()
    {
        await using var bookingOfficer = await ConnectAsync("booking@pbrms.test");
        var changed = Listen<LiveRosterChange>(bookingOfficer, "rosterChanged");
        var units = await api.UnitsAsync();
        var supervisor = await api.SignedInClientAsync("supervisor@pbrms.test");

        var saved = await supervisor.PutAsJsonAsync($"/rosters/{units["Camera"]}/2031-12", new { entries = Array.Empty<object>() }, Json);

        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        Assert.Equal(units["Camera"], (await changed.WaitAsync(Patience)).DivisionId);
    }

    [Fact]
    public async Task A_change_that_is_refused_sends_nothing()
    {
        await using var ep = await ConnectAsync("ep@pbrms.test");
        var arrived = Listen<LiveNotification>(ep, "notification");
        var booking = await api.AtFinalisationAsync(NewDay(), Venue.Studio1);
        arrived = Listen<LiveNotification>(ep, "notification");

        // Only the Booking Officer can confirm, so this changes nothing.
        var client = await api.SignedInClientAsync("ep@pbrms.test");
        var refused = await client.PostAsJsonAsync($"/bookings/{booking.Id}/transition", new { action = "Approve" }, Json);

        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        await Task.WhenAny(arrived, Task.Delay(TimeSpan.FromMilliseconds(300)));
        Assert.False(arrived.IsCompleted);
    }

    [Fact]
    public async Task Connecting_needs_a_valid_sign_in()
    {
        var client = api.CreateClient();

        var withoutToken = await client.PostAsync("/hubs/live/negotiate?negotiateVersion=1", null);
        Assert.Equal(HttpStatusCode.Unauthorized, withoutToken.StatusCode);

        // Browsers can't set a header on a WebSocket, so the token comes in the address.
        var token = await api.TokenAsync("ep@pbrms.test");
        var withToken = await client.PostAsync($"/hubs/live/negotiate?negotiateVersion=1&access_token={token}", null);
        Assert.Equal(HttpStatusCode.OK, withToken.StatusCode);
    }

    [Fact]
    public async Task Open_screens_hear_when_the_APP_room_changes()
    {
        await using var producer = await ConnectAsync("producer.sports@pbrms.test");
        var changed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        producer.On("appRoomChanged", () => changed.TrySetResult());
        var operators = await (await api.SignedInClientAsync("editor@pbrms.test")).GetFromJsonAsync<List<OperatorRow>>("/app-operators", Json);

        var editor = await api.SignedInClientAsync("editor@pbrms.test");
        var start = NewDay();
        var response = await editor.PostAsJsonAsync("/app-sessions", new { operatorId = operators![0].Id, production = "Live check", startsAt = start, endsAt = start.AddHours(1) }, Json);
        response.EnsureSuccessStatusCode();

        await changed.Task.WaitAsync(Patience);
    }

    private record OperatorRow(Guid Id);

    // ---- Helpers ----

    private async Task<BookingResponseLite> DraftAsync()
    {
        var units = await api.UnitsAsync();
        var start = NewDay();
        var producer = await api.SignedInClientAsync("producer@pbrms.test");
        var response = await producer.PostAsJsonAsync("/bookings", new
        {
            title = "Live Update Check",
            departmentId = units["News"],
            activityType = "Recording",
            venue = "Location",
            callAt = start,
            startsAt = start,
            endsAt = start.AddHours(1),
            crewSlots = new[] { new { divisionId = units["Camera"], positionLabel = "Camera 1" } }
        }, Json);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<BookingResponseLite>(Json))!;
    }

    private record BookingResponseLite(Guid Id);

    /// <summary>Opens a live connection as this person. Long polling, because the test server has no WebSockets.</summary>
    private async Task<HubConnection> ConnectAsync(string email)
    {
        var token = await api.TokenAsync(email);
        var connection = new HubConnectionBuilder()
            .WithUrl(new Uri(api.Server.BaseAddress, "hubs/live"), options =>
            {
                options.HttpMessageHandlerFactory = _ => api.Server.CreateHandler();
                options.AccessTokenProvider = () => Task.FromResult<string?>(token);
                options.Transports = HttpTransportType.LongPolling;
            })
            .Build();

        await connection.StartAsync();
        return connection;
    }

    /// <summary>Completes with the first matching message.</summary>
    private static Task<T> Listen<T>(HubConnection connection, string name, Func<T, bool>? matches = null)
    {
        var heard = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        connection.On<T>(name, message =>
        {
            if (matches is null || matches(message))
                heard.TrySetResult(message);
        });
        return heard.Task;
    }
}
