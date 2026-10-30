namespace PBRMS.Domain;

/// <summary>
/// The Booking Officer's changes to one day's roster. The sheet itself isn't stored: it is
/// built fresh from the monthly rosters and confirmed bookings each time it's opened, so a
/// late booking appears by itself. Only what the Booking Officer changed is kept here, and
/// laid over the top. Only the Booking Officer can change it (BR-05).
/// </summary>
public class DailyRoster
{
    public Guid Id { get; set; }

    public DateOnly Date { get; set; }

    /// <summary>The day's announcement, printed at the top of the sheet.</summary>
    public string? Announcement { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
    public Guid UpdatedById { get; set; }
    public User UpdatedBy { get; set; } = null!;

    public List<DailyRosterLine> Lines { get; set; } = [];

    /// <summary>Changed by SQL Server on every save, so two edits at once can't overwrite each other.</summary>
    public byte[] RowVersion { get; set; } = [];
}

/// <summary>
/// One row of the sheet as the Booking Officer set it: an edited production, or a line of
/// their own. This changes the sheet only. To change who is really on a production, change
/// its crew; that keeps the clash checks and tells the people affected.
/// </summary>
public class DailyRosterLine
{
    public Guid Id { get; set; }

    public Guid DailyRosterId { get; set; }

    /// <summary>The production this line replaces on the sheet. Empty for a line the Booking Officer added.</summary>
    public Guid? BookingId { get; set; }

    /// <summary>True to leave this production off the sheet.</summary>
    public bool Hidden { get; set; }

    public string Area { get; set; } = "";
    public DateTimeOffset? StartsAt { get; set; }
    public DateTimeOffset? EndsAt { get; set; }
    public string Title { get; set; } = "";

    /// <summary>What else the sheet says: where it is, who to phone.</summary>
    public string? Detail { get; set; }

    public List<DailyRosterCrew> Crew { get; set; } = [];
}

/// <summary>One name in a trade's column on an edited line.</summary>
public class DailyRosterCrew
{
    public Guid Id { get; set; }

    public Guid DailyRosterLineId { get; set; }

    /// <summary>The trade's column: Camera, Sound…</summary>
    public string Division { get; set; } = "";

    public string Name { get; set; } = "";
}

/// <summary>One row of the sheet as people see it, whether generated or edited.</summary>
/// <param name="LineId">The saved line, if the Booking Officer changed this row or added it.</param>
/// <param name="Rostered">True if this row came from the monthly roster rather than a production.</param>
public record DailyLine(
    Guid? LineId,
    Guid? BookingId,
    string Area,
    DateTimeOffset? StartsAt,
    DateTimeOffset? EndsAt,
    string Title,
    string? Detail,
    List<DailyCrewMember> Crew,
    bool Rostered = false);

public record DailyCrewMember(string Division, string Name);

/// <summary>A production on this day that isn't confirmed yet, with the positions it still needs.</summary>
public record UndecidedBooking(Guid BookingId, int ProductionNo, string Title, DateTimeOffset StartsAt, BookingStatus Status, List<string> OpenPositions);

/// <summary>The sheet for one day.</summary>
public record DailyRosterView(DateOnly Date, string? Announcement, List<DailyLine> Lines, List<UndecidedBooking> Undecided);

/// <summary>Where things go on the sheet. The rows follow the board's roster-rows.json.</summary>
public static class DailyRosterRules
{
    /// <summary>Printed for a studio with nothing in it, so a blank line can't read as a printing fault.</summary>
    public const string NobodyOn = "Nobody on today";

    public const string OnShiftArea = "On shift";
    public const string AwayArea = "Away";
    public const string OnDutyTitle = "On duty";

    /// <summary>The row a booking goes on. Numbered rows count up through the day.</summary>
    public static string AreaOf(Booking booking, int number) => booking switch
    {
        { IsTrip: true } => $"Trip {number}",
        { Venue: Venue.Location } => $"Production {number}",
        { Venue: Venue.FlyAway } => $"FlyAway {number}",
        { Venue: Venue.Ob } => "OB",
        { Venue: Venue.App } => "APP",
        { Venue: Venue.Streaming } => "Streaming",
        _ => StudioName(booking.Venue)
    };

    public static string StudioName(Venue studio) => $"Studio {studio.ToString()[^1]}";

    /// <summary>The order of areas down the sheet: studios first, as the floor expects.</summary>
    public static int Rank(string area)
    {
        string[] order = ["Studio", "Production", "FlyAway", "OB", "Trip", "Streaming", "APP", OnShiftArea, AwayArea];
        var rank = Array.FindIndex(order, area.StartsWith);
        return rank < 0 ? order.Length : rank;
    }
}
