using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using PBRMS.Api.Auth;
using PBRMS.Data;
using PBRMS.Domain;

namespace PBRMS.Api.Endpoints;

/// <summary>
/// POST /bookings/{id}/transition: move a booking along (submit, approve, send back,
/// start, end, cancel). The rules for who may do what are in
/// <see cref="BookingWorkflow"/>; this endpoint applies them and records the change.
/// </summary>
public static class BookingTransitionEndpoints
{
    public static void MapBookingTransitionEndpoints(this WebApplication app)
    {
        app.MapPost("/bookings/{id:guid}/transition", Transition).RequireAuthorization();
    }

    private static async Task<IResult> Transition(Guid id, TransitionRequest request, ClaimsPrincipal principal, [AsParameters] BookingServices services)
    {
        var db = services.Db;

        // One transaction, so the clash check and the confirmation stand or fall together (TR-02).
        await using var transaction = await db.Database.BeginTransactionAsync();

        var booking = await db.Bookings
            .Include(b => b.CrewSlots).ThenInclude(s => s.Assignments.Where(a => a.RemovedAt == null))
            .FirstOrDefaultAsync(b => b.Id == id);
        if (booking is null)
            return Results.NotFound();

        var caller = await Caller.LoadAsync(principal, db);
        var units = await db.OrgUnits.AsNoTracking().ToListAsync();
        var departmentChain = OrgUnit.ChainUp(booking.DepartmentId, units);

        // 1. Is this move allowed at this stage, and by this person?
        var (newStatus, problem) = await CheckMoveAsync(booking, request, caller, departmentChain, services);
        if (problem is not null)
            return problem;

        // 2. Sending back and cancelling must say why.
        var note = request.Note?.Trim();
        if (BookingWorkflow.NeedsNote(request.Action) && string.IsNullOrEmpty(note))
            return Problems.Invalid("note", "Say why, so the producer knows what happens next.");
        if (note?.Length > 1000)
            return Problems.Invalid("note", "Keep the note to 1000 characters.");

        // 3. What this particular step records.
        var now = DateTimeOffset.UtcNow;
        RecordStepDetails(booking, request, now);

        // 4. Record the change and save. Every change goes into the history (TR-03).
        booking.History.Add(new BookingStatusChange
        {
            FromStatus = booking.Status,
            ToStatus = newStatus,
            ActorId = caller.UserId,
            Note = string.IsNullOrEmpty(note) ? null : note,
            At = now
        });
        booking.Status = newStatus;

        // A booking has come in for the EP, from the producer or sent back from crewing.
        if (newStatus == BookingStatus.EpReview)
            services.Notifier.Send(
                await services.Notifier.PeopleWithRoleAsync(Role.ExecutiveProducer, departmentChain),
                $"#{booking.ProductionNo} {booking.Title} ({NbcTime.Text(booking.StartsAt)}) needs your approval.",
                booking.Id);

        // The EP approved: studio crew comes from the roster, outside crew goes to the supervisors.
        if (newStatus == BookingStatus.CrewAssignment)
            await services.CrewProgress.OpenAsync(booking, caller.UserId);

        if (newStatus == BookingStatus.Confirmed)
            await services.Notifier.TellConfirmedAsync(booking, departmentChain);

        try
        {
            await db.SaveChangesAsync();
            await transaction.CommitAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            // Someone else saved this booking between our read and our save (see Booking.RowVersion).
            return Problems.Conflict("Someone else changed this booking at the same moment. Reload it and try again.");
        }

        return Results.Ok(await BookingQueries.LoadResponseAsync(db, id));
    }

    /// <summary>The status this request moves the booking to, or the problem with it.</summary>
    private static async Task<(BookingStatus NewStatus, IResult? Problem)> CheckMoveAsync(
        Booking booking, TransitionRequest request, Caller caller, HashSet<Guid> departmentChain, BookingServices services)
    {
        if (request.Action == BookingAction.Cancel)
        {
            if (!BookingWorkflow.CanBeCancelled(booking.Status))
                return (booking.Status, Problems.Conflict($"A booking that is {booking.Status} can't be cancelled."));
            if (!BookingWorkflow.MayCancel(booking, caller.UserId, caller.Roles, departmentChain))
                return (booking.Status, Problems.Forbidden("Only the booking's producer, its department's EP, the Booking Officer or an Admin can cancel it."));
            return (BookingStatus.Cancelled, null);
        }

        var step = BookingWorkflow.FindStep(booking.Status, request.Action);
        if (step is null)
            return (booking.Status, Problems.Conflict($"A booking in {booking.Status} can't take the action '{request.Action}'."));
        if (!BookingWorkflow.MayTake(step, booking, caller.UserId, caller.Roles, departmentChain, DateTimeOffset.UtcNow))
            return (booking.Status, Problems.Forbidden($"At this stage only {Describe(step.ActedBy)} can do this."));

        // EP approval sends the booking to the supervisors of the divisions in its crew
        // list (FR-BOOK-06). With no crew list it would reach nobody.
        if (step.Action == BookingAction.Submit && booking.CrewSlots.Count == 0)
            return (booking.Status, Problems.Invalid("crewSlots", "Add at least one crew position before submitting."));

        // After 15h00 the next day is closed to producers, unless the Booking Officer allowed this one.
        if (step.Action == BookingAction.Submit
            && booking.LateAllowedById is null
            && BookingWorkflow.MissedCutOff(booking.StartsAt, DateTimeOffset.UtcNow))
        {
            var earliest = BookingWorkflow.EarliestBookableDate(DateTimeOffset.UtcNow);
            return (booking.Status, Problems.Conflict(
                $"Too late to book that day: bookings close at 15h00 the day before. The earliest you can book now is {earliest:dd/MM/yyyy}. If it can't wait, ask the Booking Officer."));
        }

        if (step.To == BookingStatus.Confirmed && await CheckConfirmAsync(booking, request, caller, services) is { } problem)
            return (booking.Status, problem);

        return (step.To, null);
    }

    /// <summary>
    /// A booking can't be confirmed with an unresolved clash (BR-06, TR-02). The locks make
    /// sure nobody takes this crew or studio between the check and the save. Warnings don't
    /// block, but the Booking Officer must see them first. Returns the problem, if any.
    /// </summary>
    private static async Task<IResult?> CheckConfirmAsync(Booking booking, TransitionRequest request, Caller caller, BookingServices services)
    {
        if (booking.CrewSlots.Any(s => s.Current is null))
            return Problems.Conflict("Every crew position must be filled before confirming.");

        await ClashLocks.TakeAsync(services.Db, ClashLocks.ForBooking(booking));
        var clashes = await services.ClashChecker.ForBookingAsync(booking);

        var blocking = clashes.Where(c => !c.IsWarning).ToList();
        if (blocking.Count > 0)
            return Problems.Clashes("This booking still has clashes to resolve.", blocking);
        if (clashes.Count > 0 && !request.AcknowledgeWarnings)
            return Problems.Warnings("Check these before confirming.", clashes);

        // Acknowledged, so act on them. These can appear after the crew was assigned: the
        // roster changed, or the booking was moved onto an outside production's crew.
        foreach (var clash in clashes)
        {
            await services.OffRosterLog.RecordIfNewAsync(clash, caller.UserId);
            if (clash.Kind == ClashKind.TakeFromOutside)
                await services.StudioPrecedence.TakeAsync(clash.AssignmentId!.Value, booking, caller.UserId);
        }

        return null;
    }

    /// <summary>What some steps record besides the status.</summary>
    private static void RecordStepDetails(Booking booking, TransitionRequest request, DateTimeOffset now)
    {
        switch (request.Action)
        {
            // The EP checks the air date and channel when approving (App Flow Document, step 3).
            case BookingAction.Approve when booking.Status == BookingStatus.EpReview:
                booking.Channel = request.Channel ?? booking.Channel;
                booking.AirAt = request.AirAt ?? booking.AirAt;
                break;
            case BookingAction.Start:
                booking.ActualStartAt = now;
                break;
            case BookingAction.End:
                booking.ActualEndAt = now;
                break;
        }
    }

    /// <summary>Who acts at a stage, in words for the error message.</summary>
    private static string Describe(Role role) => role switch
    {
        Role.Producer => "the booking's producer",
        Role.ExecutiveProducer => "the department's Executive Producer",
        Role.Supervisor => "whoever is deciding this booking's crew",
        Role.BookingOfficer => "the Booking Officer",
        _ => $"a {role}"
    };
}
