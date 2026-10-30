namespace PBRMS.Domain;

/// <summary>
/// A department or division at NBC, e.g. "News" (a content department) or
/// "Camera" (a TV Ops division). Units can sit inside other units, so Content Hub
/// can hold its sub-departments.
/// Roles are scoped to a unit: a Supervisor of Sound only manages Sound.
/// </summary>
public class OrgUnit
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public OrgUnitKind Kind { get; set; }

    /// <summary>The unit this one sits inside, or null for a top-level unit.</summary>
    public Guid? ParentId { get; set; }
    public OrgUnit? Parent { get; set; }
}

/// <summary>What sort of unit an <see cref="OrgUnit"/> is.</summary>
public enum OrgUnitKind
{
    /// <summary>A department that makes programmes and books productions (News, Sports…).</summary>
    ContentDepartment,

    /// <summary>A part of a content department (e.g. a desk inside Content Hub).</summary>
    SubDepartment,

    /// <summary>A TV Operations crew division that supplies operators (Camera, Sound…).</summary>
    TvOpsDivision
}
