using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using PBRMS.Data;
using PBRMS.Domain;
using static PBRMS.Api.Endpoints.AuditEndpoints;
using static PBRMS.Api.Endpoints.UserEndpoints;
using static PBRMS.Tests.TestSteps;

namespace PBRMS.Tests;

/// <summary>
/// The audit log (BR-18), through the real API: every change is written down with who made
/// it, in the same save as the change, and only Admin can read it. Also the first Admin,
/// who has to exist before anyone can use the People screen on NBC's server.
/// </summary>
[Collection("API")]
public class AuditTests(TestApi api)
{
    private const string Admin = "admin@pbrms.test";

    private static int counter;

    [Fact]
    public async Task A_change_to_a_booking_says_who_made_it_and_what_changed()
    {
        var booking = await api.AtCrewStageAsync(NewDay(), Venue.Location, "Camera");

        var rows = await LogAsync();

        var made = rows.Single(r => r.What == "Booking" && r.Action == AuditAction.Added && r.Item == $"#{booking.ProductionNo} {booking.Title}");
        Assert.Equal("Test Producer", made.Who);
        Assert.DoesNotContain("Production No", made.Summary); // SQL Server hands it out during the save

        Assert.Contains(rows, r => r is { What: "Booking", Action: AuditAction.Changed, Who: "Test Producer" }
                                   && r.Item == made.Item && r.Summary.Contains("Status: Draft → EpReview"));
        Assert.Contains(rows, r => r is { What: "Booking", Action: AuditAction.Changed, Who: "Test ExecutiveProducer" }
                                   && r.Item == made.Item && r.Summary.Contains("Status: EpReview → CrewAssignment"));
    }

    [Fact]
    public async Task Giving_someone_a_role_reads_in_names_not_codes()
    {
        var person = await AddPersonAsync(Role.Supervisor, "Camera");

        var row = (await LogAsync()).First(r => r is { What: "Role Assignment", Action: AuditAction.Added } && r.Summary.Contains(person.WorkEmail.Split('@')[0]));

        Assert.Equal("Test Admin", row.Who);
        Assert.Contains("Role: Supervisor", row.Summary);
        Assert.Contains("Camera", row.Summary);
        Assert.DoesNotMatch("[0-9a-f]{8}-[0-9a-f]{4}", row.Summary);
    }

    [Fact]
    public async Task Taking_a_role_away_is_recorded_with_what_it_was()
    {
        var person = await AddPersonAsync(Role.Management, null);
        await ChangeAsync(person, FormFor(person, new RoleForm(Role.TvOperationsManager, null, false)));

        var rows = await LogAsync();

        var marker = person.WorkEmail.Split('@')[0];
        Assert.Contains(rows, r => r is { What: "Role Assignment", Action: AuditAction.Removed } && r.Summary.Contains("Role: Management") && r.Summary.Contains(marker));
        Assert.Contains(rows, r => r is { What: "Role Assignment", Action: AuditAction.Added } && r.Summary.Contains("Role: TvOperationsManager") && r.Summary.Contains(marker));
    }

    [Fact]
    public async Task Changing_a_phone_number_doesnt_look_like_a_change_of_role()
    {
        var person = await AddPersonAsync(Role.Management, null);
        var before = (await LogAsync()).Count(r => r.What == "Role Assignment");

        await ChangeAsync(person, FormFor(person, new RoleForm(Role.Management, null, false)) with { Phone = "+264 81 000 0002" });

        var rows = await LogAsync();
        Assert.Equal(before, rows.Count(r => r.What == "Role Assignment"));
        Assert.Contains(rows, r => r is { What: "User", Action: AuditAction.Changed } && r.Summary.Contains("+264 81 000 0002"));
    }

    [Fact]
    public async Task A_refused_change_leaves_no_record()
    {
        var person = await AddPersonAsync(Role.Management, null);
        var client = await api.SignedInClientAsync(Admin);
        var before = (await LogAsync()).Count;

        var taken = await client.PutAsJsonAsync($"/users/{person.Id}", FormFor(person, new RoleForm(Role.Management, null, false)) with { WorkEmail = Admin }, Json);

        Assert.Equal(HttpStatusCode.Conflict, taken.StatusCode);
        Assert.Equal(before, (await LogAsync()).Count);
    }

    [Fact]
    public async Task Work_nobody_signed_in_for_is_recorded_as_the_system()
    {
        var name = $"Audit unit {Interlocked.Increment(ref counter)}";

        await api.WithDatabaseAsync(async db =>
        {
            db.OrgUnits.Add(new OrgUnit { Name = name, Kind = OrgUnitKind.ContentDepartment });
            await db.SaveChangesAsync();
        });

        Assert.Contains(await LogAsync(), r => r.Who == "System" && r.Summary.Contains(name));
    }

    [Fact]
    public async Task Notifications_arent_changes_so_they_arent_logged()
    {
        await api.AtCrewStageAsync(NewDay(), Venue.Location, "Camera");

        Assert.DoesNotContain(await LogAsync(), r => r.What == "Notification");
    }

    [Theory]
    [InlineData("booking@pbrms.test")]
    [InlineData("tvops.manager@pbrms.test")]
    [InlineData("management@pbrms.test")]
    public async Task Only_Admin_can_read_the_log(string email)
    {
        var client = await api.SignedInClientAsync(email);

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(Url())).StatusCode);
    }

    [Fact]
    public async Task A_long_log_says_theres_more_to_see()
    {
        await AddPersonAsync(Role.Management, null);
        var client = await api.SignedInClientAsync(Admin);

        var page = await client.GetFromJsonAsync<AuditReport>($"{Url()}&limit=1", Json);

        Assert.Single(page!.Rows);
        Assert.True(page.More);
    }

    // ---- The first Admin ----

    [Fact]
    public async Task The_first_Admin_can_only_be_set_up_while_NBC_has_none()
    {
        var email = $"first{Interlocked.Increment(ref counter)}@pbrms.test";

        await api.WithDatabaseAsync(async db =>
        {
            Assert.NotNull(await FirstAdmin.AddAsync(db, email, "First", "Admin")); // there's already one

            await SetAdminsActiveAsync(db, false);
            try
            {
                Assert.NotNull(await FirstAdmin.AddAsync(db, "not an email", "First", "Admin"));
                Assert.Null(await FirstAdmin.AddAsync(db, email, "First", "Admin"));
                Assert.NotNull(await FirstAdmin.AddAsync(db, $"x{email}", "Second", "Admin")); // now there is
            }
            finally
            {
                await SetAdminsActiveAsync(db, true);
                await db.Users.Where(u => u.WorkEmail == email).ExecuteUpdateAsync(u => u.SetProperty(x => x.IsActive, false));
            }
        });

        await api.WithDatabaseAsync(async db =>
            Assert.Equal(Role.Admin, (await db.RoleAssignments.SingleAsync(r => r.User.WorkEmail == email)).Role));
    }

    // ---- Helpers ----

    private static Task SetAdminsActiveAsync(PbrmsDbContext db, bool active) =>
        db.Users.Where(u => u.WorkEmail == Admin).ExecuteUpdateAsync(u => u.SetProperty(x => x.IsActive, active));

    private static string Url()
    {
        var today = NbcTime.DateOf(DateTimeOffset.UtcNow);
        return $"/reports/audit?from={today.AddDays(-1):yyyy-MM-dd}&to={today.AddDays(1):yyyy-MM-dd}";
    }

    private async Task<List<AuditRow>> LogAsync()
    {
        var client = await api.SignedInClientAsync(Admin);
        return (await client.GetFromJsonAsync<AuditReport>(Url(), Json))!.Rows;
    }

    private static PersonForm FormFor(PersonResponse person, params RoleForm[] roles) =>
        new(person.FirstName, person.Surname, person.WorkEmail, person.Phone, person.EmploymentType, person.SmsOnly, person.IsActive, [.. roles]);

    /// <summary>A new person whose surname is unique, so a test can find their rows in the log.</summary>
    private async Task<PersonResponse> AddPersonAsync(Role role, string? unit)
    {
        var units = await api.UnitsAsync();
        var marker = $"auditee{Interlocked.Increment(ref counter)}";
        var form = new PersonForm("Test", marker, $"{marker}@pbrms.test", "+264 81 000 0000", EmploymentType.Permanent, false, true,
            [new RoleForm(role, unit is null ? null : units[unit], false)]);

        var client = await api.SignedInClientAsync(Admin);
        var response = await client.PostAsJsonAsync("/users", form, Json);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<PersonResponse>(Json))!;
    }

    private async Task ChangeAsync(PersonResponse person, PersonForm form)
    {
        var client = await api.SignedInClientAsync(Admin);
        var response = await client.PutAsJsonAsync($"/users/{person.Id}", form, Json);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
    }
}
