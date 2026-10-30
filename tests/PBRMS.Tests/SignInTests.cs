using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PBRMS.Api.Auth;
using PBRMS.Data;
using PBRMS.Domain;
using static PBRMS.Api.Endpoints.MeEndpoints;

namespace PBRMS.Tests;

/// <summary>
/// Sign-in and "who am I". The rules being tested:
/// - only active PBRMS users can use the API (a valid NBC account alone is not enough);
/// - roles come from the PBRMS database;
/// - emails match whatever their capitalisation.
/// </summary>
public class SignInTests
{
    [Fact]
    public async Task Health_is_open_to_everyone()
    {
        using var api = new TestApi();

        var response = await api.CreateClient().GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Me_without_signing_in_is_refused()
    {
        using var api = new TestApi();

        var response = await api.CreateClient().GetAsync("/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Me_returns_the_signed_in_users_profile_and_roles()
    {
        using var api = new TestApi();
        var client = await api.SignedInClientAsync("supervisor@pbrms.test");

        var me = await client.GetFromJsonAsync<MeResponse>("/me", JsonOptions);

        Assert.Equal("supervisor@pbrms.test", me!.WorkEmail);
        // The sample supervisor is Supervisor of Camera and also works as a Camera operator.
        Assert.Contains(me.Roles, r => r is { Role: Role.Supervisor, Unit: "Camera" });
        Assert.Contains(me.Roles, r => r is { Role: Role.Operator, Unit: "Camera" });
    }

    [Fact]
    public async Task Email_matches_whatever_its_capitals_and_spaces()
    {
        using var api = new TestApi();

        var response = await api.CreateClient().PostAsJsonAsync("/auth/dev-login", new { email = "  Producer@PBRMS.test " });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Dev_login_refuses_an_email_that_is_not_a_PBRMS_user()
    {
        using var api = new TestApi();

        var response = await api.CreateClient().PostAsJsonAsync("/auth/dev-login", new { email = "stranger@pbrms.test" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Dev_login_refuses_someone_who_has_left()
    {
        using var api = new TestApi();

        var response = await api.CreateClient().PostAsJsonAsync("/auth/dev-login", new { email = DevSeed.FormerStaffEmail });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task A_valid_token_for_someone_not_in_PBRMS_is_forbidden()
    {
        // Stands in for a real NBC Microsoft account that has no PBRMS user:
        // the token itself is fine, but the API must still refuse it.
        using var api = new TestApi();
        var client = api.CreateClient();
        var token = api.Services.GetRequiredService<DevTokens>().IssueFor("someone.else@pbrms.test");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync("/me");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Someone_deactivated_after_signing_in_is_refused_on_their_next_request()
    {
        using var api = new TestApi();
        var client = await api.SignedInClientAsync("operator@pbrms.test");

        await api.WithDatabaseAsync(db =>
            db.Users.Where(u => u.WorkEmail == "operator@pbrms.test")
                .ExecuteUpdateAsync(u => u.SetProperty(x => x.IsActive, false)));
        var response = await client.GetAsync("/me");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // The API sends enums as text ("Supervisor"), so the test must read them the same way.
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };
}
