using PBRMS.Domain;

namespace PBRMS.Web.Services;

/// <summary>What the API said: it worked, or why not, with any clashes and warnings to show.</summary>
public record ApiResult(bool Ok, string? Error, List<Clash> Clashes, bool NeedsAcknowledgement)
{
    public static readonly ApiResult Success = new(true, null, [], false);
}

/// <summary>The same, with the data that came back.</summary>
public record ApiResult<T>(T? Value, bool Ok, string? Error, List<Clash> Clashes, bool NeedsAcknowledgement)
{
    public static ApiResult<T> From(T? value) => new(value, true, null, [], false);

    public static ApiResult<T> From(ApiResult problem) =>
        new(default, false, problem.Error, problem.Clashes, problem.NeedsAcknowledgement);
}

// Shapes of the API's smaller answers, with only what these screens use.

public record MeRole(Role Role, string? Unit);

public record MeResponse(Guid Id, string FirstName, string Surname, string WorkEmail, List<MeRole> Roles)
{
    public string Name => $"{FirstName} {Surname}";
}

public record EarliestDate(DateOnly Date);

public record OrgUnitRow(Guid Id, string Name, OrgUnitKind Kind);

public record OperatorRow(Guid Id, string FirstName, string Surname)
{
    public string Name => $"{FirstName} {Surname}";
}

public record NotificationRow(Guid Id, string Text, Guid? BookingId, DateTimeOffset CreatedAt, DateTimeOffset? ReadAt);

/// <summary>One cell of the monthly roster. An empty code blanks it.</summary>
public record RosterCell(Guid OperatorId, DateOnly Date, string? Code);

public record RosterView(Guid DivisionId, string Month, int Version, RosterStatus? Status, List<RosterCell> Entries);

public record RosterPublished(RosterView Roster, List<Clash> Clashes);

/// <summary>A shift code on a division's legend, as the API sends it and takes it back.</summary>
public record ShiftCode(
    Guid Id, string Code, string Label, ShiftKind Kind,
    TimeOnly? Start, TimeOnly? End, TimeOnly? SecondStart, TimeOnly? SecondEnd,
    string? Colour, bool IsActive, Venue? Studio);

/// <summary>A row of the daily roster as the Booking Officer wants it.</summary>
public record DailyLineForm(
    Guid? Id, Guid? BookingId, bool Hidden, string Area, DateTimeOffset? StartsAt, DateTimeOffset? EndsAt,
    string Title, string? Detail, List<DailyCrewMember> Crew);

public record AppOperatorRow(Guid Id, string Name);

public record AppBusyRow(DateTimeOffset StartsAt, DateTimeOffset EndsAt, string Operator);

public record AppRoomStatus(bool Busy, string? Production, string? BookedBy, string? Operator, DateTimeOffset? Until, DateTimeOffset? NextStartsAt);

public record AppSessionRow(
    Guid Id, string Production, string Requester, string Operator, DateTimeOffset StartsAt, DateTimeOffset EndsAt,
    AppSessionStatus Status, string? Note, DateTimeOffset? SuggestedStartsAt, DateTimeOffset? SuggestedEndsAt, bool IsWalkIn);
