using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using PBRMS.Api.Auth;
using PBRMS.Data;
using PBRMS.Domain;

namespace PBRMS.Api.Endpoints;

/// <summary>
/// A division's legend of shift codes. Everyone can read it; the division's supervisor
/// or an Admin keeps it up to date (PRD assumptions: supervisors keep their shift codes current).
/// Codes are never deleted, because past rosters use them; retire one with isActive = false.
/// </summary>
public static class ShiftDefinitionEndpoints
{
    public record ShiftResponse(
        Guid Id, string Code, string Label, ShiftKind Kind,
        TimeOnly? Start, TimeOnly? End, TimeOnly? SecondStart, TimeOnly? SecondEnd,
        string? Colour, bool IsActive, Venue? Studio);

    /// <summary>A code as filled in on the legend. The second pair of hours is for two-shift days.</summary>
    public record ShiftForm(
        string Code, string Label, ShiftKind Kind,
        TimeOnly? Start, TimeOnly? End, TimeOnly? SecondStart, TimeOnly? SecondEnd,
        string? Colour, bool IsActive = true, Venue? Studio = null);

    public static void MapShiftDefinitionEndpoints(this WebApplication app)
    {
        app.MapGet("/shift-definitions/{divisionId:guid}", async (Guid divisionId, PbrmsDbContext db) =>
            await db.ShiftDefinitions.AsNoTracking()
                .Where(s => s.DivisionId == divisionId)
                .OrderBy(s => s.Code)
                .Select(s => new ShiftResponse(s.Id, s.Code, s.Label, s.Kind, s.Start, s.End, s.SecondStart, s.SecondEnd, s.Colour, s.IsActive, s.Studio))
                .ToListAsync())
            .RequireAuthorization();

        app.MapPost("/shift-definitions/{divisionId:guid}", AddCode).RequireAuthorization();
        app.MapPut("/shift-definitions/{divisionId:guid}/{id:guid}", ChangeCode).RequireAuthorization();
    }

    private static async Task<IResult> AddCode(Guid divisionId, ShiftForm form, ClaimsPrincipal principal, PbrmsDbContext db)
    {
        if (await MayEditLegendAsync(principal, db, divisionId) is false)
            return Problems.Forbidden("Only this division's supervisor or an Admin can change its legend.");
        if (Validate(form) is { } problem)
            return problem;
        if (!await db.OrgUnits.AnyAsync(u => u.Id == divisionId && u.Kind == OrgUnitKind.TvOpsDivision))
            return Results.NotFound();

        var code = form.Code.Trim();
        if (await db.ShiftDefinitions.AnyAsync(s => s.DivisionId == divisionId && s.Code == code))
            return Problems.Conflict($"This division already has a code {code}.");

        var shift = new ShiftDefinition { DivisionId = divisionId };
        Fill(shift, form);
        db.ShiftDefinitions.Add(shift);
        await db.SaveChangesAsync();

        return Results.Created($"/shift-definitions/{divisionId}", Respond(shift));
    }

    private static async Task<IResult> ChangeCode(Guid divisionId, Guid id, ShiftForm form, ClaimsPrincipal principal, PbrmsDbContext db)
    {
        if (await MayEditLegendAsync(principal, db, divisionId) is false)
            return Problems.Forbidden("Only this division's supervisor or an Admin can change its legend.");
        if (Validate(form) is { } problem)
            return problem;

        var shift = await db.ShiftDefinitions.FirstOrDefaultAsync(s => s.Id == id && s.DivisionId == divisionId);
        if (shift is null)
            return Results.NotFound();

        // Rosters store the code as text, so renaming it would orphan every cell using it.
        if (form.Code.Trim() != shift.Code)
            return Problems.Invalid("code", "A code can't be renamed. Retire it and add a new one.");

        Fill(shift, form);
        await db.SaveChangesAsync();
        return Results.Ok(Respond(shift));
    }

    private static async Task<bool> MayEditLegendAsync(ClaimsPrincipal principal, PbrmsDbContext db, Guid divisionId)
    {
        var caller = await Caller.LoadAsync(principal, db);
        var division = new HashSet<Guid> { divisionId };
        return BookingWorkflow.HoldsRoleIn(caller.Roles, Role.Supervisor, division)
               || BookingWorkflow.HoldsRoleIn(caller.Roles, Role.Admin, division);
    }

    /// <summary>Checks the form. Returns the problem, or null if it is fine.</summary>
    private static IResult? Validate(ShiftForm form)
    {
        var code = form.Code?.Trim() ?? "";
        if (code.Length is 0 or > 12)
            return Problems.Invalid("code", "Give the code 1 to 12 characters.");
        if (code.Contains('+'))
            return Problems.Invalid("code", "A code can't contain \"+\": that joins two codes in a roster cell.");
        if (string.IsNullOrWhiteSpace(form.Label) || form.Label.Trim().Length > 80)
            return Problems.Invalid("label", "Give the code a label of up to 80 characters.");
        if (form.Colour?.Length > 9)
            return Problems.Invalid("colour", "Use a colour like #5FB87A.");

        // Hours come in pairs: a start without an end would be read as "no fixed hours".
        if (form.Start.HasValue != form.End.HasValue || form.SecondStart.HasValue != form.SecondEnd.HasValue)
            return Problems.Invalid("end", "Give both a start and an end, or neither.");
        if (form.SecondStart.HasValue && !form.Start.HasValue)
            return Problems.Invalid("start", "Fill in the first shift before the second.");

        return null;
    }

    private static void Fill(ShiftDefinition shift, ShiftForm form)
    {
        shift.Code = form.Code.Trim();
        shift.Label = form.Label.Trim();
        shift.Kind = form.Kind;
        shift.Start = form.Start;
        shift.End = form.End;
        shift.SecondStart = form.SecondStart;
        shift.SecondEnd = form.SecondEnd;
        shift.Colour = form.Colour?.Trim();
        shift.IsActive = form.IsActive;
        shift.Studio = form.Studio;
    }

    private static ShiftResponse Respond(ShiftDefinition s) =>
        new(s.Id, s.Code, s.Label, s.Kind, s.Start, s.End, s.SecondStart, s.SecondEnd, s.Colour, s.IsActive, s.Studio);
}
