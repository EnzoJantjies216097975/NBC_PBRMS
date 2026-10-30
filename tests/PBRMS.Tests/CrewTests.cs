using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using PBRMS.Api.Endpoints;
using PBRMS.Domain;
using static PBRMS.Api.Endpoints.OrgUnitEndpoints;

namespace PBRMS.Tests;

/// <summary>
/// The last stages of the approval chain through the real API: supervisors assign
/// crew, the Booking Officer resolves clashes and confirms. The main rule being tested
/// is BR-06: nobody and no studio can be double-booked on a confirmed booking.
///
/// Every test uses its own new operators and its own day, so tests can't clash with
/// each other. Sample people: News producer, PM and EP; Camera and Sound supervisors;
/// the Booking Officer.
/// </summary>
[Collection("API")]
public class CrewTests(TestApi api)
{
    [Fact]
    public async Task A_booking_goes_all_the_way_to_confirmed_and_the_producer_gets_the_crew_phone_numbers()
    {
        var (start, cameraOp, soundOp) = (NewDay(), await NewOperatorAsync("Camera"), await NewOperatorAsync("Sound"));
        var booking = await AtCrewStageAsync(start, Venue.Studio1, "Camera", "Sound");

        var afterCamera = await AssignOkAsync("supervisor@pbrms.test", booking, "Camera", cameraOp);
        Assert.Equal(BookingStatus.CrewAssignment, afterCamera.Status); // Sound still to come

        var afterSound = await AssignOkAsync("supervisor.sound@pbrms.test", booking, "Sound", soundOp);
        Assert.Equal(BookingStatus.Finalisation, afterSound.Status); // last slot filled: on to the Booking Officer
        Assert.All(afterSound.CrewSlots, s => Assert.Null(s.OperatorPhone)); // no phone numbers until the crew is final

        var confirmed = await TransitionAsync("booking@pbrms.test", booking.Id, BookingAction.Approve);

        Assert.Equal(BookingStatus.Confirmed, confirmed.Status);
        Assert.All(confirmed.CrewSlots, s => Assert.Equal("+264 81 000 0000", s.OperatorPhone));
    }

    [Fact]
    public async Task A_supervisor_can_only_assign_in_their_own_division()
    {
        var booking = await AtCrewStageAsync(NewDay(), Venue.Studio1, "Camera");

        var response = await AssignAsync("supervisor.sound@pbrms.test", booking, "Camera", await NewOperatorAsync("Camera"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task An_operator_from_another_division_is_refused()
    {
        var booking = await AtCrewStageAsync(NewDay(), Venue.Studio1, "Camera");

        var response = await AssignAsync("supervisor@pbrms.test", booking, "Camera", await NewOperatorAsync("Sound"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task An_operator_already_on_another_production_is_refused_and_the_production_is_named()
    {
        var start = NewDay();
        var op = await NewOperatorAsync("Camera");
        var first = await AtCrewStageAsync(start, Venue.Studio1, "Camera");
        await AssignOkAsync("supervisor@pbrms.test", first, "Camera", op);
        var second = await AtCrewStageAsync(start.AddHours(1), Venue.Studio2, "Camera");

        var response = await AssignAsync("supervisor@pbrms.test", second, "Camera", op);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains(first.ProductionNo.ToString(), await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task An_operator_can_go_straight_from_one_production_to_the_next()
    {
        var start = NewDay();
        var op = await NewOperatorAsync("Camera");
        var morning = await AtCrewStageAsync(start, Venue.Studio1, "Camera");
        await AssignOkAsync("supervisor@pbrms.test", morning, "Camera", op);
        var afternoon = await AtCrewStageAsync(start.AddHours(2), Venue.Studio1, "Camera"); // starts as the first ends

        var response = await AssignAsync("supervisor@pbrms.test", afternoon, "Camera", op);

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
            var op = await NewOperatorAsync("Camera");
            var first = await AtCrewStageAsync(start, Venue.Studio1, "Camera");
            var second = await AtCrewStageAsync(start, Venue.Studio2, "Camera");
            var supervisor = await api.SignedInClientAsync("supervisor@pbrms.test");
            var bookingOfficer = await api.SignedInClientAsync("booking@pbrms.test");

            var responses = await Task.WhenAll(
                supervisor.PutAsJsonAsync($"/bookings/{first.Id}/crew", new CrewRequest([new(first.CrewSlots[0].Id, op)]), Json),
                bookingOfficer.PutAsJsonAsync($"/bookings/{second.Id}/crew", new CrewRequest([new(second.CrewSlots[0].Id, op)]), Json));

            Assert.Equal(
                new[] { HttpStatusCode.OK, HttpStatusCode.Conflict },
                responses.Select(r => r.StatusCode).Order());
        }
    }

    [Fact]
    public async Task A_cancelled_booking_frees_its_crew()
    {
        var start = NewDay();
        var op = await NewOperatorAsync("Camera");
        var cancelled = await AtCrewStageAsync(start, Venue.Studio1, "Camera");
        await AssignOkAsync("supervisor@pbrms.test", cancelled, "Camera", op);
        await TransitionAsync("booking@pbrms.test", cancelled.Id, BookingAction.Cancel, "Event called off");
        var other = await AtCrewStageAsync(start, Venue.Studio2, "Camera");

        var response = await AssignAsync("supervisor@pbrms.test", other, "Camera", op);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Changing_the_operator_keeps_the_old_assignment_on_record()
    {
        var (first, second) = (await NewOperatorAsync("Camera"), await NewOperatorAsync("Camera"));
        var booking = await AtCrewStageAsync(NewDay(), Venue.Studio1, "Camera");
        await AssignOkAsync("supervisor@pbrms.test", booking, "Camera", first);

        var changed = await AssignOkAsync("supervisor@pbrms.test", booking, "Camera", second);

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
        var confirmedFirst = await AtFinalisationAsync(start, Venue.Studio3);
        await TransitionAsync("booking@pbrms.test", confirmedFirst.Id, BookingAction.Approve);
        var clashing = await AtFinalisationAsync(start.AddHours(1), Venue.Studio3);
        var bookingOfficer = await api.SignedInClientAsync("booking@pbrms.test");

        var refused = await bookingOfficer.PostAsJsonAsync($"/bookings/{clashing.Id}/transition", new TransitionRequest(BookingAction.Approve, null, null, null), Json);
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Contains(confirmedFirst.ProductionNo.ToString(), await refused.Content.ReadAsStringAsync());

        var clashes = await bookingOfficer.GetFromJsonAsync<List<Clash>>($"/bookings/{clashing.Id}/clashes", Json);
        Assert.Single(clashes!);

        var moved = await bookingOfficer.PutAsJsonAsync($"/bookings/{clashing.Id}/schedule",
            new ScheduleRequest(Venue.Studio4, null, clashing.CallAt, clashing.StartsAt, clashing.EndsAt), Json);
        Assert.Equal(HttpStatusCode.OK, moved.StatusCode);

        var confirmed = await TransitionAsync("booking@pbrms.test", clashing.Id, BookingAction.Approve);
        Assert.Equal(BookingStatus.Confirmed, confirmed.Status);
    }

    [Fact]
    public async Task A_booking_at_a_location_never_clashes_over_its_venue()
    {
        var start = NewDay();
        var first = await AtFinalisationAsync(start, Venue.Location);
        await TransitionAsync("booking@pbrms.test", first.Id, BookingAction.Approve);
        var second = await AtFinalisationAsync(start, Venue.Location);

        var confirmed = await TransitionAsync("booking@pbrms.test", second.Id, BookingAction.Approve);

        Assert.Equal(BookingStatus.Confirmed, confirmed.Status);
    }

    [Fact]
    public async Task Moving_a_booking_onto_a_time_its_operator_is_taken_blocks_confirming()
    {
        var start = NewDay();
        var op = await NewOperatorAsync("Camera");
        var other = await AtCrewStageAsync(start, Venue.Studio1, "Camera");
        await AssignOkAsync("supervisor@pbrms.test", other, "Camera", op);
        var later = await AtCrewStageAsync(start.AddHours(5), Venue.Studio2, "Camera");
        await AssignOkAsync("supervisor@pbrms.test", later, "Camera", op);
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
        var booking = await AtFinalisationAsync(NewDay(), Venue.Studio1);
        var bookingOfficer = await api.SignedInClientAsync("booking@pbrms.test");
        await bookingOfficer.PutAsJsonAsync($"/bookings/{booking.Id}/crew", new CrewRequest([new(booking.CrewSlots[0].Id, null)]), Json);

        var response = await bookingOfficer.PostAsJsonAsync($"/bookings/{booking.Id}/transition", new TransitionRequest(BookingAction.Approve, null, null, null), Json);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Only_the_booking_officer_can_confirm()
    {
        var booking = await AtFinalisationAsync(NewDay(), Venue.Studio1);
        var ep = await api.SignedInClientAsync("ep@pbrms.test");

        var response = await ep.PostAsJsonAsync($"/bookings/{booking.Id}/transition", new TransitionRequest(BookingAction.Approve, null, null, null), Json);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task The_crew_picker_lists_a_divisions_operators_and_shows_what_they_are_on()
    {
        var start = NewDay();
        var op = await NewOperatorAsync("Camera");
        var booking = await AtCrewStageAsync(start, Venue.Studio1, "Camera");
        await AssignOkAsync("supervisor@pbrms.test", booking, "Camera", op);
        var supervisor = await api.SignedInClientAsync("supervisor@pbrms.test");
        var units = await UnitsAsync();

        var operators = await supervisor.GetFromJsonAsync<List<OperatorEndpoints.OperatorResponse>>($"/org-units/{units["Camera"]}/operators", Json);
        var from = Uri.EscapeDataString(start.ToString("O"));
        var to = Uri.EscapeDataString(start.AddHours(1).ToString("O"));
        var conflicts = await supervisor.GetFromJsonAsync<List<Clash>>($"/operators/{op}/conflicts?from={from}&to={to}", Json);

        Assert.Contains(operators!, o => o.Id == op);
        Assert.Equal(booking.ProductionNo, Assert.Single(conflicts!).ProductionNo);
    }

    // ---- Helpers ----

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private static int dayCounter;

    /// <summary>10h00 on a day no other test uses. The database is wiped each run, so fixed dates are safe.</summary>
    private static DateTimeOffset NewDay() =>
        new DateTimeOffset(2030, 1, 1, 10, 0, 0, TimeSpan.FromHours(2)).AddDays(Interlocked.Increment(ref dayCounter));

    private async Task<Dictionary<string, Guid>> UnitsAsync()
    {
        var client = await api.SignedInClientAsync("producer@pbrms.test");
        var units = await client.GetFromJsonAsync<List<OrgUnitResponse>>("/org-units", Json);
        return units!.ToDictionary(u => u.Name, u => u.Id);
    }

    /// <summary>Adds an operator nobody else uses, so no earlier test can have booked them.</summary>
    private async Task<Guid> NewOperatorAsync(string division)
    {
        var units = await UnitsAsync();
        var id = Guid.NewGuid();
        await api.WithDatabaseAsync(async db =>
        {
            db.Users.Add(new User
            {
                Id = id,
                FirstName = "Temp",
                Surname = "Operator",
                WorkEmail = $"{id:N}@pbrms.test",
                Phone = "+264 81 000 0000",
                Roles = [new() { Role = Role.Operator, OrgUnitId = units[division] }]
            });
            await db.SaveChangesAsync();
        });
        return id;
    }

    /// <summary>A two-hour News booking that has been approved by the PM and EP, with one crew slot per division named.</summary>
    private async Task<BookingResponse> AtCrewStageAsync(DateTimeOffset start, Venue venue, params string[] divisions)
    {
        var units = await UnitsAsync();
        var crew = divisions.Select(d => new CrewSlotForm(units[d], $"{d} 1", null, null, null)).ToList();
        var form = new BookingForm(
            "Evening News Special", units["News"], null, ActivityType.Recording, venue, null,
            start.AddMinutes(-30), start, start.AddHours(2), false, false, null, null, crew);

        var producer = await api.SignedInClientAsync("producer@pbrms.test");
        var created = await producer.PostAsJsonAsync("/bookings", form, Json);
        var booking = (await created.Content.ReadFromJsonAsync<BookingResponse>(Json))!;

        await TransitionAsync("producer@pbrms.test", booking.Id, BookingAction.Submit);
        await TransitionAsync("pm@pbrms.test", booking.Id, BookingAction.Approve);
        return await TransitionAsync("ep@pbrms.test", booking.Id, BookingAction.Approve);
    }

    /// <summary>A booking with its one Camera slot filled, waiting for the Booking Officer.</summary>
    private async Task<BookingResponse> AtFinalisationAsync(DateTimeOffset start, Venue venue)
    {
        var booking = await AtCrewStageAsync(start, venue, "Camera");
        return await AssignOkAsync("supervisor@pbrms.test", booking, "Camera", await NewOperatorAsync("Camera"));
    }

    private async Task<HttpResponseMessage> AssignAsync(string email, BookingResponse booking, string division, Guid operatorId)
    {
        var client = await api.SignedInClientAsync(email);
        var slot = booking.CrewSlots.Single(s => s.Division == division);
        return await client.PutAsJsonAsync($"/bookings/{booking.Id}/crew", new CrewRequest([new(slot.Id, operatorId)]), Json);
    }

    private async Task<BookingResponse> AssignOkAsync(string email, BookingResponse booking, string division, Guid operatorId)
    {
        var response = await AssignAsync(email, booking, division, operatorId);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<BookingResponse>(Json))!;
    }

    private async Task<BookingResponse> TransitionAsync(string email, Guid bookingId, BookingAction action, string? note = null)
    {
        var client = await api.SignedInClientAsync(email);
        var response = await client.PostAsJsonAsync($"/bookings/{bookingId}/transition", new TransitionRequest(action, note, null, null), Json);
        Assert.True(response.IsSuccessStatusCode, $"{email} {action}: {await response.Content.ReadAsStringAsync()}");
        return (await response.Content.ReadFromJsonAsync<BookingResponse>(Json))!;
    }
}
