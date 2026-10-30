using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using PBRMS.Api.Auth;
using PBRMS.Data;
using PBRMS.Domain;

namespace PBRMS.Api.Endpoints;

/// <summary>
/// GET /me: the signed-in person's profile and roles. The web and mobile apps call
/// it straight after sign-in to decide which screens to show. The server still
/// checks roles on every endpoint; hiding a screen is only for convenience.
/// </summary>
public static class MeEndpoints
{
    public record MeResponse(Guid Id, string FirstName, string Surname, string WorkEmail, List<MeRole> Roles);

    /// <summary>One role and where it applies. Unit is null for NBC-wide roles such as Admin.</summary>
    public record MeRole(Role Role, string? Unit);

    public static void MapMeEndpoints(this WebApplication app)
    {
        app.MapGet("/me", async (ClaimsPrincipal principal, PbrmsDbContext db) =>
        {
            var userId = PbrmsClaims.UserIdOf(principal);

            return await db.Users
                .Where(u => u.Id == userId)
                .Select(u => new MeResponse(
                    u.Id,
                    u.FirstName,
                    u.Surname,
                    u.WorkEmail,
                    u.Roles.Select(r => new MeRole(r.Role, r.OrgUnit == null ? null : r.OrgUnit.Name)).ToList()))
                .SingleAsync();
        }).RequireAuthorization();
    }
}
