using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using PBRMS.Data;
using PBRMS.Domain;

namespace PBRMS.Api.Auth;

/// <summary>
/// The signed-in person and their role assignments, with the unit each role is for.
/// Role names alone (the token's claims) say "is a Supervisor", but permission
/// checks on a booking also need "Supervisor of which division", so endpoints load this.
/// </summary>
public record Caller(Guid UserId, List<RoleAssignment> Roles)
{
    public static async Task<Caller> LoadAsync(ClaimsPrincipal principal, PbrmsDbContext db)
    {
        var userId = PbrmsClaims.UserIdOf(principal);
        var roles = await db.RoleAssignments.AsNoTracking().Where(r => r.UserId == userId).ToListAsync();
        return new Caller(userId, roles);
    }

    public bool Holds(params Role[] roles) => Roles.Any(r => roles.Contains(r.Role));
}
