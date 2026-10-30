using PBRMS.Domain;

namespace PBRMS.Tests;

/// <summary>What counts as a clash, on its own with no database.</summary>
public class ClashRulesTests
{
    private static readonly DateTimeOffset Nine = new(2030, 1, 7, 9, 0, 0, TimeSpan.FromHours(2));

    [Fact]
    public void Overlapping_periods_clash()
    {
        Assert.True(ClashRules.Overlaps(Nine, Nine.AddHours(2), Nine.AddHours(1), Nine.AddHours(3)));
    }

    [Fact]
    public void A_period_inside_another_clashes()
    {
        Assert.True(ClashRules.Overlaps(Nine, Nine.AddHours(4), Nine.AddHours(1), Nine.AddHours(2)));
    }

    [Fact]
    public void Back_to_back_periods_do_not_clash()
    {
        Assert.False(ClashRules.Overlaps(Nine, Nine.AddHours(2), Nine.AddHours(2), Nine.AddHours(3)));
    }

    [Theory]
    [InlineData(Venue.Studio1, true)]
    [InlineData(Venue.App, true)]
    [InlineData(Venue.Location, false)]
    [InlineData(Venue.Ob, false)]
    public void Only_rooms_can_clash(Venue venue, bool canClash)
    {
        Assert.Equal(canClash, ClashRules.VenueCanClash(venue));
    }

    [Theory]
    [InlineData(Venue.Studio1, Venue.Location, BookingStatus.Confirmed, true)]      // studio over outside
    [InlineData(Venue.Studio1, Venue.Ob, BookingStatus.CrewAssignment, true)]
    [InlineData(Venue.Studio1, Venue.Location, BookingStatus.InProgress, false)]    // it's already running
    [InlineData(Venue.Studio1, Venue.Studio2, BookingStatus.Confirmed, false)]      // studios don't take from each other
    [InlineData(Venue.Location, Venue.Studio1, BookingStatus.Confirmed, false)]     // outside never takes from a studio
    public void A_studio_takes_precedence_over_an_outside_production_that_hasnt_started(Venue wanted, Venue other, BookingStatus status, bool takes)
    {
        Assert.Equal(takes, ClashRules.StudioTakesPrecedence(wanted, other, status));
    }

    [Fact]
    public void Drafts_and_cancelled_bookings_do_not_hold_crew()
    {
        Assert.DoesNotContain(BookingStatus.Draft, ClashRules.StatusesHoldingCrew);
        Assert.DoesNotContain(BookingStatus.Cancelled, ClashRules.StatusesHoldingCrew);
        Assert.Contains(BookingStatus.CrewAssignment, ClashRules.StatusesHoldingCrew);
    }

    [Fact]
    public void A_booking_still_being_approved_does_not_hold_a_studio()
    {
        Assert.DoesNotContain(BookingStatus.EpReview, ClashRules.StatusesHoldingVenue);
        Assert.Contains(BookingStatus.Finalisation, ClashRules.StatusesHoldingVenue);
    }
}
