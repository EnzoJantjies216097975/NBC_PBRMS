using Microsoft.EntityFrameworkCore;
using PBRMS.Domain;

namespace PBRMS.Data;

/// <summary>
/// Sends notifications, and finds who should get them. <see cref="Send"/> only adds to
/// the current database work: the caller's save writes them with the change they report,
/// so they stand or fall together.
/// </summary>
public class Notifier(PbrmsDbContext db)
{
    /// <summary>Tells these people something. Anyone listed twice gets it once.</summary>
    public void Send(IEnumerable<Guid> userIds, string text, Guid? bookingId = null)
    {
        var now = DateTimeOffset.UtcNow;
        db.Notifications.AddRange(userIds.Distinct().Select(userId => new Notification
        {
            UserId = userId,
            Text = text,
            BookingId = bookingId,
            CreatedAt = now
        }));
    }

    /// <summary>
    /// Active people who hold this role in one of these units, or NBC-wide. Pass no units
    /// for roles that are only held NBC-wide, such as the Booking Officer.
    /// </summary>
    public async Task<List<Guid>> PeopleWithRoleAsync(Role role, params IEnumerable<Guid> units)
    {
        var unitIds = units.ToList();
        return await db.RoleAssignments.AsNoTracking()
            .Where(r => r.Role == role
                        && r.User.IsActive
                        && (r.OrgUnitId == null || unitIds.Contains(r.OrgUnitId.Value)))
            .Select(r => r.UserId)
            .Distinct()
            .ToListAsync();
    }
}
