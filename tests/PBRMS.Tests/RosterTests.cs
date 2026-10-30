using System.Net;
using System.Net.Http.Json;
using PBRMS.Domain;
using static PBRMS.Api.Endpoints.OffRosterEndpoints;
using static PBRMS.Api.Endpoints.RosterEndpoints;
using static PBRMS.Api.Endpoints.ShiftDefinitionEndpoints;
using static PBRMS.Tests.TestSteps;

namespace PBRMS.Tests;

/// <summary>
/// Shift codes and monthly rosters through the real API, and how the published roster
/// feeds the clash check. Every test uses its own month and its own operators, so
/// tests can't affect each other. Sample legend (DevSeed): O, L, TR, OB, A1 (08h00–17h00),
/// ST1, ST2, N.
/// </summary>
[Collection("API")]
public class RosterTests(TestApi api)
{
    private const string Supervisor = "supervisor@pbrms.test"; // of Camera

    [Fact]
    public async Task A_supervisor_builds_a_draft_and_publishes_it()
    {
        var month = NewMonth();
        var op = await api.NewOperatorAsync("Camera");

        var draft = await SaveCellsOkAsync(month, new RosterCell(op, DayOf(month, 1), "A1"), new RosterCell(op, DayOf(month, 2), "N + ST2"));
        Assert.Equal((1, RosterStatus.Draft), (draft.Version, draft.Status));
        Assert.Equal("N+ST2", draft.Entries[1].Code);

        var published = await PublishOkAsync(month);
        Assert.Equal(RosterStatus.Published, published.Roster.Status);
    }

    [Fact]
    public async Task Changing_a_published_roster_makes_a_new_version_and_the_old_one_stays_in_force_until_published()
    {
        var month = NewMonth();
        var op = await api.NewOperatorAsync("Camera");
        var start = StartOn(month, 3);
        await SaveCellsOkAsync(month, new RosterCell(op, DayOf(month, 3), "A1"));
        await PublishOkAsync(month);

        // Draft version 2 says off, but version 1 is still in force, so assigning works.
        var draft = await SaveCellsOkAsync(month, new RosterCell(op, DayOf(month, 3), "O"));
        Assert.Equal(2, draft.Version);
        var first = await api.AtCrewStageAsync(start, Venue.Location, "Camera");
        Assert.Equal(HttpStatusCode.OK, (await api.AssignAsync(first, "Camera", op)).StatusCode);
        var bookingOfficer = await api.SignedInClientAsync("booking@pbrms.test");
        Assert.Equal(HttpStatusCode.OK, (await bookingOfficer.PostAsJsonAsync($"/bookings/{first.Id}/transition", new { action = "Cancel", note = "Clearing the way" }, Json)).StatusCode);

        // Once version 2 is published, the same assignment is refused.
        await PublishOkAsync(month);
        var second = await api.AtCrewStageAsync(start, Venue.Location, "Camera");
        Assert.Equal(HttpStatusCode.Conflict, (await api.AssignAsync(second, "Camera", op)).StatusCode);

        await api.WithDatabaseAsync(db =>
        {
            Assert.Equal(2, db.MonthlyRosters.Count(r => r.Month == DateOnly.FromDateTime(month.DateTime)));
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task An_operator_rostered_off_cant_be_assigned_and_the_reason_is_given()
    {
        var month = NewMonth();
        var op = await api.NewOperatorAsync("Camera");
        await SaveCellsOkAsync(month, new RosterCell(op, DayOf(month, 4), "L"));
        await PublishOkAsync(month);
        var booking = await api.AtCrewStageAsync(StartOn(month, 4), Venue.Studio1, "Camera");

        var response = await api.AssignAsync(booking, "Camera", op);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("Leave", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_draft_roster_and_a_blank_cell_are_not_clashes()
    {
        var month = NewMonth();
        var (draftOff, blank) = (await api.NewOperatorAsync("Camera"), await api.NewOperatorAsync("Camera"));
        await SaveCellsOkAsync(month, new RosterCell(draftOff, DayOf(month, 5), "O"));
        var first = await api.AtCrewStageAsync(StartOn(month, 5), Venue.Location, "Camera");
        var second = await api.AtCrewStageAsync(StartOn(month, 5), Venue.Location, "Camera");

        Assert.Equal(HttpStatusCode.OK, (await api.AssignAsync(first, "Camera", draftOff)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await api.AssignAsync(second, "Camera", blank)).StatusCode);
    }

    [Fact]
    public async Task Someone_rostered_away_can_go_on_a_trip_but_not_work_in_a_studio()
    {
        var month = NewMonth();
        var op = await api.NewOperatorAsync("Camera");
        await SaveCellsOkAsync(month, new RosterCell(op, DayOf(month, 6), "TR"));
        await PublishOkAsync(month);
        var studio = await api.AtCrewStageAsync(StartOn(month, 6), Venue.Studio1, "Camera");
        var trip = await api.AtCrewStageAsync(StartOn(month, 6), Venue.Location, isTrip: true, "Camera");

        Assert.Equal(HttpStatusCode.Conflict, (await api.AssignAsync(studio, "Camera", op, acknowledge: true)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await api.AssignAsync(trip, "Camera", op, acknowledge: true)).StatusCode);
    }

    [Fact]
    public async Task Booking_someone_outside_their_shift_is_a_warning_and_is_logged_for_the_manager()
    {
        var month = NewMonth();
        var op = await api.NewOperatorAsync("Camera");
        await SaveCellsOkAsync(month, new RosterCell(op, DayOf(month, 7), "A1")); // 08h00–17h00
        await PublishOkAsync(month);
        var evening = await api.AtCrewStageAsync(StartOn(month, 7).AddHours(9), Venue.Studio1, "Camera"); // 19h00

        var warned = await api.AssignAsync(evening, "Camera", op);
        Assert.Contains("needsAcknowledgement", await warned.Content.ReadAsStringAsync());
        Assert.Contains("Not on shift", await warned.Content.ReadAsStringAsync());
        Assert.Empty(await LoggedAsync("tvops.manager@pbrms.test", month, op));

        Assert.Equal(HttpStatusCode.OK, (await api.AssignAsync(evening, "Camera", op, acknowledge: true)).StatusCode);

        var logged = Assert.Single(await LoggedAsync("tvops.manager@pbrms.test", month, op));
        Assert.Equal((ClashKind.OutsideShift, evening.ProductionNo), (logged.Kind, logged.ProductionNo));
        Assert.Single(await LoggedAsync(Supervisor, month, op));

        Assert.Contains(await api.NotificationsAsync(OperatorEmail(op)), n => n.Text.Contains("outside your shift"));
        Assert.Contains(await api.NotificationsAsync("tvops.manager@pbrms.test"), n => n.BookingId == evening.Id);
    }

    [Fact]
    public async Task A_call_in_is_logged_but_not_shown_to_another_divisions_supervisor_or_to_operators()
    {
        var month = NewMonth();
        var op = await api.NewOperatorAsync("Camera");
        await SaveCellsOkAsync(month, new RosterCell(op, DayOf(month, 17), "O"));
        await PublishOkAsync(month);
        var booking = await api.AtCrewStageAsync(StartOn(month, 17), Venue.Studio1, "Camera");

        await api.AssignAsync(booking, "Camera", op, acknowledge: true);

        Assert.Equal(ClashKind.CallIn, Assert.Single(await LoggedAsync(Supervisor, month, op)).Kind);
        Assert.Empty(await LoggedAsync("supervisor.sound@pbrms.test", month, op));

        var operatorClient = await api.SignedInClientAsync(OperatorEmail(op));
        var response = await operatorClient.GetAsync(ReportUrl(month));
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Someone_replaced_before_the_day_drops_out_of_the_report()
    {
        var month = NewMonth();
        var (calledIn, replacement) = (await api.NewOperatorAsync("Camera"), await api.NewOperatorAsync("Camera"));
        await SaveCellsOkAsync(month, new RosterCell(calledIn, DayOf(month, 18), "O"));
        await PublishOkAsync(month);
        var booking = await api.AtCrewStageAsync(StartOn(month, 18), Venue.Studio1, "Camera");
        await api.AssignAsync(booking, "Camera", calledIn, acknowledge: true);

        await api.AssignOkAsync(booking, "Camera", replacement);

        Assert.Empty(await LoggedAsync("tvops.manager@pbrms.test", month, calledIn));
    }

    [Fact]
    public async Task A_day_off_that_appears_after_assigning_is_logged_when_the_booking_officer_confirms()
    {
        var month = NewMonth();
        var op = await api.NewOperatorAsync("Camera");
        var booking = await api.AtCrewStageAsync(StartOn(month, 19), Venue.Studio1, "Camera");
        await api.AssignOkAsync(booking, "Camera", op);
        await SaveCellsOkAsync(month, new RosterCell(op, DayOf(month, 19), "O"));
        await PublishOkAsync(month);
        var bookingOfficer = await api.SignedInClientAsync("booking@pbrms.test");

        await bookingOfficer.PostAsJsonAsync($"/bookings/{booking.Id}/transition", new { action = "Approve", acknowledgeWarnings = true }, Json);

        Assert.Single(await LoggedAsync("tvops.manager@pbrms.test", month, op));
    }

    [Fact]
    public async Task A_roster_that_gives_a_confirmed_operator_a_day_off_is_logged_when_published()
    {
        var month = NewMonth();
        var op = await api.NewOperatorAsync("Camera");
        var booking = await api.AtCrewStageAsync(StartOn(month, 20), Venue.Studio1, "Camera");
        await api.AssignOkAsync(booking, "Camera", op);
        await api.TransitionAsync("booking@pbrms.test", booking.Id, BookingAction.Approve);
        await SaveCellsOkAsync(month, new RosterCell(op, DayOf(month, 20), "O"));

        await PublishOkAsync(month);

        Assert.Single(await LoggedAsync("tvops.manager@pbrms.test", month, op));
    }

    [Fact]
    public async Task A_roster_change_after_assigning_blocks_confirming()
    {
        var month = NewMonth();
        var op = await api.NewOperatorAsync("Camera");
        var booking = await api.AtCrewStageAsync(StartOn(month, 8), Venue.Studio1, "Camera");
        await api.AssignOkAsync(booking, "Camera", op);
        await SaveCellsOkAsync(month, new RosterCell(op, DayOf(month, 8), "L"));
        await PublishOkAsync(month);
        var bookingOfficer = await api.SignedInClientAsync("booking@pbrms.test");

        var response = await bookingOfficer.PostAsJsonAsync($"/bookings/{booking.Id}/transition", new { action = "Approve" }, Json);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Publishing_warns_about_a_clash_with_a_confirmed_booking_but_still_publishes()
    {
        var month = NewMonth();
        var op = await api.NewOperatorAsync("Camera");
        var booking = await api.AtCrewStageAsync(StartOn(month, 9), Venue.Studio2, "Camera");
        await api.AssignOkAsync(booking, "Camera", op);
        await api.TransitionAsync("booking@pbrms.test", booking.Id, BookingAction.Approve);
        await SaveCellsOkAsync(month, new RosterCell(op, DayOf(month, 9), "L"));

        var published = await PublishOkAsync(month);

        Assert.Equal(RosterStatus.Published, published.Roster.Status);
        Assert.Equal(booking.ProductionNo, Assert.Single(published.Clashes).ProductionNo);
    }

    [Fact]
    public async Task A_supervisor_can_only_edit_their_own_divisions_roster()
    {
        var month = NewMonth();
        var units = await api.UnitsAsync();
        var sound = await api.SignedInClientAsync("supervisor.sound@pbrms.test");

        var response = await sound.PutAsJsonAsync(RosterUrl(units["Camera"], month), new RosterRequest([]), Json);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData("ZZ")]        // not on the legend
    [InlineData("A1+ZZ")]     // half on the legend
    public async Task A_code_not_on_the_legend_is_refused(string code)
    {
        var month = NewMonth();
        var response = await SaveCellsAsync(month, new RosterCell(await api.NewOperatorAsync("Camera"), DayOf(month, 1), code));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task A_date_in_another_month_is_refused()
    {
        var month = NewMonth();

        var response = await SaveCellsAsync(month, new RosterCell(await api.NewOperatorAsync("Camera"), DayOf(month, 1).AddMonths(1), "A1"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task An_operator_from_another_division_is_refused()
    {
        var month = NewMonth();

        var response = await SaveCellsAsync(month, new RosterCell(await api.NewOperatorAsync("Sound"), DayOf(month, 1), "A1"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task An_empty_code_blanks_a_cell()
    {
        var month = NewMonth();
        var op = await api.NewOperatorAsync("Camera");
        await SaveCellsOkAsync(month, new RosterCell(op, DayOf(month, 1), "A1"));

        var saved = await SaveCellsOkAsync(month, new RosterCell(op, DayOf(month, 1), ""));

        Assert.Empty(saved.Entries);
    }

    [Fact]
    public async Task There_is_nothing_to_publish_without_a_draft()
    {
        var month = NewMonth();
        var units = await api.UnitsAsync();
        var client = await api.SignedInClientAsync(Supervisor);

        var response = await client.PostAsync($"{RosterUrl(units["Camera"], month)}/publish", null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task A_supervisor_can_add_a_code_and_use_it_then_retire_it()
    {
        var units = await api.UnitsAsync();
        var client = await api.SignedInClientAsync(Supervisor);
        var code = $"X{Guid.NewGuid():N}"[..8];

        var added = await client.PostAsJsonAsync($"/shift-definitions/{units["Camera"]}", new ShiftForm(code, "Test code", ShiftKind.Worked, null, null, null, null, null), Json);
        Assert.Equal(HttpStatusCode.Created, added.StatusCode);
        var shift = (await added.Content.ReadFromJsonAsync<ShiftResponse>(Json))!;

        var month = NewMonth();
        var op = await api.NewOperatorAsync("Camera");
        await SaveCellsOkAsync(month, new RosterCell(op, DayOf(month, 1), code));

        var retired = await client.PutAsJsonAsync($"/shift-definitions/{units["Camera"]}/{shift.Id}",
            new ShiftForm(code, "Test code", ShiftKind.Worked, null, null, null, null, null, IsActive: false), Json);
        Assert.Equal(HttpStatusCode.OK, retired.StatusCode);

        var refused = await SaveCellsAsync(month, new RosterCell(op, DayOf(month, 2), code));
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
    }

    [Fact]
    public async Task A_supervisor_cant_change_another_divisions_legend()
    {
        var units = await api.UnitsAsync();
        var client = await api.SignedInClientAsync("supervisor.sound@pbrms.test");

        var response = await client.PostAsJsonAsync($"/shift-definitions/{units["Camera"]}", new ShiftForm("ZZ", "Nope", ShiftKind.Off, null, null, null, null, null), Json);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Calling_someone_in_on_a_day_off_needs_acknowledging_and_tells_the_operator_and_the_booking_officer()
    {
        var month = NewMonth();
        var op = await api.NewOperatorAsync("Camera");
        await SaveCellsOkAsync(month, new RosterCell(op, DayOf(month, 10), "O"));
        await PublishOkAsync(month);
        var booking = await api.AtCrewStageAsync(StartOn(month, 10), Venue.Studio1, "Camera");

        // First try: a warning, and nothing is saved.
        var warned = await api.AssignAsync(booking, "Camera", op);
        Assert.Equal(HttpStatusCode.Conflict, warned.StatusCode);
        Assert.Contains("needsAcknowledgement", await warned.Content.ReadAsStringAsync());
        Assert.DoesNotContain(await api.NotificationsAsync(OperatorEmail(op)), n => n.BookingId == booking.Id);

        // Going ahead books them.
        var assigned = await api.AssignAsync(booking, "Camera", op, acknowledge: true);
        Assert.Equal(HttpStatusCode.OK, assigned.StatusCode);

        var toOperator = Assert.Single(await api.NotificationsAsync(OperatorEmail(op)), n => n.BookingId == booking.Id);
        Assert.Contains("called in on your day off", toOperator.Text);
        Assert.Contains($"#{booking.ProductionNo}", toOperator.Text);
        Assert.Contains(await api.NotificationsAsync("booking@pbrms.test"), n => n.Text.Contains($"#{booking.ProductionNo}"));
    }

    [Fact]
    public async Task Someone_on_leave_cant_be_booked_even_when_acknowledged()
    {
        var month = NewMonth();
        var op = await api.NewOperatorAsync("Camera");
        await SaveCellsOkAsync(month, new RosterCell(op, DayOf(month, 11), "L"));
        await PublishOkAsync(month);
        var booking = await api.AtCrewStageAsync(StartOn(month, 11), Venue.Studio1, "Camera");

        var response = await api.AssignAsync(booking, "Camera", op, acknowledge: true);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.DoesNotContain("needsAcknowledgement", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task The_booking_officer_is_warned_of_a_day_off_when_confirming()
    {
        var month = NewMonth();
        var op = await api.NewOperatorAsync("Camera");
        var booking = await api.AtCrewStageAsync(StartOn(month, 12), Venue.Studio1, "Camera");
        await api.AssignOkAsync(booking, "Camera", op);
        await SaveCellsOkAsync(month, new RosterCell(op, DayOf(month, 12), "O"));
        await PublishOkAsync(month);
        var bookingOfficer = await api.SignedInClientAsync("booking@pbrms.test");

        var warned = await bookingOfficer.PostAsJsonAsync($"/bookings/{booking.Id}/transition", new { action = "Approve" }, Json);
        Assert.Equal(HttpStatusCode.Conflict, warned.StatusCode);
        Assert.Contains("needsAcknowledgement", await warned.Content.ReadAsStringAsync());

        var confirmed = await bookingOfficer.PostAsJsonAsync($"/bookings/{booking.Id}/transition", new { action = "Approve", acknowledgeWarnings = true }, Json);
        Assert.Equal(HttpStatusCode.OK, confirmed.StatusCode);
    }

    [Fact]
    public async Task Moving_someone_from_their_studio_to_another_warns_and_tells_the_supervisor_to_make_changes()
    {
        var month = NewMonth();
        var op = await api.NewOperatorAsync("Camera");
        await SaveCellsOkAsync(month, new RosterCell(op, DayOf(month, 13), "ST1")); // Studio 1, 13h00–21h00
        await PublishOkAsync(month);
        var booking = await api.AtCrewStageAsync(StartOn(month, 13).AddHours(4), Venue.Studio2, "Camera");

        var warned = await api.AssignAsync(booking, "Camera", op);
        Assert.Contains("needsAcknowledgement", await warned.Content.ReadAsStringAsync());

        var moved = await api.AssignAsync(booking, "Camera", op, acknowledge: true);
        Assert.Equal(HttpStatusCode.OK, moved.StatusCode);

        var toSupervisor = (await api.NotificationsAsync(Supervisor)).First(n => n.Text.Contains($"#{booking.ProductionNo}"));
        Assert.Contains("Studio1", toSupervisor.Text);
        Assert.Contains("make the changes", toSupervisor.Text);
    }

    [Fact]
    public async Task Someone_rostered_to_their_own_studio_gets_no_warning()
    {
        var month = NewMonth();
        var op = await api.NewOperatorAsync("Camera");
        await SaveCellsOkAsync(month, new RosterCell(op, DayOf(month, 14), "ST1"));
        await PublishOkAsync(month);
        var booking = await api.AtCrewStageAsync(StartOn(month, 14).AddHours(4), Venue.Studio1, "Camera");

        var response = await api.AssignAsync(booking, "Camera", op);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task A_studio_in_use_keeps_its_rostered_person_because_studios_have_priority()
    {
        var month = NewMonth();
        var op = await api.NewOperatorAsync("Camera");
        await SaveCellsOkAsync(month, new RosterCell(op, DayOf(month, 15), "ST1"));
        await PublishOkAsync(month);
        var start = StartOn(month, 15).AddHours(4);
        await api.AtCrewStageAsync(start, Venue.Studio1, "Sound"); // Studio 1 has a production then
        var shoot = await api.AtCrewStageAsync(start, Venue.Location, "Camera");

        var response = await api.AssignAsync(shoot, "Camera", op, acknowledge: true);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("in use", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task An_idle_studio_can_give_up_its_rostered_person_with_a_warning()
    {
        var month = NewMonth();
        var op = await api.NewOperatorAsync("Camera");
        await SaveCellsOkAsync(month, new RosterCell(op, DayOf(month, 16), "ST1"));
        await PublishOkAsync(month);
        var shoot = await api.AtCrewStageAsync(StartOn(month, 16).AddHours(4), Venue.Location, "Camera");

        var warned = await api.AssignAsync(shoot, "Camera", op);
        Assert.Contains("needsAcknowledgement", await warned.Content.ReadAsStringAsync());

        var response = await api.AssignAsync(shoot, "Camera", op, acknowledge: true);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task A_studio_production_is_crewed_automatically_from_the_published_roster()
    {
        var month = NewMonth();
        var op = await api.NewOperatorAsync("Camera");
        await SaveCellsOkAsync(month, new RosterCell(op, DayOf(month, 21), "ST1"));
        await PublishOkAsync(month);

        var booking = await api.AtCrewStageAsync(StartOn(month, 21).AddHours(4), Venue.Studio1, "Camera");

        Assert.Equal(op, booking.CrewSlots[0].OperatorId);
        Assert.Equal(BookingStatus.Finalisation, booking.Status);

        // The operator hears only once the Booking Officer has confirmed it.
        Assert.DoesNotContain(await api.NotificationsAsync(OperatorEmail(op)), n => n.BookingId == booking.Id);
        await api.TransitionAsync("booking@pbrms.test", booking.Id, BookingAction.Approve);
        Assert.Contains(await api.NotificationsAsync(OperatorEmail(op)), n => n.BookingId == booking.Id);
    }

    [Fact]
    public async Task Auto_crewing_leaves_the_rest_for_the_booking_officer()
    {
        var month = NewMonth();
        var op = await api.NewOperatorAsync("Camera");
        await SaveCellsOkAsync(month, new RosterCell(op, DayOf(month, 22), "ST1"));
        await PublishOkAsync(month);

        var booking = await api.AtCrewStageAsync(StartOn(month, 22).AddHours(4), Venue.Studio1, "Camera", "Sound");

        Assert.Equal(BookingStatus.CrewAssignment, booking.Status);
        Assert.Equal(op, booking.CrewSlots.Single(s => s.Division == "Camera").OperatorId);
        Assert.Null(booking.CrewSlots.Single(s => s.Division == "Sound").OperatorId);
    }

    [Fact]
    public async Task Auto_crewing_skips_someone_already_on_another_production()
    {
        var month = NewMonth();
        var op = await api.NewOperatorAsync("Camera");
        await SaveCellsOkAsync(month, new RosterCell(op, DayOf(month, 23), "ST1"));
        await PublishOkAsync(month);
        var start = StartOn(month, 23).AddHours(4);
        var first = await api.AtCrewStageAsync(start, Venue.Studio1, "Camera");

        var second = await api.AtCrewStageAsync(start, Venue.Studio1, "Camera");

        Assert.Equal(op, first.CrewSlots[0].OperatorId);
        Assert.Null(second.CrewSlots[0].OperatorId);
    }

    [Fact]
    public async Task A_draft_roster_doesnt_crew_a_studio_production()
    {
        var month = NewMonth();
        var op = await api.NewOperatorAsync("Camera");
        await SaveCellsOkAsync(month, new RosterCell(op, DayOf(month, 24), "ST1"));

        var booking = await api.AtCrewStageAsync(StartOn(month, 24).AddHours(4), Venue.Studio1, "Camera");

        Assert.Null(booking.CrewSlots[0].OperatorId);
    }

    [Fact]
    public async Task Operators_are_told_when_a_roster_is_published()
    {
        var month = NewMonth();
        var op = await api.NewOperatorAsync("Camera");
        await SaveCellsOkAsync(month, new RosterCell(op, DayOf(month, 1), "A1"));

        await PublishOkAsync(month);

        Assert.Contains(await api.NotificationsAsync(OperatorEmail(op)), n => n.Text.Contains("roster has been published"));
    }

    // ---- Helpers ----

    private static int monthCounter;

    /// <summary>The first of a month no other test uses, in a year no booking test uses.</summary>
    private static DateTimeOffset NewMonth() =>
        new DateTimeOffset(2032, 1, 1, 10, 0, 0, NbcTime.Offset).AddMonths(Interlocked.Increment(ref monthCounter));

    private static DateOnly DayOf(DateTimeOffset month, int day) => new(month.Year, month.Month, day);

    /// <summary>10h00 on that day of the month.</summary>
    private static DateTimeOffset StartOn(DateTimeOffset month, int day) => month.AddDays(day - 1);

    private static string RosterUrl(Guid division, DateTimeOffset month) => $"/rosters/{division}/{month:yyyy-MM}";

    private static string ReportUrl(DateTimeOffset month) =>
        $"/reports/off-roster-work?from={month:yyyy-MM}-01&to={month.AddMonths(1).AddDays(-1):yyyy-MM-dd}";

    /// <summary>What this person sees in the off-roster report for this operator and month.</summary>
    private async Task<List<OffRosterResponse>> LoggedAsync(string email, DateTimeOffset month, Guid operatorId)
    {
        var client = await api.SignedInClientAsync(email);
        var rows = await client.GetFromJsonAsync<List<OffRosterResponse>>(ReportUrl(month), Json);
        return rows!.Where(r => r.OperatorId == operatorId).ToList();
    }

    /// <summary>Saves cells on the Camera roster as the Camera supervisor.</summary>
    private async Task<HttpResponseMessage> SaveCellsAsync(DateTimeOffset month, params RosterCell[] cells)
    {
        var units = await api.UnitsAsync();
        var client = await api.SignedInClientAsync(Supervisor);
        return await client.PutAsJsonAsync(RosterUrl(units["Camera"], month), new RosterRequest([.. cells]), Json);
    }

    private async Task<RosterResponse> SaveCellsOkAsync(DateTimeOffset month, params RosterCell[] cells)
    {
        var response = await SaveCellsAsync(month, cells);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<RosterResponse>(Json))!;
    }

    private async Task<PublishResponse> PublishOkAsync(DateTimeOffset month)
    {
        var units = await api.UnitsAsync();
        var client = await api.SignedInClientAsync(Supervisor);
        var response = await client.PostAsync($"{RosterUrl(units["Camera"], month)}/publish", null);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<PublishResponse>(Json))!;
    }
}
