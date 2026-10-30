using System.Security.Claims;
using PBRMS.Domain;

namespace PBRMS.Api.Auth;

/// <summary>
/// Names of the facts ("claims") the API attaches to a signed-in user, and helpers
/// to read them back in endpoints.
/// </summary>
public static class PbrmsClaims
{
    /// <summary>The signed-in person's PBRMS user id. Present only if they are a known, active user.</summary>
    public const string UserId = "pbrms_user_id";

    /// <summary>One claim per role the user holds, e.g. "Supervisor".</summary>
    public const string Role = "role";

    /// <summary>The email claim in Development tokens, and in Microsoft tokens when NBC IT enables it.</summary>
    public const string Email = "email";

    /// <summary>
    /// The signed-in person's email, normalised. Microsoft tokens may carry it under
    /// "email", "preferred_username" (usually the NBC email) or "upn", so try each.
    /// </summary>
    public static string? EmailOf(ClaimsPrincipal user)
    {
        var email = user.FindFirst(Email)?.Value
                    ?? user.FindFirst("preferred_username")?.Value
                    ?? user.FindFirst("upn")?.Value;
        return email is null ? null : User.NormaliseEmail(email);
    }

    /// <summary>The PBRMS user id. Only call from endpoints that require sign-in.</summary>
    public static Guid UserIdOf(ClaimsPrincipal user) => Guid.Parse(user.FindFirst(UserId)!.Value);
}
