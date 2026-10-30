using System.Globalization;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using PBRMS.Api.Auth;
using PBRMS.Data;
using PBRMS.Domain;

namespace PBRMS.Api.Endpoints;

/// <summary>
/// The monthly roster grid (Backend Schema Document, /rosters/{division}/{month}, where
/// month looks like "2026-11"). Supervisors build and publish their own division's
/// roster; the Booking Officer can read them all. See <see cref="MonthlyRoster"/> for
/// how versions work.
/// </summary>
public static class RosterEndpoints
{
    /// <summary>One cell. An empty Code blanks the cell.</summary>
    public record RosterCell(Guid OperatorId, DateOnly Date, string? Code);

    public record RosterRequest(List<RosterCell> Entries);

    /// <summary>A roster version. Version 0 with no status means nothing has been started for that month.</summary>
    public record RosterResponse(Guid DivisionId, string Month, int Version, RosterStatus? Status, List<RosterCell> Entries);

    /// <summary>A published roster, and anyone it now clashes with. Clashes are warnings: the supervisor has already decided (E2-01).</summary>
    public record PublishResponse(RosterResponse Roster, List<Clash> Clashes);

    public static void MapRosterEndpoints(this WebApplication app)
    {
        string[] readers = [nameof(Role.Supervisor), nameof(Role.BookingOfficer)];

        app.MapGet("/rosters/{divisionId:guid}/{month}", GetRoster).RequireAuthorization(p => p.RequireRole(readers));
        app.MapGet("/rosters/{divisionId:guid}/{month}/clashes", GetClashes).RequireAuthorization(p => p.RequireRole(readers));
        app.MapPut("/rosters/{divisionId:guid}/{month}", EditRoster).RequireAuthorization(p => p.RequireRole(nameof(Role.Supervisor)));
        app.MapPost("/rosters/{divisionId:guid}/{month}/publish", Publish).RequireAuthorization(p => p.RequireRole(nameof(Role.Supervisor)));
    }

    /// <summary>The newest version: the draft being worked on, or else what's published.</summary>
    private static async Task<IResult> GetRoster(Guid divisionId, string month, PbrmsDbContext db)
    {
        if (ParseMonth(month) is not DateOnly first)
            return BadMonth();

        var roster = await LatestAsync(db, divisionId, first);
        return Results.Ok(Respond(divisionId, first, roster));
    }

    /// <summary>A preview of what publishing would warn about.</summary>
    private static async Task<IResult> GetClashes(Guid divisionId, string month, PbrmsDbContext db, ClashChecker clashChecker)
    {
        if (ParseMonth(month) is not DateOnly first)
            return BadMonth();

        var roster = await LatestAsync(db, divisionId, first);
        return Results.Ok(roster is null ? [] : await clashChecker.ForRosterAsync(roster));
    }

    /// <summary>Saves changed cells. Cells not listed stay as they are, so the grid only sends what changed.</summary>
    private static async Task<IResult> EditRoster(Guid divisionId, string month, RosterRequest request, ClaimsPrincipal principal, PbrmsDbContext db)
    {
        if (ParseMonth(month) is not DateOnly first)
            return BadMonth();
        if (!await IsSupervisorOfAsync(principal, db, divisionId))
            return Problems.Forbidden("You can only edit your own division's roster.");

        var cells = request.Entries ?? [];

        // 1. Check every cell before changing anything.
        var legend = await db.ShiftDefinitions.AsNoTracking()
            .Where(s => s.DivisionId == divisionId && s.IsActive)
            .Select(s => s.Code)
            .ToListAsync();
        var operators = await db.RoleAssignments.AsNoTracking()
            .Where(r => r.OrgUnitId == divisionId && r.Role == Role.Operator && r.User.IsActive)
            .Select(r => r.UserId)
            .ToListAsync();

        foreach (var cell in cells)
        {
            if (cell.Date.Year != first.Year || cell.Date.Month != first.Month)
                return Problems.Invalid("date", $"{cell.Date:dd/MM/yyyy} isn't in this month.");
            if (!operators.Contains(cell.OperatorId))
                return Problems.Invalid("operatorId", "That person isn't an active operator in this division.");

            var unknown = RosterRules.SplitCodes(cell.Code ?? "").FirstOrDefault(c => !legend.Contains(c));
            if (unknown is not null)
                return Problems.Invalid("code", $"{unknown} isn't on this division's legend.");
            if (cell.Code?.Trim().Length > 30)
                return Problems.Invalid("code", "Keep a cell to 30 characters.");
        }

        // 2. Find the draft to edit. Editing a published roster starts a new draft copied
        //    from it, and the published version stays in force until this one is published.
        var latest = await LatestAsync(db, divisionId, first, tracked: true);
        var draft = latest;
        if (draft is null || draft.Status == RosterStatus.Published)
        {
            draft = new MonthlyRoster
            {
                DivisionId = divisionId,
                Month = first,
                Version = (latest?.Version ?? 0) + 1,
                Entries = latest?.Entries
                    .Select(e => new RosterEntry { OperatorId = e.OperatorId, Date = e.Date, ShiftCode = e.ShiftCode })
                    .ToList() ?? []
            };
            db.MonthlyRosters.Add(draft);
        }

        // 3. Apply the cells.
        foreach (var cell in cells)
        {
            var code = string.Join('+', RosterRules.SplitCodes(cell.Code ?? ""));
            var entry = draft.Entries.FirstOrDefault(e => e.OperatorId == cell.OperatorId && e.Date == cell.Date);

            if (code.Length == 0 && entry is not null)
                draft.Entries.Remove(entry);
            else if (code.Length > 0 && entry is not null)
                entry.ShiftCode = code;
            else if (code.Length > 0)
                draft.Entries.Add(new RosterEntry { OperatorId = cell.OperatorId, Date = cell.Date, ShiftCode = code });
        }

        // Always changes the row, so the row version catches two people saving at once.
        draft.UpdatedAt = DateTimeOffset.UtcNow;

        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException) // also covers DbUpdateConcurrencyException
        {
            return Problems.Conflict("Someone else saved this roster at the same moment. Reload it and try again.");
        }

        return Results.Ok(Respond(divisionId, first, draft));
    }

    /// <summary>
    /// Puts the draft in force and tells the operators on it (App Flow Document, step 4).
    /// A clash with a confirmed booking doesn't stop it, because someone may really be
    /// on leave. It comes back as a warning so the supervisor can get the crew changed.
    /// </summary>
    private static async Task<IResult> Publish(Guid divisionId, string month, ClaimsPrincipal principal, PbrmsDbContext db, ClashChecker clashChecker, Notifier notifier)
    {
        if (ParseMonth(month) is not DateOnly first)
            return BadMonth();
        if (!await IsSupervisorOfAsync(principal, db, divisionId))
            return Problems.Forbidden("You can only publish your own division's roster.");

        var roster = await LatestAsync(db, divisionId, first, tracked: true);
        if (roster is null || roster.Status != RosterStatus.Draft)
            return Problems.Conflict("There is no draft to publish for this month.");

        var clashes = await clashChecker.ForRosterAsync(roster);

        roster.Status = RosterStatus.Published;
        roster.PublishedAt = DateTimeOffset.UtcNow;
        roster.PublishedById = PbrmsClaims.UserIdOf(principal);
        roster.UpdatedAt = DateTimeOffset.UtcNow;

        var monthName = first.ToString("MMMM yyyy", CultureInfo.InvariantCulture);
        notifier.Send(roster.Entries.Select(e => e.OperatorId), $"The {monthName} roster has been published.");

        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            return Problems.Conflict("Someone else saved this roster at the same moment. Reload it and try again.");
        }

        return Results.Ok(new PublishResponse(Respond(divisionId, first, roster), clashes));
    }

    private static async Task<MonthlyRoster?> LatestAsync(PbrmsDbContext db, Guid divisionId, DateOnly month, bool tracked = false)
    {
        var rosters = db.MonthlyRosters.Include(r => r.Entries).Where(r => r.DivisionId == divisionId && r.Month == month);
        return await (tracked ? rosters : rosters.AsNoTracking()).OrderByDescending(r => r.Version).FirstOrDefaultAsync();
    }

    private static async Task<bool> IsSupervisorOfAsync(ClaimsPrincipal principal, PbrmsDbContext db, Guid divisionId)
    {
        var caller = await Caller.LoadAsync(principal, db);
        return BookingWorkflow.HoldsRoleIn(caller.Roles, Role.Supervisor, new HashSet<Guid> { divisionId });
    }

    /// <summary>"2026-11" as the first day of that month, or null if it isn't a month.</summary>
    private static DateOnly? ParseMonth(string month) =>
        DateOnly.TryParseExact(month, "yyyy-MM", CultureInfo.InvariantCulture, DateTimeStyles.None, out var first) ? first : null;

    private static IResult BadMonth() => Problems.Invalid("month", "Give the month like 2026-11.");

    private static RosterResponse Respond(Guid divisionId, DateOnly month, MonthlyRoster? roster) => new(
        divisionId,
        month.ToString("yyyy-MM", CultureInfo.InvariantCulture),
        roster?.Version ?? 0,
        roster?.Status,
        roster?.Entries
            .OrderBy(e => e.Date)
            .Select(e => new RosterCell(e.OperatorId, e.Date, e.ShiftCode))
            .ToList() ?? []);
}
