using System.Net.Mail;
using Microsoft.EntityFrameworkCore;
using PBRMS.Data;
using PBRMS.Domain;

namespace PBRMS.Api.Endpoints;

/// <summary>
/// The people who use PBRMS and their roles, for Admin only. Nobody is deleted: someone who
/// leaves stays on file with IsActive false, so their history stands, and they can come back.
/// Roles are read from the database on every request, so a change works straight away.
/// </summary>
public static class UserEndpoints
{
    public record RoleForm(Role Role, Guid? OrgUnitId, bool StudioBound);

    /// <param name="IsActive">False once they've left NBC. They can no longer sign in.</param>
    /// <param name="AcknowledgeWarnings">
    /// Admin has seen the productions this person would drop out of. Without it, those come
    /// back as a 409 listing them, and nothing is saved.
    /// </param>
    public record PersonForm(
        string FirstName,
        string Surname,
        string WorkEmail,
        string? Phone,
        EmploymentType EmploymentType,
        bool SmsOnly,
        bool IsActive,
        List<RoleForm> Roles,
        bool AcknowledgeWarnings = false);

    public record PersonRole(Role Role, Guid? OrgUnitId, string? Unit, bool StudioBound);

    public record PersonResponse(
        Guid Id,
        string FirstName,
        string Surname,
        string WorkEmail,
        string? Phone,
        EmploymentType EmploymentType,
        bool SmsOnly,
        bool IsActive,
        List<PersonRole> Roles);

    public static void MapUserEndpoints(this WebApplication app)
    {
        var admin = nameof(Role.Admin);

        app.MapGet("/users", List).RequireAuthorization(p => p.RequireRole(admin));
        app.MapPost("/users", Add).RequireAuthorization(p => p.RequireRole(admin));
        app.MapPut("/users/{id:guid}", Change).RequireAuthorization(p => p.RequireRole(admin));
    }

    private static IQueryable<PersonResponse> Shown(IQueryable<User> users) =>
        users.AsNoTracking()
            .OrderBy(u => u.Surname).ThenBy(u => u.FirstName)
            .Select(u => new PersonResponse(
                u.Id, u.FirstName, u.Surname, u.WorkEmail, u.Phone, u.EmploymentType, u.SmsOnly, u.IsActive,
                u.Roles.Select(r => new PersonRole(r.Role, r.OrgUnitId, r.OrgUnit == null ? null : r.OrgUnit.Name, r.StudioBound)).ToList()));

    private static Task<PersonResponse> OneAsync(Guid id, PbrmsDbContext db) => Shown(db.Users.Where(u => u.Id == id)).FirstAsync();

    private static async Task<List<PersonResponse>> List(PbrmsDbContext db) => await Shown(db.Users).ToListAsync();

    private static async Task<IResult> Add(PersonForm form, PbrmsDbContext db)
    {
        if (await ProblemWithAsync(form, db) is { } problem)
            return problem;

        var user = new User();
        Fill(user, form);

        if (await db.Users.AnyAsync(u => u.WorkEmail == user.WorkEmail))
            return EmailTaken;

        db.Users.Add(user);
        if (!await SavedAsync(db))
            return EmailTaken;

        return Results.Created($"/users/{user.Id}", await OneAsync(user.Id, db));
    }

    private static async Task<IResult> Change(Guid id, PersonForm form, PbrmsDbContext db, Notifier notifier)
    {
        var user = await db.Users.Include(u => u.Roles).FirstOrDefaultAsync(u => u.Id == id);
        if (user is null)
            return Results.NotFound();

        if (await ProblemWithAsync(form, db) is { } problem)
            return problem;

        var email = User.NormaliseEmail(form.WorkEmail);
        if (await db.Users.AnyAsync(u => u.WorkEmail == email && u.Id != id))
            return EmailTaken;

        if (await WouldLeaveNoAdminAsync(user, form, db))
            return Problems.Conflict("NBC would have no Admin left. Give someone else the Admin role first.");

        // Only someone who could work until now can drop out of anything.
        var dropped = user.IsActive ? await WorkDroppedAsync(user, form, db) : [];
        var name = $"{user.FirstName} {user.Surname}";

        if (dropped.Count > 0 && !form.AcknowledgeWarnings)
        {
            var warnings = dropped.Select(w => new Clash(ClashKind.Operator, name, w.ProductionNo, w.Title, w.StartsAt, w.EndsAt, w.AssignmentId)).ToList();
            return Problems.Warnings($"{name} is still on these productions. Their supervisors will be told to replace them.", warnings);
        }

        await TellAboutDroppedWorkAsync(notifier, name, dropped);
        Fill(user, form);

        if (!await SavedAsync(db))
            return EmailTaken;

        return Results.Ok(await OneAsync(id, db));
    }

    private static IResult EmailTaken => Problems.Conflict("Someone already has that email. Find them in the list, including people who have left.");

    /// <summary>Saves, or returns false if the database refused: two people given one email at the same moment.</summary>
    private static async Task<bool> SavedAsync(PbrmsDbContext db)
    {
        try
        {
            await db.SaveChangesAsync();
            return true;
        }
        catch (DbUpdateException)
        {
            return false;
        }
    }

    /// <summary>What's wrong with the form, in words, or null if nothing is.</summary>
    private static async Task<IResult?> ProblemWithAsync(PersonForm form, PbrmsDbContext db)
    {
        if (string.IsNullOrWhiteSpace(form.FirstName) || form.FirstName.Trim().Length > 80)
            return Problems.Invalid("firstName", "Give a first name of up to 80 characters.");
        if (string.IsNullOrWhiteSpace(form.Surname) || form.Surname.Trim().Length > 80)
            return Problems.Invalid("surname", "Give a surname of up to 80 characters.");

        var email = User.NormaliseEmail(form.WorkEmail ?? "");
        if (email.Length > 160 || !MailAddress.TryCreate(email, out var parsed) || parsed.Address != email)
            return Problems.Invalid("workEmail", "Give their NBC email: it's how they sign in.");

        if (form.Phone?.Trim().Length > 20)
            return Problems.Invalid("phone", "Keep the phone number to 20 characters.");
        if (form.SmsOnly && string.IsNullOrWhiteSpace(form.Phone))
            return Problems.Invalid("phone", "Someone on SMS needs a phone number.");

        if (form.IsActive && (form.Roles ?? []).Count == 0)
            return Problems.Invalid("roles", "Give them at least one role, or they'll see nothing.");

        return await ProblemWithRolesAsync(form.Roles ?? [], db);
    }

    private static async Task<IResult?> ProblemWithRolesAsync(List<RoleForm> roles, PbrmsDbContext db)
    {
        var units = await db.OrgUnits.AsNoTracking().ToDictionaryAsync(u => u.Id, u => u.Kind);

        foreach (var role in roles)
        {
            OrgUnitKind? kind = role.OrgUnitId is { } unit && units.TryGetValue(unit, out var found) ? found : null;
            if ((role.OrgUnitId is not null && kind is null) || !RoleRules.Fits(role.Role, kind))
                return Problems.Invalid("roles", $"{role.Role} is in the wrong place. {WhereItGoes(role.Role)}");
            if (role.StudioBound && role.Role != Role.Operator)
                return Problems.Invalid("roles", "Only operators can be kept off trips.");
        }

        return null;
    }

    private static string WhereItGoes(Role role) => RoleRules.ScopeOf(role) switch
    {
        RoleScope.Division => "It belongs to a TV Ops division.",
        RoleScope.Department => "It belongs to a department.",
        RoleScope.DepartmentOrWholeOfNbc => "It belongs to a department, or to all of NBC.",
        _ => "It covers all of NBC, so it has no unit."
    };

    private static void Fill(User user, PersonForm form)
    {
        user.FirstName = form.FirstName.Trim();
        user.Surname = form.Surname.Trim();
        user.WorkEmail = User.NormaliseEmail(form.WorkEmail);
        user.Phone = string.IsNullOrWhiteSpace(form.Phone) ? null : form.Phone.Trim();
        user.EmploymentType = form.EmploymentType;
        user.SmsOnly = form.SmsOnly;
        user.IsActive = form.IsActive;

        user.Roles.Clear();
        user.Roles.AddRange((form.Roles ?? [])
            .DistinctBy(r => (r.Role, r.OrgUnitId))
            .Select(r => new RoleAssignment { Role = r.Role, OrgUnitId = r.OrgUnitId, StudioBound = r.StudioBound }));
    }

    /// <summary>Nobody could then manage people or roles, and only a database change could put that right.</summary>
    private static async Task<bool> WouldLeaveNoAdminAsync(User user, PersonForm form, PbrmsDbContext db)
    {
        var isTheirsNow = user.IsActive && user.Roles.Any(r => r.Role == Role.Admin);
        var staysAdmin = form.IsActive && (form.Roles ?? []).Any(r => r.Role == Role.Admin);

        return isTheirsNow && !staysAdmin
               && !await db.RoleAssignments.AnyAsync(r => r.Role == Role.Admin && r.User.IsActive && r.UserId != user.Id);
    }

    private sealed record DroppedWork(Guid AssignmentId, Guid BookingId, Guid DivisionId, int ProductionNo, string Title, Venue Venue, DateTimeOffset StartsAt, DateTimeOffset EndsAt);

    /// <summary>
    /// Crew positions still to come in divisions this person would no longer be an operator
    /// in, because they've left or moved. They stay on the crew until someone replaces them:
    /// an empty position on a confirmed production would be worse than a name to follow up.
    /// </summary>
    private static async Task<List<DroppedWork>> WorkDroppedAsync(User user, PersonForm form, PbrmsDbContext db)
    {
        var keptDivisions = form.IsActive
            ? (form.Roles ?? []).Where(r => r is { Role: Role.Operator, OrgUnitId: not null }).Select(r => r.OrgUnitId!.Value).ToList()
            : [];
        var now = DateTimeOffset.UtcNow;

        return await db.CrewAssignments.AsNoTracking()
            .Where(a => a.OperatorId == user.Id && a.RemovedAt == null && a.CrewSlot.EndsAt > now
                        && a.CrewSlot.Booking.Status != BookingStatus.Cancelled && a.CrewSlot.Booking.Status != BookingStatus.Completed
                        && !keptDivisions.Contains(a.CrewSlot.DivisionId))
            .OrderBy(a => a.CrewSlot.StartsAt)
            .Select(a => new DroppedWork(
                a.Id, a.CrewSlot.BookingId, a.CrewSlot.DivisionId, a.CrewSlot.Booking.ProductionNo, a.CrewSlot.Booking.Title,
                a.CrewSlot.Booking.Venue, a.CrewSlot.StartsAt, a.CrewSlot.EndsAt))
            .ToListAsync();
    }

    /// <summary>The division's supervisors must replace them, and the Booking Officer too on a studio production.</summary>
    private static async Task TellAboutDroppedWorkAsync(Notifier notifier, string name, List<DroppedWork> dropped)
    {
        foreach (var work in dropped)
        {
            var supervisors = await notifier.PeopleWithRoleAsync(Role.Supervisor, work.DivisionId);
            var bookingOfficers = BookingWorkflow.CrewLead(work.Venue) == Role.BookingOfficer
                ? await notifier.PeopleWithRoleAsync(Role.BookingOfficer)
                : [];

            notifier.Send(
                supervisors.Concat(bookingOfficers),
                $"{name} can no longer work on #{work.ProductionNo} {work.Title} ({NbcTime.Text(work.StartsAt)}). Please name who replaces them.",
                work.BookingId);
        }
    }
}
