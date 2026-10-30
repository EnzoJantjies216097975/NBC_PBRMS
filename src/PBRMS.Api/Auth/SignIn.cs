using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;

namespace PBRMS.Api.Auth;

/// <summary>
/// Sets up how the API knows who is calling.
///
/// Every request carries a token ("Authorization: Bearer ..."). Where the token
/// comes from depends on "Auth:Mode" in appsettings:
///
///   Microsoft   — People sign in with their NBC email through Microsoft (Entra ID).
///                 The app gets a token from Microsoft and the API checks it.
///                 PBRMS never sees a password. This is the mode for NBC's servers.
///
///   Development — The API makes its own tokens (POST /auth/dev-login), for building
///                 and testing without access to NBC's Microsoft accounts. Refused
///                 unless the API runs in the Development environment.
///
/// Either way the rest of the API sees the same thing: a signed-in user matched to
/// a PBRMS user by email, with that user's roles attached (see <see cref="PbrmsUserClaims"/>).
/// </summary>
public static class SignIn
{
    public static bool IsDevelopmentMode(IConfiguration config) => config["Auth:Mode"] == "Development";

    public static void AddPbrmsSignIn(this WebApplicationBuilder builder)
    {
        var config = builder.Configuration;
        var auth = builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme);

        if (IsDevelopmentMode(config))
        {
            // Guard: a server mistakenly set to Development mode would let anyone sign in
            // as anyone with no password. Stop at start-up instead.
            if (!builder.Environment.IsDevelopment())
                throw new InvalidOperationException("Auth:Mode 'Development' is only allowed in the Development environment.");

            var devTokens = new DevTokens();
            builder.Services.AddSingleton(devTokens);
            auth.AddJwtBearer(options =>
            {
                options.MapInboundClaims = false; // keep claim names as written in the token ("email")
                options.TokenValidationParameters = devTokens.ValidationParameters;
            });
        }
        else if (config["Auth:Mode"] == "Microsoft")
        {
            // Both values come from the API's app registration in NBC's Microsoft tenant,
            // which NBC IT has to create (see README.md).
            var tenantId = Required(config, "Auth:Microsoft:TenantId");
            var clientId = Required(config, "Auth:Microsoft:ClientId");

            auth.AddJwtBearer(options =>
            {
                options.MapInboundClaims = false;
                // Microsoft publishes the keys its tokens are signed with here; the API fetches them itself.
                options.Authority = $"https://login.microsoftonline.com/{tenantId}/v2.0";
                // Microsoft writes the audience and issuer two ways, depending on the token
                // version set in the app registration. Accept both forms, for NBC's tenant only.
                options.TokenValidationParameters.ValidAudiences = [clientId, $"api://{clientId}"];
                options.TokenValidationParameters.ValidIssuers =
                [
                    $"https://login.microsoftonline.com/{tenantId}/v2.0",
                    $"https://sts.windows.net/{tenantId}/"
                ];
            });
        }
        else
        {
            throw new InvalidOperationException("Auth:Mode must be 'Microsoft' or 'Development'.");
        }

        // After a token is accepted, attach the PBRMS user and their roles.
        builder.Services.AddScoped<IClaimsTransformation, PbrmsUserClaims>();

        // By default an endpoint needs a known PBRMS user, not just any valid NBC account.
        // Endpoints that need a particular role add it on top, e.g. .RequireAuthorization(p => p.RequireRole("Supervisor")).
        builder.Services.AddAuthorizationBuilder()
            .SetDefaultPolicy(new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .RequireClaim(PbrmsClaims.UserId)
                .Build());
    }

    private static string Required(IConfiguration config, string key) =>
        config[key] is { Length: > 0 } value
            ? value
            : throw new InvalidOperationException($"'{key}' must be set when Auth:Mode is 'Microsoft'.");
}
