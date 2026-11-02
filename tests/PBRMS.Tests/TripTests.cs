using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using PBRMS.Domain;
using static PBRMS.Tests.TestSteps;

namespace PBRMS.Tests;

/// <summary>
/// The trip rotation through the real API. Every test gets a new division with its own
/// supervisor and operators, because everyone in a division shares one queue.
/// The ordering itself is tested in <see cref="TripRotationTests"/>.
/// </summary>
[Collection("API")]
public class TripTests(TestApi api)
{
    /// <summary>A new division and the sign-in email of its supervisor.</summary>
    private async Task<(string Name, Guid Id, string Supervisor)> NewDivisionAsync()
    {
        var division = new OrgUnit { Id = Guid.NewGuid(), Name = $"Trips {Guid.NewGuid():N}", Kind = OrgUnitKind.TvOpsDivision };
        var email = $"{Guid.NewGuid():N}@pbrms.test";

        await api.WithDatabaseAsync(async db =>
        {
            db.OrgUnits.Add(division);
            db.Users.Add(new User
            {
                FirstName = "Temp",
                Surname = "Supervisor",
                WorkEmail = email,
                Roles = [new() { Role = Role.Supervisor, OrgUnitId = division.Id }]
            });
            await db.SaveChangesAsync();
        });

        return (division.Name, division.Id, email);
    }

    /// <summary>An operator named so the tests know their place when nothing else separates them.</summary>
    private async Task<Guid> OperatorAsync(string division, string firstName)
    {
        var id = await api.NewOperatorAsync(division);
        await api.WithDatabaseAsync(db => db.Users.Where(u => u.Id == id).ExecuteUpdateAsync(u => u.SetProperty(x => x.FirstName, firstName)));
        return id;
    }

    private async Task<List<TripStanding>> RotationAsync(string email, Guid divisionId)
    {
        var client = await api.SignedInClientAsync(email);
        return (await client.GetFromJsonAsync<List<TripStanding>>($"/org-units/{divisionId}/trip-rotation", Json))!;
    }

    private async Task LogTripAsync(string email, Guid divisionId, Guid operatorId, DateOnly from, DateOnly to)
    {
        var client = await api.SignedInClientAsync(email);
        var response = await client.PostAsJsonAsync($"/org-units/{divisionId}/trip-rotation/{operatorId}/trips", new LoggedTripForm("Rundu", from, to), Json);
        response.EnsureSuccessStatusCode();
    }

    private static readonly DateOnly LastYear = DateOnly.FromDateTime(DateTime.Today.AddYears(-1));

    [Fact]
    public async Task The_next_in_turn_goes_without_a_reason()
    {
        var (division, _, supervisor) = await NewDivisionAsync();
        var first = await OperatorAsync(division, "Anna");
        await OperatorAsync(division, "Ben");
        var trip = await api.AtCrewStageAsync(NewDay(), Venue.Location, isTrip: true, division);

        var response = await api.AssignAsync(supervisor, trip, division, first);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Going_out_of_turn_is_refused_and_names_who_is_passed_over()
    {
        var (division, _, supervisor) = await NewDivisionAsync();
        await OperatorAsync(division, "Anna");
        var second = await OperatorAsync(division, "Ben");
        var trip = await api.AtCrewStageAsync(NewDay(), Venue.Location, isTrip: true, division);

        var response = await api.AssignAsync(supervisor, trip, division, second);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("TripTurn", body);
        Assert.Contains("Anna", body);
    }

    [Fact]
    public async Task Going_out_of_turn_with_a_reason_is_kept_in_the_booking_history()
    {
        var (division, _, supervisor) = await NewDivisionAsync();
        await OperatorAsync(division, "Anna");
        var second = await OperatorAsync(division, "Ben");
        var trip = await api.AtCrewStageAsync(NewDay(), Venue.Location, isTrip: true, division);

        var response = await api.AssignAsync(supervisor, trip, division, second, reason: "Only drone pilot");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var booking = await response.Content.ReadFromJsonAsync<BookingResponse>(Json);
        Assert.Contains(booking!.History, h => h.Note == "Trip order passed over Anna Operator: Only drone pilot");
    }

    [Fact]
    public async Task A_blank_reason_doesnt_count()
    {
        var (division, _, supervisor) = await NewDivisionAsync();
        await OperatorAsync(division, "Anna");
        var second = await OperatorAsync(division, "Ben");
        var trip = await api.AtCrewStageAsync(NewDay(), Venue.Location, isTrip: true, division);

        var response = await api.AssignAsync(supervisor, trip, division, second, reason: "   ");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Someone_ahead_who_cant_go_isnt_passed_over()
    {
        var start = NewDay();
        var (division, _, supervisor) = await NewDivisionAsync();
        var first = await OperatorAsync(division, "Anna");
        var second = await OperatorAsync(division, "Ben");
        var other = await api.AtCrewStageAsync(start, Venue.Location, division);
        await api.AssignOkAsync(supervisor, other, division, first); // busy at that time, on a job that isn't a trip
        var trip = await api.AtCrewStageAsync(start, Venue.Location, isTrip: true, division);

        var response = await api.AssignAsync(supervisor, trip, division, second);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task A_reason_doesnt_get_past_a_real_clash()
    {
        var start = NewDay();
        var (division, _, supervisor) = await NewDivisionAsync();
        var first = await OperatorAsync(division, "Anna");
        var other = await api.AtCrewStageAsync(start, Venue.Location, division);
        await api.AssignOkAsync(supervisor, other, division, first);
        var trip = await api.AtCrewStageAsync(start, Venue.Location, isTrip: true, division);

        var response = await api.AssignAsync(supervisor, trip, division, first, reason: "Please");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Two_positions_filled_together_dont_pass_each_other_over()
    {
        var (division, _, supervisor) = await NewDivisionAsync();
        var first = await OperatorAsync(division, "Anna");
        var second = await OperatorAsync(division, "Ben");
        var trip = await api.AtCrewStageAsync(NewDay(), Venue.Location, isTrip: true, division, division);

        var client = await api.SignedInClientAsync(supervisor);
        var request = new CrewRequest([new(trip.CrewSlots[0].Id, second), new(trip.CrewSlots[1].Id, first)]);
        var response = await client.PutAsJsonAsync($"/bookings/{trip.Id}/crew", request, Json);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task A_trip_with_a_crew_moves_them_out_of_the_queue_and_two_positions_count_once()
    {
        var (division, divisionId, supervisor) = await NewDivisionAsync();
        var traveller = await OperatorAsync(division, "Anna");
        await OperatorAsync(division, "Ben");
        var trip = await api.AtCrewStageAsync(NewDay(), Venue.Location, isTrip: true, division, division);

        var client = await api.SignedInClientAsync(supervisor);
        var both = new CrewRequest([new(trip.CrewSlots[0].Id, traveller), new(trip.CrewSlots[1].Id, traveller)]);
        (await client.PutAsJsonAsync($"/bookings/{trip.Id}/crew", both, Json)).EnsureSuccessStatusCode();

        var rotation = await RotationAsync(supervisor, divisionId);
        var row = rotation.Single(r => r.OperatorId == traveller);
        Assert.Null(row.Place);
        Assert.Equal(1, row.Trips);
        Assert.Equal(1, rotation.Single(r => r.OperatorId != traveller).Place);
    }

    [Fact]
    public async Task A_cancelled_trip_gives_their_turn_back()
    {
        var (division, divisionId, supervisor) = await NewDivisionAsync();
        var traveller = await OperatorAsync(division, "Anna");
        var trip = await api.AtCrewStageAsync(NewDay(), Venue.Location, isTrip: true, division);
        await api.AssignOkAsync(supervisor, trip, division, traveller);

        await api.TransitionAsync("ep@pbrms.test", trip.Id, BookingAction.Cancel, "Called off");

        var row = (await RotationAsync(supervisor, divisionId)).Single();
        Assert.Equal(1, row.Place);
        Assert.Equal(0, row.Trips);
    }

    [Fact]
    public async Task A_trip_logged_by_hand_moves_someone_back_and_removing_it_undoes_that()
    {
        var (division, divisionId, supervisor) = await NewDivisionAsync();
        var anna = await OperatorAsync(division, "Anna");
        await OperatorAsync(division, "Ben");

        await LogTripAsync(supervisor, divisionId, anna, LastYear, LastYear.AddDays(3));

        var rotation = await RotationAsync(supervisor, divisionId);
        Assert.Equal("Ben Operator", rotation[0].Name);
        var logged = rotation[1].History.Single();
        Assert.Equal(LastYear.AddDays(3), rotation[1].LastBack);

        var client = await api.SignedInClientAsync(supervisor);
        var removed = await client.DeleteAsync($"/org-units/{divisionId}/trip-rotation/{anna}/trips/{logged.LoggedId}");

        removed.EnsureSuccessStatusCode();
        Assert.Equal("Anna Operator", (await RotationAsync(supervisor, divisionId))[0].Name);
    }

    [Fact]
    public async Task A_trip_still_to_come_cant_be_logged_by_hand()
    {
        var (division, divisionId, supervisor) = await NewDivisionAsync();
        var anna = await OperatorAsync(division, "Anna");
        var tomorrow = DateOnly.FromDateTime(DateTime.Today.AddDays(1));

        var client = await api.SignedInClientAsync(supervisor);
        var response = await client.PostAsJsonAsync($"/org-units/{divisionId}/trip-rotation/{anna}/trips", new LoggedTripForm("Rundu", tomorrow, tomorrow), Json);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Someone_sitting_out_goes_only_with_a_reason()
    {
        var (division, divisionId, supervisor) = await NewDivisionAsync();
        var anna = await OperatorAsync(division, "Anna");
        var ben = await OperatorAsync(division, "Ben");
        var client = await api.SignedInClientAsync(supervisor);
        (await client.PutAsJsonAsync($"/org-units/{divisionId}/trip-rotation/{anna}/sit-out", new TripSitOutForm(TripSitOutReason.Studies, null), Json))
            .EnsureSuccessStatusCode();
        var trip = await api.AtCrewStageAsync(NewDay(), Venue.Location, isTrip: true, division);

        var refused = await api.AssignAsync(supervisor, trip, division, anna);
        var allowed = await api.AssignAsync(supervisor, trip, division, anna, reason: "Only one who knows the site");

        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        Assert.Null((await RotationAsync(supervisor, divisionId)).Single(r => r.OperatorId == anna).SittingOut);
        Assert.Equal(1, (await RotationAsync(supervisor, divisionId)).Single(r => r.OperatorId == ben).Place);
    }

    [Fact]
    public async Task Putting_someone_back_in_restores_their_turn()
    {
        var (division, divisionId, supervisor) = await NewDivisionAsync();
        var anna = await OperatorAsync(division, "Anna");
        var client = await api.SignedInClientAsync(supervisor);
        var url = $"/org-units/{divisionId}/trip-rotation/{anna}/sit-out";

        await client.PutAsJsonAsync(url, new TripSitOutForm(TripSitOutReason.Family, null), Json);
        Assert.Null((await RotationAsync(supervisor, divisionId)).Single().Place);

        await client.PutAsJsonAsync(url, new TripSitOutForm(null, null), Json);
        Assert.Equal(1, (await RotationAsync(supervisor, divisionId)).Single().Place);
    }

    [Fact]
    public async Task Operators_who_cant_travel_arent_on_the_rotation()
    {
        var (division, divisionId, supervisor) = await NewDivisionAsync();
        var kept = await OperatorAsync(division, "Anna");
        await api.WithDatabaseAsync(db => db.RoleAssignments
            .Where(r => r.UserId == kept)
            .ExecuteUpdateAsync(r => r.SetProperty(x => x.StudioBound, true)));

        Assert.Empty(await RotationAsync(supervisor, divisionId));
    }

    [Fact]
    public async Task Only_the_people_who_decide_can_read_the_rotation_because_reasons_are_personal()
    {
        var (division, divisionId, _) = await NewDivisionAsync();
        var anna = await OperatorAsync(division, "Anna");

        async Task<HttpStatusCode> ReadAs(string email) =>
            (await (await api.SignedInClientAsync(email)).GetAsync($"/org-units/{divisionId}/trip-rotation")).StatusCode;

        Assert.Equal(HttpStatusCode.OK, await ReadAs("booking@pbrms.test"));
        Assert.Equal(HttpStatusCode.Forbidden, await ReadAs("supervisor@pbrms.test")); // another division's supervisor
        Assert.Equal(HttpStatusCode.Forbidden, await ReadAs("producer@pbrms.test"));
        Assert.Equal(HttpStatusCode.Forbidden, await ReadAs(OperatorEmail(anna)));
    }

    [Fact]
    public async Task Only_the_divisions_supervisor_can_change_the_rotation()
    {
        var (division, divisionId, _) = await NewDivisionAsync();
        var anna = await OperatorAsync(division, "Anna");
        var url = $"/org-units/{divisionId}/trip-rotation/{anna}/sit-out";

        var officer = await (await api.SignedInClientAsync("booking@pbrms.test")).PutAsJsonAsync(url, new TripSitOutForm(TripSitOutReason.Other, null), Json);
        var other = await (await api.SignedInClientAsync("supervisor@pbrms.test")).PutAsJsonAsync(url, new TripSitOutForm(TripSitOutReason.Other, null), Json);

        Assert.Equal(HttpStatusCode.Forbidden, officer.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, other.StatusCode);
    }
}
