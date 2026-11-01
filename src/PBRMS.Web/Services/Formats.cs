using PBRMS.Domain;

namespace PBRMS.Web.Services;

/// <summary>How the screens write things, the way NBC does: 24-hour times like 06h30, and dates dd/mm/yyyy.</summary>
public static class Formats
{
    public static string When(DateTimeOffset moment) => NbcTime.Text(moment);

    public static string Clock(DateTimeOffset moment) => moment.ToOffset(NbcTime.Offset).ToString("HH'h'mm");

    public static string Hours(DateTimeOffset? start, DateTimeOffset? end) =>
        start is { } from && end is { } to ? $"{Clock(from)}–{Clock(to)}" : "";

    /// <summary>A date and time picked on screen, taken as Windhoek time.</summary>
    public static DateTimeOffset Windhoek(DateTime picked) => new(picked, NbcTime.Offset);

    /// <summary>A venue in words: Studio 1, not Studio1.</summary>
    public static string Venue(Venue venue) => venue switch
    {
        Domain.Venue.FlyAway => "Fly-away",
        Domain.Venue.Ob => "OB",
        Domain.Venue.App => "APP",
        _ when ClashRules.IsStudio(venue) => DailyRosterRules.StudioName(venue),
        _ => venue.ToString()
    };

    /// <summary>A shift's hours, like 05h00–14h00 and 13h00–22h00, or nothing if it has none.</summary>
    public static string ShiftHours(ShiftCode code)
    {
        static string Pair(TimeOnly? start, TimeOnly? end) =>
            start is { } from && end is { } to ? $"{from:HH'h'mm}–{to:HH'h'mm}" : "";

        return string.Join(" and ", new[] { Pair(code.Start, code.End), Pair(code.SecondStart, code.SecondEnd) }.Where(p => p.Length > 0));
    }

    /// <summary>What a kind of shift code means, in words.</summary>
    public static string ShiftKindName(ShiftKind kind) => kind switch
    {
        ShiftKind.Off => "Day off, can be called in",
        ShiftKind.Leave => "Leave, can't be booked",
        ShiftKind.Away => "Out of town",
        _ => "On duty"
    };

    /// <summary>A time today, or with its date if it isn't today.</summary>
    public static string Until(DateTimeOffset moment) =>
        NbcTime.DateOf(moment) == NbcTime.DateOf(DateTimeOffset.UtcNow) ? Clock(moment) : When(moment);

    /// <summary>A status in words.</summary>
    public static string Status(BookingStatus status) => status switch
    {
        BookingStatus.EpReview => "With the EP",
        BookingStatus.CrewAssignment => "Needs crew",
        BookingStatus.Finalisation => "Ready to confirm",
        BookingStatus.InProgress => "In progress",
        _ => status.ToString()
    };

    /// <summary>The badge colour for a status. Colour is never alone: the badge also says it in words.</summary>
    public static string StatusClass(BookingStatus status) => status switch
    {
        BookingStatus.Confirmed or BookingStatus.InProgress or BookingStatus.Completed => "badge good",
        BookingStatus.Cancelled => "badge bad",
        BookingStatus.CrewAssignment or BookingStatus.Finalisation => "badge wait",
        _ => "badge"
    };

    /// <summary>Who has to do what about a clash, in words.</summary>
    public static string Clash(Clash clash)
    {
        var production = clash.ProductionNo is { } number ? $"#{number} " : "";
        return $"{clash.Who}: {production}{clash.Title} ({Hours(clash.StartsAt, clash.EndsAt)})";
    }
}
