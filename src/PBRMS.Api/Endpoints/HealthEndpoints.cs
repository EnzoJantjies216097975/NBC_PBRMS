using PBRMS.Data;

namespace PBRMS.Api.Endpoints;

/// <summary>
/// GET /health: is the API up, and can it reach the database? For whoever looks
/// after the server (NBC IT, IIS monitoring). Open to everyone and shows no data.
/// </summary>
public static class HealthEndpoints
{
    public static void MapHealthEndpoints(this WebApplication app)
    {
        app.MapGet("/health", async (PbrmsDbContext db) => new
        {
            ok = true,
            database = await db.Database.CanConnectAsync() ? "reachable" : "NOT reachable",
            time = DateTimeOffset.UtcNow
        }).AllowAnonymous();
    }
}
