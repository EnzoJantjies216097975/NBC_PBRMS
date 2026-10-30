namespace PBRMS.Domain;

/// <summary>
/// One division's roster for one month: the grid of operators by days that used to be
/// pinned on the wall.
///
/// Rosters are versioned. A supervisor builds version 1 as a draft and publishes it.
/// Changing a published roster starts a new draft (version 2), copied from it, and
/// version 1 stays in force until version 2 is published. Old versions are kept
/// (Backend Schema Document, "Audit and deletion").
/// </summary>
public class MonthlyRoster
{
    public Guid Id { get; set; }

    public Guid DivisionId { get; set; }
    public OrgUnit Division { get; set; } = null!;

    /// <summary>The first day of the month.</summary>
    public DateOnly Month { get; set; }

    public int Version { get; set; }
    public RosterStatus Status { get; set; } = RosterStatus.Draft;

    public DateTimeOffset UpdatedAt { get; set; }

    public DateTimeOffset? PublishedAt { get; set; }
    public Guid? PublishedById { get; set; }
    public User? PublishedBy { get; set; }

    public List<RosterEntry> Entries { get; set; } = [];

    /// <summary>
    /// Changed by SQL Server on every save, so if two people edit the same draft at
    /// once, the second save is refused instead of silently overwriting the first.
    /// </summary>
    public byte[] RowVersion { get; set; } = [];
}

/// <summary>Whether a roster version is still being built.</summary>
public enum RosterStatus
{
    Draft,

    /// <summary>Visible to everyone and counted by the clash check.</summary>
    Published
}

/// <summary>
/// One cell of the grid: what one operator is doing on one day. There is no entry for a
/// blank cell, and a blank cell means "not known yet", never "off".
/// </summary>
public class RosterEntry
{
    public Guid Id { get; set; }

    public Guid MonthlyRosterId { get; set; }
    public MonthlyRoster Roster { get; set; } = null!;

    public Guid OperatorId { get; set; }
    public User Operator { get; set; } = null!;

    public DateOnly Date { get; set; }

    /// <summary>
    /// The code from the division's legend. It is text, not a link, because one cell can
    /// hold two codes joined by "+", such as "N+ST2" (see <see cref="RosterRules.SplitCodes"/>).
    /// </summary>
    public string ShiftCode { get; set; } = "";
}
