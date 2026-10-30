using Microsoft.EntityFrameworkCore;
using PBRMS.Domain;

namespace PBRMS.Data;

/// <summary>
/// Writes the record of work outside the roster (see <see cref="OffRosterWork"/>). Like
/// notifications, the rows go in with the caller's save, so they stand or fall with the change.
/// </summary>
public class OffRosterLog(PbrmsDbContext db)
{
    /// <summary>Records a new assignment that was made despite this warning.</summary>
    public void Record(CrewAssignment assignment, Clash warning, Guid recordedById) =>
        db.OffRosterWork.Add(new OffRosterWork
        {
            CrewAssignment = assignment,
            Kind = warning.Kind,
            Reason = warning.Title,
            RecordedById = recordedById,
            RecordedAt = DateTimeOffset.UtcNow
        });

    /// <summary>
    /// Records an existing assignment, unless it's already recorded. For warnings that
    /// appear after someone was assigned: at confirmation, or when a roster is published.
    /// </summary>
    public async Task RecordIfNewAsync(Clash warning, Guid recordedById)
    {
        if (warning.AssignmentId is not Guid assignmentId || !ClashRules.IsOffRoster(warning.Kind))
            return;

        if (await db.OffRosterWork.AnyAsync(w => w.CrewAssignmentId == assignmentId && w.Kind == warning.Kind))
            return;

        db.OffRosterWork.Add(new OffRosterWork
        {
            CrewAssignmentId = assignmentId,
            Kind = warning.Kind,
            Reason = warning.Title,
            RecordedById = recordedById,
            RecordedAt = DateTimeOffset.UtcNow
        });
    }
}
