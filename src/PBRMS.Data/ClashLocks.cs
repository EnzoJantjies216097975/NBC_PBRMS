using Microsoft.EntityFrameworkCore;
using PBRMS.Domain;

namespace PBRMS.Data;

/// <summary>
/// Stops two people double-booking the same operator or studio at the same instant.
///
/// A clash check on its own isn't enough. If two supervisors assign the same operator
/// at the same moment, both checks can run before either save, both find nothing, and
/// both save. So whoever is about to check and save first takes a lock on that operator
/// (or studio). The second waits until the first has committed, then its check sees the
/// first assignment and reports the clash (BR-06; Implementation Plan, "Concurrency test").
///
/// These are SQL Server "application locks": named locks that belong to the current
/// transaction and are released when it commits or rolls back. Must be called inside a
/// transaction.
/// </summary>
public static class ClashLocks
{
    public static string ForOperator(Guid operatorId) => $"pbrms-operator-{operatorId}";

    public static string ForVenue(Venue venue) => $"pbrms-venue-{venue}";

    /// <summary>The locks needed to confirm a booking: its studio, if it can clash, and its crew.</summary>
    public static IEnumerable<string> ForBooking(Booking booking)
    {
        var locks = booking.CrewSlots
            .Where(s => s.Current is not null)
            .Select(s => ForOperator(s.Current!.OperatorId))
            .ToList();

        if (ClashRules.VenueCanClash(booking.Venue))
            locks.Add(ForVenue(booking.Venue));

        return locks;
    }

    /// <summary>
    /// Takes the locks, waiting up to 15 seconds for each. Always in the same (sorted)
    /// order: if one request took A then B while another took B then A, each would wait
    /// for the other for ever.
    /// </summary>
    public static async Task TakeAsync(PbrmsDbContext db, IEnumerable<string> names)
    {
        foreach (var name in names.Distinct().Order(StringComparer.Ordinal))
        {
            await db.Database.ExecuteSqlAsync($"""
                DECLARE @result int;
                EXEC @result = sp_getapplock @Resource = {name}, @LockMode = 'Exclusive',
                                             @LockOwner = 'Transaction', @LockTimeout = 15000;
                IF @result < 0 THROW 50001, 'Timed out waiting for a clash-check lock.', 1;
                """);
        }
    }
}
