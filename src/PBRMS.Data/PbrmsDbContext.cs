using Microsoft.EntityFrameworkCore;
using PBRMS.Domain;

namespace PBRMS.Data;

/// <summary>
/// The PBRMS database (SQL Server). Every table is listed here, and the rules
/// that are not obvious from the classes (lengths, unique keys, relationships)
/// are set in <see cref="OnModelCreating"/>.
/// To change the schema: edit the classes in PBRMS.Domain, then add a migration
/// (see README.md).
/// </summary>
public class PbrmsDbContext(DbContextOptions<PbrmsDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<OrgUnit> OrgUnits => Set<OrgUnit>();
    public DbSet<RoleAssignment> RoleAssignments => Set<RoleAssignment>();

    protected override void ConfigureConventions(ModelConfigurationBuilder conventions)
    {
        // Store enums as text ("Supervisor") rather than numbers (3), so the data
        // can be read and reported on (e.g. Power BI) without the code.
        conventions.Properties<Enum>().HaveConversion<string>().HaveMaxLength(40);
    }

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<User>(user =>
        {
            user.Property(u => u.FirstName).HasMaxLength(80);
            user.Property(u => u.Surname).HasMaxLength(80);
            user.Property(u => u.WorkEmail).HasMaxLength(160);
            user.Property(u => u.PersonalEmail).HasMaxLength(160);
            user.Property(u => u.Phone).HasMaxLength(20);

            // One PBRMS user per NBC email: sign-in depends on it.
            user.HasIndex(u => u.WorkEmail).IsUnique();
        });

        model.Entity<OrgUnit>(unit =>
        {
            unit.Property(u => u.Name).HasMaxLength(80);

            // Restrict: a unit that still has units inside it cannot be deleted.
            unit.HasOne(u => u.Parent)
                .WithMany()
                .HasForeignKey(u => u.ParentId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        model.Entity<RoleAssignment>(assignment =>
        {
            assignment.HasOne(a => a.User)
                .WithMany(u => u.Roles)
                .HasForeignKey(a => a.UserId);

            // Restrict: a unit people still hold roles in cannot be deleted.
            assignment.HasOne(a => a.OrgUnit)
                .WithMany()
                .HasForeignKey(a => a.OrgUnitId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
}
