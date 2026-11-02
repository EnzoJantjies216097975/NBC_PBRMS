using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using PBRMS.Api.Endpoints;
using PBRMS.Data;
using PBRMS.Domain;
using static PBRMS.Tests.TestSteps;

namespace PBRMS.Tests;

/// <summary>
/// What happens once a booking is confirmed: who is told, replacing crew, the producer
/// marking start and end, and the overtime alert. Sample people: News producer and
/// EP; Camera supervisor; the Booking Officer.
/// </summary>
[Collection("API")]
public class ProductionDayTests(TestApi api)
{
    private const string Supervisor = "supervisor@pbrms.test"; // of Camera
    private const string Producer = "producer@pbrms.test";
    private const string BookingOfficer = "booking@pbrms.test";

    [Fact]
    public async Task Confirming_tells_the_crew_the_producer_and_the_EP()
    {
        var op = await api.NewOperatorAsync("Camera");
        var booking = await api.AtCrewStageAsync(NewDay(), Venue.Studio1, "Camera");
        await api.AssignOkAsync(booking, "Camera", op);

        await api.TransitionAsync(BookingOfficer, booking.Id, BookingAction.Approve);

        foreach (var person in new[] { OperatorEmail(op), Producer, "ep@pbrms.test" })
            Assert.Contains(await api.NotificationsAsync(person), n => n.Text.Contains($"#{booking.ProductionNo}") && n.Text.Contains("is confirmed"));
    }

    [Fact]
    public async Task A_confirmed_operator_can_be_replaced_and_everyone_affected_is_told()
    {
        var (first, second) = (await api.NewOperatorAsync("Camera"), await api.NewOperatorAsync("Camera"));
        var booking = await ConfirmedAsync(first);

        var changed = await api.AssignOkAsync(booking, "Camera", second);

        Assert.Equal(second, changed.CrewSlots[0].OperatorId);
        Assert.Equal(BookingStatus.Confirmed, changed.Status);
        Assert.Contains(await api.NotificationsAsync(OperatorEmail(first)), n => n.Text.Contains("no longer on"));
        Assert.Contains(await api.NotificationsAsync(OperatorEmail(second)), n => n.Text.Contains("added to"));
        Assert.Contains(await api.NotificationsAsync(Producer), n => n.Text.Contains($"#{booking.ProductionNo}") && n.Text.Contains("crew"));
    }

    [Fact]
    public async Task A_confirmed_position_cant_be_left_empty()
    {
        var booking = await ConfirmedAsync(await api.NewOperatorAsync("Camera"));
        var bookingOfficer = await api.SignedInClientAsync(BookingOfficer);

        var response = await bookingOfficer.PutAsJsonAsync($"/bookings/{booking.Id}/crew", new CrewRequest([new(booking.CrewSlots[0].Id, null)]), Json);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Moving_a_booking_to_another_studio_tells_the_supervisors_to_make_changes()
    {
        var booking = await api.AtFinalisationAsync(NewDay(), Venue.Studio1);
        var bookingOfficer = await api.SignedInClientAsync(BookingOfficer);

        await bookingOfficer.PutAsJsonAsync($"/bookings/{booking.Id}/schedule",
            new ScheduleRequest(Venue.Studio2, null, booking.CallAt, booking.StartsAt, booking.EndsAt), Json);

        Assert.Contains(await api.NotificationsAsync(Supervisor), n => n.Text.Contains($"#{booking.ProductionNo}") && n.Text.Contains("make the changes"));
    }

    [Fact]
    public async Task The_producer_marks_the_start_and_end_and_the_times_are_recorded()
    {
        var booking = await ConfirmedAsync(await api.NewOperatorAsync("Camera"));

        var started = await api.TransitionAsync(Producer, booking.Id, BookingAction.Start);
        Assert.Equal(BookingStatus.InProgress, started.Status);
        Assert.NotNull(started.ActualStartAt);

        var ended = await api.TransitionAsync(Producer, booking.Id, BookingAction.End);
        Assert.Equal(BookingStatus.Completed, ended.Status);
        Assert.NotNull(ended.ActualEndAt);
    }

    [Fact]
    public async Task Only_the_producer_can_mark_the_start()
    {
        var booking = await ConfirmedAsync(await api.NewOperatorAsync("Camera"));
        var bookingOfficer = await api.SignedInClientAsync(BookingOfficer);

        var response = await bookingOfficer.PostAsJsonAsync($"/bookings/{booking.Id}/transition", new TransitionRequest(BookingAction.Start, null, null, null), Json);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Supervisors_are_told_once_when_a_production_goes_overtime()
    {
        var booking = await ConfirmedAsync(await api.NewOperatorAsync("Camera"));
        await api.TransitionAsync(Producer, booking.Id, BookingAction.Start);

        // Running 29 minutes over isn't overtime yet...
        await CheckOvertimeAtAsync(booking.EndsAt.AddMinutes(29));
        Assert.Empty(await OvertimeAlertsAsync(Supervisor, booking));

        // ...31 minutes is...
        await CheckOvertimeAtAsync(booking.EndsAt.AddMinutes(31));
        Assert.Single(await OvertimeAlertsAsync(Supervisor, booking));

        // ...and later checks don't repeat it.
        await CheckOvertimeAtAsync(booking.EndsAt.AddHours(2));
        Assert.Single(await OvertimeAlertsAsync(Supervisor, booking));
    }

    [Fact]
    public async Task A_production_that_has_ended_doesnt_go_overtime()
    {
        var booking = await ConfirmedAsync(await api.NewOperatorAsync("Camera"));
        await api.TransitionAsync(Producer, booking.Id, BookingAction.Start);
        await api.TransitionAsync(Producer, booking.Id, BookingAction.End);

        await CheckOvertimeAtAsync(booking.EndsAt.AddHours(1));

        Assert.Empty(await OvertimeAlertsAsync(Supervisor, booking));
    }

    [Fact]
    public async Task A_studio_production_takes_an_operator_off_an_outside_production_still_being_crewed_and_everyone_is_told()
    {
        var start = NewDay();
        var op = await api.NewOperatorAsync("Camera");
        var outside = await api.AtCrewStageAsync(start, Venue.Location, "Camera", "Sound"); // Sound keeps it unconfirmed
        await api.AssignOkAsync(outside, "Camera", op);
        var studio = await api.AtCrewStageAsync(start.AddHours(1), Venue.Studio1, "Camera");

        // First a warning, and nothing changes.
        var warned = await api.AssignAsync(studio, "Camera", op);
        Assert.Contains("needsAcknowledgement", await warned.Content.ReadAsStringAsync());

        var taken = await api.AssignAsync(studio, "Camera", op, acknowledge: true);
        Assert.Equal(HttpStatusCode.OK, taken.StatusCode);

        var client = await api.SignedInClientAsync(Producer);
        var after = await client.GetFromJsonAsync<BookingResponse>($"/bookings/{outside.Id}", Json);
        Assert.Equal(BookingStatus.CrewAssignment, after!.Status);
        Assert.Null(after.CrewSlots.Single(s => s.Division == "Camera").OperatorId);

        Assert.Contains(await api.NotificationsAsync(Supervisor), n => n.BookingId == outside.Id && n.Text.Contains("needs someone else"));
        Assert.Contains(await api.NotificationsAsync(Producer), n => n.BookingId == outside.Id && n.Text.Contains("needs someone else"));
        Assert.Contains(await api.NotificationsAsync(OperatorEmail(op)), n => n.Text.Contains("moved from"));
    }

    [Fact]
    public async Task A_studio_cant_take_an_operator_off_a_confirmed_outside_production()
    {
        // The supervisor phoned them before deciding, so that decision stands.
        var start = NewDay();
        var op = await api.NewOperatorAsync("Camera");
        var outside = await api.AtCrewStageAsync(start, Venue.Location, "Camera");
        await api.AssignOkAsync(outside, "Camera", op);
        var studio = await api.AtCrewStageAsync(start, Venue.Studio1, "Camera");

        var response = await api.AssignAsync(studio, "Camera", op, acknowledge: true);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task A_studio_cant_take_an_operator_off_a_production_already_running()
    {
        var start = NewDay();
        var op = await api.NewOperatorAsync("Camera");
        var outside = await api.AtCrewStageAsync(start, Venue.Location, "Camera");
        await api.AssignOkAsync(outside, "Camera", op);
        await api.TransitionAsync(Producer, outside.Id, BookingAction.Start);
        var studio = await api.AtCrewStageAsync(start, Venue.Studio1, "Camera");

        var response = await api.AssignAsync(studio, "Camera", op, acknowledge: true);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task An_outside_production_cant_take_an_operator_off_a_studio()
    {
        var start = NewDay();
        var op = await api.NewOperatorAsync("Camera");
        var studio = await api.AtCrewStageAsync(start, Venue.Studio1, "Camera");
        await api.AssignOkAsync(studio, "Camera", op);
        var outside = await api.AtCrewStageAsync(start, Venue.Location, "Camera");

        var response = await api.AssignAsync(outside, "Camera", op, acknowledge: true);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Outside_crew_has_to_wait_until_a_studio_at_that_time_is_filled()
    {
        var start = NewDay();
        var studio = await api.AtCrewStageAsync(start, Venue.Studio2, "Camera");
        var outside = await api.AtCrewStageAsync(start, Venue.Location, "Camera");
        var op = await api.NewOperatorAsync("Camera");

        // Refused, and acknowledging doesn't get round it. The studio is named.
        var refused = await api.AssignAsync(outside, "Camera", op, acknowledge: true);
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        var body = await refused.Content.ReadAsStringAsync();
        Assert.DoesNotContain("needsAcknowledgement", body);
        Assert.Contains(studio.ProductionNo.ToString(), body);

        // Once the studio is filled, the outside production can be booked.
        await api.AssignOkAsync(studio, "Camera", await api.NewOperatorAsync("Camera"));
        Assert.Equal(HttpStatusCode.OK, (await api.AssignAsync(outside, "Camera", op)).StatusCode);
    }

    [Fact]
    public async Task A_short_studio_only_holds_up_its_own_division_at_its_own_time()
    {
        var start = NewDay();
        await api.AtCrewStageAsync(start, Venue.Studio2, "Camera");
        var otherDivision = await api.AtCrewStageAsync(start, Venue.Location, "Sound");
        var otherTime = await api.AtCrewStageAsync(start.AddHours(5), Venue.Location, "Camera");

        Assert.Equal(HttpStatusCode.OK, (await api.AssignAsync(otherDivision, "Sound", await api.NewOperatorAsync("Sound"))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await api.AssignAsync(otherTime, "Camera", await api.NewOperatorAsync("Camera"))).StatusCode);
    }

    [Fact]
    public async Task People_only_see_and_mark_their_own_notifications()
    {
        var op = await api.NewOperatorAsync("Camera");
        var booking = await ConfirmedAsync(op);
        var mine = (await api.NotificationsAsync(OperatorEmail(op))).First(n => n.BookingId == booking.Id);
        var someoneElse = await api.SignedInClientAsync("operator@pbrms.test");
        var owner = await api.SignedInClientAsync(OperatorEmail(op));

        Assert.Equal(HttpStatusCode.NotFound, (await someoneElse.PostAsync($"/notifications/{mine.Id}/read", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await owner.PostAsync($"/notifications/{mine.Id}/read", null)).StatusCode);
        Assert.NotNull((await api.NotificationsAsync(OperatorEmail(op))).First(n => n.Id == mine.Id).ReadAt);
    }

    [Fact]
    public async Task The_crew_and_producer_are_called_once_an_hour_before_the_start()
    {
        var op = await api.NewOperatorAsync("Camera");
        var booking = await ConfirmedAsync(op);

        await CheckTheDayAtAsync(booking.StartsAt.AddMinutes(-61));
        Assert.Empty(await CallsAsync(OperatorEmail(op), booking));

        await CheckTheDayAtAsync(booking.StartsAt.AddMinutes(-60));
        await CheckTheDayAtAsync(booking.StartsAt.AddMinutes(-30));

        Assert.Single(await CallsAsync(OperatorEmail(op), booking));
        Assert.Single(await CallsAsync(Producer, booking));
    }

    [Fact]
    public async Task The_producer_marks_who_has_arrived_and_can_undo_a_mistake()
    {
        var booking = await ConfirmedAsync(await api.NewOperatorAsync("Camera"));
        var producer = await api.SignedInClientAsync(Producer);
        var slot = booking.CrewSlots[0].Id;

        var arrived = await ReadAsync(await producer.PostAsJsonAsync($"/bookings/{booking.Id}/attendance", new AttendanceRequest(slot, true), Json));
        Assert.NotNull(arrived.CrewSlots[0].ArrivedAt);

        var undone = await ReadAsync(await producer.PostAsJsonAsync($"/bookings/{booking.Id}/attendance", new AttendanceRequest(slot, false), Json));
        Assert.Null(undone.CrewSlots[0].ArrivedAt);
    }

    [Fact]
    public async Task Attendance_is_the_producers_and_only_on_a_confirmed_production()
    {
        var unconfirmed = await api.AtFinalisationAsync(NewDay(), Venue.Studio3);
        var confirmed = await ConfirmedAsync(await api.NewOperatorAsync("Camera"));
        var producer = await api.SignedInClientAsync(Producer);
        var bookingOfficer = await api.SignedInClientAsync(BookingOfficer);

        var tooEarly = await producer.PostAsJsonAsync($"/bookings/{unconfirmed.Id}/attendance", new AttendanceRequest(unconfirmed.CrewSlots[0].Id, true), Json);
        var notTheirs = await bookingOfficer.PostAsJsonAsync($"/bookings/{confirmed.Id}/attendance", new AttendanceRequest(confirmed.CrewSlots[0].Id, true), Json);

        Assert.Equal(HttpStatusCode.Conflict, tooEarly.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, notTheirs.StatusCode);
    }

    [Fact]
    public async Task A_signal_reaches_the_operator_who_hasnt_arrived_and_is_recorded()
    {
        var op = await api.NewOperatorAsync("Camera");
        var booking = await ConfirmedAsync(op);
        var producer = await api.SignedInClientAsync(Producer);

        var signalled = await ReadAsync(await producer.PostAsJsonAsync($"/bookings/{booking.Id}/signal", new SignalRequest(booking.CrewSlots[0].Id), Json));

        Assert.NotNull(signalled.CrewSlots[0].SignalledAt);
        Assert.Contains(await api.NotificationsAsync(OperatorEmail(op)), n => n.BookingId == booking.Id && n.Text.StartsWith("URGENT"));
    }

    [Fact]
    public async Task Someone_who_has_arrived_cant_be_signalled()
    {
        var booking = await ConfirmedAsync(await api.NewOperatorAsync("Camera"));
        var producer = await api.SignedInClientAsync(Producer);
        var slot = booking.CrewSlots[0].Id;
        await producer.PostAsJsonAsync($"/bookings/{booking.Id}/attendance", new AttendanceRequest(slot, true), Json);

        var response = await producer.PostAsJsonAsync($"/bookings/{booking.Id}/signal", new SignalRequest(slot), Json);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task The_producer_confirms_a_location_shoot_has_left_and_it_isnt_flagged()
    {
        var booking = await ConfirmedShootAsync();
        var producer = await api.SignedInClientAsync(Producer);

        var left = await ReadAsync(await producer.PostAsync($"/bookings/{booking.Id}/departure", null));
        await CheckTheDayAtAsync(booking.StartsAt.AddHours(3));

        Assert.NotNull(left.DepartedAt);
        Assert.Empty(await NeverLeftAlertsAsync(Supervisor, booking));
    }

    [Fact]
    public async Task A_studio_production_doesnt_leave()
    {
        var booking = await ConfirmedAsync(await api.NewOperatorAsync("Camera"));
        var producer = await api.SignedInClientAsync(Producer);

        Assert.Equal(HttpStatusCode.Conflict, (await producer.PostAsync($"/bookings/{booking.Id}/departure", null)).StatusCode);

        await CheckTheDayAtAsync(booking.StartsAt.AddHours(3));
        Assert.Empty(await NeverLeftAlertsAsync(Supervisor, booking));
    }

    [Fact]
    public async Task Supervisors_and_the_producer_are_told_once_when_a_shoot_never_left()
    {
        var booking = await ConfirmedShootAsync();

        await CheckTheDayAtAsync(booking.StartsAt.AddMinutes(119));
        Assert.Empty(await NeverLeftAlertsAsync(Supervisor, booking));

        await CheckTheDayAtAsync(booking.StartsAt.AddHours(2));
        await CheckTheDayAtAsync(booking.StartsAt.AddHours(3));

        Assert.Single(await NeverLeftAlertsAsync(Supervisor, booking));
        Assert.Single(await NeverLeftAlertsAsync(Producer, booking));
        Assert.NotNull((await LoadAsync(booking)).DepartureMissedAt);
    }

    [Fact]
    public async Task A_shoot_the_producer_marked_as_started_has_clearly_left()
    {
        var booking = await ConfirmedShootAsync();
        await api.TransitionAsync(Producer, booking.Id, BookingAction.Start);

        await CheckTheDayAtAsync(booking.StartsAt.AddHours(3));

        Assert.Empty(await NeverLeftAlertsAsync(Supervisor, booking));
    }

    [Fact]
    public async Task Confirming_late_that_a_flagged_shoot_left_clears_the_flag_and_tells_the_supervisors()
    {
        var booking = await ConfirmedShootAsync();
        await CheckTheDayAtAsync(booking.StartsAt.AddHours(2));
        var producer = await api.SignedInClientAsync(Producer);

        var left = await ReadAsync(await producer.PostAsync($"/bookings/{booking.Id}/departure", null));

        Assert.Null(left.DepartureMissedAt);
        Assert.Contains(await api.NotificationsAsync(Supervisor), n => n.BookingId == booking.Id && n.Text.Contains("has left after all"));
    }

    [Fact]
    public async Task The_supervisor_can_release_the_crew_of_a_flagged_shoot_but_not_before_it_is_flagged()
    {
        var op = await api.NewOperatorAsync("Camera");
        var booking = await ConfirmedShootAsync(op);
        var supervisor = await api.SignedInClientAsync(Supervisor);
        var otherDivision = await api.SignedInClientAsync("supervisor.sound@pbrms.test");
        var cancel = new TransitionRequest(BookingAction.Cancel, "Never left", null, null);

        var tooEarly = await supervisor.PostAsJsonAsync($"/bookings/{booking.Id}/transition", cancel, Json);
        await CheckTheDayAtAsync(booking.StartsAt.AddHours(2));
        var notTheirs = await otherDivision.PostAsJsonAsync($"/bookings/{booking.Id}/transition", cancel, Json);
        var released = await supervisor.PostAsJsonAsync($"/bookings/{booking.Id}/transition", cancel, Json);

        Assert.Equal(HttpStatusCode.Forbidden, tooEarly.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, notTheirs.StatusCode);
        Assert.Equal(BookingStatus.Cancelled, (await ReadAsync(released)).Status);
    }

    [Fact]
    public async Task Cancelling_a_confirmed_production_tells_its_crew_and_producer()
    {
        var op = await api.NewOperatorAsync("Camera");
        var booking = await ConfirmedAsync(op);

        await api.TransitionAsync(BookingOfficer, booking.Id, BookingAction.Cancel, "Presenter ill");

        Assert.Contains(await api.NotificationsAsync(OperatorEmail(op)), n => n.BookingId == booking.Id && n.Text.Contains("has been cancelled") && n.Text.Contains("Presenter ill"));
        Assert.Contains(await api.NotificationsAsync(Producer), n => n.BookingId == booking.Id && n.Text.Contains("has been cancelled"));
    }

    // ---- Helpers ----

    /// <summary>A location shoot on a day of its own, confirmed by its Camera supervisor.</summary>
    private async Task<BookingResponse> ConfirmedShootAsync(Guid? operatorId = null)
    {
        var booking = await api.AtCrewStageAsync(NewDay(), Venue.Location, "Camera");
        return await api.AssignOkAsync(booking, "Camera", operatorId ?? await api.NewOperatorAsync("Camera"));
    }

    private static async Task<BookingResponse> ReadAsync(HttpResponseMessage response)
    {
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<BookingResponse>(Json))!;
    }

    private async Task<BookingResponse> LoadAsync(BookingResponse booking)
    {
        var producer = await api.SignedInClientAsync(Producer);
        return (await producer.GetFromJsonAsync<BookingResponse>($"/bookings/{booking.Id}", Json))!;
    }

    private async Task CheckTheDayAtAsync(DateTimeOffset now)
    {
        using var scope = api.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<ProductionDayWatcher>().CheckAsync(now);
    }

    private async Task<List<NotificationEndpoints.NotificationResponse>> CallsAsync(string email, BookingResponse booking) =>
        (await api.NotificationsAsync(email)).Where(n => n.BookingId == booking.Id && n.Text.StartsWith("Call:")).ToList();

    private async Task<List<NotificationEndpoints.NotificationResponse>> NeverLeftAlertsAsync(string email, BookingResponse booking) =>
        (await api.NotificationsAsync(email)).Where(n => n.BookingId == booking.Id && n.Text.Contains("hasn't been confirmed as leaving")).ToList();

    /// <summary>A confirmed booking on a day of its own, in a studio, with this operator on its one Camera slot.</summary>
    private async Task<BookingResponse> ConfirmedAsync(Guid operatorId)
    {
        var booking = await api.AtCrewStageAsync(NewDay(), Venue.Studio3, "Camera");
        await api.AssignOkAsync(booking, "Camera", operatorId);
        return await api.TransitionAsync(BookingOfficer, booking.Id, BookingAction.Approve);
    }

    /// <summary>Runs the check the API runs every minute, as if it were this time.</summary>
    private async Task CheckOvertimeAtAsync(DateTimeOffset now)
    {
        using var scope = api.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<OvertimeWatcher>().CheckAsync(now);
    }

    private async Task<List<NotificationEndpoints.NotificationResponse>> OvertimeAlertsAsync(string email, BookingResponse booking) =>
        (await api.NotificationsAsync(email)).Where(n => n.BookingId == booking.Id && n.Text.Contains("gone overtime")).ToList();
}
