using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PBRMS.Data;

namespace PBRMS.Tests;

/// <summary>
/// Runs the real API in memory for tests, against a real SQL Server test database.
///
/// There is one test database, PBRMS_Tests, kept between test runs. Creating a SQL
/// Server database can take 40 seconds on a machine with antivirus scanning new files,
/// so it is created only once (by the real migrations, on the first run). At the start
/// of each run it is wiped and the sample people from DevSeed are added again, so every
/// run starts from the same data.
///
/// Every API test class shares this one instance through the "API" collection, so
/// they run one after another, not side by side. Tests should create the data they
/// change (their own bookings, their own extra users) rather than change the sample
/// people, so a test never depends on what an earlier test did.
/// </summary>
public sealed class TestApi : WebApplicationFactory<Program>, IAsyncLifetime
{
    // Never point this at the development database ("PBRMS"): every run wipes it.
    // The timeouts are longer than the 15 s / 30 s defaults, so a busy development
    // machine (low memory, antivirus scanning SQL Server's files) gives slow tests
    // rather than failed ones.
    private const string ConnectionString =
        "Server=localhost;Database=PBRMS_Tests;Trusted_Connection=True;TrustServerCertificate=True;" +
        "Connect Timeout=60;Command Timeout=120";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Development: dev sign-in, and the database is created/migrated on start-up.
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:Pbrms", ConnectionString);
    }

    /// <summary>Runs once before the first test: start the API, then reset the data.</summary>
    public async Task InitializeAsync()
    {
        await WithDatabaseAsync(async db =>
        {
            // Delete children before parents, so the foreign keys allow it.
            // Add new tables here, in the same order, when the schema grows.
            await db.Notifications.ExecuteDeleteAsync();
            await db.DailyRosters.ExecuteDeleteAsync();
            await db.OffRosterWork.ExecuteDeleteAsync();
            await db.RosterEntries.ExecuteDeleteAsync();
            await db.MonthlyRosters.ExecuteDeleteAsync();
            await db.ShiftDefinitions.ExecuteDeleteAsync();
            await db.BookingStatusChanges.ExecuteDeleteAsync();
            await db.CrewAssignments.ExecuteDeleteAsync();
            await db.CrewSlots.ExecuteDeleteAsync();
            await db.Bookings.ExecuteDeleteAsync();
            await db.RoleAssignments.ExecuteDeleteAsync();
            await db.Users.ExecuteDeleteAsync();
            await db.OrgUnits.Where(u => u.ParentId != null).ExecuteDeleteAsync();
            await db.OrgUnits.ExecuteDeleteAsync();

            await DevSeed.RunAsync(db);
        });
    }

    // Nothing extra to clean up: the data stays for the next run to wipe, which also
    // leaves it there to look at if a test fails. The API itself shuts down when xUnit
    // disposes this factory.
    Task IAsyncLifetime.DisposeAsync() => Task.CompletedTask;

    /// <summary>Reads or changes data directly in the test database, e.g. to add a user.</summary>
    public async Task WithDatabaseAsync(Func<PbrmsDbContext, Task> work)
    {
        using var scope = Services.CreateScope();
        await work(scope.ServiceProvider.GetRequiredService<PbrmsDbContext>());
    }

    /// <summary>An HTTP client already signed in as the user with this email.</summary>
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
}

/// <summary>Test classes marked [Collection("API")] share one <see cref="TestApi"/> and run one at a time.</summary>
[CollectionDefinition("API")]
public class ApiCollection : ICollectionFixture<TestApi>;
