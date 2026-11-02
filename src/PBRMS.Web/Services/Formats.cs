using PBRMS.Domain;

namespace PBRMS.Web.Services;

/// <summary>How the screens write things, the way NBC does: 24-hour times like 06h30, and dates dd/mm/yyyy.</summary>
public static class Formats
{
    public static string When(DateTimeOffset moment) => NbcTime.Text(moment);

    public static string Clock(DateTimeOffset moment) => moment.ToOffset(NbcTime.Offset).ToString("HH'h'mm");

    public static string Hours(DateTimeOffset? start, DateTimeOffset? end) =>
        start is { } from && end is { } to ? $"{Clock(from)}–{Clock(to)}" : "";

    /// <summary>When something runs. The end only gets its date if it's another day, as on a multi-day trip.</summary>
    public static string Period(DateTimeOffset start, DateTimeOffset end) =>
        $"{When(start)} to {(NbcTime.DateOf(start) == NbcTime.DateOf(end) ? Clock(end) : When(end))}";

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

    /// <summary>A length of time, the way NBC writes hours: 90 minutes is 1h30.</summary>
    public static string Duration(int minutes) => $"{minutes / 60}h{minutes % 60:00}";

    /// <summary>A role in words. Nothing if there's no role, as when the monthly roster filled a position.</summary>
    public static string RoleName(Role? role) => role switch
    {
        null => "",
        Role.ExecutiveProducer => "Executive Producer",
        Role.BookingOfficer => "Booking Officer",
        Role.AppOperator => "APP operator",
        Role.TvOperationsManager => "TV Operations Manager",
        _ => role.ToString()!
    };

    /// <summary>What sort of unit something is, in words.</summary>
    public static string UnitKind(OrgUnitKind kind) => kind switch
    {
        OrgUnitKind.ContentDepartment => "Department",
        OrgUnitKind.SubDepartment => "Part of a department",
        _ => "TV Ops division"
    };

    /// <summary>Why work is on the off-roster report, in words.</summary>
    public static string OffRosterKind(ClashKind kind) => kind switch
    {
        ClashKind.CallIn => "Called in on a day off",
        ClashKind.OutsideShift => "Outside their shift",
        _ => kind.ToString()
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

    /// <summary>A date the way NBC writes it.</summary>
    public static string Date(DateOnly date) => date.ToString("dd/MM/yyyy");

    /// <summary>Why someone is sitting out the trip rotation, in words.</summary>
    public static string SitOutReason(TripSitOutReason reason) => reason switch
    {
        TripSitOutReason.Family => "Family",
        TripSitOutReason.School => "School obligations",
        TripSitOutReason.Other => "Other commitment",
        _ => reason.ToString()
    };

    /// <summary>Where someone stands in the rotation, or why they aren't in the queue.</summary>
    public static string TripStatus(TripStanding person)
    {
        if (person.NextTrip is { } next)
            return $"On a trip from {Date(next)}";
        if (person.SittingOut is { } reason)
            return person.SittingOutUntil is { } until ? $"Sitting out until {Date(until)}: {SitOutReason(reason)}" : $"Sitting out: {SitOutReason(reason)}";

        return person.Trips == 0 ? "No trips yet"
            : person.LastBack is { } back ? $"{TripCount(person.Trips)}, last back {Date(back)}"
            : TripCount(person.Trips);
    }

    /// <summary>One person in the crew picker on a trip, with their turn so the fair choice is the obvious one.</summary>
    public static string TripChoice(TripStanding person)
    {
        var turn = person.Place is { } place ? $"{place}. " : "";
        return $"{turn}{person.Name} ({TripStatus(person)})";
    }

    public static string TripCount(int trips) => trips == 1 ? "1 trip" : $"{trips} trips";

    /// <summary>Who has to do what about a clash, in words.</summary>
    public static string Clash(Clash clash)
    {
        if (clash.Kind == ClashKind.TripTurn)
            return $"{clash.Who} is ahead in the trip order ({clash.Title})";

        var production = clash.ProductionNo is { } number ? $"#{number} " : "";
        return $"{clash.Who}: {production}{clash.Title} ({Hours(clash.StartsAt, clash.EndsAt)})";
    }
}
