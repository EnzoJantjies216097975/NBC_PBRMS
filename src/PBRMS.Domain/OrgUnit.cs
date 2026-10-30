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

    /// <summary>
    /// The unit and every unit above it, e.g. a Content Hub sub-department plus
    /// Content Hub itself. Someone with a role in any of these units covers this unit.
    /// </summary>
    /// <param name="allUnits">Every org unit. NBC has a few dozen, so they are loaded together.</param>
    public static HashSet<Guid> ChainUp(Guid unitId, IReadOnlyCollection<OrgUnit> allUnits)
    {
        var chain = new HashSet<Guid>();
        Guid? current = unitId;

        // chain.Add returns false if we've seen the unit before, which also stops a
        // mistakenly circular parent link from looping for ever.
        while (current is Guid id && chain.Add(id))
            current = allUnits.FirstOrDefault(u => u.Id == id)?.ParentId;

        return chain;
    }
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
