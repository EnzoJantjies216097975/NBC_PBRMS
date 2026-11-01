using Microsoft.EntityFrameworkCore;
using PBRMS.Data;
using PBRMS.Domain;

namespace PBRMS.Api.Endpoints;

/// <summary>
/// NBC's departments and TV Ops divisions. GET /org-units fills the pickers on the booking
/// form and the other screens; Admin adds, renames and removes units.
/// </summary>
public static class OrgUnitEndpoints
{
    public record OrgUnitResponse(Guid Id, string Name, OrgUnitKind Kind, Guid? ParentId);

    /// <param name="ParentId">The content department a sub-department sits inside. Empty for anything else.</param>
    public record OrgUnitForm(string Name, OrgUnitKind Kind, Guid? ParentId);

    public static void MapOrgUnitEndpoints(this WebApplication app)
    {
        var admin = nameof(Role.Admin);

        app.MapGet("/org-units", async (PbrmsDbContext db) =>
            await db.OrgUnits.AsNoTracking()
                .OrderBy(u => u.Name)
                .Select(u => new OrgUnitResponse(u.Id, u.Name, u.Kind, u.ParentId))
                .ToListAsync())
            .RequireAuthorization();

        app.MapPost("/org-units", Add).RequireAuthorization(p => p.RequireRole(admin));
        app.MapPut("/org-units/{id:guid}", Change).RequireAuthorization(p => p.RequireRole(admin));
        app.MapDelete("/org-units/{id:guid}", Remove).RequireAuthorization(p => p.RequireRole(admin));
    }

    private static async Task<IResult> Add(OrgUnitForm form, PbrmsDbContext db)
    {
        if (await ProblemWithAsync(form, null, db) is { } problem)
            return problem;

        var unit = new OrgUnit { Name = form.Name.Trim(), Kind = form.Kind, ParentId = form.ParentId };
        db.OrgUnits.Add(unit);
        await db.SaveChangesAsync();

        return Results.Created($"/org-units/{unit.Id}", new OrgUnitResponse(unit.Id, unit.Name, unit.Kind, unit.ParentId));
    }

    /// <summary>Renames a unit or moves it to another department. Its kind stays: a division's rosters and crew depend on it.</summary>
    private static async Task<IResult> Change(Guid id, OrgUnitForm form, PbrmsDbContext db)
    {
        var unit = await db.OrgUnits.FirstOrDefaultAsync(u => u.Id == id);
        if (unit is null)
            return Results.NotFound();

        if (form.Kind != unit.Kind)
            return Problems.Invalid("kind", "A unit can't change kind. Add a new one instead.");
        if (await ProblemWithAsync(form, id, db) is { } problem)
            return problem;

        unit.Name = form.Name.Trim();
        unit.ParentId = form.ParentId;
        await db.SaveChangesAsync();

        return Results.Ok(new OrgUnitResponse(unit.Id, unit.Name, unit.Kind, unit.ParentId));
    }

    /// <summary>For a unit added by mistake. One that has ever been used stays, because the history points at it.</summary>
    private static async Task<IResult> Remove(Guid id, PbrmsDbContext db)
    {
        var unit = await db.OrgUnits.FirstOrDefaultAsync(u => u.Id == id);
        if (unit is null)
            return Results.NotFound();

        var inUse = await db.RoleAssignments.AnyAsync(r => r.OrgUnitId == id)
                    || await db.OrgUnits.AnyAsync(u => u.ParentId == id)
                    || await db.Bookings.AnyAsync(b => b.DepartmentId == id)
                    || await db.CrewSlots.AnyAsync(s => s.DivisionId == id)
                    || await db.MonthlyRosters.AnyAsync(r => r.DivisionId == id)
                    || await db.ShiftDefinitions.AnyAsync(s => s.DivisionId == id);
        if (inUse)
            return Problems.Conflict($"{unit.Name} is in use: it has people, bookings, rosters, shift codes or units inside it, so it has to stay.");

        db.OrgUnits.Remove(unit);
        await db.SaveChangesAsync();
        return Results.NoContent();
    }

    /// <summary>What's wrong with the form, in words, or null if nothing is.</summary>
    /// <param name="unitId">The unit being changed, or null for a new one.</param>
    private static async Task<IResult?> ProblemWithAsync(OrgUnitForm form, Guid? unitId, PbrmsDbContext db)
    {
        if (string.IsNullOrWhiteSpace(form.Name) || form.Name.Trim().Length > 80)
            return Problems.Invalid("name", "Give it a name of up to 80 characters.");

        // Screens tell units apart by name, so two can't share one. SQL Server's default
        // comparison ignores case, so "NEWS" is found as "News".
        var name = form.Name.Trim();
        if (await db.OrgUnits.AnyAsync(u => u.Name == name && u.Id != unitId))
            return Problems.Conflict($"There is already a unit called {name}.");

        if (form.Kind != OrgUnitKind.SubDepartment)
            return form.ParentId is null ? null : Problems.Invalid("parentId", "Only a part of a department sits inside another unit.");

        // Only a content department can hold one, so units can never end up inside each other in a circle.
        var parentFits = await db.OrgUnits.AnyAsync(u => u.Id == form.ParentId && u.Kind == OrgUnitKind.ContentDepartment);
        return parentFits ? null : Problems.Invalid("parentId", "Choose the department it's part of.");
    }
}
