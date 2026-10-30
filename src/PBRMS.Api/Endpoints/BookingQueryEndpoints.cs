using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using PBRMS.Api.Auth;
using PBRMS.Data;
using PBRMS.Domain;

namespace PBRMS.Api.Endpoints;

/// <summary>
/// Reading bookings: one booking, my own bookings (producer), and my approval queue
/// (bookings waiting for me to act).
/// </summary>
public static class BookingQueryEndpoints
{
    /// <summary>Everyone except operators can read bookings (Backend Schema Document). Operators will see their own crew calls instead.</summary>
    internal static readonly string[] BookingReaders = Enum.GetNames<Role>().Where(r => r != nameof(Role.Operator)).ToArray();

    public static void MapBookingQueryEndpoints(this WebApplication app)
    {
        app.MapGet("/bookings/{id:guid}", GetBooking).RequireAuthorization(p => p.RequireRole(BookingReaders));
        app.MapGet("/bookings/mine", MyBookings).RequireAuthorization(p => p.RequireRole(nameof(Role.Producer)));
        app.MapGet("/bookings/queue", MyQueue).RequireAuthorization(p => p.RequireRole(BookingReaders));
    }

    private static async Task<IResult> GetBooking(Guid id, PbrmsDbContext db)
    {
        var booking = await db.Bookings.WithDetails().FirstOrDefaultAsync(b => b.Id == id);
        return booking is null ? Results.NotFound() : Results.Ok(BookingResponse.From(booking));
    }

    /// <summary>The signed-in producer's bookings, soonest first.</summary>
    private static async Task<List<BookingResponse>> MyBookings(ClaimsPrincipal principal, PbrmsDbContext db)
    {
        var userId = PbrmsClaims.UserIdOf(principal);
        var bookings = await db.Bookings.WithDetails()
            .Where(b => b.ProducerId == userId)
            .OrderBy(b => b.StartsAt)
            .ToListAsync();
        return bookings.Select(BookingResponse.From).ToList();
    }

    /// <summary>
    /// Bookings waiting for the signed-in person to approve or send back, soonest first.
    /// It uses the same <see cref="BookingWorkflow.MayTake"/> rule as the transition
    /// endpoint, so a booking is in someone's queue exactly when they are allowed to act on it.
    /// </summary>
    private static async Task<List<BookingResponse>> MyQueue(ClaimsPrincipal principal, PbrmsDbContext db)
    {
        var caller = await Caller.LoadAsync(principal, db);
        var units = await db.OrgUnits.AsNoTracking().ToListAsync();

        // Stages where someone else must act on the booking. Stages that belong to its
        // producer (their draft, the production day) show in /bookings/mine.
        var reviewStages = BookingWorkflow.Steps
            .Where(s => s.ActedBy != Role.Producer)
            .Select(s => s.From)
            .Distinct()
            .ToList();

        // Load everything at a review stage, then keep what this person may act on. There
        // are only ever tens of bookings in review, and keeping the rule in one place in C#
        // beats repeating it in SQL.
        var inReview = await db.Bookings.WithDetails()
            .Where(b => reviewStages.Contains(b.Status))
            .OrderBy(b => b.StartsAt)
            .ToListAsync();

        return inReview
            .Where(b => BookingWorkflow.Steps.Any(step =>
                step.From == b.Status &&
                BookingWorkflow.MayTake(step, b, caller.UserId, caller.Roles, OrgUnit.ChainUp(b.DepartmentId, units))))
            .Select(BookingResponse.From)
            .ToList();
    }
}
