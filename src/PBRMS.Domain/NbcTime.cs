namespace PBRMS.Domain;

/// <summary>
/// NBC's working time. Rosters are written in local dates and shift hours, while
/// bookings are moments in time, so comparing them needs one conversion. Namibia is
/// UTC+2 all year, so a fixed offset is enough.
/// </summary>
public static class NbcTime
{
    public static readonly TimeSpan Offset = TimeSpan.FromHours(2);

    /// <summary>The Windhoek date of a moment in time.</summary>
    public static DateOnly DateOf(DateTimeOffset moment) => DateOnly.FromDateTime(moment.ToOffset(Offset).DateTime);

    /// <summary>A Windhoek date and time as a moment in time.</summary>
    public static DateTimeOffset At(DateOnly date, TimeOnly time) => new(date.ToDateTime(time), Offset);
}
