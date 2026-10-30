using System.Net;
using System.Net.Http.Json;
using PBRMS.Domain;
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
        Assert.Equal(HttpStatusCode.OK, (await api.AssignAsync(Supervisor, first, "Camera", op)).StatusCode);
        var bookingOfficer = await api.SignedInClientAsync("booking@pbrms.test");
        Assert.Equal(HttpStatusCode.OK, (await bookingOfficer.PostAsJsonAsync($"/bookings/{first.Id}/transition", new { action = "Cancel", note = "Clearing the way" }, Json)).StatusCode);

        // Once version 2 is published, the same assignment is refused.
        await PublishOkAsync(month);
        var second = await api.AtCrewStageAsync(start, Venue.Location, "Camera");
        Assert.Equal(HttpStatusCode.Conflict, (await api.AssignAsync(Supervisor, second, "Camera", op)).StatusCode);

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

        var response = await api.AssignAsync(Supervisor, booking, "Camera", op);

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

        Assert.Equal(HttpStatusCode.OK, (await api.AssignAsync(Supervisor, first, "Camera", draftOff)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await api.AssignAsync(Supervisor, second, "Camera", blank)).StatusCode);
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

        Assert.Equal(HttpStatusCode.Conflict, (await api.AssignAsync(Supervisor, studio, "Camera", op)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await api.AssignAsync(Supervisor, trip, "Camera", op)).StatusCode);
    }

    [Fact]
    public async Task A_booking_entirely_outside_the_operators_shift_is_refused()
    {
        var month = NewMonth();
        var op = await api.NewOperatorAsync("Camera");
        await SaveCellsOkAsync(month, new RosterCell(op, DayOf(month, 7), "A1")); // 08h00–17h00
        await PublishOkAsync(month);
        var evening = await api.AtCrewStageAsync(StartOn(month, 7).AddHours(9), Venue.Studio1, "Camera"); // 19h00

        var response = await api.AssignAsync(Supervisor, evening, "Camera", op);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("Not on shift", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_roster_change_after_assigning_blocks_confirming()
    {
        var month = NewMonth();
        var op = await api.NewOperatorAsync("Camera");
        var booking = await api.AtCrewStageAsync(StartOn(month, 8), Venue.Studio1, "Camera");
        await api.AssignOkAsync(Supervisor, booking, "Camera", op);
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
        await api.AssignOkAsync(Supervisor, booking, "Camera", op);
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

    // ---- Helpers ----

    private static int monthCounter;

    /// <summary>The first of a month no other test uses, in a year no booking test uses.</summary>
    private static DateTimeOffset NewMonth() =>
        new DateTimeOffset(2032, 1, 1, 10, 0, 0, NbcTime.Offset).AddMonths(Interlocked.Increment(ref monthCounter));

    private static DateOnly DayOf(DateTimeOffset month, int day) => new(month.Year, month.Month, day);

    /// <summary>10h00 on that day of the month.</summary>
    private static DateTimeOffset StartOn(DateTimeOffset month, int day) => month.AddDays(day - 1);

    private static string RosterUrl(Guid division, DateTimeOffset month) => $"/rosters/{division}/{month:yyyy-MM}";

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
