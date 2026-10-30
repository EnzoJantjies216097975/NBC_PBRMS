using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using PBRMS.Api.Auth;
using PBRMS.Data;
using PBRMS.Domain;

namespace PBRMS.Api.Endpoints;

/// <summary>
/// POST /bookings/{id}/transition: move a booking along the approval chain (submit,
/// approve, send back, cancel). The rules for who may do what are in
/// <see cref="BookingWorkflow"/>; this endpoint applies them and records the change.
/// </summary>
public static class BookingTransitionEndpoints
{
    public static void MapBookingTransitionEndpoints(this WebApplication app)
    {
        app.MapPost("/bookings/{id:guid}/transition", Transition).RequireAuthorization();
    }

    private static async Task<IResult> Transition(Guid id, TransitionRequest request, ClaimsPrincipal principal, PbrmsDbContext db)
    {
        var booking = await db.Bookings.Include(b => b.CrewSlots).FirstOrDefaultAsync(b => b.Id == id);
        if (booking is null)
            return Results.NotFound();

        var caller = await Caller.LoadAsync(principal, db);
        var departmentChain = OrgUnit.ChainUp(booking.DepartmentId, await db.OrgUnits.AsNoTracking().ToListAsync());

        // 1. Is this move allowed at this stage, and by this person?
        BookingStatus newStatus;
        if (request.Action == BookingAction.Cancel)
        {
            if (!BookingWorkflow.CanBeCancelled(booking.Status))
                return Problems.Conflict($"A booking that is {booking.Status} can't be cancelled.");
            if (!BookingWorkflow.MayCancel(booking, caller.UserId, caller.Roles, departmentChain))
                return Problems.Forbidden("Only the booking's producer, its department's PM or EP, the Booking Officer or an Admin can cancel it.");
            newStatus = BookingStatus.Cancelled;
        }
        else
        {
            var step = BookingWorkflow.FindStep(booking.Status, request.Action);
            if (step is null)
                return Problems.Conflict($"A booking in {booking.Status} can't take the action '{request.Action}'.");
            if (!BookingWorkflow.MayTake(step, booking, caller.UserId, caller.Roles, departmentChain))
                return Problems.Forbidden($"At this stage only {Describe(step.ActedBy)} can do this.");

            // EP approval sends the booking to the supervisors of the divisions in its crew
            // list (FR-BOOK-06). With no crew list it would reach nobody.
            if (step.Action == BookingAction.Submit && booking.CrewSlots.Count == 0)
                return Problems.Invalid("crewSlots", "Add at least one crew position before submitting.");

            newStatus = step.To;
        }

        // 2. Sending back and cancelling must say why.
        var note = request.Note?.Trim();
        if (BookingWorkflow.NeedsNote(request.Action) && string.IsNullOrEmpty(note))
            return Problems.Invalid("note", "Say why, so the producer knows what happens next.");
        if (note?.Length > 1000)
            return Problems.Invalid("note", "Keep the note to 1000 characters.");

        // 3. The EP checks the air date and channel when approving (App Flow Document, step 3).
        if (booking.Status == BookingStatus.EpReview && request.Action == BookingAction.Approve)
        {
            booking.Channel = request.Channel ?? booking.Channel;
            booking.AirAt = request.AirAt ?? booking.AirAt;
        }

        // 4. Record the change and save. Every change goes into the history (TR-03).
        booking.History.Add(new BookingStatusChange
        {
            FromStatus = booking.Status,
            ToStatus = newStatus,
            ActorId = caller.UserId,
            Note = string.IsNullOrEmpty(note) ? null : note,
            At = DateTimeOffset.UtcNow
        });
        booking.Status = newStatus;

        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            // Someone else saved this booking between our read and our save (see Booking.RowVersion).
            return Problems.Conflict("Someone else changed this booking at the same moment. Reload it and try again.");
        }

        return Results.Ok(await BookingQueries.LoadResponseAsync(db, id));
    }

    /// <summary>Who acts at a stage, in words for the error message.</summary>
    private static string Describe(Role role) => role switch
    {
        Role.Producer => "the booking's producer",
        Role.ProductionManager => "the department's Production Manager",
        Role.ExecutiveProducer => "the department's Executive Producer",
        Role.Supervisor => "a supervisor of one of the booking's crew divisions",
        _ => $"a {role}"
    };
}
