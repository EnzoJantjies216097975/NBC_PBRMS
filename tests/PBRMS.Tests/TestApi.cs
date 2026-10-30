using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PBRMS.Data;

namespace PBRMS.Tests;

/// <summary>
/// Runs the real API in memory for tests, with the sample people from DevSeed.
///
/// The database is SQLite held in memory instead of SQL Server, so tests need no
/// database installed and start clean every time. That is fine for sign-in and
/// roles. Tests that depend on SQL Server behaviour (locking, concurrency) will need
/// a real SQL Server test database later.
/// </summary>
public sealed class TestApi : WebApplicationFactory<Program>
{
    // An in-memory SQLite database lives only while its connection is open, so keep one open for the whole test.
    private readonly SqliteConnection connection = new("DataSource=:memory:");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development"); // Development sign-in (/auth/dev-login)
        builder.UseSetting("Database:PrepareOnStartup", "false"); // we create the database below instead

        builder.ConfigureServices(services =>
        {
            // Swap SQL Server for SQLite. Both registrations EF made for SQL Server must go,
            // or EF complains that two database providers are configured.
            services.RemoveAll<DbContextOptions<PbrmsDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<PbrmsDbContext>>();

            connection.Open();
            services.AddDbContext<PbrmsDbContext>(options => options.UseSqlite(connection));

            using var scope = services.BuildServiceProvider().CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<PbrmsDbContext>();
            db.Database.EnsureCreated();
            DevSeed.RunAsync(db).GetAwaiter().GetResult();
        });
    }

    /// <summary>Changes data directly in the test database, e.g. to deactivate someone.</summary>
    public async Task WithDatabaseAsync(Func<PbrmsDbContext, Task> change)
    {
        using var scope = Services.CreateScope();
        await change(scope.ServiceProvider.GetRequiredService<PbrmsDbContext>());
    }

    /// <summary>An HTTP client already signed in as the sample user with this email.</summary>
    public async Task<HttpClient> SignedInClientAsync(string email)
    {
        var client = CreateClient();
        var response = await client.PostAsJsonAsync("/auth/dev-login", new { email });
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<TokenResponse>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", body!.Token);
        return client;
    }

    private record TokenResponse(string Token);

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        connection.Dispose();
    }
}
