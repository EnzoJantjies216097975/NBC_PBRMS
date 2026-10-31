using System.Net;
using System.Net.Http.Json;
using PBRMS.Api.Endpoints;
using PBRMS.Domain;
using static PBRMS.Tests.TestSteps;

namespace PBRMS.Tests;

/// <summary>
/// The last stages of the approval chain through the real API: supervisors assign
/// crew, the Booking Officer resolves clashes and confirms. The main rule being tested
/// is BR-06: nobody and no studio can be double-booked on a confirmed booking.
///
/// Every test uses its own new operators and its own day, so tests can't clash with
/// each other. Sample people: News producer and EP; Camera and Sound supervisors;
/// the Booking Officer.
/// </summary>
[Collection("API")]
public class CrewTests(TestApi api)
{
    [Fact]
    public async Task A_booking_goes_all_the_way_to_confirmed_and_the_producer_gets_the_crew_phone_numbers()
    {
        var (start, cameraOp, soundOp) = (NewDay(), await api.NewOperatorAsync("Camera"), await api.NewOperatorAsync("Sound"));
        var booking = await api.AtCrewStageAsync(start, Venue.Studio1, "Camera", "Sound");

        var afterCamera = await api.AssignOkAsync(booking, "Camera", cameraOp);
        Assert.Equal(BookingStatus.CrewAssignment, afterCamera.Status); // Sound still to come

        var afterSound = await api.AssignOkAsync(booking, "Sound", soundOp);
        Assert.Equal(BookingStatus.Finalisation, afterSound.Status); // last slot filled: on to the Booking Officer
        Assert.All(afterSound.CrewSlots, s => Assert.Null(s.OperatorPhone)); // no phone numbers until the crew is final

        var confirmed = await api.TransitionAsync("booking@pbrms.test", booking.Id, BookingAction.Approve);

        Assert.Equal(BookingStatus.Confirmed, confirmed.Status);
        Assert.All(confirmed.CrewSlots, s => Assert.Equal("+264 81 000 0000", s.OperatorPhone));
    }

    [Fact]
    public async Task A_supervisor_can_only_assign_in_their_own_division()
    {
        var booking = await api.AtCrewStageAsync(NewDay(), Venue.Location, "Camera");

        var response = await api.AssignAsync("supervisor.sound@pbrms.test", booking, "Camera", await api.NewOperatorAsync("Camera"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task An_operator_from_another_division_is_refused()
    {
        var booking = await api.AtCrewStageAsync(NewDay(), Venue.Studio1, "Camera");

        var response = await api.AssignAsync(booking, "Camera", await api.NewOperatorAsync("Sound"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task An_operator_already_on_another_production_is_refused_and_the_production_is_named()
    {
        var start = NewDay();
        var op = await api.NewOperatorAsync("Camera");
        var first = await api.AtCrewStageAsync(start, Venue.Studio1, "Camera");
        await api.AssignOkAsync(first, "Camera", op);
        var second = await api.AtCrewStageAsync(start.AddHours(1), Venue.Studio2, "Camera");

        var response = await api.AssignAsync(second, "Camera", op);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains(first.ProductionNo.ToString(), await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task An_operator_can_go_straight_from_one_production_to_the_next()
    {
        var start = NewDay();
        var op = await api.NewOperatorAsync("Camera");
        var morning = await api.AtCrewStageAsync(start, Venue.Studio1, "Camera");
        await api.AssignOkAsync(morning, "Camera", op);
        var afternoon = await api.AtCrewStageAsync(start.AddHours(2), Venue.Studio1, "Camera"); // starts as the first ends

        var response = await api.AssignAsync(afternoon, "Camera", op);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Two_supervisors_assigning_the_same_operator_at_once_give_one_assignment_and_one_clash()
    {
        // The Implementation Plan's concurrency test. Repeated, because a race
        // that is only sometimes lost would pass a single attempt.
        for (var round = 0; round < 5; round++)
        {
            var start = NewDay();
            var op = await api.NewOperatorAsync("Camera");
            var first = await api.AtCrewStageAsync(start, Venue.Location, "Camera");
            var second = await api.AtCrewStageAsync(start, Venue.Location, "Camera");
            var supervisor = await api.SignedInClientAsync("supervisor@pbrms.test");
            var otherSupervisor = await api.SignedInClientAsync("supervisor@pbrms.test");

            var responses = await Task.WhenAll(
                supervisor.PutAsJsonAsync($"/bookings/{first.Id}/crew", new CrewRequest([new(first.CrewSlots[0].Id, op)]), Json),
                otherSupervisor.PutAsJsonAsync($"/bookings/{second.Id}/crew", new CrewRequest([new(second.CrewSlots[0].Id, op)]), Json));

            Assert.Equal(
                new[] { HttpStatusCode.OK, HttpStatusCode.Conflict },
                responses.Select(r => r.StatusCode).Order());
        }
    }

    [Fact]
    public async Task A_cancelled_booking_frees_its_crew()
    {
        var start = NewDay();
        var op = await api.NewOperatorAsync("Camera");
        var cancelled = await api.AtCrewStageAsync(start, Venue.Studio1, "Camera");
        await api.AssignOkAsync(cancelled, "Camera", op);
        await api.TransitionAsync("booking@pbrms.test", cancelled.Id, BookingAction.Cancel, "Event called off");
        var other = await api.AtCrewStageAsync(start, Venue.Studio2, "Camera");

        var response = await api.AssignAsync(other, "Camera", op);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Changing_the_operator_keeps_the_old_assignment_on_record()
    {
        var (first, second) = (await api.NewOperatorAsync("Camera"), await api.NewOperatorAsync("Camera"));
        var booking = await api.AtCrewStageAsync(NewDay(), Venue.Studio1, "Camera");
        await api.AssignOkAsync(booking, "Camera", first);

        var changed = await api.AssignOkAsync(booking, "Camera", second);

        Assert.Equal(second, changed.CrewSlots[0].OperatorId);
        await api.WithDatabaseAsync(async db =>
        {
            var old = db.CrewAssignments.Single(a => a.CrewSlotId == booking.CrewSlots[0].Id && a.OperatorId == first);
            Assert.NotNull(old.RemovedAt);
            await Task.CompletedTask;
        });
    }

    [Fact]
    public async Task A_studio_clash_blocks_confirming_until_the_booking_officer_moves_one_of_them()
    {
        var start = NewDay();
        var confirmedFirst = await api.AtFinalisationAsync(start, Venue.Studio3);
        await api.TransitionAsync("booking@pbrms.test", confirmedFirst.Id, BookingAction.Approve);
        var clashing = await api.AtFinalisationAsync(start.AddHours(1), Venue.Studio3);
        var bookingOfficer = await api.SignedInClientAsync("booking@pbrms.test");

        var refused = await bookingOfficer.PostAsJsonAsync($"/bookings/{clashing.Id}/transition", new TransitionRequest(BookingAction.Approve, null, null, null), Json);
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Contains(confirmedFirst.ProductionNo.ToString(), await refused.Content.ReadAsStringAsync());

        var clashes = await bookingOfficer.GetFromJsonAsync<List<Clash>>($"/bookings/{clashing.Id}/clashes", Json);
        Assert.Single(clashes!);

        var moved = await bookingOfficer.PutAsJsonAsync($"/bookings/{clashing.Id}/schedule",
            new ScheduleRequest(Venue.Studio4, null, clashing.CallAt, clashing.StartsAt, clashing.EndsAt), Json);
        Assert.Equal(HttpStatusCode.OK, moved.StatusCode);

        var confirmed = await api.TransitionAsync("booking@pbrms.test", clashing.Id, BookingAction.Approve);
        Assert.Equal(BookingStatus.Confirmed, confirmed.Status);
    }

    [Fact]
    public async Task Bookings_at_a_location_never_clash_over_their_venue_and_are_confirmed_when_the_supervisor_decides()
    {
        var start = NewDay();
        var first = await api.AtCrewStageAsync(start, Venue.Location, "Camera");
        var second = await api.AtCrewStageAsync(start, Venue.Location, "Camera");

        var firstAfter = await api.AssignOkAsync(first, "Camera", await api.NewOperatorAsync("Camera"));
        var secondAfter = await api.AssignOkAsync(second, "Camera", await api.NewOperatorAsync("Camera"));

        // Supervisors phone their operators first, so their decision is the confirmation.
        Assert.Equal(BookingStatus.Confirmed, firstAfter.Status);
        Assert.Equal(BookingStatus.Confirmed, secondAfter.Status);
    }

    [Fact]
    public async Task An_outside_production_is_confirmed_only_when_every_division_has_decided()
    {
        var (cameraOp, soundOp) = (await api.NewOperatorAsync("Camera"), await api.NewOperatorAsync("Sound"));
        var booking = await api.AtCrewStageAsync(NewDay(), Venue.Location, "Camera", "Sound");

        var afterCamera = await api.AssignOkAsync(booking, "Camera", cameraOp);
        Assert.Equal(BookingStatus.CrewAssignment, afterCamera.Status);
        Assert.DoesNotContain(await api.NotificationsAsync(OperatorEmail(cameraOp)), n => n.BookingId == booking.Id);

        var afterSound = await api.AssignOkAsync(booking, "Sound", soundOp);
        Assert.Equal(BookingStatus.Confirmed, afterSound.Status);

        // Only now are the operators told.
        Assert.Contains(await api.NotificationsAsync(OperatorEmail(cameraOp)), n => n.BookingId == booking.Id && n.Text.Contains("is confirmed"));
    }

    [Fact]
    public async Task The_booking_officer_cant_decide_outside_crew_before_the_daily_roster_is_due()
    {
        var booking = await api.AtCrewStageAsync(NewDay(), Venue.Location, "Camera");

        var response = await api.AssignAsync("booking@pbrms.test", booking, "Camera", await api.NewOperatorAsync("Camera"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Undecided_outside_crew_goes_to_the_booking_officer_once_the_daily_roster_is_due()
    {
        // Starting in two hours, so the deadline (16h00 the day before) has passed.
        var booking = await api.AtCrewStageAsync(DateTimeOffset.UtcNow.AddHours(2), Venue.Location, "Camera");
        var bookingOfficer = await api.SignedInClientAsync("booking@pbrms.test");

        var queue = await bookingOfficer.GetFromJsonAsync<List<BookingResponse>>("/bookings/queue", Json);
        Assert.Contains(queue!, b => b.Id == booking.Id);

        var decided = await api.AssignAsync("booking@pbrms.test", booking, "Camera", await api.NewOperatorAsync("Camera"));
        Assert.Equal(HttpStatusCode.OK, decided.StatusCode);
    }

    [Fact]
    public async Task A_supervisor_can_change_their_divisions_studio_crew_and_the_booking_officer_is_told()
    {
        var booking = await api.AtCrewStageAsync(NewDay(), Venue.Studio1, "Camera");

        var response = await api.AssignAsync("supervisor@pbrms.test", booking, "Camera", await api.NewOperatorAsync("Camera"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(await api.NotificationsAsync("booking@pbrms.test"), n => n.BookingId == booking.Id && n.Text.Contains("A supervisor changed the crew"));
    }

    [Fact]
    public async Task A_supervisor_cant_change_another_divisions_studio_crew()
    {
        var booking = await api.AtCrewStageAsync(NewDay(), Venue.Studio1, "Camera");

        var response = await api.AssignAsync("supervisor.sound@pbrms.test", booking, "Camera", await api.NewOperatorAsync("Camera"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Admin_sees_who_decided_each_position_and_who_confirmed_each_booking()
    {
        var start = NewDay();
        var studio = await api.AtFinalisationAsync(start, Venue.Studio1);
        await api.TransitionAsync("booking@pbrms.test", studio.Id, BookingAction.Approve);
        var outside = await api.AtCrewStageAsync(start, Venue.Location, "Camera");
        await api.AssignOkAsync(outside, "Camera", await api.NewOperatorAsync("Camera"));

        var admin = await api.SignedInClientAsync("admin@pbrms.test");
        var day = $"{start:yyyy-MM-dd}";
        var rows = await admin.GetFromJsonAsync<List<ConfirmationEndpoints.ConfirmationResponse>>($"/reports/confirmations?from={day}&to={day}", Json);

        var studioRow = rows!.Single(r => r.ProductionNo == studio.ProductionNo);
        Assert.Equal((Role.BookingOfficer, Role.BookingOfficer), (studioRow.DecidedByRole!.Value, studioRow.ConfirmedByRole!.Value));
        var outsideRow = rows!.Single(r => r.ProductionNo == outside.ProductionNo);
        Assert.Equal((Role.Supervisor, Role.Supervisor), (outsideRow.DecidedByRole!.Value, outsideRow.ConfirmedByRole!.Value));

        var supervisor = await api.SignedInClientAsync("supervisor@pbrms.test");
        Assert.Equal(HttpStatusCode.Forbidden, (await supervisor.GetAsync($"/reports/confirmations?from={day}&to={day}")).StatusCode);
    }

    [Fact]
    public async Task Moving_a_booking_onto_a_time_its_operator_is_taken_blocks_confirming()
    {
        var start = NewDay();
        var op = await api.NewOperatorAsync("Camera");
        var other = await api.AtCrewStageAsync(start, Venue.Studio1, "Camera");
        await api.AssignOkAsync(other, "Camera", op);
        var later = await api.AtCrewStageAsync(start.AddHours(5), Venue.Studio2, "Camera");
        await api.AssignOkAsync(later, "Camera", op);
        var bookingOfficer = await api.SignedInClientAsync("booking@pbrms.test");

        // Moving is allowed, because a busy day may take several moves to sort out...
        var moved = await bookingOfficer.PutAsJsonAsync($"/bookings/{later.Id}/schedule",
            new ScheduleRequest(Venue.Studio2, null, start, start, start.AddHours(2)), Json);
        Assert.Equal(HttpStatusCode.OK, moved.StatusCode);

        // ...the crew moves with the booking...
        var body = await moved.Content.ReadFromJsonAsync<BookingResponse>(Json);
        Assert.Equal(start, body!.CrewSlots[0].StartsAt);

        // ...but it can't be confirmed like that.
        var refused = await bookingOfficer.PostAsJsonAsync($"/bookings/{later.Id}/transition", new TransitionRequest(BookingAction.Approve, null, null, null), Json);
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
    }

    [Fact]
    public async Task A_booking_with_an_empty_crew_position_cannot_be_confirmed()
    {
        var booking = await api.AtFinalisationAsync(NewDay(), Venue.Studio1);
        var bookingOfficer = await api.SignedInClientAsync("booking@pbrms.test");
        await bookingOfficer.PutAsJsonAsync($"/bookings/{booking.Id}/crew", new CrewRequest([new(booking.CrewSlots[0].Id, null)]), Json);

        var response = await bookingOfficer.PostAsJsonAsync($"/bookings/{booking.Id}/transition", new TransitionRequest(BookingAction.Approve, null, null, null), Json);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Only_the_booking_officer_can_confirm()
    {
        var booking = await api.AtFinalisationAsync(NewDay(), Venue.Studio1);
        var ep = await api.SignedInClientAsync("ep@pbrms.test");

        var response = await ep.PostAsJsonAsync($"/bookings/{booking.Id}/transition", new TransitionRequest(BookingAction.Approve, null, null, null), Json);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task The_crew_picker_lists_a_divisions_operators_and_shows_what_they_are_on()
    {
        var start = NewDay();
        var op = await api.NewOperatorAsync("Camera");
        var booking = await api.AtCrewStageAsync(start, Venue.Studio1, "Camera");
        await api.AssignOkAsync(booking, "Camera", op);
        var supervisor = await api.SignedInClientAsync("supervisor@pbrms.test");
        var units = await api.UnitsAsync();

        var operators = await supervisor.GetFromJsonAsync<List<OperatorEndpoints.OperatorResponse>>($"/org-units/{units["Camera"]}/operators", Json);
        var from = Uri.EscapeDataString(start.ToString("O"));
        var to = Uri.EscapeDataString(start.AddHours(1).ToString("O"));
        var conflicts = await supervisor.GetFromJsonAsync<List<Clash>>($"/operators/{op}/conflicts?from={from}&to={to}", Json);

        Assert.Contains(operators!, o => o.Id == op);
        Assert.Equal(booking.ProductionNo, Assert.Single(conflicts!).ProductionNo);
    }
}
