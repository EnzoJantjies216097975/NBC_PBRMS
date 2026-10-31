using PBRMS.Domain;

namespace PBRMS.Tests;

/// <summary>
/// The approval rules on their own, with no database or API: who may move a booking,
/// and how roles reach across units.
/// </summary>
public class BookingWorkflowTests
{
    // A small org chart: Content Hub with a sub-department, plus News and two crew divisions.
    private static readonly OrgUnit ContentHub = new() { Id = Guid.NewGuid(), Name = "Content Hub", Kind = OrgUnitKind.ContentDepartment };
    private static readonly OrgUnit Drama = new() { Id = Guid.NewGuid(), Name = "Drama", Kind = OrgUnitKind.SubDepartment, ParentId = ContentHub.Id };
    private static readonly OrgUnit News = new() { Id = Guid.NewGuid(), Name = "News", Kind = OrgUnitKind.ContentDepartment };
    private static readonly OrgUnit Camera = new() { Id = Guid.NewGuid(), Name = "Camera", Kind = OrgUnitKind.TvOpsDivision };
    private static readonly OrgUnit Sound = new() { Id = Guid.NewGuid(), Name = "Sound", Kind = OrgUnitKind.TvOpsDivision };
    private static readonly OrgUnit[] AllUnits = [ContentHub, Drama, News, Camera, Sound];

    private static readonly Guid ProducerId = Guid.NewGuid();

    // A booking at 10h00 on Wednesday, so the crew deadline is 16h00 on Tuesday.
    private static readonly DateTimeOffset Wednesday = new(2031, 3, 5, 10, 0, 0, NbcTime.Offset);
    private static readonly DateTimeOffset BeforeDeadline = new(2031, 3, 4, 15, 59, 0, NbcTime.Offset);
    private static readonly DateTimeOffset AtDeadline = new(2031, 3, 4, 16, 0, 0, NbcTime.Offset);

    /// <summary>A Drama location shoot on Wednesday that needs a Camera crew.</summary>
    private static Booking DramaBooking(BookingStatus status) => new()
    {
        ProducerId = ProducerId,
        DepartmentId = Drama.Id,
        Status = status,
        Venue = Venue.Location,
        StartsAt = Wednesday,
        CrewSlots = [new CrewSlot { DivisionId = Camera.Id, PositionLabel = "Camera 1" }]
    };

    private static RoleAssignment[] Holds(Role role, OrgUnit? unit) => [new() { Role = role, OrgUnitId = unit?.Id }];

    private static bool MayTake(BookingStatus status, BookingAction action, Guid userId, RoleAssignment[] roles)
    {
        var booking = DramaBooking(status);
        var step = BookingWorkflow.FindStep(status, action)!;
        return BookingWorkflow.MayTake(step, booking, userId, roles, OrgUnit.ChainUp(booking.DepartmentId, AllUnits), BeforeDeadline);
    }

    [Fact]
    public void An_EP_of_a_department_covers_its_sub_departments()
    {
        Assert.True(MayTake(BookingStatus.EpReview, BookingAction.Approve, Guid.NewGuid(), Holds(Role.ExecutiveProducer, ContentHub)));
    }

    [Fact]
    public void An_EP_of_another_department_may_not_approve()
    {
        Assert.False(MayTake(BookingStatus.EpReview, BookingAction.Approve, Guid.NewGuid(), Holds(Role.ExecutiveProducer, News)));
    }

    [Fact]
    public void An_NBC_wide_role_covers_every_department()
    {
        Assert.True(MayTake(BookingStatus.EpReview, BookingAction.Approve, Guid.NewGuid(), Holds(Role.ExecutiveProducer, null)));
    }

    [Fact]
    public void Only_the_bookings_own_producer_may_submit_it()
    {
        Assert.True(MayTake(BookingStatus.Draft, BookingAction.Submit, ProducerId, Holds(Role.Producer, Drama)));
        Assert.False(MayTake(BookingStatus.Draft, BookingAction.Submit, Guid.NewGuid(), Holds(Role.Producer, Drama)));
    }

    [Fact]
    public void Only_a_supervisor_of_a_division_in_the_crew_list_may_send_back_at_crew_assignment()
    {
        Assert.True(MayTake(BookingStatus.CrewAssignment, BookingAction.SendBack, Guid.NewGuid(), Holds(Role.Supervisor, Camera)));
        Assert.False(MayTake(BookingStatus.CrewAssignment, BookingAction.SendBack, Guid.NewGuid(), Holds(Role.Supervisor, Sound)));
    }

    [Fact]
    public void Sending_back_returns_to_the_producer_from_the_EP_but_to_the_EP_from_supervisors()
    {
        Assert.Equal(BookingStatus.Draft, BookingWorkflow.FindStep(BookingStatus.EpReview, BookingAction.SendBack)!.To);
        Assert.Equal(BookingStatus.EpReview, BookingWorkflow.FindStep(BookingStatus.CrewAssignment, BookingAction.SendBack)!.To);
    }

    [Fact]
    public void Supervisors_can_staff_their_division_anywhere_and_the_booking_officer_can_staff_studios()
    {
        var shoot = DramaBooking(BookingStatus.CrewAssignment);
        var studio = DramaBooking(BookingStatus.CrewAssignment);
        studio.Venue = Venue.Studio1;

        Assert.True(BookingWorkflow.MayStaff(shoot, Camera.Id, Holds(Role.Supervisor, Camera), BeforeDeadline));
        Assert.False(BookingWorkflow.MayStaff(shoot, Camera.Id, Holds(Role.Supervisor, Sound), BeforeDeadline));
        Assert.False(BookingWorkflow.MayStaff(shoot, Camera.Id, Holds(Role.BookingOfficer, null), BeforeDeadline));

        Assert.True(BookingWorkflow.MayStaff(studio, Camera.Id, Holds(Role.BookingOfficer, null), BeforeDeadline));
        Assert.True(BookingWorkflow.MayStaff(studio, Camera.Id, Holds(Role.Supervisor, Camera), BeforeDeadline));
        Assert.False(BookingWorkflow.MayStaff(studio, Camera.Id, Holds(Role.Supervisor, Sound), BeforeDeadline));
    }

    [Fact]
    public void The_booking_officer_can_decide_outside_crew_once_the_daily_roster_is_due()
    {
        var shoot = DramaBooking(BookingStatus.CrewAssignment);

        Assert.True(BookingWorkflow.MayStaff(shoot, Camera.Id, Holds(Role.BookingOfficer, null), AtDeadline));
    }

    [Theory]
    [InlineData(14, 59, 1)] // before 15h00: tomorrow is still open
    [InlineData(15, 0, 2)]  // from 15h00: tomorrow is closed
    [InlineData(23, 30, 2)]
    public void Bookings_for_the_next_day_close_at_15h00(int hour, int minute, int daysAhead)
    {
        var now = new DateTimeOffset(2031, 3, 4, hour, minute, 0, NbcTime.Offset);

        Assert.Equal(new DateOnly(2031, 3, 4).AddDays(daysAhead), BookingWorkflow.EarliestBookableDate(now));
    }

    [Fact]
    public void Today_is_always_too_late_to_book()
    {
        var morning = new DateTimeOffset(2031, 3, 4, 7, 0, 0, NbcTime.Offset);

        Assert.True(BookingWorkflow.MissedCutOff(morning.AddHours(5), morning));
        Assert.False(BookingWorkflow.MissedCutOff(morning.AddDays(1), morning));
    }

    [Fact]
    public void Approving_a_draft_is_not_a_move()
    {
        Assert.Null(BookingWorkflow.FindStep(BookingStatus.Draft, BookingAction.Approve));
    }

    [Fact]
    public void A_finished_or_cancelled_booking_cannot_be_cancelled()
    {
        Assert.True(BookingWorkflow.CanBeCancelled(BookingStatus.Confirmed));
        Assert.False(BookingWorkflow.CanBeCancelled(BookingStatus.Completed));
        Assert.False(BookingWorkflow.CanBeCancelled(BookingStatus.Cancelled));
    }

    [Fact]
    public void An_operator_may_not_cancel_but_the_booking_officer_may()
    {
        var booking = DramaBooking(BookingStatus.EpReview);
        var chain = OrgUnit.ChainUp(booking.DepartmentId, AllUnits);

        Assert.False(BookingWorkflow.MayCancel(booking, Guid.NewGuid(), Holds(Role.Operator, Camera), chain));
        Assert.True(BookingWorkflow.MayCancel(booking, Guid.NewGuid(), Holds(Role.BookingOfficer, null), chain));
    }

    [Fact]
    public void ChainUp_stops_on_a_circular_parent_link()
    {
        var a = new OrgUnit { Id = Guid.NewGuid() };
        var b = new OrgUnit { Id = Guid.NewGuid(), ParentId = a.Id };
        a.ParentId = b.Id; // a mistake in the data: a and b each inside the other

        Assert.Equivalent(new[] { a.Id, b.Id }, OrgUnit.ChainUp(a.Id, [a, b]));
    }
}
