using System.Net;
using System.Net.Http.Json;
using PBRMS.Api.Endpoints;
using PBRMS.Domain;
using static PBRMS.Api.Endpoints.OrgUnitEndpoints;
using static PBRMS.Tests.TestSteps;

namespace PBRMS.Tests;

/// <summary>
/// Booking form and approval chain through the real API: producer → EP → crew
/// assignment, sending back, cancelling, and who is refused along the way.
/// Sample people (DevSeed): a News producer and EP; a Camera and a Sound
/// supervisor; a Sports producer.
/// </summary>
[Collection("API")]
public class BookingTests(TestApi api)
{
    [Fact]
    public async Task A_booking_goes_from_draft_to_crew_assignment_with_every_step_in_its_history()
    {
        var booking = await CreateNewsBookingAsync(api);
        Assert.Equal(BookingStatus.Draft, booking.Status);

        await TransitionAsync(api, "producer@pbrms.test", booking.Id, new(BookingAction.Submit, null, null, null));
        var airAt = booking.StartsAt.AddDays(1);
        var final = await TransitionAsync(api, "ep@pbrms.test", booking.Id, new(BookingAction.Approve, "Looks fine", Channel.Nbc1, airAt));

        Assert.Equal(BookingStatus.CrewAssignment, final.Status);
        Assert.Equal(Channel.Nbc1, final.Channel); // the EP set the channel and air date when approving
        Assert.Equal(airAt, final.AirAt);
        Assert.Equal(
            new[] { BookingStatus.EpReview, BookingStatus.CrewAssignment },
            final.History.Select(h => h.To));
        Assert.Equal("Looks fine", final.History[1].Note);
    }

    [Fact]
    public async Task Sending_back_needs_a_reason_and_returns_the_booking_to_the_producer_to_fix()
    {
        var booking = await CreateNewsBookingAsync(api);
        await TransitionAsync(api, "producer@pbrms.test", booking.Id, new(BookingAction.Submit, null, null, null));
        var ep = await api.SignedInClientAsync("ep@pbrms.test");

        var withoutReason = await ep.PostAsJsonAsync($"/bookings/{booking.Id}/transition", new TransitionRequest(BookingAction.SendBack, " ", null, null), Json);
        Assert.Equal(HttpStatusCode.BadRequest, withoutReason.StatusCode);

        var sentBack = await TransitionAsync(api, "ep@pbrms.test", booking.Id, new(BookingAction.SendBack, "Add the venue address", null, null));
        Assert.Equal(BookingStatus.Draft, sentBack.Status);

        // The producer can now fix the draft.
        var producer = await api.SignedInClientAsync("producer@pbrms.test");
        var edit = await producer.PutAsJsonAsync($"/bookings/{booking.Id}", await NewsFormAsync(api, venueText: "Katutura Community Hall"), Json);
        Assert.Equal(HttpStatusCode.OK, edit.StatusCode);
    }

    [Fact]
    public async Task An_EP_of_another_department_cannot_approve()
    {
        var units = await UnitsAsync(api);
        var sportsProducer = await api.SignedInClientAsync("producer.sports@pbrms.test");
        var created = await sportsProducer.PostAsJsonAsync("/bookings", await NewsFormAsync(api, departmentId: units["Sports"]), Json);
        var booking = (await created.Content.ReadFromJsonAsync<BookingResponse>(Json))!;
        await TransitionAsync(api, "producer.sports@pbrms.test", booking.Id, new(BookingAction.Submit, null, null, null));

        var newsEp = await api.SignedInClientAsync("ep@pbrms.test");
        var response = await newsEp.PostAsJsonAsync($"/bookings/{booking.Id}/transition", new TransitionRequest(BookingAction.Approve, null, null, null), Json);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task A_producer_cannot_approve_their_own_booking()
    {
        var booking = await CreateNewsBookingAsync(api);
        await TransitionAsync(api, "producer@pbrms.test", booking.Id, new(BookingAction.Submit, null, null, null));

        var producer = await api.SignedInClientAsync("producer@pbrms.test");
        var response = await producer.PostAsJsonAsync($"/bookings/{booking.Id}/transition", new TransitionRequest(BookingAction.Approve, null, null, null), Json);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Approving_a_booking_at_the_wrong_stage_is_a_conflict()
    {
        var booking = await CreateNewsBookingAsync(api); // still a draft
        var ep = await api.SignedInClientAsync("ep@pbrms.test");

        var response = await ep.PostAsJsonAsync($"/bookings/{booking.Id}/transition", new TransitionRequest(BookingAction.Approve, null, null, null), Json);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Only_a_supervisor_of_a_division_in_the_crew_list_can_send_back_at_crew_assignment()
    {
        var booking = await CreateNewsBookingAsync(api); // needs Camera crew only
        await TransitionAsync(api, "producer@pbrms.test", booking.Id, new(BookingAction.Submit, null, null, null));
        await TransitionAsync(api, "ep@pbrms.test", booking.Id, new(BookingAction.Approve, null, null, null));

        var sound = await api.SignedInClientAsync("supervisor.sound@pbrms.test");
        var refused = await sound.PostAsJsonAsync($"/bookings/{booking.Id}/transition", new TransitionRequest(BookingAction.SendBack, "No sound crew free", null, null), Json);
        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);

        var declined = await TransitionAsync(api, "supervisor@pbrms.test", booking.Id, new(BookingAction.SendBack, "All cameras out on a trip", null, null));
        Assert.Equal(BookingStatus.EpReview, declined.Status);
    }

    [Fact]
    public async Task A_booking_with_no_crew_positions_cannot_be_submitted()
    {
        var producer = await api.SignedInClientAsync("producer@pbrms.test");
        var created = await producer.PostAsJsonAsync("/bookings", await NewsFormAsync(api, withCrew: false), Json);
        var booking = (await created.Content.ReadFromJsonAsync<BookingResponse>(Json))!;

        var response = await producer.PostAsJsonAsync($"/bookings/{booking.Id}/transition", new TransitionRequest(BookingAction.Submit, null, null, null), Json);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task A_form_that_ends_before_it_starts_is_refused()
    {
        var producer = await api.SignedInClientAsync("producer@pbrms.test");
        var form = await NewsFormAsync(api);

        var response = await producer.PostAsJsonAsync("/bookings", form with { EndsAt = form.StartsAt.AddHours(-1) }, Json);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task A_producer_cannot_book_for_another_department()
    {
        var units = await UnitsAsync(api);
        var newsProducer = await api.SignedInClientAsync("producer@pbrms.test");

        var response = await newsProducer.PostAsJsonAsync("/bookings", await NewsFormAsync(api, departmentId: units["Sports"]), Json);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Operators_cannot_read_bookings()
    {
        var booking = await CreateNewsBookingAsync(api);
        var operatorClient = await api.SignedInClientAsync("operator@pbrms.test");

        var response = await operatorClient.GetAsync($"/bookings/{booking.Id}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task A_submitted_booking_goes_straight_to_the_EPs_queue()
    {
        var booking = await CreateNewsBookingAsync(api);
        Assert.DoesNotContain(await QueueAsync(api, "ep@pbrms.test"), b => b.Id == booking.Id);

        var submitted = await TransitionAsync(api, "producer@pbrms.test", booking.Id, new(BookingAction.Submit, null, null, null));

        Assert.Equal(BookingStatus.EpReview, submitted.Status);
        Assert.Contains(await QueueAsync(api, "ep@pbrms.test"), b => b.Id == booking.Id);
    }

    [Fact]
    public async Task After_the_EP_a_booking_is_with_the_supervisors_and_the_booking_officer_at_the_same_time()
    {
        var booking = await api.AtCrewStageAsync(NewDay(), Venue.Studio1, "Camera");

        Assert.DoesNotContain(await QueueAsync(api, "ep@pbrms.test"), b => b.Id == booking.Id);
        Assert.Contains(await QueueAsync(api, "supervisor@pbrms.test"), b => b.Id == booking.Id);
        Assert.Contains(await QueueAsync(api, "booking@pbrms.test"), b => b.Id == booking.Id);
    }

    [Fact]
    public async Task The_booking_officer_leads_on_a_studio_production_and_is_asked_to_assign_the_crew()
    {
        var booking = await api.AtCrewStageAsync(NewDay(), Venue.Studio1, "Camera");

        Assert.Equal(Role.BookingOfficer, booking.CrewLead);
        Assert.Contains(await api.NotificationsAsync("booking@pbrms.test"), n => n.BookingId == booking.Id && n.Text.Contains("Please assign the crew"));
        Assert.Contains(await api.NotificationsAsync("supervisor@pbrms.test"), n => n.BookingId == booking.Id && n.Text.Contains("the Booking Officer will lead"));
    }

    [Fact]
    public async Task Supervisors_lead_on_a_production_outside_and_are_asked_to_assign_the_crew()
    {
        var booking = await api.AtCrewStageAsync(NewDay(), Venue.Location, "Camera");

        Assert.Equal(Role.Supervisor, booking.CrewLead);
        Assert.Contains(await api.NotificationsAsync("supervisor@pbrms.test"), n => n.BookingId == booking.Id && n.Text.Contains("Please assign the crew"));
        Assert.Contains(await api.NotificationsAsync("booking@pbrms.test"), n => n.BookingId == booking.Id && n.Text.Contains("the supervisors will lead"));
    }

    [Fact]
    public async Task The_booking_officer_can_also_send_a_booking_back_from_crew_assignment()
    {
        var booking = await api.AtCrewStageAsync(NewDay(), Venue.Studio1, "Camera");

        var sentBack = await api.TransitionAsync("booking@pbrms.test", booking.Id, BookingAction.SendBack, "Studio 1 is closed that day");

        Assert.Equal(BookingStatus.EpReview, sentBack.Status);
    }

    [Fact]
    public async Task There_is_no_crew_lead_once_the_crew_is_assigned()
    {
        var booking = await api.AtFinalisationAsync(NewDay(), Venue.Studio1);

        Assert.Null(booking.CrewLead);
    }

    [Fact]
    public async Task A_cancelled_booking_is_kept_and_cannot_be_cancelled_again()
    {
        var booking = await CreateNewsBookingAsync(api);

        var cancelled = await TransitionAsync(api, "producer@pbrms.test", booking.Id, new(BookingAction.Cancel, "Event postponed", null, null));
        Assert.Equal(BookingStatus.Cancelled, cancelled.Status);

        var bookingOfficer = await api.SignedInClientAsync("booking@pbrms.test");
        var again = await bookingOfficer.PostAsJsonAsync($"/bookings/{booking.Id}/transition", new TransitionRequest(BookingAction.Cancel, "Duplicate", null, null), Json);
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
    }

    [Fact]
    public async Task Each_booking_gets_the_next_production_number()
    {

        var first = await CreateNewsBookingAsync(api);
        var second = await CreateNewsBookingAsync(api);

        Assert.Equal(first.ProductionNo + 1, second.ProductionNo);
    }

    // ---- Helpers ----

    /// <summary>Org unit ids by name, e.g. units["Camera"].</summary>
    private static async Task<Dictionary<string, Guid>> UnitsAsync(TestApi api)
    {
        var client = await api.SignedInClientAsync("producer@pbrms.test");
        var units = await client.GetFromJsonAsync<List<OrgUnitResponse>>("/org-units", Json);
        return units!.ToDictionary(u => u.Name, u => u.Id);
    }

    /// <summary>A filled-in form for a News recording next week, needing one camera operator.</summary>
    private static async Task<BookingForm> NewsFormAsync(TestApi api, Guid? departmentId = null, bool withCrew = true, string? venueText = null)
    {
        var units = await UnitsAsync(api);
        var start = new DateTimeOffset(DateTime.Today.AddDays(7).AddHours(10), TimeSpan.FromHours(2)); // Windhoek time
        List<CrewSlotForm> crew = withCrew ? [new(units["Camera"], "Camera 1", null, null, null)] : [];

        return new BookingForm(
            "Evening News Special", departmentId ?? units["News"], null,
            ActivityType.Recording, Venue.Studio1, venueText,
            start.AddMinutes(-30), start, start.AddHours(2),
            IsTrip: false, IsOb: false, AllocationCode: null, Notes: null, crew);
    }

    private static async Task<BookingResponse> CreateNewsBookingAsync(TestApi api)
    {
        var producer = await api.SignedInClientAsync("producer@pbrms.test");
        var response = await producer.PostAsJsonAsync("/bookings", await NewsFormAsync(api), Json);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<BookingResponse>(Json))!;
    }

    /// <summary>Signs in as this person and moves the booking; fails the test if refused.</summary>
    private static async Task<BookingResponse> TransitionAsync(TestApi api, string email, Guid bookingId, TransitionRequest request)
    {
        var client = await api.SignedInClientAsync(email);
        var response = await client.PostAsJsonAsync($"/bookings/{bookingId}/transition", request, Json);
        Assert.True(response.IsSuccessStatusCode, $"{email} {request.Action}: {(int)response.StatusCode} {await response.Content.ReadAsStringAsync()}");
        return (await response.Content.ReadFromJsonAsync<BookingResponse>(Json))!;
    }

    private static async Task<List<BookingResponse>> QueueAsync(TestApi api, string email)
    {
        var client = await api.SignedInClientAsync(email);
        return (await client.GetFromJsonAsync<List<BookingResponse>>("/bookings/queue", Json))!;
    }
}
