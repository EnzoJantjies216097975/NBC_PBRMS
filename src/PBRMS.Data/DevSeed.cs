using Microsoft.EntityFrameworkCore;
using PBRMS.Domain;

namespace PBRMS.Data;

/// <summary>
/// Sample data for development and tests, so there is someone to sign in as.
/// Runs only in Development, never on NBC's servers.
///
/// The people are made up on purpose: real staff names, phone numbers and
/// addresses must not live on a development machine or in the code (the board's
/// SRS, NFR-SEC-11). Emails use ".test", a domain reserved for testing that can
/// never reach a real inbox.
/// </summary>
public static class DevSeed
{
    /// <summary>Email of the sample user who has left NBC (inactive), for testing refusals.</summary>
    public const string FormerStaffEmail = "former.staff@pbrms.test";

    /// <summary>Adds the sample data if the database has no users yet. Safe to run every start.</summary>
    public static async Task RunAsync(PbrmsDbContext db)
    {
        if (await db.Users.AnyAsync())
            return;

        // A few real NBC unit names, enough to scope roles. The full list will be
        // imported from the Production Board's reference data later.
        var news = Unit("News", OrgUnitKind.ContentDepartment);
        var sports = Unit("Sports", OrgUnitKind.ContentDepartment);
        var camera = Unit("Camera", OrgUnitKind.TvOpsDivision);
        var sound = Unit("Sound", OrgUnitKind.TvOpsDivision);
        db.OrgUnits.AddRange(news, sports, camera, sound);

        // The News approval chain from producer to supervisors, plus people in other
        // units to check that roles only reach their own unit.
        // Sign in as any of them with POST /auth/dev-login.
        db.Users.AddRange(
            Person("Test", "Producer", "producer@pbrms.test", (Role.Producer, news)),
            Person("Test", "ProductionManager", "pm@pbrms.test", (Role.ProductionManager, news)),
            Person("Test", "ExecutiveProducer", "ep@pbrms.test", (Role.ExecutiveProducer, news)),
            Person("Test", "Supervisor", "supervisor@pbrms.test", (Role.Supervisor, camera), (Role.Operator, camera)),
            Person("Test", "SoundSupervisor", "supervisor.sound@pbrms.test", (Role.Supervisor, sound)),
            Person("Test", "Operator", "operator@pbrms.test", (Role.Operator, sound)),
            Person("Test", "CameraOperator", "camera@pbrms.test", (Role.Operator, camera)),
            Person("Test", "SportsProducer", "producer.sports@pbrms.test", (Role.Producer, sports)),

            // NBC-wide roles: not tied to one unit.
            Person("Test", "BookingOfficer", "booking@pbrms.test", (Role.BookingOfficer, null)),
            Person("Test", "Admin", "admin@pbrms.test", (Role.Admin, null)),
            Person("Test", "Management", "management@pbrms.test", (Role.Management, null)));

        var former = Person("Former", "Staff", FormerStaffEmail, (Role.Operator, camera));
        former.IsActive = false;
        db.Users.Add(former);

        await db.SaveChangesAsync();
    }

    private static OrgUnit Unit(string name, OrgUnitKind kind) => new() { Name = name, Kind = kind };

    private static User Person(string first, string surname, string email, params (Role Role, OrgUnit? Unit)[] roles) => new()
    {
        FirstName = first,
        Surname = surname,
        WorkEmail = User.NormaliseEmail(email),
        EmploymentType = EmploymentType.Permanent,
        Roles = roles.Select(r => new RoleAssignment { Role = r.Role, OrgUnit = r.Unit }).ToList()
    };
}
