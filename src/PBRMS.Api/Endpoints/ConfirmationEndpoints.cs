using Microsoft.EntityFrameworkCore;
using PBRMS.Data;
using PBRMS.Domain;

namespace PBRMS.Api.Endpoints;

/// <summary>
/// GET /reports/confirmations: for Admin, who decided each crew position, and who
/// confirmed each booking, between the Booking Officers and the supervisors. One row
/// per crew position that is in force now.
/// </summary>
public static class ConfirmationEndpoints
{
    /// <param name="DecidedBy">The person, or "Monthly roster" if it was filled automatically.</param>
    /// <param name="DecidedByRole">BookingOfficer, Supervisor, or null if it came from the roster.</param>
    /// <param name="ConfirmedBy">Who confirmed the booking. Empty until it's confirmed.</param>
    public record ConfirmationResponse(
        int ProductionNo,
        string Title,
        Venue Venue,
        DateTimeOffset StartsAt,
        BookingStatus Status,
        string Position,
        string Operator,
        string DecidedBy,
        Role? DecidedByRole,
        DateTimeOffset DecidedAt,
        string? ConfirmedBy,
        Role? ConfirmedByRole,
        DateTimeOffset? ConfirmedAt);

    public static void MapConfirmationEndpoints(this WebApplication app)
    {
        app.MapGet("/reports/confirmations", Report).RequireAuthorization(p => p.RequireRole(nameof(Role.Admin)));
    }

    private static async Task<List<ConfirmationResponse>> Report(DateOnly from, DateOnly to, PbrmsDbContext db)
    {
        var (start, end) = (NbcTime.At(from, TimeOnly.MinValue), NbcTime.At(to.AddDays(1), TimeOnly.MinValue));

        var positions = await db.CrewAssignments.AsNoTracking()
            .Where(a => a.RemovedAt == null && a.CrewSlot.StartsAt >= start && a.CrewSlot.StartsAt < end)
            .OrderBy(a => a.CrewSlot.StartsAt)
            .Select(a => new
            {
                a.CrewSlot.BookingId,
                a.CrewSlot.Booking.ProductionNo,
                a.CrewSlot.Booking.Title,
                a.CrewSlot.Booking.Venue,
                a.CrewSlot.StartsAt,
                a.CrewSlot.Booking.Status,
                a.CrewSlot.PositionLabel,
                Operator = a.Operator.FirstName + " " + a.Operator.Surname,
                a.AssignedById,
                DecidedBy = a.AssignedBy == null ? null : a.AssignedBy.FirstName + " " + a.AssignedBy.Surname,
                a.AssignedAt
            })
            .ToListAsync();

        var bookingIds = positions.Select(p => p.BookingId).Distinct().ToList();

        // The latest confirmation of each booking: it can go back for crew and be confirmed again.
        var confirmations = (await db.BookingStatusChanges.AsNoTracking()
                .Where(c => bookingIds.Contains(c.BookingId) && c.ToStatus == BookingStatus.Confirmed)
                .Select(c => new { c.BookingId, c.ActorId, c.At, Name = c.Actor.FirstName + " " + c.Actor.Surname })
                .ToListAsync())
            .GroupBy(c => c.BookingId)
            .ToDictionary(g => g.Key, g => g.MaxBy(c => c.At)!);

        var bookingOfficers = await db.RoleAssignments.AsNoTracking()
            .Where(r => r.Role == Role.BookingOfficer)
            .Select(r => r.UserId)
            .ToListAsync();

        Role? RoleOf(Guid? userId) => userId is null ? null
            : bookingOfficers.Contains(userId.Value) ? Role.BookingOfficer : Role.Supervisor;

        return positions
            .Select(p =>
            {
                var confirmed = confirmations.GetValueOrDefault(p.BookingId);
                return new ConfirmationResponse(
                    p.ProductionNo, p.Title, p.Venue, p.StartsAt, p.Status, p.PositionLabel, p.Operator,
                    p.DecidedBy ?? "Monthly roster", RoleOf(p.AssignedById), p.AssignedAt,
                    confirmed?.Name, RoleOf(confirmed?.ActorId), confirmed?.At);
            })
            .ToList();
    }
}
