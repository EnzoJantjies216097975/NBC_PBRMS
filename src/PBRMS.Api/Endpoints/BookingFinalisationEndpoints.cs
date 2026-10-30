using Microsoft.EntityFrameworkCore;
using PBRMS.Data;
using PBRMS.Domain;

namespace PBRMS.Api.Endpoints;

/// <summary>
/// The Booking Officer's tools at the Finalisation stage: see what clashes, and adjust
/// studios and times to resolve it (App Flow Document, step 5). Confirming is a normal
/// transition; see <see cref="BookingTransitionEndpoints"/>.
/// </summary>
public static class BookingFinalisationEndpoints
{
    public static void MapBookingFinalisationEndpoints(this WebApplication app)
    {
        app.MapGet("/bookings/{id:guid}/clashes", GetClashes)
            .RequireAuthorization(p => p.RequireRole(BookingQueryEndpoints.BookingReaders));

        app.MapPut("/bookings/{id:guid}/schedule", ChangeSchedule)
            .RequireAuthorization(p => p.RequireRole(nameof(Role.BookingOfficer)));
    }

    private static async Task<IResult> GetClashes(Guid id, PbrmsDbContext db, ClashChecker clashChecker)
    {
        var booking = await db.Bookings.WithDetails().FirstOrDefaultAsync(b => b.Id == id);
        return booking is null ? Results.NotFound() : Results.Ok(await clashChecker.ForBookingAsync(booking));
    }

    /// <summary>
    /// Moves the booking to another studio or time. This may create or clear clashes. It
    /// doesn't refuse them, because the Booking Officer may need several moves to sort out
    /// a busy day; confirming is what's blocked while a clash remains.
    /// </summary>
    private static async Task<IResult> ChangeSchedule(Guid id, ScheduleRequest request, PbrmsDbContext db, Notifier notifier)
    {
        var booking = await db.Bookings.Include(b => b.CrewSlots).FirstOrDefaultAsync(b => b.Id == id);
        if (booking is null)
            return Results.NotFound();

        if (booking.Status != BookingStatus.Finalisation)
            return Problems.Conflict($"Only bookings at Finalisation can be moved, and this one is in {booking.Status}.");
        if (request.EndsAt <= request.StartsAt)
            return Problems.Invalid("endsAt", "The end must be after the start.");
        if (request.CallAt > request.StartsAt)
            return Problems.Invalid("callAt", "The crew call must be at or before the start.");
        if (request.VenueText?.Trim().Length > 160)
            return Problems.Invalid("venueText", "Keep this to 160 characters.");

        // Crew positions move with the booking. A position on the booking's own hours takes
        // the new hours; one with its own hours (e.g. a rigger starting early) keeps them,
        // shifted by the same amount as the start.
        var shift = request.StartsAt - booking.StartsAt;
        foreach (var slot in booking.CrewSlots)
        {
            var onBookingHours = slot.StartsAt == booking.StartsAt && slot.EndsAt == booking.EndsAt;
            slot.StartsAt = onBookingHours ? request.StartsAt : slot.StartsAt + shift;
            slot.EndsAt = onBookingHours ? request.EndsAt : slot.EndsAt + shift;
        }

        // A new studio can undo what the supervisors planned, so they are told to check their crew.
        if (request.Venue != booking.Venue)
        {
            var supervisors = await notifier.PeopleWithRoleAsync(Role.Supervisor, booking.CrewSlots.Select(s => s.DivisionId));
            notifier.Send(
                supervisors,
                $"#{booking.ProductionNo} {booking.Title} has moved from {booking.Venue} to {request.Venue}. Please make the changes needed to your crew.",
                booking.Id);
        }

        booking.Venue = request.Venue;
        booking.VenueText = request.VenueText?.Trim();
        booking.CallAt = request.CallAt;
        booking.StartsAt = request.StartsAt;
        booking.EndsAt = request.EndsAt;

        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            return Problems.Conflict("Someone else changed this booking at the same moment. Reload it and try again.");
        }

        return Results.Ok(await BookingQueries.LoadResponseAsync(db, id));
    }
}
