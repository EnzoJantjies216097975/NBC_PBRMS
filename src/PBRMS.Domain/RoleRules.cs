namespace PBRMS.Domain;

/// <summary>Where a role can be held. Everything that reads roles relies on this being right.</summary>
public enum RoleScope
{
    /// <summary>Covers all of NBC, so it has no unit: the Booking Officer, Admin and the managers.</summary>
    WholeOfNbc,

    /// <summary>Held in one TV Ops division: a supervisor manages only that division's operators.</summary>
    Division,

    /// <summary>Held in one content department, which decides whose bookings it sees.</summary>
    Department,

    /// <summary>Either a content department, or all of NBC: editors can work for anyone.</summary>
    DepartmentOrWholeOfNbc
}

/// <summary>Which roles go where, for the Admin screens and the API that checks them.</summary>
public static class RoleRules
{
    public static RoleScope ScopeOf(Role role) => role switch
    {
        Role.Operator or Role.Supervisor or Role.AppOperator => RoleScope.Division,
        Role.Producer or Role.ExecutiveProducer => RoleScope.Department,
        Role.Editor => RoleScope.DepartmentOrWholeOfNbc,
        _ => RoleScope.WholeOfNbc
    };

    /// <summary>True if this kind of unit suits the role. A null kind means no unit: all of NBC.</summary>
    public static bool Fits(Role role, OrgUnitKind? kind)
    {
        var isDepartment = kind is OrgUnitKind.ContentDepartment or OrgUnitKind.SubDepartment;

        return ScopeOf(role) switch
        {
            RoleScope.Division => kind == OrgUnitKind.TvOpsDivision,
            RoleScope.Department => isDepartment,
            RoleScope.DepartmentOrWholeOfNbc => isDepartment || kind is null,
            _ => kind is null
        };
    }
}
