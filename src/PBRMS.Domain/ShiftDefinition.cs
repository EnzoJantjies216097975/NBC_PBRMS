namespace PBRMS.Domain;

/// <summary>
/// One shift code on a division's legend: what a roster cell like "ST2", "A3" or "O"
/// means. Each TV Ops division has its own legend, and the same code can mean different
/// things in different divisions. Supervisors keep their own legend up to date.
/// </summary>
/// <remarks>
/// The schema document also has a day_type column (weekday, Saturday, Sunday), but
/// none of the 212 codes on the current board change hours by day, so it is left out.
/// Rules for what a code means for a booking are in <see cref="RosterRules"/>.
/// </remarks>
public class ShiftDefinition
{
    public Guid Id { get; set; }

    public Guid DivisionId { get; set; }
    public OrgUnit Division { get; set; } = null!;

    public string Code { get; set; } = "";
    public string Label { get; set; } = "";
    public ShiftKind Kind { get; set; }

    /// <summary>
    /// The shift's hours. Both empty means no fixed hours: "whatever the booking says".
    /// An end earlier than the start runs past midnight (a night shift).
    /// </summary>
    public TimeOnly? Start { get; set; }
    public TimeOnly? End { get; set; }

    /// <summary>A second shift on the same day. Only a few codes have one, e.g. Studio 2 morning and afternoon.</summary>
    public TimeOnly? SecondStart { get; set; }
    public TimeOnly? SecondEnd { get; set; }

    /// <summary>
    /// The studio this shift ties someone to, if any. Studio productions have priority, so
    /// moving a person away from their studio needs the supervisor to sort out the cover.
    /// </summary>
    public Venue? Studio { get; set; }

    /// <summary>The colour shown on the grid, e.g. "#5FB87A".</summary>
    public string? Colour { get; set; }

    /// <summary>False once the code is retired. Past rosters still use it, so it stays, but it can't be chosen any more.</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>The hours on this day as moments in time. Empty if the code has no fixed hours.</summary>
    public IEnumerable<(DateTimeOffset Start, DateTimeOffset End)> WindowsOn(DateOnly day)
    {
        if (Start is TimeOnly start && End is TimeOnly end)
            yield return Window(day, start, end);
        if (SecondStart is TimeOnly secondStart && SecondEnd is TimeOnly secondEnd)
            yield return Window(day, secondStart, secondEnd);
    }

    private static (DateTimeOffset, DateTimeOffset) Window(DateOnly day, TimeOnly start, TimeOnly end) =>
        (NbcTime.At(day, start), NbcTime.At(end > start ? day : day.AddDays(1), end));
}

/// <summary>What a shift code says about the person that day.</summary>
public enum ShiftKind
{
    /// <summary>A day off, such as a rest day or public holiday. They can be called in, with a warning.</summary>
    Off,

    /// <summary>On leave. They can't be booked at all: the supervisor must book someone else.</summary>
    Leave,

    /// <summary>Out of town: a trip or fly-away. They can't be in the building, but they can be on a trip.</summary>
    Away,

    /// <summary>On duty, in the hours given, or with no fixed hours.</summary>
    Worked
}
