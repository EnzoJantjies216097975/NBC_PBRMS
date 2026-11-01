using Microsoft.EntityFrameworkCore;
using PBRMS.Data;
using PBRMS.Domain;

namespace PBRMS.Api.Endpoints;

/// <summary>Loading bookings with everything a <see cref="BookingResponse"/> shows.</summary>
public static class BookingQueries
{
    public static IQueryable<Booking> WithDetails(this IQueryable<Booking> bookings) => bookings
        .AsNoTracking()
        .Include(b => b.Department)
        .Include(b => b.Producer)
        .Include(b => b.CrewSlots).ThenInclude(s => s.Division)
        .Include(b => b.CrewSlots).ThenInclude(s => s.Assignments.Where(a => a.RemovedAt == null)).ThenInclude(a => a.Operator)
        .Include(b => b.History).ThenInclude(h => h.Actor)
        .AsSplitQuery(); // one query per list instead of one huge joined result

    public static async Task<BookingResponse> LoadResponseAsync(PbrmsDbContext db, Guid bookingId) =>
        BookingResponse.From(await db.Bookings.WithDetails().SingleAsync(b => b.Id == bookingId));
}

/// <summary>
/// The services the crew and transition endpoints use, gathered into one parameter so
/// their signatures stay short. ASP.NET fills it in from the [AsParameters] attribute.
/// </summary>
public record BookingServices(
    PbrmsDbContext Db,
    ClashChecker ClashChecker,
    Notifier Notifier,
    OffRosterLog OffRosterLog,
    StudioPrecedence StudioPrecedence,
    CrewProgress CrewProgress);
