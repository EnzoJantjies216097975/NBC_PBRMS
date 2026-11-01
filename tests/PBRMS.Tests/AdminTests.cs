using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using PBRMS.Domain;
using static PBRMS.Api.Endpoints.OrgUnitEndpoints;
using static PBRMS.Api.Endpoints.UserEndpoints;
using static PBRMS.Tests.TestSteps;

namespace PBRMS.Tests;

/// <summary>
/// What Admin looks after, through the real API: the people who use PBRMS and their roles,
/// and NBC's departments and divisions. Tests add their own people and units.
/// </summary>
[Collection("API")]
public class AdminTests(TestApi api)
{
    private const string Admin = "admin@pbrms.test";
    private const string BookingOfficer = "booking@pbrms.test";

    private static int counter;

    // ---- People ----

    [Fact]
    public async Task Admin_can_add_someone_and_they_can_sign_in_with_their_NBC_email()
    {
        var email = NewEmail();
        var units = await api.UnitsAsync();
        var client = await api.SignedInClientAsync(Admin);

        var response = await client.PostAsJsonAsync("/users", Form($"  {email.ToUpperInvariant()} ", new RoleForm(Role.Operator, units["Camera"], false)), Json);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var person = (await response.Content.ReadFromJsonAsync<PersonResponse>(Json))!;
        Assert.Equal(email, person.WorkEmail);
        Assert.Equal("Camera", Assert.Single(person.Roles).Unit);

        var newcomer = await api.SignedInClientAsync(email);
        Assert.Equal(HttpStatusCode.OK, (await newcomer.GetAsync("/me")).StatusCode);
    }

    [Theory]
    [InlineData("producer@pbrms.test")]
    [InlineData("booking@pbrms.test")]
    [InlineData("management@pbrms.test")]
    public async Task Only_Admin_can_see_or_change_people(string email)
    {
        var client = await api.SignedInClientAsync(email);

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/users")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/users", Form(NewEmail(), new RoleForm(Role.Admin, null, false)), Json)).StatusCode);
    }

    [Fact]
    public async Task An_email_belongs_to_one_person()
    {
        var email = NewEmail();
        var (first, second) = (await AddAsync(email, new RoleForm(Role.Management, null, false)), await AddAsync(NewEmail(), new RoleForm(Role.Management, null, false)));
        var client = await api.SignedInClientAsync(Admin);

        var again = await client.PostAsJsonAsync("/users", Form(email, new RoleForm(Role.Management, null, false)), Json);
        var changed = await client.PutAsJsonAsync($"/users/{second.Id}", Form(first.WorkEmail, new RoleForm(Role.Management, null, false)), Json);

        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, changed.StatusCode);
    }

    [Theory]
    [InlineData(Role.Operator, "News")]         // operators are in a division
    [InlineData(Role.Operator, null)]
    [InlineData(Role.Supervisor, null)]
    [InlineData(Role.AppOperator, "News")]
    [InlineData(Role.Producer, "Camera")]       // producers are in a department
    [InlineData(Role.ExecutiveProducer, null)]
    [InlineData(Role.BookingOfficer, "Camera")] // the Booking Officer covers all of NBC
    [InlineData(Role.Admin, "News")]
    public async Task A_role_has_to_be_in_the_right_kind_of_unit(Role role, string? unit)
    {
        var units = await api.UnitsAsync();
        var client = await api.SignedInClientAsync(Admin);

        var response = await client.PostAsJsonAsync("/users", Form(NewEmail(), new RoleForm(role, unit is null ? null : units[unit], false)), Json);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task An_editor_can_be_in_a_department_or_cover_all_of_NBC()
    {
        var units = await api.UnitsAsync();

        await AddAsync(NewEmail(), new RoleForm(Role.Editor, null, false));
        await AddAsync(NewEmail(), new RoleForm(Role.Editor, units["News"], false));
    }

    [Fact]
    public async Task A_form_with_mistakes_is_refused()
    {
        var units = await api.UnitsAsync();
        var client = await api.SignedInClientAsync(Admin);
        var role = new RoleForm(Role.Management, null, false);

        PersonForm[] mistakes =
        [
            Form("not an email", role),
            Form(NewEmail()),                                                      // no role
            Form(NewEmail(), role) with { FirstName = " " },
            Form(NewEmail(), role) with { SmsOnly = true, Phone = null },          // nowhere to send the SMS
            Form(NewEmail(), new RoleForm(Role.Supervisor, units["Camera"], true)) // only operators go on trips
        ];

        foreach (var mistake in mistakes)
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/users", mistake, Json)).StatusCode);
    }

    [Fact]
    public async Task A_change_of_role_works_straight_away()
    {
        var email = NewEmail();
        var person = await AddAsync(email, new RoleForm(Role.Management, null, false));
        var theirs = await api.SignedInClientAsync(email);
        Assert.Equal(HttpStatusCode.Forbidden, (await theirs.GetAsync("/users")).StatusCode);

        await ChangeAsync(person, Form(email, new RoleForm(Role.Admin, null, false)));
        Assert.Equal(HttpStatusCode.OK, (await theirs.GetAsync("/users")).StatusCode);

        // Back again, so the last-Admin test still has one Admin to protect.
        await ChangeAsync(person, Form(email, new RoleForm(Role.Management, null, false)));
    }

    [Fact]
    public async Task Someone_who_has_left_is_shut_out_at_once_and_can_come_back()
    {
        var email = NewEmail();
        var person = await AddAsync(email, new RoleForm(Role.Management, null, false));
        var theirs = await api.SignedInClientAsync(email);

        await ChangeAsync(person, Form(email, new RoleForm(Role.Management, null, false)) with { IsActive = false });
        Assert.Equal(HttpStatusCode.Forbidden, (await theirs.GetAsync("/me")).StatusCode);

        await ChangeAsync(person, Form(email, new RoleForm(Role.Management, null, false)));
        Assert.Equal(HttpStatusCode.OK, (await theirs.GetAsync("/me")).StatusCode);
    }

    [Fact]
    public async Task NBC_can_never_be_left_without_an_Admin()
    {
        await api.WithDatabaseAsync(db => db.Users
            .Where(u => u.WorkEmail != Admin && u.Roles.Any(r => r.Role == Role.Admin))
            .ExecuteUpdateAsync(u => u.SetProperty(x => x.IsActive, false)));
        var client = await api.SignedInClientAsync(Admin);
        var people = await client.GetFromJsonAsync<List<PersonResponse>>("/users", Json);
        var last = people!.Single(p => p.WorkEmail == Admin);

        var noRole = await client.PutAsJsonAsync($"/users/{last.Id}", Form(Admin, new RoleForm(Role.Management, null, false)), Json);
        var left = await client.PutAsJsonAsync($"/users/{last.Id}", Form(Admin, new RoleForm(Role.Admin, null, false)) with { IsActive = false }, Json);

        Assert.Equal(HttpStatusCode.Conflict, noRole.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, left.StatusCode);
    }

    [Fact]
    public async Task One_of_several_Admins_can_step_down()
    {
        var email = NewEmail();
        var second = await AddAsync(email, new RoleForm(Role.Admin, null, false));

        var changed = await ChangeAsync(second, Form(email, new RoleForm(Role.Management, null, false)));

        Assert.Equal(Role.Management, Assert.Single(changed.Roles).Role);
    }

    [Fact]
    public async Task Someone_leaving_with_work_ahead_needs_a_second_look_and_the_people_who_must_replace_them_are_told()
    {
        var (person, booking) = await OperatorOnConfirmedStudioBookingAsync();
        var client = await api.SignedInClientAsync(Admin);
        var units = await api.UnitsAsync();
        var form = Form(person.WorkEmail, new RoleForm(Role.Operator, units["Camera"], false)) with { IsActive = false };

        var warned = await client.PutAsJsonAsync($"/users/{person.Id}", form, Json);

        Assert.Equal(HttpStatusCode.Conflict, warned.StatusCode);
        Assert.Contains($"\"productionNo\":{booking.ProductionNo}", await warned.Content.ReadAsStringAsync());
        Assert.True((await Find(person)).IsActive); // nothing was saved

        var gone = await client.PutAsJsonAsync($"/users/{person.Id}", form with { AcknowledgeWarnings = true }, Json);

        Assert.Equal(HttpStatusCode.OK, gone.StatusCode);
        Assert.False((await Find(person)).IsActive);
        Assert.Contains(await api.NotificationsAsync("supervisor@pbrms.test"), n => n.BookingId == booking.Id && n.Text.Contains("replaces them"));
        Assert.Contains(await api.NotificationsAsync(BookingOfficer), n => n.BookingId == booking.Id && n.Text.Contains("replaces them"));
    }

    [Fact]
    public async Task Moving_an_operator_to_another_division_warns_about_their_work_in_the_old_one()
    {
        var (person, _) = await OperatorOnConfirmedStudioBookingAsync();
        var client = await api.SignedInClientAsync(Admin);
        var units = await api.UnitsAsync();

        var moved = await client.PutAsJsonAsync($"/users/{person.Id}", Form(person.WorkEmail, new RoleForm(Role.Operator, units["Sound"], false)), Json);

        Assert.Equal(HttpStatusCode.Conflict, moved.StatusCode);
    }

    [Fact]
    public async Task Changing_someones_phone_doesnt_warn_about_their_work()
    {
        var (person, _) = await OperatorOnConfirmedStudioBookingAsync();
        var units = await api.UnitsAsync();

        await ChangeAsync(person, Form(person.WorkEmail, new RoleForm(Role.Operator, units["Camera"], false)) with { Phone = "+264 81 000 0001" });
    }

    [Fact]
    public async Task Cancelled_or_finished_work_doesnt_hold_up_someone_leaving()
    {
        var (person, booking) = await OperatorOnConfirmedStudioBookingAsync();
        var units = await api.UnitsAsync();
        await api.TransitionAsync(BookingOfficer, booking.Id, BookingAction.Cancel, "Presenter ill");

        var gone = await ChangeAsync(person, Form(person.WorkEmail, new RoleForm(Role.Operator, units["Camera"], false)) with { IsActive = false });

        Assert.False(gone.IsActive);
    }

    // ---- Departments and divisions ----

    [Fact]
    public async Task Admin_can_add_a_division_and_people_can_be_put_in_it()
    {
        var name = NewUnitName();
        var client = await api.SignedInClientAsync(Admin);

        var added = await client.PostAsJsonAsync("/org-units", new OrgUnitForm($" {name} ", OrgUnitKind.TvOpsDivision, null), Json);

        Assert.Equal(HttpStatusCode.Created, added.StatusCode);
        var units = await api.UnitsAsync();
        await AddAsync(NewEmail(), new RoleForm(Role.Supervisor, units[name], false));
    }

    [Fact]
    public async Task Only_Admin_can_change_departments()
    {
        var client = await api.SignedInClientAsync(BookingOfficer);

        var response = await client.PostAsJsonAsync("/org-units", new OrgUnitForm(NewUnitName(), OrgUnitKind.ContentDepartment, null), Json);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Two_units_cant_share_a_name()
    {
        var client = await api.SignedInClientAsync(Admin);

        var response = await client.PostAsJsonAsync("/org-units", new OrgUnitForm("nEwS", OrgUnitKind.ContentDepartment, null), Json);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task A_part_of_a_department_sits_inside_a_content_department()
    {
        var units = await api.UnitsAsync();
        var client = await api.SignedInClientAsync(Admin);

        var orphan = await client.PostAsJsonAsync("/org-units", new OrgUnitForm(NewUnitName(), OrgUnitKind.SubDepartment, null), Json);
        var inADivision = await client.PostAsJsonAsync("/org-units", new OrgUnitForm(NewUnitName(), OrgUnitKind.SubDepartment, units["Camera"]), Json);
        var nestedDivision = await client.PostAsJsonAsync("/org-units", new OrgUnitForm(NewUnitName(), OrgUnitKind.TvOpsDivision, units["News"]), Json);
        var desk = await client.PostAsJsonAsync("/org-units", new OrgUnitForm(NewUnitName(), OrgUnitKind.SubDepartment, units["News"]), Json);

        Assert.Equal(HttpStatusCode.BadRequest, orphan.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, inADivision.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, nestedDivision.StatusCode);
        Assert.Equal(HttpStatusCode.Created, desk.StatusCode);
    }

    [Fact]
    public async Task A_unit_can_be_renamed_but_not_turned_into_another_kind()
    {
        var unit = await AddUnitAsync(OrgUnitKind.ContentDepartment);
        var newName = NewUnitName();
        var client = await api.SignedInClientAsync(Admin);

        var renamed = await client.PutAsJsonAsync($"/org-units/{unit.Id}", new OrgUnitForm(newName, OrgUnitKind.ContentDepartment, null), Json);
        var rekinded = await client.PutAsJsonAsync($"/org-units/{unit.Id}", new OrgUnitForm(newName, OrgUnitKind.TvOpsDivision, null), Json);

        Assert.Equal(HttpStatusCode.OK, renamed.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, rekinded.StatusCode);
        Assert.Contains(newName, (await api.UnitsAsync()).Keys);
    }

    [Fact]
    public async Task A_unit_added_by_mistake_can_be_removed_but_one_in_use_cant()
    {
        var mistake = await AddUnitAsync(OrgUnitKind.ContentDepartment);
        var units = await api.UnitsAsync();
        var client = await api.SignedInClientAsync(Admin);

        var removed = await client.DeleteAsync($"/org-units/{mistake.Id}");
        var inUse = await client.DeleteAsync($"/org-units/{units["News"]}");

        Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, inUse.StatusCode);
        Assert.DoesNotContain(mistake.Name, (await api.UnitsAsync()).Keys);
    }

    [Fact]
    public async Task A_department_with_parts_inside_it_cant_be_removed()
    {
        var department = await AddUnitAsync(OrgUnitKind.ContentDepartment);
        var client = await api.SignedInClientAsync(Admin);
        await client.PostAsJsonAsync("/org-units", new OrgUnitForm(NewUnitName(), OrgUnitKind.SubDepartment, department.Id), Json);

        Assert.Equal(HttpStatusCode.Conflict, (await client.DeleteAsync($"/org-units/{department.Id}")).StatusCode);
    }

    // ---- Helpers ----

    private static string NewEmail() => $"person{Interlocked.Increment(ref counter)}@pbrms.test";

    private static string NewUnitName() => $"Unit {Interlocked.Increment(ref counter)}";

    private static PersonForm Form(string email, params RoleForm[] roles) =>
        new("Test", "Person", email, "+264 81 000 0000", EmploymentType.Permanent, false, true, [.. roles]);

    private async Task<PersonResponse> AddAsync(string email, params RoleForm[] roles)
    {
        var client = await api.SignedInClientAsync(Admin);
        var response = await client.PostAsJsonAsync("/users", Form(email, roles), Json);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<PersonResponse>(Json))!;
    }

    private async Task<PersonResponse> ChangeAsync(PersonResponse person, PersonForm form)
    {
        var client = await api.SignedInClientAsync(Admin);
        var response = await client.PutAsJsonAsync($"/users/{person.Id}", form, Json);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<PersonResponse>(Json))!;
    }

    private async Task<PersonResponse> Find(PersonResponse person)
    {
        var client = await api.SignedInClientAsync(Admin);
        var people = await client.GetFromJsonAsync<List<PersonResponse>>("/users", Json);
        return people!.Single(p => p.Id == person.Id);
    }

    private async Task<OrgUnitResponse> AddUnitAsync(OrgUnitKind kind)
    {
        var client = await api.SignedInClientAsync(Admin);
        var response = await client.PostAsJsonAsync("/org-units", new OrgUnitForm(NewUnitName(), kind, null), Json);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<OrgUnitResponse>(Json))!;
    }

    /// <summary>A new Camera operator, on a confirmed studio booking in the future.</summary>
    private async Task<(PersonResponse Person, BookingResponse Booking)> OperatorOnConfirmedStudioBookingAsync()
    {
        var units = await api.UnitsAsync();
        var person = await AddAsync(NewEmail(), new RoleForm(Role.Operator, units["Camera"], false));

        var booking = await api.AtCrewStageAsync(NewDay(), Venue.Studio1, "Camera");
        await api.AssignOkAsync(booking, "Camera", person.Id);
        await api.TransitionAsync(BookingOfficer, booking.Id, BookingAction.Approve);

        return (person, booking);
    }
}
