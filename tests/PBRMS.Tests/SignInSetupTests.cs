using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace PBRMS.Tests;

/// <summary>
/// The API must refuse to start with a sign-in setup that would be unsafe or broken
/// on NBC's servers, rather than start and fail later.
/// </summary>
public class SignInSetupTests
{
    [Fact]
    public void Development_sign_in_is_refused_outside_the_Development_environment()
    {
        // Passwordless dev sign-in on a real server would let anyone in as anyone.
        using var api = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseEnvironment("Production");
            b.UseSetting("Auth:Mode", "Development");
        });

        var error = Assert.Throws<InvalidOperationException>(() => api.CreateClient());
        Assert.Contains("only allowed in the Development environment", error.Message);
    }

    [Fact]
    public void Microsoft_sign_in_without_NBC_tenant_details_is_refused()
    {
        // appsettings.json ships with Microsoft mode and empty ids; the server must fill them in.
        using var api = new WebApplicationFactory<Program>().WithWebHostBuilder(b => b.UseEnvironment("Production"));

        var error = Assert.Throws<InvalidOperationException>(() => api.CreateClient());
        Assert.Contains("Auth:Microsoft:TenantId", error.Message);
    }
}
