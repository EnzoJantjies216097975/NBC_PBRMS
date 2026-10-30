using System.Security.Cryptography;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace PBRMS.Api.Auth;

/// <summary>
/// Makes and checks sign-in tokens in Development mode only (see <see cref="SignIn"/>).
/// It stands in for Microsoft: the token carries an "email" claim, the same as
/// Microsoft's, so everything after sign-in works the same in both modes.
///
/// The signing key is random and made fresh at every start, so no secret is stored
/// anywhere. The cost is that tokens stop working when the API restarts: sign in again.
/// </summary>
public sealed class DevTokens
{
    private const string Issuer = "pbrms-dev";
    private const string Audience = "pbrms-api";

    private readonly SymmetricSecurityKey key = new(RandomNumberGenerator.GetBytes(32));

    public TokenValidationParameters ValidationParameters => new()
    {
        ValidIssuer = Issuer,
        ValidAudience = Audience,
        IssuerSigningKey = key
    };

    /// <summary>A token for this email, valid for one working day.</summary>
    public string IssueFor(string email) => new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
    {
        Issuer = Issuer,
        Audience = Audience,
        Claims = new Dictionary<string, object> { [PbrmsClaims.Email] = email },
        Expires = DateTime.UtcNow.AddHours(8),
        SigningCredentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256)
    });
}
