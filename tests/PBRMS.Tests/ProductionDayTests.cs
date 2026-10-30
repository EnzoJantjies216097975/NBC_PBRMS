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
/// marking start and end, and the overtime alert. Sample people: News producer, PM and
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
        await api.AssignOkAsync(Supervisor, booking, "Camera", op);

        await api.TransitionAsync(BookingOfficer, booking.Id, BookingAction.Approve);

        foreach (var person in new[] { OperatorEmail(op), Producer, "ep@pbrms.test" })
            Assert.Contains(await api.NotificationsAsync(person), n => n.Text.Contains($"#{booking.ProductionNo}") && n.Text.Contains("is confirmed"));
    }

    [Fact]
    public async Task A_confirmed_operator_can_be_replaced_and_everyone_affected_is_told()
    {
        var (first, second) = (await api.NewOperatorAsync("Camera"), await api.NewOperatorAsync("Camera"));
        var booking = await ConfirmedAsync(first);

        var changed = await api.AssignOkAsync(Supervisor, booking, "Camera", second);

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
        var supervisor = await api.SignedInClientAsync(Supervisor);

        var response = await supervisor.PutAsJsonAsync($"/bookings/{booking.Id}/crew", new CrewRequest([new(booking.CrewSlots[0].Id, null)]), Json);

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
    public async Task Supervisors_and_managers_are_told_once_when_a_production_goes_overtime()
    {
        var booking = await ConfirmedAsync(await api.NewOperatorAsync("Camera"));
        await api.TransitionAsync(Producer, booking.Id, BookingAction.Start);

        // Running 29 minutes over isn't overtime yet...
        await CheckOvertimeAtAsync(booking.EndsAt.AddMinutes(29));
        Assert.Empty(await OvertimeAlertsAsync(Supervisor, booking));

        // ...31 minutes is...
        await CheckOvertimeAtAsync(booking.EndsAt.AddMinutes(31));
        Assert.Single(await OvertimeAlertsAsync(Supervisor, booking));
        Assert.Single(await OvertimeAlertsAsync("pm@pbrms.test", booking));

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

    // ---- Helpers ----

    /// <summary>A confirmed booking on a day of its own, in a studio, with this operator on its one Camera slot.</summary>
    private async Task<BookingResponse> ConfirmedAsync(Guid operatorId)
    {
        var booking = await api.AtCrewStageAsync(NewDay(), Venue.Studio3, "Camera");
        await api.AssignOkAsync(Supervisor, booking, "Camera", operatorId);
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
