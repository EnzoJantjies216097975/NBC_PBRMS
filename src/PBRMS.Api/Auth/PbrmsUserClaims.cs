using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using PBRMS.Data;

namespace PBRMS.Api.Auth;

/// <summary>
/// Runs on every signed-in request, after the token has been checked. It finds the
/// active PBRMS user with the same email and attaches their user id and roles.
///
/// Roles come from the PBRMS database, not from Microsoft. NBC IT only vouches for
/// who the person is; what they may do in PBRMS is managed here.
///
/// An NBC account with no matching active user gets nothing attached, so the
/// default policy (see <see cref="SignIn"/>) refuses them with 403 Forbidden.
/// </summary>
public class PbrmsUserClaims(PbrmsDbContext db) : IClaimsTransformation
{
    public async Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
    {
        // ASP.NET can call this more than once per request; only do the work once.
        if (principal.Identity?.IsAuthenticated != true || principal.HasClaim(c => c.Type == PbrmsClaims.UserId))
            return principal;

        var email = PbrmsClaims.EmailOf(principal);
        if (email is null)
            return principal;

        var user = await db.Users
            .AsNoTracking()
            .Include(u => u.Roles)
            .FirstOrDefaultAsync(u => u.WorkEmail == email && u.IsActive);
        if (user is null)
            return principal;

        var pbrms = new ClaimsIdentity("pbrms", nameType: PbrmsClaims.Email, roleType: PbrmsClaims.Role);
        pbrms.AddClaim(new Claim(PbrmsClaims.UserId, user.Id.ToString()));
        foreach (var role in user.Roles.Select(r => r.Role).Distinct())
            pbrms.AddClaim(new Claim(PbrmsClaims.Role, role.ToString()));

        // Work on a copy, as ASP.NET recommends, rather than changing the original.
        var result = principal.Clone();
        result.AddIdentity(pbrms);
        return result;
    }
}
