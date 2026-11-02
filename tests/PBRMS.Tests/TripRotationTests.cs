using PBRMS.Domain;

namespace PBRMS.Tests;

/// <summary>
/// Who is owed the next trip (BR-10, SRS FR-TRP-01 to 03). This is the rule most likely to be
/// argued over, so each step of the order has its own test.
/// </summary>
public class TripRotationTests
{
    private static readonly DateOnly Today = new(2026, 11, 2);

    private static TripLine Trip(int daysFromToday, int days = 1)
    {
        var from = Today.AddDays(daysFromToday);
        return new TripLine(null, "Trip", from, from.AddDays(days - 1));
    }

    private static TripOperator Person(string name, params TripLine[] trips) =>
        new(Guid.NewGuid(), name, null, null, [.. trips]);

    private static List<string> Queue(params TripOperator[] people) =>
        TripRotation.Rank(people, Today).Where(s => s.Place is not null).Select(s => s.Name).ToList();

    [Fact]
    public void Fewest_trips_goes_first()
    {
        var order = Queue(
            Person("Two trips", Trip(-90), Trip(-30)),
            Person("One trip", Trip(-5)));

        Assert.Equal(["One trip", "Two trips"], order);
    }

    [Fact]
    public void With_the_same_trips_whoever_has_waited_longest_goes_first()
    {
        var order = Queue(
            Person("Back last week", Trip(-7)),
            Person("Back in March", Trip(-200)));

        Assert.Equal(["Back in March", "Back last week"], order);
    }

    [Fact]
    public void Waiting_counts_from_the_day_they_came_back()
    {
        var order = Queue(
            Person("Long trip, back this week", Trip(-30, days: 28)),
            Person("Short trip, back last week", Trip(-8)));

        Assert.Equal(["Short trip, back last week", "Long trip, back this week"], order);
    }

    [Fact]
    public void A_tie_goes_by_name_so_the_order_never_wobbles()
    {
        var order = Queue(Person("Zelda"), Person("anna"), Person("Ben"));

        Assert.Equal(["anna", "Ben", "Zelda"], order);
    }

    [Fact]
    public void Someone_on_a_trip_still_to_come_is_out_of_the_queue_but_stays_listed()
    {
        var booked = Person("Booked", Trip(5));
        var rows = TripRotation.Rank([booked, Person("Free", Trip(-30), Trip(-60))], Today);

        Assert.Equal("Free", rows[0].Name);
        Assert.Equal(1, rows[0].Place);
        Assert.Equal("Booked", rows[1].Name);
        Assert.Null(rows[1].Place);
        Assert.Equal(Today.AddDays(5), rows[1].NextTrip);
    }

    [Fact]
    public void A_trip_ending_today_isnt_over_yet()
    {
        var row = TripRotation.Rank([Person("Away", Trip(-2, days: 3))], Today).Single();

        Assert.Null(row.Place);
        Assert.Null(row.LastBack);
    }

    [Fact]
    public void Sitting_out_takes_someone_out_of_the_queue_without_touching_their_trips()
    {
        var sitting = Person("Sitting", Trip(-30)) with { SitOutReason = TripSitOutReason.Studies };

        var row = TripRotation.Rank([sitting], Today).Single();

        Assert.Null(row.Place);
        Assert.Equal(TripSitOutReason.Studies, row.SittingOut);
        Assert.Equal(1, row.Trips);
    }

    [Fact]
    public void Sitting_out_lapses_by_itself_after_the_last_day()
    {
        var lastDayToday = Person("Today") with { SitOutReason = TripSitOutReason.Family, SitOutUntil = Today };
        var lastDayYesterday = Person("Yesterday") with { SitOutReason = TripSitOutReason.Family, SitOutUntil = Today.AddDays(-1) };

        var rows = TripRotation.Rank([lastDayToday, lastDayYesterday], Today);

        Assert.Null(rows.Single(r => r.Name == "Today").Place);
        Assert.Equal(1, rows.Single(r => r.Name == "Yesterday").Place);
    }

    [Fact]
    public void Being_crewed_on_a_trip_outranks_sitting_out()
    {
        var crewed = Person("Crewed", Trip(3)) with { SitOutReason = TripSitOutReason.Health };

        var row = TripRotation.Rank([crewed], Today).Single();

        Assert.Null(row.SittingOut);
        Assert.Equal(Today.AddDays(3), row.NextTrip);
    }

    [Fact]
    public void A_finished_trip_counts_only_if_it_was_confirmed()
    {
        var lastWeek = Today.AddDays(-7);

        Assert.True(TripRotation.Counts(BookingStatus.Completed, lastWeek, Today));
        Assert.True(TripRotation.Counts(BookingStatus.Confirmed, lastWeek, Today));
        Assert.False(TripRotation.Counts(BookingStatus.CrewAssignment, lastWeek, Today));
    }

    [Fact]
    public void A_trip_still_to_come_counts_as_soon_as_they_are_on_the_crew()
    {
        Assert.True(TripRotation.Counts(BookingStatus.CrewAssignment, Today.AddDays(7), Today));
    }
}
