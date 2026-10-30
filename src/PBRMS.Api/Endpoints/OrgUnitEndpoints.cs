using Microsoft.EntityFrameworkCore;
using PBRMS.Data;
using PBRMS.Domain;

namespace PBRMS.Api.Endpoints;

/// <summary>
/// GET /org-units: NBC's departments and TV Ops divisions. The booking form uses it to
/// fill its department and crew-division pickers.
/// </summary>
public static class OrgUnitEndpoints
{
    public record OrgUnitResponse(Guid Id, string Name, OrgUnitKind Kind, Guid? ParentId);

    public static void MapOrgUnitEndpoints(this WebApplication app)
    {
        app.MapGet("/org-units", async (PbrmsDbContext db) =>
            await db.OrgUnits.AsNoTracking()
                .OrderBy(u => u.Name)
                .Select(u => new OrgUnitResponse(u.Id, u.Name, u.Kind, u.ParentId))
                .ToListAsync())
            .RequireAuthorization();
    }
}
