namespace PBRMS.Domain;

/// <summary>What the roster says about a period: what kind of problem, and why.</summary>
/// <param name="Studio">For <see cref="ClashKind.RosteredElsewhere"/>: the studio the person is rostered to.</param>
public record RosterFinding(ClashKind Kind, string Reason, Venue? Studio = null);

/// <summary>
/// What a roster says about whether someone can work a production. The hours rules
/// come from the current Production Board (lib/shifts.js and the Flags tab); the rules
/// for leave, days off and studios were decided for PBRMS in October 2026.
///
///   On leave                  Refused. The supervisor must book someone else.
///   Rostered away             Refused, unless the booking is itself a trip.
///   Not on shift at that time A warning, logged for HR.
///   Day off                   A warning, logged for HR. They can be called in, and are then notified.
///   Rostered to another studio A warning, and the supervisor sorts out the cover.
/// </summary>
public static class RosterRules
{
    /// <summary>Splits a cell into its codes: "N+ST2" is a night shift plus a Studio 2 shift.</summary>
    public static string[] SplitCodes(string cell) =>
        cell.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>
    /// The roster days a period falls on. A production that runs past midnight belongs
    /// to the day it started, so someone doesn't clash with a day off that begins when
    /// their night shift ends. Anything of 24 hours or more, such as a trip, covers every day.
    /// </summary>
    public static List<DateOnly> DaysCovered(DateTimeOffset from, DateTimeOffset to)
    {
        var first = NbcTime.DateOf(from);
        if (to - from < TimeSpan.FromHours(24))
            return [first];

        var days = new List<DateOnly>();
        for (var day = first; day <= NbcTime.DateOf(to); day = day.AddDays(1))
            days.Add(day);
        return days;
    }

    /// <summary>
    /// What this roster cell means for working this period, or null if it's fine.
    /// Pass every code in the cell. With none (a blank cell, or a code no longer on the
    /// legend) there is no answer, and "unknown" must never be reported as a clash.
    /// </summary>
    /// <param name="forTrip">The period is for an out-of-town booking.</param>
    /// <param name="venue">Where the work is, or null if that isn't known yet.</param>
    public static RosterFinding? Check(
        IReadOnlyCollection<ShiftDefinition> shifts, DateOnly day, DateTimeOffset from, DateTimeOffset to, bool forTrip, Venue? venue = null)
    {
        if (shifts.Count == 0)
            return null;

        // Not working only if every code says so: half of "O+ST2" is still a working day.
        // Leave outranks a day off, because leave can't be overridden.
        var working = shifts.Where(s => s.Kind is not (ShiftKind.Off or ShiftKind.Leave)).ToList();
        if (working.Count == 0)
        {
            return shifts.Any(s => s.Kind == ShiftKind.Leave)
                ? new RosterFinding(ClashKind.OnLeave, $"On leave ({Describe(shifts)})")
                : new RosterFinding(ClashKind.CallIn, $"Day off on the roster ({Describe(shifts)})");
        }

        // Someone rostered out of town can only be on a trip.
        if (!forTrip && working.All(s => s.Kind == ShiftKind.Away))
            return new RosterFinding(ClashKind.Roster, $"Away on the roster ({Describe(shifts)})");

        // The shifts that touch this period. Any overlap is enough: running past the end
        // of a shift is overtime, not a clash.
        var touching = working
            .Where(s => s.WindowsOn(day).Any(window => ClashRules.Overlaps(window.Start, window.End, from, to)))
            .ToList();

        // Hours don't apply to trips or to anything longer than a day: going out of town
        // isn't a question of hours (the board's rule). A code with no fixed hours fits anything.
        var hoursMatter = !forTrip && to - from < TimeSpan.FromHours(24) && working.All(s => s.Start is not null);
        if (hoursMatter && touching.Count == 0)
            return new RosterFinding(ClashKind.OutsideShift, $"Not on shift at that time ({Describe(shifts)})");

        // Rostered to a studio, but this work is somewhere else. A shift not tied to a
        // studio leaves them free to go anywhere.
        var studios = touching.Select(s => s.Studio).ToList();
        if (studios.Count > 0 && studios.All(s => s is not null) && !studios.Contains(venue) && venue is not null)
            return new RosterFinding(ClashKind.RosteredElsewhere, $"Rostered to {studios[0]} ({Describe(touching)})", studios[0]);

        return null;
    }

    /// <summary>
    /// Do these shifts put someone in this studio during this period? That's who the roster
    /// has already chosen for a studio production, so the crew can be filled automatically.
    /// </summary>
    public static bool IsRosteredTo(Venue studio, IEnumerable<ShiftDefinition> shifts, DateOnly day, DateTimeOffset from, DateTimeOffset to) =>
        shifts.Any(s => s.Studio == studio
                        && s.WindowsOn(day).Any(window => ClashRules.Overlaps(window.Start, window.End, from, to)));

    private static string Describe(IEnumerable<ShiftDefinition> shifts) =>
        string.Join(", ", shifts.Select(s => $"{s.Code}: {s.Label}"));
}
