using Microsoft.EntityFrameworkCore;
using PBRMS.Domain;

namespace PBRMS.Data;

/// <summary>
/// Studios are filled first. A studio can take an operator off an outside production that
/// isn't confirmed yet. That production then needs someone else, and its supervisors are
/// told to find them. A confirmed crew is left alone: the supervisor has already phoned them.
/// </summary>
public class StudioPrecedence(PbrmsDbContext db, Notifier notifier)
{
    /// <summary>
    /// Takes the operator off the outside production, which is still being crewed.
    /// </summary>
    /// <param name="studio">The studio production taking them.</param>
    public async Task TakeAsync(Guid assignmentId, Booking studio, Guid actorId)
    {
        var taken = await db.CrewAssignments
            .Include(a => a.Operator)
            .Include(a => a.CrewSlot).ThenInclude(s => s.Booking)
            .SingleAsync(a => a.Id == assignmentId);

        var outside = taken.CrewSlot.Booking;
        taken.RemovedAt = DateTimeOffset.UtcNow;

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
