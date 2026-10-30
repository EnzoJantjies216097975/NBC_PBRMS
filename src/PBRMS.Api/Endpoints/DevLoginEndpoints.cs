using Microsoft.EntityFrameworkCore;
using PBRMS.Api.Auth;
using PBRMS.Data;
using PBRMS.Domain;

namespace PBRMS.Api.Endpoints;

/// <summary>
/// POST /auth/dev-login: sign in as any active user by email, with no password.
/// Development mode only (Program.cs maps it only then). It stands in for the
/// Microsoft sign-in the apps will use on NBC's servers.
/// </summary>
public static class DevLoginEndpoints
{
    public record DevLoginRequest(string Email);

    public static void MapDevLoginEndpoints(this WebApplication app)
    {
        app.MapPost("/auth/dev-login", async (DevLoginRequest request, PbrmsDbContext db, DevTokens tokens) =>
        {
            var email = User.NormaliseEmail(request.Email);
            var isActiveUser = await db.Users.AnyAsync(u => u.WorkEmail == email && u.IsActive);

            return isActiveUser
                ? Results.Ok(new { token = tokens.IssueFor(email) })
                : Results.Problem("No active PBRMS user has that email.", statusCode: StatusCodes.Status401Unauthorized);
        }).AllowAnonymous();
    }
}
