namespace PBRMS.Domain;

/// <summary>
/// A person who uses PBRMS or appears on its rosters: producers, supervisors,
/// operators, the booking officer, management.
/// People sign in with their NBC work email, so <see cref="WorkEmail"/> is how a
/// sign-in is matched to a user. Nobody is deleted: leavers get
/// <see cref="IsActive"/> = false and keep their history.
/// </summary>
/// <remarks>
/// The schema document also lists date of birth and home address. They are left out
/// until NBC confirms it may collect them (DPA 2024; see "NBC IT input — data").
/// Add them here when that is agreed.
/// </remarks>
public class User
{
    public Guid Id { get; set; }
    public string FirstName { get; set; } = "";
    public string Surname { get; set; } = "";

    /// <summary>NBC work email, the sign-in identifier. Always stored lower-case.</summary>
    public string WorkEmail { get; set; } = "";

    public string? PersonalEmail { get; set; }
    public string? Phone { get; set; }
    public EmploymentType EmploymentType { get; set; }

    /// <summary>Has no smartphone, so notifications go by SMS instead of push (BR-15).</summary>
    public bool SmsOnly { get; set; }

    /// <summary>False once the person has left. They can no longer sign in.</summary>
    public bool IsActive { get; set; } = true;

    public List<RoleAssignment> Roles { get; set; } = [];

    /// <summary>
    /// Puts an email in the one form we store and compare, so "Jane.Doe@NBC.na "
    /// and "jane.doe@nbc.na" are the same person.
    /// </summary>
    public static string NormaliseEmail(string email) => email.Trim().ToLowerInvariant();
}

/// <summary>How a person is employed at NBC (from the schema document).</summary>
public enum EmploymentType
{
    Permanent,
    Contract,
    Freelance,
    Apprentice,
    Intern
}
