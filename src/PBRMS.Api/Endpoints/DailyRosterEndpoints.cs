using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using PBRMS.Api.Auth;
using PBRMS.Data;
using PBRMS.Domain;

namespace PBRMS.Api.Endpoints;

/// <summary>
/// The daily roster (Backend Schema Document, /daily-roster/{date}, where date looks like
/// 2026-11-20). Everyone can read it; only the Booking Officer can change or print it (BR-05).
/// See <see cref="DailyRoster"/> for how the sheet is built.
/// </summary>
public static class DailyRosterEndpoints
{
    public record AnnouncementRequest(string? Text);

    public record DailyCrewForm(string Division, string Name);

    /// <summary>A row as the Booking Officer wants it.</summary>
    /// <param name="BookingId">The production this row replaces. Leave empty to add a row of your own.</param>
    /// <param name="Id">A saved row being changed again. Leave empty for a new one.</param>
    /// <param name="Hidden">True to leave this production off the sheet.</param>
    public record DailyLineForm(
        Guid? Id,
        Guid? BookingId,
        bool Hidden,
        string Area,
        DateTimeOffset? StartsAt,
        DateTimeOffset? EndsAt,
        string Title,
        string? Detail,
        List<DailyCrewForm>? Crew);

    public static void MapDailyRosterEndpoints(this WebApplication app)
    {
        var bookingOfficer = nameof(Role.BookingOfficer);

        app.MapGet("/daily-roster/{date}", GetRoster).RequireAuthorization();
        app.MapGet("/daily-roster/{date}/pdf", GetPdf).RequireAuthorization(p => p.RequireRole(bookingOfficer));
        app.MapPut("/daily-roster/{date}/announcement", SetAnnouncement).RequireAuthorization(p => p.RequireRole(bookingOfficer));
        app.MapPut("/daily-roster/{date}/lines", SaveLine).RequireAuthorization(p => p.RequireRole(bookingOfficer));
        app.MapDelete("/daily-roster/{date}/lines/{lineId:guid}", DiscardLine).RequireAuthorization(p => p.RequireRole(bookingOfficer));
    }

    private static async Task<DailyRosterView> GetRoster(DateOnly date, DailyRosterComposer composer) =>
        await composer.ComposeAsync(date);

    private static async Task<IResult> GetPdf(DateOnly date, DailyRosterComposer composer)
    {
        var roster = await composer.ComposeAsync(date);
        return Results.File(DailyRosterPdf.Render(roster), "application/pdf", DailyRosterPdf.FileName(date));
    }

    private static async Task<IResult> SetAnnouncement(DateOnly date, AnnouncementRequest request, ClaimsPrincipal principal, PbrmsDbContext db, DailyRosterComposer composer)
    {
        if (request.Text?.Trim().Length > 1000)
            return Problems.Invalid("text", "Keep the announcement to 1000 characters.");

        var roster = await FindOrStartAsync(date, principal, db);
        roster.Announcement = string.IsNullOrWhiteSpace(request.Text) ? null : request.Text.Trim();

        return await SaveAndShowAsync(date, db, composer);
    }

    /// <summary>Saves a row. Saving the same production or row again replaces what was saved.</summary>
    private static async Task<IResult> SaveLine(DateOnly date, DailyLineForm form, ClaimsPrincipal principal, PbrmsDbContext db, DailyRosterComposer composer)
    {
        if (string.IsNullOrWhiteSpace(form.Area) || form.Area.Trim().Length > 40)
            return Problems.Invalid("area", "Name the area in up to 40 characters, e.g. \"Studio 1\".");
        if (string.IsNullOrWhiteSpace(form.Title) || form.Title.Trim().Length > 200)
            return Problems.Invalid("title", "Give the row a title of up to 200 characters.");
        if (form.Detail?.Trim().Length > 300)
            return Problems.Invalid("detail", "Keep the detail to 300 characters.");
        if (form.StartsAt >= form.EndsAt)
            return Problems.Invalid("endsAt", "The end must be after the start.");
        if ((form.Crew ?? []).Any(c => string.IsNullOrWhiteSpace(c.Name) || c.Name.Trim().Length > 170 || string.IsNullOrWhiteSpace(c.Division) || c.Division.Trim().Length > 80))
            return Problems.Invalid("crew", "Each crew member needs a name and a trade.");
        if (form.BookingId is Guid bookingId && !await db.Bookings.AnyAsync(b => b.Id == bookingId))
            return Problems.Invalid("bookingId", "That booking doesn't exist.");

        var roster = await FindOrStartAsync(date, principal, db);

        var line = roster.Lines.FirstOrDefault(l => (form.Id is not null && l.Id == form.Id)
                                                    || (form.BookingId is not null && l.BookingId == form.BookingId));
        if (line is null && form.Id is not null)
            return Results.NotFound();

        if (line is null)
        {
            line = new DailyRosterLine();
            roster.Lines.Add(line);
        }

        line.BookingId = form.BookingId;
        line.Hidden = form.Hidden;
        line.Area = form.Area.Trim();
        line.StartsAt = form.StartsAt;
        line.EndsAt = form.EndsAt;
        line.Title = form.Title.Trim();
        line.Detail = form.Detail?.Trim();
        line.Crew.Clear();
        line.Crew.AddRange((form.Crew ?? []).Select(c => new DailyRosterCrew { Division = c.Division.Trim(), Name = c.Name.Trim() }));

        return await SaveAndShowAsync(date, db, composer);
    }

    /// <summary>Discards a saved row: an edited production goes back to what was generated, and an added row goes away.</summary>
    private static async Task<IResult> DiscardLine(DateOnly date, Guid lineId, ClaimsPrincipal principal, PbrmsDbContext db, DailyRosterComposer composer)
    {
        var roster = await FindOrStartAsync(date, principal, db);
        var line = roster.Lines.FirstOrDefault(l => l.Id == lineId);
        if (line is null)
            return Results.NotFound();

        roster.Lines.Remove(line);
        return await SaveAndShowAsync(date, db, composer);
    }

    /// <summary>The day's saved changes, or a new empty record of them, marked as changed by this person.</summary>
    private static async Task<DailyRoster> FindOrStartAsync(DateOnly date, ClaimsPrincipal principal, PbrmsDbContext db)
    {
        var roster = await db.DailyRosters
            .Include(r => r.Lines).ThenInclude(l => l.Crew)
            .FirstOrDefaultAsync(r => r.Date == date);

        if (roster is null)
        {
            roster = new DailyRoster { Date = date };
            db.DailyRosters.Add(roster);
        }

        roster.UpdatedAt = DateTimeOffset.UtcNow;
        roster.UpdatedById = PbrmsClaims.UserIdOf(principal);
        return roster;
    }

    private static async Task<IResult> SaveAndShowAsync(DateOnly date, PbrmsDbContext db, DailyRosterComposer composer)
    {
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException) // also covers DbUpdateConcurrencyException
        {
            return Problems.Conflict("Someone else saved this roster at the same moment. Reload it and try again.");
        }

        return Results.Ok(await composer.ComposeAsync(date));
    }
}
