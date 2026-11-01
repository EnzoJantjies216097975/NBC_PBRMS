using System.Net.Mail;
using Microsoft.EntityFrameworkCore;
using PBRMS.Domain;

namespace PBRMS.Data;

/// <summary>
/// Sets up the first Admin on a new server. Only an Admin can add people, so the first one
/// can't come from the People screen. This works only while NBC has no active Admin, so it
/// can never be used to give someone access later: run from the server, it needs the
/// database rights an installer has. Everyone else is then added under People.
/// </summary>
public static class FirstAdmin
{
    /// <summary>Adds them, or says in words why not.</summary>
    public static async Task<string?> AddAsync(PbrmsDbContext db, string email, string firstName, string surname)
    {
        var address = User.NormaliseEmail(email);
        if (!MailAddress.TryCreate(address, out var parsed) || parsed.Address != address)
            return $"\"{email}\" isn't an email address.";

        if (await db.RoleAssignments.AnyAsync(r => r.Role == Role.Admin && r.User.IsActive))
            return "NBC already has an Admin. Add people under People in the web app.";

        if (await db.Users.AnyAsync(u => u.WorkEmail == address))
            return $"{address} is already on file. Give them the Admin role directly in the database, or use another email.";

        db.Users.Add(new User
        {
            FirstName = firstName.Trim(),
            Surname = surname.Trim(),
            WorkEmail = address,
            EmploymentType = EmploymentType.Permanent,
            Roles = [new RoleAssignment { Role = Role.Admin }]
        });

        await db.SaveChangesAsync();
        return null;
    }
}
