using System.Globalization;
using Microsoft.EntityFrameworkCore;
using PBRMS.Domain;

namespace PBRMS.Data;

/// <summary>
/// Reminds supervisors each morning about a monthly roster that's due and not published,
/// once a day per division, until it is. The API runs this every minute (see MinuteChecksService),
/// so if the API was down at 08h00 the reminder goes out when it's back.
/// </summary>
public class RosterReminderWatcher(PbrmsDbContext db, Notifier notifier)
{
    /// <param name="now">The time to check at. Passed in so tests don't have to wait.</param>
    public async Task CheckAsync(DateTimeOffset now)
    {
        if (TimeOnly.FromTimeSpan(now.ToOffset(NbcTime.Offset).TimeOfDay) < RosterRules.ReminderTime)
            return;

        var today = NbcTime.DateOf(now);
        var months = RosterRules.MonthsDue(today);

        // A division with no operators has nothing to put on a roster.
        var divisions = await db.OrgUnits
            .Where(u => u.Kind == OrgUnitKind.TvOpsDivision
                        && u.RosterReminderSentOn != today
                        && db.RoleAssignments.Any(r => r.OrgUnitId == u.Id && r.Role == Role.Operator && r.User.IsActive))
            .ToListAsync();
        if (divisions.Count == 0)
            return;

        var published = await db.MonthlyRosters
            .AsNoTracking()
            .Where(r => r.Status == RosterStatus.Published && months.Contains(r.Month))
            .Select(r => new { r.DivisionId, r.Month })
            .ToListAsync();

        foreach (var division in divisions)
        {
            // The earliest one is the most urgent, and publishing it leaves the next to be chased.
            var missing = months.Where(m => !published.Contains(new { DivisionId = division.Id, Month = m })).ToList();
            if (missing.Count == 0)
                continue;

            var month = missing[0].ToString("MMMM yyyy", CultureInfo.InvariantCulture);
            notifier.Send(
                await notifier.PeopleWithRoleAsync(Role.Supervisor, division.Id),
                $"The {month} roster for {division.Name} isn't published yet.");
            division.RosterReminderSentOn = today;
        }

        await db.SaveChangesAsync();
    }
}
