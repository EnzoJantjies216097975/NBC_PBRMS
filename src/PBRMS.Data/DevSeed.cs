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

    /// <summary>Adds whatever sample data is missing, keeping what's there. Safe to run every start.</summary>
    public static async Task RunAsync(PbrmsDbContext db)
    {
        await AddPeopleAsync(db);
        await AddOfficePeopleAsync(db);
        await AddLegendsAsync(db);
    }

    /// <summary>
    /// A small legend for each crew division, in the style of the printed ones. The real
    /// legends will be imported from the Production Board later.
    /// </summary>
    private static async Task AddLegendsAsync(PbrmsDbContext db)
    {
        if (await db.ShiftDefinitions.AnyAsync())
            return;

        var divisions = await db.OrgUnits.Where(u => u.Kind == OrgUnitKind.TvOpsDivision).ToListAsync();
        foreach (var division in divisions)
        {
            db.ShiftDefinitions.AddRange(
                new ShiftDefinition { Division = division, Code = "O", Label = "Off", Kind = ShiftKind.Off },
                new ShiftDefinition { Division = division, Code = "L", Label = "Leave", Kind = ShiftKind.Leave },
                new ShiftDefinition { Division = division, Code = "TR", Label = "Trip, out of town", Kind = ShiftKind.Away },
                new ShiftDefinition { Division = division, Code = "OB", Label = "Outside broadcast", Kind = ShiftKind.Worked },
                new ShiftDefinition { Division = division, Code = "A1", Label = "Any production", Kind = ShiftKind.Worked, Start = new(8, 0), End = new(17, 0) },
                new ShiftDefinition { Division = division, Code = "ST1", Label = "Studio 1", Kind = ShiftKind.Worked, Studio = Venue.Studio1, Start = new(13, 0), End = new(21, 0) },
                new ShiftDefinition
                {
                    Division = division, Code = "ST2", Label = "Studio 2", Kind = ShiftKind.Worked, Studio = Venue.Studio2,
                    Start = new(5, 0), End = new(14, 0), SecondStart = new(13, 0), SecondEnd = new(22, 0)
                },
                new ShiftDefinition { Division = division, Code = "N", Label = "Night", Kind = ShiftKind.Worked, Start = new(19, 0), End = new(2, 0) });
        }

        await db.SaveChangesAsync();
    }

    /// <summary>People added after the first sample set, so they reach existing development databases too.</summary>
    private static async Task AddOfficePeopleAsync(PbrmsDbContext db)
    {
        var sound = await db.OrgUnits.FirstOrDefaultAsync(u => u.Name == "Sound");

        User[] people =
        [
            Person("Test", "TvOperationsManager", "tvops.manager@pbrms.test", (Role.TvOperationsManager, null)),
            Person("Test", "Editor", "editor@pbrms.test", (Role.Editor, null)),
            Person("Test", "AppOperator", "app@pbrms.test", (Role.AppOperator, sound))
        ];

        foreach (var person in people)
        {
            if (!await db.Users.AnyAsync(u => u.WorkEmail == person.WorkEmail))
                db.Users.Add(person);
        }

        await db.SaveChangesAsync();
    }

    private static async Task AddPeopleAsync(PbrmsDbContext db)
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
