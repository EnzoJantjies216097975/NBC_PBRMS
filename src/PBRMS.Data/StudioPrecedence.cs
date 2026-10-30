using Microsoft.EntityFrameworkCore;
using PBRMS.Domain;

namespace PBRMS.Data;

/// <summary>
/// Studio productions take precedence over outside ones: studios are normally filled
/// first. So a studio can take an operator straight off an outside production. That
/// production then needs someone else, and its supervisors are told to find them.
/// </summary>
public class StudioPrecedence(PbrmsDbContext db, Notifier notifier)
{
    /// <summary>
    /// Takes the operator off the outside production. If it had got past crew assignment
    /// it goes back there, even if it was confirmed, and comes through the Booking
    /// Officer again once the position is filled.
    /// </summary>
    /// <param name="studio">The studio production taking them.</param>
    public async Task TakeAsync(Guid assignmentId, Booking studio, Guid actorId)
    {
        var taken = await db.CrewAssignments
            .Include(a => a.Operator)
            .Include(a => a.CrewSlot).ThenInclude(s => s.Booking)
            .SingleAsync(a => a.Id == assignmentId);

        var now = DateTimeOffset.UtcNow;
        var outside = taken.CrewSlot.Booking;
        taken.RemovedAt = now;

        if (outside.Status is BookingStatus.Finalisation or BookingStatus.Confirmed)
        {
            db.BookingStatusChanges.Add(new BookingStatusChange
            {
                BookingId = outside.Id,
                FromStatus = outside.Status,
                ToStatus = BookingStatus.CrewAssignment,
                ActorId = actorId,
                Note = $"{taken.CrewSlot.PositionLabel} taken for studio production #{studio.ProductionNo}",
                At = now
            });
            outside.Status = BookingStatus.CrewAssignment;
        }

        var operatorName = $"{taken.Operator.FirstName} {taken.Operator.Surname}";
        var production = $"#{outside.ProductionNo} {outside.Title} ({NbcTime.Text(taken.CrewSlot.StartsAt)})";

        notifier.Send(
            [taken.OperatorId],
            $"You have been moved from {production} to studio production #{studio.ProductionNo} {studio.Title}.",
            studio.Id);

        var supervisors = await notifier.PeopleWithRoleAsync(Role.Supervisor, taken.CrewSlot.DivisionId);
        notifier.Send(
            supervisors.Append(outside.ProducerId).Where(person => person != actorId),
            $"{operatorName} has been taken off {production} for studio production #{studio.ProductionNo}. {taken.CrewSlot.PositionLabel} needs someone else.",
            outside.Id);
    }
}
