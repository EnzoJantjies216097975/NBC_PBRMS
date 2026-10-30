namespace PBRMS.Domain;

/// <summary>
/// Gives a user one <see cref="Role"/> within one org unit, e.g.
/// "Supervisor of Camera". Endpoints check these on the server to decide what the
/// user may see and change (TRD TR-06).
/// </summary>
public class RoleAssignment
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }
    public User User { get; set; } = null!;

    public Role Role { get; set; }

    /// <summary>
    /// The unit this role applies to. Null means the whole of NBC, used for roles
    /// that are not tied to one department, such as Admin or Management.
    /// </summary>
    public Guid? OrgUnitId { get; set; }
    public OrgUnit? OrgUnit { get; set; }

    /// <summary>
    /// The operator may only work in the studio and is never put on trips (BR-09).
    /// </summary>
    public bool StudioBound { get; set; }
}
