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
    public DbSet<Booking> Bookings => Set<Booking>();
    public DbSet<CrewSlot> CrewSlots => Set<CrewSlot>();
    public DbSet<BookingStatusChange> BookingStatusChanges => Set<BookingStatusChange>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // Store enums as text ("Supervisor") rather than numbers (3), so the data
        // can be read and reported on (e.g. Power BI) without the code.
        configurationBuilder.Properties<Enum>().HaveConversion<string>().HaveMaxLength(40);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>(user =>
        {
            user.Property(u => u.FirstName).HasMaxLength(80);
            user.Property(u => u.Surname).HasMaxLength(80);
            user.Property(u => u.WorkEmail).HasMaxLength(160);
            user.Property(u => u.PersonalEmail).HasMaxLength(160);
            user.Property(u => u.Phone).HasMaxLength(20);

            // One PBRMS user per NBC email: sign-in depends on it.
            user.HasIndex(u => u.WorkEmail).IsUnique();
        });

        modelBuilder.Entity<OrgUnit>(unit =>
        {
            unit.Property(u => u.Name).HasMaxLength(80);

            // Restrict: a unit that still has units inside it cannot be deleted.
            unit.HasOne(u => u.Parent)
                .WithMany()
                .HasForeignKey(u => u.ParentId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<RoleAssignment>(assignment =>
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

        // Production numbers come from a SQL Server sequence, so two bookings saved at
        // the same moment can never get the same number.
        modelBuilder.HasSequence<int>("ProductionNumbers");

        modelBuilder.Entity<Booking>(booking =>
        {
            booking.Property(b => b.ProductionNo).HasDefaultValueSql("NEXT VALUE FOR ProductionNumbers");
            booking.HasIndex(b => b.ProductionNo).IsUnique();

            booking.Property(b => b.Title).HasMaxLength(160);
            booking.Property(b => b.VenueText).HasMaxLength(160);
            booking.Property(b => b.AllocationCode).HasMaxLength(30);
            booking.Property(b => b.Notes).HasMaxLength(2000);
            booking.Property(b => b.RowVersion).IsRowVersion();

            // Restrict: departments and producers with bookings cannot be deleted.
            booking.HasOne(b => b.Department).WithMany().HasForeignKey(b => b.DepartmentId).OnDelete(DeleteBehavior.Restrict);
            booking.HasOne(b => b.Producer).WithMany().HasForeignKey(b => b.ProducerId).OnDelete(DeleteBehavior.Restrict);

            // For the approval queues and the day plan (Backend Schema Document, "Key indexes").
            booking.HasIndex(b => new { b.Status, b.DepartmentId });
            booking.HasIndex(b => new { b.Venue, b.StartsAt });
        });

        modelBuilder.Entity<CrewSlot>(slot =>
        {
            slot.Property(s => s.PositionLabel).HasMaxLength(60);
            slot.Property(s => s.SpecialEquipment).HasMaxLength(200);

            // Slots belong to their booking. While a booking is a draft the producer can
            // remove a slot, and the row goes with it.
            slot.HasOne<Booking>().WithMany(b => b.CrewSlots).HasForeignKey(s => s.BookingId);
            slot.HasOne(s => s.Division).WithMany().HasForeignKey(s => s.DivisionId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<BookingStatusChange>(change =>
        {
            change.Property(c => c.Note).HasMaxLength(1000);
            change.HasOne<Booking>().WithMany(b => b.History).HasForeignKey(c => c.BookingId);
            change.HasOne(c => c.Actor).WithMany().HasForeignKey(c => c.ActorId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
