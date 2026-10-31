using System.Net;
using System.Net.Http.Json;
using PBRMS.Api.Endpoints;
using PBRMS.Domain;
using static PBRMS.Api.Endpoints.AppSessionEndpoints;
using static PBRMS.Tests.TestSteps;

namespace PBRMS.Tests;

/// <summary>
/// The 15h00 cut-off, and the Booking Officer's overrides. Producers can't book the next
/// day from 15h00, so supervisors and the Booking Officer can finalise it. The Booking
/// Officer can make an exception only with a reason. A booking starting later today is
/// always past the cut-off, so these tests pass at any time of day.
/// </summary>
[Collection("API")]
public class BookingCutOffTests(TestApi api)
{
    private const string Producer = "producer@pbrms.test";
    private const string BookingOfficer = "booking@pbrms.test";

    private static DateTimeOffset LaterToday() => DateTimeOffset.UtcNow.AddHours(3);

    [Fact]
    public async Task A_producer_can_save_a_draft_but_cant_submit_after_the_cut_off()
    {
        var draft = await DraftAsync(LaterToday());
        var producer = await api.SignedInClientAsync(Producer);

        var response = await SubmitAsync(producer, draft.Id);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("earliest you can book", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_producer_can_book_the_earliest_date_they_are_told()
    {
        var producer = await api.SignedInClientAsync(Producer);
        var earliest = await producer.GetFromJsonAsync<EarliestDate>("/bookings/earliest-date", Json);
        var draft = await DraftAsync(NbcTime.At(earliest!.Date, new(23, 0)));

        var response = await SubmitAsync(producer, draft.Id);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task The_booking_officer_can_let_a_booking_through_with_a_reason_and_it_is_recorded()
    {
        var draft = await DraftAsync(LaterToday());
        var officer = await api.SignedInClientAsync(BookingOfficer);
        var producer = await api.SignedInClientAsync(Producer);

        var noReason = await officer.PostAsJsonAsync($"/bookings/{draft.Id}/allow-late", new OverrideRequest(" "), Json);
        Assert.Equal(HttpStatusCode.BadRequest, noReason.StatusCode);

        var allowed = await officer.PostAsJsonAsync($"/bookings/{draft.Id}/allow-late", new OverrideRequest("State visit announced today"), Json);
        Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        var booking = await allowed.Content.ReadFromJsonAsync<BookingResponse>(Json);
        Assert.Contains(booking!.History, h => h.Note?.Contains("State visit announced today") == true);
        Assert.Contains(await api.NotificationsAsync("tvops.manager@pbrms.test"), n => n.BookingId == draft.Id);

        Assert.Equal(HttpStatusCode.OK, (await SubmitAsync(producer, draft.Id)).StatusCode);
    }

    [Fact]
    public async Task Only_the_booking_officer_can_let_a_booking_through()
    {
        var draft = await DraftAsync(LaterToday());
        var ep = await api.SignedInClientAsync("ep@pbrms.test");

        var response = await ep.PostAsJsonAsync($"/bookings/{draft.Id}/allow-late", new OverrideRequest("Please"), Json);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Short_notice_for_APP_is_a_warning_and_the_operator_is_told()
    {
        var start = LaterToday().AddHours(1);
        var units = await api.UnitsAsync();
        var editor = await api.SignedInClientAsync("editor@pbrms.test");
        var operators = await editor.GetFromJsonAsync<List<AppOperatorResponse>>("/app-operators", Json);
        var production = $"Short {Guid.NewGuid():N}"[..16];
        var form = new AppSessionForm(operators![0].Id, production, start, start.AddMinutes(30), null);

        var warned = await editor.PostAsJsonAsync("/app-sessions", form, Json);
        Assert.Equal(HttpStatusCode.Conflict, warned.StatusCode);
        Assert.Contains("needsAcknowledgement", await warned.Content.ReadAsStringAsync());

        var booked = await editor.PostAsJsonAsync("/app-sessions", form with { AcknowledgeShortNotice = true }, Json);
        Assert.Equal(HttpStatusCode.Created, booked.StatusCode);
        Assert.Contains(await api.NotificationsAsync("app@pbrms.test"), n => n.Text.StartsWith("Short notice") && n.Text.Contains(production));

        var session = await booked.Content.ReadFromJsonAsync<AppSessionResponse>(Json);
        await editor.PostAsJsonAsync($"/app-sessions/{session!.Id}/cancel", new AppNoteRequest(null), Json);
    }

    [Fact]
    public async Task The_booking_officer_can_override_studios_first_with_a_reason_only_after_the_deadline()
    {
        // Past the deadline, because it starts today. Sound, so no other test is held up.
        var start = LaterToday();
        var studio = await api.AtCrewStageAsync(start, Venue.Studio4, "Sound");
        var outside = await api.AtCrewStageAsync(start, Venue.Location, "Sound");
        var op = await api.NewOperatorAsync("Sound");
        var slot = outside.CrewSlots[0].Id;
        var officer = await api.SignedInClientAsync(BookingOfficer);

        // Without a reason, it stays refused.
        var refused = await officer.PutAsJsonAsync($"/bookings/{outside.Id}/crew", new CrewRequest([new(slot, op)]), Json);
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);

        var overridden = await officer.PutAsJsonAsync($"/bookings/{outside.Id}/crew", new CrewRequest([new(slot, op)], OverrideReason: "Presidential visit can't move"), Json);
        Assert.Equal(HttpStatusCode.OK, overridden.StatusCode);

        var booking = await overridden.Content.ReadFromJsonAsync<BookingResponse>(Json);
        Assert.Contains(booking!.History, h => h.Note?.Contains("Presidential visit can't move") == true);
        Assert.Contains(await api.NotificationsAsync("supervisor.sound@pbrms.test"), n => n.BookingId == outside.Id && n.Text.Contains("still short"));
        Assert.Contains(await api.NotificationsAsync("tvops.manager@pbrms.test"), n => n.BookingId == outside.Id);

        await api.TransitionAsync(BookingOfficer, studio.Id, BookingAction.Cancel, "Test finished");
    }

    [Fact]
    public async Task A_supervisor_cant_override_studios_first()
    {
        var start = NewDay();
        var studio = await api.AtCrewStageAsync(start, Venue.Studio4, "Sound");
        var outside = await api.AtCrewStageAsync(start, Venue.Location, "Sound");
        var op = await api.NewOperatorAsync("Sound");
        var supervisor = await api.SignedInClientAsync("supervisor.sound@pbrms.test");

        var response = await supervisor.PutAsJsonAsync($"/bookings/{outside.Id}/crew", new CrewRequest([new(outside.CrewSlots[0].Id, op)], OverrideReason: "I'd like to"), Json);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    // ---- Helpers ----

    private record EarliestDate(DateOnly Date);

    private async Task<BookingResponse> DraftAsync(DateTimeOffset start)
    {
        var units = await api.UnitsAsync();
        var form = new BookingForm(
            "Cut-off Check", units["News"], null, ActivityType.Recording, Venue.Location, null,
            start, start, start.AddMinutes(30), false, false, null, null,
            [new CrewSlotForm(units["Camera"], "Camera 1", null, null, null)]);

        var producer = await api.SignedInClientAsync(Producer);
        var response = await producer.PostAsJsonAsync("/bookings", form, Json);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<BookingResponse>(Json))!;
    }

    private static Task<HttpResponseMessage> SubmitAsync(HttpClient producer, Guid bookingId) =>
        producer.PostAsJsonAsync($"/bookings/{bookingId}/transition", new TransitionRequest(BookingAction.Submit, null, null, null), Json);
}
