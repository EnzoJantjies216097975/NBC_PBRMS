using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using PBRMS.Api.Auth;
using PBRMS.Data;

namespace PBRMS.Api.Endpoints;

/// <summary>
/// The signed-in person's notifications. Each person only ever sees their own.
/// </summary>
public static class NotificationEndpoints
{
    public record NotificationResponse(Guid Id, string Text, Guid? BookingId, DateTimeOffset CreatedAt, DateTimeOffset? ReadAt);

    public static void MapNotificationEndpoints(this WebApplication app)
    {
        // The newest 100. Older ones stay in the database.
        app.MapGet("/notifications", async (ClaimsPrincipal principal, PbrmsDbContext db) =>
        {
            var userId = PbrmsClaims.UserIdOf(principal);
            return await db.Notifications.AsNoTracking()
                .Where(n => n.UserId == userId)
                .OrderByDescending(n => n.CreatedAt)
                .Take(100)
                .Select(n => new NotificationResponse(n.Id, n.Text, n.BookingId, n.CreatedAt, n.ReadAt))
                .ToListAsync();
        }).RequireAuthorization();

        app.MapPost("/notifications/{id:guid}/read", async (Guid id, ClaimsPrincipal principal, PbrmsDbContext db) =>
        {
            var userId = PbrmsClaims.UserIdOf(principal);
            var changed = await db.Notifications
                .Where(n => n.Id == id && n.UserId == userId && n.ReadAt == null)
                .ExecuteUpdateAsync(n => n.SetProperty(x => x.ReadAt, DateTimeOffset.UtcNow));

            // Already read is fine. Someone else's notification looks the same as one that doesn't exist.
            var exists = changed > 0 || await db.Notifications.AnyAsync(n => n.Id == id && n.UserId == userId);
            return exists ? Results.NoContent() : Results.NotFound();
        }).RequireAuthorization();
    }
}
