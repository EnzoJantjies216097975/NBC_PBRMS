namespace PBRMS.Domain;

/// <summary>
/// What a roster says about whether someone can work a production. These rules come
/// from the current Production Board (lib/shifts.js and the Flags tab), because the
/// PBRMS documents only say "resolved through shift_definitions".
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
    /// Why someone with these shifts can't work this period, or null if they can.
    /// Pass every code in the cell. With none (a blank cell, or a code no longer on the
    /// legend) there is no answer, and "unknown" must never be reported as a clash.
    /// </summary>
    /// <param name="forTrip">The period is for an out-of-town booking.</param>
    public static string? ClashReason(IReadOnlyCollection<ShiftDefinition> shifts, DateOnly day, DateTimeOffset from, DateTimeOffset to, bool forTrip)
    {
        if (shifts.Count == 0)
            return null;

        // Off only if every code says so: half of "O+ST2" is still a working day.
        if (shifts.All(s => s.Kind == ShiftKind.Off))
            return $"Off on the roster ({Describe(shifts)})";

        var working = shifts.Where(s => s.Kind != ShiftKind.Off).ToList();

        // Someone rostered out of town can only be on a trip.
        if (!forTrip && working.All(s => s.Kind == ShiftKind.Away))
            return $"Away on the roster ({Describe(shifts)})";

        // Hours don't apply to trips or to anything longer than a day: going out of town
        // isn't a question of hours (the board's rule).
        if (forTrip || to - from >= TimeSpan.FromHours(24))
            return null;

        // A code with no fixed hours fits anything.
        if (working.Any(s => s.Start is null))
            return null;

        // Any overlap is enough. Running past the end of a shift is overtime, not a clash.
        var onShift = working
            .SelectMany(s => s.WindowsOn(day))
            .Any(window => ClashRules.Overlaps(window.Start, window.End, from, to));

        return onShift ? null : $"Not on shift at that time ({Describe(shifts)})";
    }

    private static string Describe(IEnumerable<ShiftDefinition> shifts) =>
        string.Join(", ", shifts.Select(s => $"{s.Code}: {s.Label}"));
}
