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
    public DbSet<CrewAssignment> CrewAssignments => Set<CrewAssignment>();
    public DbSet<BookingStatusChange> BookingStatusChanges => Set<BookingStatusChange>();
    public DbSet<ShiftDefinition> ShiftDefinitions => Set<ShiftDefinition>();
    public DbSet<MonthlyRoster> MonthlyRosters => Set<MonthlyRoster>();
    public DbSet<RosterEntry> RosterEntries => Set<RosterEntry>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<OffRosterWork> OffRosterWork => Set<OffRosterWork>();

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

            // A computed shortcut (the assignment now in force), not a column.
            slot.Ignore(s => s.Current);

            // Slots belong to their booking. While a booking is a draft the producer can
            // remove a slot, and the row goes with it.
            slot.HasOne(s => s.Booking).WithMany(b => b.CrewSlots).HasForeignKey(s => s.BookingId);
            slot.HasOne(s => s.Division).WithMany().HasForeignKey(s => s.DivisionId).OnDelete(DeleteBehavior.Restrict);

            // For the clash check.
            slot.HasIndex(s => new { s.StartsAt, s.EndsAt });
        });

        modelBuilder.Entity<BookingStatusChange>(change =>
        {
            change.Property(c => c.Note).HasMaxLength(1000);
            change.HasOne<Booking>().WithMany(b => b.History).HasForeignKey(c => c.BookingId);
            change.HasOne(c => c.Actor).WithMany().HasForeignKey(c => c.ActorId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<CrewAssignment>(assignment =>
        {
            // Goes with its slot if the producer removes the slot from a draft. Otherwise
            // assignments are never deleted, only marked removed.
            assignment.HasOne(a => a.CrewSlot).WithMany(s => s.Assignments).HasForeignKey(a => a.CrewSlotId);

            assignment.HasOne(a => a.Operator).WithMany().HasForeignKey(a => a.OperatorId).OnDelete(DeleteBehavior.Restrict);
            assignment.HasOne(a => a.AssignedBy).WithMany().HasForeignKey(a => a.AssignedById).OnDelete(DeleteBehavior.Restrict);

            // At most one current assignment per slot. A database rule, so it holds even
            // if two people fill the same slot at the same moment.
            assignment.HasIndex(a => a.CrewSlotId).IsUnique().HasFilter("[RemovedAt] IS NULL");

            // For the clash check (Backend Schema Document, "Key indexes").
            assignment.HasIndex(a => a.OperatorId);
        });

        modelBuilder.Entity<ShiftDefinition>(shift =>
        {
            shift.Property(s => s.Code).HasMaxLength(12);
            shift.Property(s => s.Label).HasMaxLength(80);
            shift.Property(s => s.Colour).HasMaxLength(9);
            shift.HasOne(s => s.Division).WithMany().HasForeignKey(s => s.DivisionId).OnDelete(DeleteBehavior.Restrict);

            // A code means one thing within a division. Different divisions may reuse it.
            shift.HasIndex(s => new { s.DivisionId, s.Code }).IsUnique();
        });

        modelBuilder.Entity<MonthlyRoster>(roster =>
        {
            roster.Property(r => r.RowVersion).IsRowVersion();
            roster.HasOne(r => r.Division).WithMany().HasForeignKey(r => r.DivisionId).OnDelete(DeleteBehavior.Restrict);
            roster.HasOne(r => r.PublishedBy).WithMany().HasForeignKey(r => r.PublishedById).OnDelete(DeleteBehavior.Restrict);

            // Also stops two people starting the same new version at the same moment.
            roster.HasIndex(r => new { r.DivisionId, r.Month, r.Version }).IsUnique();
        });

        modelBuilder.Entity<RosterEntry>(entry =>
        {
            entry.Property(e => e.ShiftCode).HasMaxLength(30);
            entry.HasOne(e => e.Roster).WithMany(r => r.Entries).HasForeignKey(e => e.MonthlyRosterId);
            entry.HasOne(e => e.Operator).WithMany().HasForeignKey(e => e.OperatorId).OnDelete(DeleteBehavior.Restrict);

            // One cell per operator per day in each roster version.
            entry.HasIndex(e => new { e.MonthlyRosterId, e.OperatorId, e.Date }).IsUnique();

            // For schedules and the clash check (Backend Schema Document, "Key indexes").
            entry.HasIndex(e => new { e.OperatorId, e.Date });
        });

        modelBuilder.Entity<Notification>(notification =>
        {
            notification.Property(n => n.Text).HasMaxLength(500);
            notification.HasOne(n => n.User).WithMany().HasForeignKey(n => n.UserId).OnDelete(DeleteBehavior.Restrict);
            notification.HasOne<Booking>().WithMany().HasForeignKey(n => n.BookingId).OnDelete(DeleteBehavior.Restrict);

            // For showing someone their newest notifications.
            notification.HasIndex(n => new { n.UserId, n.CreatedAt });
        });

        modelBuilder.Entity<OffRosterWork>(work =>
        {
            work.Property(w => w.Reason).HasMaxLength(500);

            // Restrict: this is an HR record, so the assignment behind it can't be deleted.
            work.HasOne(w => w.CrewAssignment).WithMany().HasForeignKey(w => w.CrewAssignmentId).OnDelete(DeleteBehavior.Restrict);
            work.HasOne(w => w.RecordedBy).WithMany().HasForeignKey(w => w.RecordedById).OnDelete(DeleteBehavior.Restrict);

            // Recorded once, however many times the warning is acknowledged.
            work.HasIndex(w => new { w.CrewAssignmentId, w.Kind }).IsUnique();
        });
    }
}
