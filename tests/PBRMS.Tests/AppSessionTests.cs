using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using PBRMS.Domain;
using static PBRMS.Api.Endpoints.AppSessionEndpoints;
using static PBRMS.Tests.TestSteps;

namespace PBRMS.Tests;

/// <summary>
/// APP bookings through the real API: an editor or producer books the APP operator
/// directly, the operator confirms, and if they can't, the Booking Officer suggests a
/// time. Each test uses its own day, because they share the sample APP operator.
/// </summary>
[Collection("API")]
public class AppSessionTests(TestApi api)
{
    private const string Editor = "editor@pbrms.test";
    private const string AppOperator = "app@pbrms.test";
    private const string BookingOfficer = "booking@pbrms.test";

    [Fact]
    public async Task An_editor_books_the_app_operator_directly_and_the_operator_confirms()
    {
        var day = NewAppDay();

        var session = await RequestAsync(Editor, day);
        Assert.Equal(AppSessionStatus.Requested, session.Status);
        Assert.Contains(await api.NotificationsAsync(AppOperator), n => n.Text.Contains(session.Production) && n.Text.Contains("Please confirm"));

        var confirmed = await PostOkAsync(AppOperator, session.Id, "confirm");

        Assert.Equal(AppSessionStatus.Confirmed, confirmed.Status);
        foreach (var person in new[] { Editor, "supervisor.sound@pbrms.test", BookingOfficer })
            Assert.Contains(await api.NotificationsAsync(person), n => n.Text.Contains(session.Production) && n.Text.Contains("is confirmed"));
    }

    [Fact]
    public async Task A_producer_can_book_too_but_an_operator_cant()
    {
        var day = NewAppDay();
        Assert.Equal(AppSessionStatus.Requested, (await RequestAsync("producer@pbrms.test", day)).Status);

        var response = await SendRequestAsync("operator@pbrms.test", day);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Only_the_app_operator_on_the_booking_can_confirm_it()
    {
        var session = await RequestAsync(Editor, NewAppDay());
        var someoneElse = await api.SignedInClientAsync(BookingOfficer);

        var response = await someoneElse.PostAsync($"/app-sessions/{session.Id}/confirm", null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task If_the_operator_is_unavailable_the_booking_officer_suggests_a_time_and_the_editor_accepts()
    {
        var day = NewAppDay();
        var session = await RequestAsync(Editor, day);

        var unavailable = await PostOkAsync(AppOperator, session.Id, "unavailable", new AppNoteRequest("On a production that morning"));
        Assert.Equal(AppSessionStatus.Unavailable, unavailable.Status);

        // It lands with the Booking Officer.
        var officer = await api.SignedInClientAsync(BookingOfficer);
        var queue = await officer.GetFromJsonAsync<List<AppSessionResponse>>("/app-sessions/queue", Json);
        Assert.Contains(queue!, s => s.Id == session.Id);

        var later = day.AddHours(5);
        var suggested = await PostOkAsync(BookingOfficer, session.Id, "suggest-time", new AppSuggestionRequest(later, later.AddHours(2), "After the bulletin"));
        Assert.Equal(AppSessionStatus.TimeSuggested, suggested.Status);
        Assert.Contains(await api.NotificationsAsync(Editor), n => n.Text.Contains("Suggested time") && n.Text.Contains(session.Production));

        var accepted = await PostOkAsync(Editor, session.Id, "accept");

        Assert.Equal(AppSessionStatus.Confirmed, accepted.Status);
        Assert.Equal(later, accepted.StartsAt);
    }

    [Fact]
    public async Task The_booking_officer_cant_suggest_a_time_the_operator_is_busy()
    {
        var day = NewAppDay();
        var busy = await RequestAsync(Editor, day);
        await PostOkAsync(AppOperator, busy.Id, "confirm");
        var second = await RequestAsync(Editor, day.AddHours(5));
        await PostOkAsync(AppOperator, second.Id, "unavailable", new AppNoteRequest(null));
        var officer = await api.SignedInClientAsync(BookingOfficer);

        var response = await officer.PostAsJsonAsync($"/app-sessions/{second.Id}/suggest-time", new AppSuggestionRequest(day, day.AddHours(1), null), Json);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task The_operator_cant_confirm_over_another_session()
    {
        var day = NewAppDay();
        var first = await RequestAsync(Editor, day);
        var second = await RequestAsync("producer@pbrms.test", day.AddMinutes(30));
        await PostOkAsync(AppOperator, first.Id, "confirm");
        var client = await api.SignedInClientAsync(AppOperator);

        var response = await client.PostAsync($"/app-sessions/{second.Id}/confirm", null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task The_room_can_only_hold_one_session_whichever_operator_is_on_it()
    {
        var day = NewAppDay();
        var (firstOperator, secondOperator) = await TwoAppOperatorsAsync();
        var first = await RequestWithAsync(firstOperator, day);
        var second = await RequestWithAsync(secondOperator, day.AddMinutes(30));
        await PostOkAsync(OperatorEmail(firstOperator), first.Id, "confirm");
        var client = await api.SignedInClientAsync(OperatorEmail(secondOperator));

        var response = await client.PostAsync($"/app-sessions/{second.Id}/confirm", null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("APP room", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Booking_a_time_the_room_is_taken_is_refused_straight_away()
    {
        var day = NewAppDay();
        var first = await RequestAsync(Editor, day);
        await PostOkAsync(AppOperator, first.Id, "confirm");

        var response = await SendRequestAsync("producer@pbrms.test", day.AddHours(1));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Anyone_can_see_when_the_room_is_taken_so_they_can_pick_a_free_time()
    {
        var day = NewAppDay();
        var session = await RequestAsync(Editor, day);
        await PostOkAsync(AppOperator, session.Id, "confirm");
        await RequestAsync(Editor, day.AddHours(4));
        var client = await api.SignedInClientAsync("producer@pbrms.test");

        var busy = await client.GetFromJsonAsync<List<AppBusyTime>>($"/app-room/busy?from={day:yyyy-MM-dd}&to={day:yyyy-MM-dd}", Json);

        Assert.Equal(day, Assert.Single(busy!).StartsAt);
    }

    [Fact]
    public async Task APP_cant_be_booked_on_the_normal_booking_form()
    {
        var day = NewAppDay();
        var units = await api.UnitsAsync();
        var producer = await api.SignedInClientAsync("producer@pbrms.test");

        var response = await producer.PostAsJsonAsync("/bookings", new
        {
            title = "Voice-over",
            departmentId = units["News"],
            activityType = "Editing",
            venue = "App",
            callAt = day,
            startsAt = day,
            endsAt = day.AddHours(1)
        }, Json);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("APP operator", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task The_booking_officer_cant_move_a_production_into_the_APP_room()
    {
        var booking = await api.AtFinalisationAsync(NewAppDay(), Venue.Studio1);
        var officer = await api.SignedInClientAsync(BookingOfficer);

        var response = await officer.PutAsJsonAsync($"/bookings/{booking.Id}/schedule",
            new PBRMS.Api.Endpoints.ScheduleRequest(Venue.App, null, booking.CallAt, booking.StartsAt, booking.EndsAt), Json);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task A_walk_in_is_confirmed_straight_away_with_nothing_for_the_operator_to_do()
    {
        var operatorId = await AppOperatorIdAsync();

        var walkIn = await WalkInOkAsync(operatorId, "Urgent News edit");

        Assert.Equal(AppSessionStatus.Confirmed, walkIn.Status);
        Assert.True(walkIn.IsWalkIn);
        Assert.Contains(await api.NotificationsAsync(AppOperator), n => n.Text.Contains("Urgent News edit") && n.Text.Contains("in APP now"));
        Assert.DoesNotContain(await api.NotificationsAsync(AppOperator), n => n.Text.Contains("Urgent News edit") && n.Text.Contains("Please confirm"));

        await PostOkAsync(Editor, walkIn.Id, "finish");
    }

    [Fact]
    public async Task Other_producers_can_see_APP_is_busy_right_now_and_until_when()
    {
        var walkIn = await WalkInOkAsync(await AppOperatorIdAsync(), "Sports promo");
        var otherProducer = await api.SignedInClientAsync("producer.sports@pbrms.test");

        var busy = await otherProducer.GetFromJsonAsync<AppRoomStatus>("/app-room/status", Json);
        Assert.True(busy!.Busy);
        Assert.Equal("Sports promo", busy.Production);
        Assert.Equal(walkIn.EndsAt, busy.Until);

        // The person using the room says they've finished, and it shows free straight away.
        await PostOkAsync(Editor, walkIn.Id, "finish");

        var free = await otherProducer.GetFromJsonAsync<AppRoomStatus>("/app-room/status", Json);
        Assert.False(free!.Busy);
    }

    [Fact]
    public async Task A_walk_in_is_refused_if_the_room_is_taken()
    {
        var operatorId = await AppOperatorIdAsync();
        var first = await WalkInOkAsync(operatorId, "First in");
        var second = await api.SignedInClientAsync("producer@pbrms.test");

        var response = await second.PostAsJsonAsync("/app-sessions/walk-in", new AppWalkInForm(operatorId, "Second in", DateTimeOffset.UtcNow.AddHours(1), null), Json);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        await PostOkAsync(Editor, first.Id, "finish");
    }

    [Fact]
    public async Task Only_the_person_who_booked_can_finish_a_session_not_the_operator()
    {
        var walkIn = await WalkInOkAsync(await AppOperatorIdAsync(), "Not the operator's job");
        var appOperator = await api.SignedInClientAsync(AppOperator);

        var response = await appOperator.PostAsync($"/app-sessions/{walkIn.Id}/finish", null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        await PostOkAsync(Editor, walkIn.Id, "finish");
    }

    [Fact]
    public async Task Walk_ins_show_in_the_report()
    {
        var day = DateTimeOffset.UtcNow.ToOffset(NbcTime.Offset);
        var walkIn = await WalkInOkAsync(await AppOperatorIdAsync(), $"Walk-in {Guid.NewGuid():N}"[..20]);
        var manager = await api.SignedInClientAsync("tvops.manager@pbrms.test");

        var report = await manager.GetFromJsonAsync<AppUsageReport>($"/reports/app-usage?from={day:yyyy-MM-dd}&to={day.AddDays(1):yyyy-MM-dd}", Json);

        Assert.True(report!.Sessions.Single(s => s.Production == walkIn.Production).WalkIn);
        Assert.Equal(1, report.ByProduction.Single(t => t.Name == walkIn.Production).WalkIns);
        await PostOkAsync(Editor, walkIn.Id, "finish");
    }

    [Fact]
    public async Task A_confirmed_session_is_on_the_daily_roster()
    {
        var day = NewAppDay();
        var session = await RequestAsync(Editor, day);
        await PostOkAsync(AppOperator, session.Id, "confirm");
        var client = await api.SignedInClientAsync(BookingOfficer);

        var sheet = await client.GetFromJsonAsync<DailyRosterView>($"/daily-roster/{day:yyyy-MM-dd}", Json);

        var line = sheet!.Lines.Single(l => l.Area == "APP");
        Assert.Equal(session.Production, line.Title);
        Assert.Equal("APP", Assert.Single(line.Crew).Division);
    }

    [Fact]
    public async Task A_cancelled_session_frees_the_operator_and_tells_the_other_person()
    {
        var day = NewAppDay();
        var session = await RequestAsync(Editor, day);
        await PostOkAsync(AppOperator, session.Id, "confirm");

        await PostOkAsync(Editor, session.Id, "cancel", new AppNoteRequest("Edit finished early"));

        Assert.Contains(await api.NotificationsAsync(AppOperator), n => n.Text.Contains(session.Production) && n.Text.Contains("cancelled"));
        var again = await RequestAsync(Editor, day);
        Assert.Equal(AppSessionStatus.Confirmed, (await PostOkAsync(AppOperator, again.Id, "confirm")).Status);
    }

    [Fact]
    public async Task The_report_shows_which_productions_editors_and_producers_used_APP()
    {
        var day = NewAppDay();
        var byEditor = await RequestAsync(Editor, day, "Report Show");
        var byProducer = await RequestAsync("producer@pbrms.test", day.AddHours(3), "Report Show");
        var notConfirmed = await RequestAsync(Editor, day.AddHours(6), "Never Happened");
        await PostOkAsync(AppOperator, byEditor.Id, "confirm");
        await PostOkAsync(AppOperator, byProducer.Id, "confirm");

        foreach (var reader in new[] { "supervisor.sound@pbrms.test", "tvops.manager@pbrms.test" })
        {
            var client = await api.SignedInClientAsync(reader);
            var report = await client.GetFromJsonAsync<AppUsageReport>($"/reports/app-usage?from={day:yyyy-MM-dd}&to={day:yyyy-MM-dd}", Json);

            Assert.Equal(2, report!.Sessions.Count);
            Assert.Contains(report.Sessions, s => s.RequesterRole == Role.Editor);
            Assert.Contains(report.Sessions, s => s.RequesterRole == Role.Producer);
            Assert.Equal((2, 240), (report.ByProduction.Single().Sessions, report.ByProduction.Single().Minutes));
            Assert.Equal(2, report.ByRequester.Count);
        }

        var producer = await api.SignedInClientAsync("producer@pbrms.test");
        Assert.Equal(HttpStatusCode.Forbidden, (await producer.GetAsync($"/reports/app-usage?from={day:yyyy-MM-dd}&to={day:yyyy-MM-dd}")).StatusCode);
    }

    // ---- Helpers ----

    private static int dayCounter;

    /// <summary>10h00 on a day no other test uses.</summary>
    private static DateTimeOffset NewAppDay() =>
        new DateTimeOffset(2036, 1, 1, 10, 0, 0, NbcTime.Offset).AddDays(Interlocked.Increment(ref dayCounter));

    /// <summary>The sample APP operator, found by email because some tests add more.</summary>
    private async Task<Guid> AppOperatorIdAsync()
    {
        Guid id = default;
        await api.WithDatabaseAsync(async db =>
        {
            id = await db.Users.Where(u => u.WorkEmail == AppOperator).Select(u => u.Id).SingleAsync();
        });
        return id;
    }

    /// <summary>The editor checks in to the room now. Each test finishes it, so the next finds the room free.</summary>
    private async Task<AppSessionResponse> WalkInOkAsync(Guid operatorId, string production)
    {
        var client = await api.SignedInClientAsync(Editor);
        var response = await client.PostAsJsonAsync("/app-sessions/walk-in", new AppWalkInForm(operatorId, production, DateTimeOffset.UtcNow.AddHours(1), null), Json);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<AppSessionResponse>(Json))!;
    }

    /// <summary>Two new APP operators, for the times NBC has a second one in.</summary>
    private async Task<(Guid, Guid)> TwoAppOperatorsAsync()
    {
        var (first, second) = (await api.NewOperatorAsync("Sound"), await api.NewOperatorAsync("Sound"));
        await api.WithDatabaseAsync(async db =>
        {
            db.RoleAssignments.AddRange(
                new RoleAssignment { UserId = first, Role = Role.AppOperator },
                new RoleAssignment { UserId = second, Role = Role.AppOperator });
            await db.SaveChangesAsync();
        });
        return (first, second);
    }

    private async Task<AppSessionResponse> RequestWithAsync(Guid operatorId, DateTimeOffset start)
    {
        var client = await api.SignedInClientAsync(Editor);
        var response = await client.PostAsJsonAsync("/app-sessions", new AppSessionForm(operatorId, $"Voice-over {start:HHmm}", start, start.AddHours(2), null), Json);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<AppSessionResponse>(Json))!;
    }

    private async Task<HttpResponseMessage> SendRequestAsync(string email, DateTimeOffset start, string production = "")
    {
        var operatorId = await AppOperatorIdAsync();
        var client = await api.SignedInClientAsync(email);
        var title = production.Length > 0 ? production : $"Voice-over {Guid.NewGuid():N}"[..20];
        return await client.PostAsJsonAsync("/app-sessions", new AppSessionForm(operatorId, title, start, start.AddHours(2), null), Json);
    }

    private async Task<AppSessionResponse> RequestAsync(string email, DateTimeOffset start, string production = "")
    {
        var response = await SendRequestAsync(email, start, production);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<AppSessionResponse>(Json))!;
    }

    private async Task<AppSessionResponse> PostOkAsync(string email, Guid id, string action, object? body = null)
    {
        var client = await api.SignedInClientAsync(email);
        var url = $"/app-sessions/{id}/{action}";
        var response = body is null ? await client.PostAsync(url, null) : await client.PostAsJsonAsync(url, body, Json);
        Assert.True(response.IsSuccessStatusCode, $"{email} {action}: {await response.Content.ReadAsStringAsync()}");
        return (await response.Content.ReadFromJsonAsync<AppSessionResponse>(Json))!;
    }
}
