using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using PBRMS.Api.Endpoints;
using PBRMS.Domain;
using static PBRMS.Api.Endpoints.OrgUnitEndpoints;

namespace PBRMS.Tests;

/// <summary>
/// Steps shared by the API tests: making an operator nobody else uses, and getting a
/// booking to a given stage of the approval chain. Sample people: News producer and
/// EP; Camera and Sound supervisors; the Booking Officer.
/// </summary>
public static class TestSteps
{
    // The API sends enums as text ("Draft"), so the tests read and write them the same way.
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private static int dayCounter;

    /// <summary>10h00 on a day no other test uses. The database is wiped each run, so fixed dates are safe.</summary>
    public static DateTimeOffset NewDay() =>
        new DateTimeOffset(2030, 1, 1, 10, 0, 0, TimeSpan.FromHours(2)).AddDays(Interlocked.Increment(ref dayCounter));

    /// <summary>Org unit ids by name, e.g. units["Camera"].</summary>
    public static async Task<Dictionary<string, Guid>> UnitsAsync(this TestApi api)
    {
        var client = await api.SignedInClientAsync("producer@pbrms.test");
        var units = await client.GetFromJsonAsync<List<OrgUnitResponse>>("/org-units", Json);
        return units!.ToDictionary(u => u.Name, u => u.Id);
    }

    /// <summary>Adds an operator nobody else uses, so no earlier test can have booked them.</summary>
    public static async Task<Guid> NewOperatorAsync(this TestApi api, string division)
    {
        var units = await api.UnitsAsync();
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

    /// <summary>A two-hour News booking approved by the EP, with one crew slot per division named.</summary>
    public static async Task<BookingResponse> AtCrewStageAsync(this TestApi api, DateTimeOffset start, Venue venue, params string[] divisions) =>
        await api.AtCrewStageAsync(start, venue, isTrip: false, divisions);

    public static async Task<BookingResponse> AtCrewStageAsync(this TestApi api, DateTimeOffset start, Venue venue, bool isTrip, params string[] divisions)
    {
        var units = await api.UnitsAsync();
        var crew = divisions.Select(d => new CrewSlotForm(units[d], $"{d} 1", null, null, null)).ToList();
        var form = new BookingForm(
            "Evening News Special", units["News"], null, ActivityType.Recording, venue, null,
            start.AddMinutes(-30), start, start.AddHours(2), isTrip, false, null, null, crew);

        var producer = await api.SignedInClientAsync("producer@pbrms.test");
        var created = await producer.PostAsJsonAsync("/bookings", form, Json);
        var booking = (await created.Content.ReadFromJsonAsync<BookingResponse>(Json))!;

        // Past the 15h00 cut-off, which only the Booking Officer can allow.
        if (BookingWorkflow.MissedCutOff(start, DateTimeOffset.UtcNow))
        {
            var officer = await api.SignedInClientAsync("booking@pbrms.test");
            (await officer.PostAsJsonAsync($"/bookings/{booking.Id}/allow-late", new OverrideRequest("Test booking"), Json)).EnsureSuccessStatusCode();
        }

        await api.TransitionAsync("producer@pbrms.test", booking.Id, BookingAction.Submit);
        return await api.TransitionAsync("ep@pbrms.test", booking.Id, BookingAction.Approve);
    }

    /// <summary>A studio booking with its one Camera slot filled, waiting for the Booking Officer to confirm.</summary>
    public static async Task<BookingResponse> AtFinalisationAsync(this TestApi api, DateTimeOffset start, Venue venue)
    {
        var booking = await api.AtCrewStageAsync(start, venue, "Camera");
        return await api.AssignOkAsync(booking, "Camera", await api.NewOperatorAsync("Camera"));
    }

    public static async Task<HttpResponseMessage> AssignAsync(this TestApi api, string email, BookingResponse booking, string division, Guid operatorId, bool acknowledge = false)
    {
        var client = await api.SignedInClientAsync(email);
        var slot = booking.CrewSlots.Single(s => s.Division == division);
        return await client.PutAsJsonAsync($"/bookings/{booking.Id}/crew", new CrewRequest([new(slot.Id, operatorId)], acknowledge), Json);
    }

    /// <summary>Whoever decides this booking's crew: the Booking Officer for a studio, the Camera or Sound supervisor outside.</summary>
    private static string Decider(BookingResponse booking, string division) =>
        booking.CrewLead == Role.BookingOfficer || ClashRules.IsStudio(booking.Venue) ? "booking@pbrms.test"
        : division == "Sound" ? "supervisor.sound@pbrms.test" : "supervisor@pbrms.test";

    public static Task<HttpResponseMessage> AssignAsync(this TestApi api, BookingResponse booking, string division, Guid operatorId, bool acknowledge = false) =>
        api.AssignAsync(Decider(booking, division), booking, division, operatorId, acknowledge);

    public static Task<BookingResponse> AssignOkAsync(this TestApi api, BookingResponse booking, string division, Guid operatorId) =>
        api.AssignOkAsync(Decider(booking, division), booking, division, operatorId);

    /// <summary>The sign-in email of an operator made by <see cref="NewOperatorAsync"/>.</summary>
    public static string OperatorEmail(Guid operatorId) => $"{operatorId:N}@pbrms.test";

    public static async Task<List<NotificationEndpoints.NotificationResponse>> NotificationsAsync(this TestApi api, string email)
    {
        var client = await api.SignedInClientAsync(email);
        return (await client.GetFromJsonAsync<List<NotificationEndpoints.NotificationResponse>>("/notifications", Json))!;
    }

    public static async Task<BookingResponse> AssignOkAsync(this TestApi api, string email, BookingResponse booking, string division, Guid operatorId)
    {
        var response = await api.AssignAsync(email, booking, division, operatorId);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<BookingResponse>(Json))!;
    }

    public static async Task<BookingResponse> TransitionAsync(this TestApi api, string email, Guid bookingId, BookingAction action, string? note = null)
    {
        var client = await api.SignedInClientAsync(email);
        var response = await client.PostAsJsonAsync($"/bookings/{bookingId}/transition", new TransitionRequest(action, note, null, null), Json);
        Assert.True(response.IsSuccessStatusCode, $"{email} {action}: {await response.Content.ReadAsStringAsync()}");
        return (await response.Content.ReadFromJsonAsync<BookingResponse>(Json))!;
    }
}
