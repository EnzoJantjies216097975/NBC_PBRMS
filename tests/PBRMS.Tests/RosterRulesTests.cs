using PBRMS.Domain;

namespace PBRMS.Tests;

/// <summary>What a roster says about someone working a period, with no database.</summary>
public class RosterRulesTests
{
    private static readonly DateOnly Monday = new(2031, 3, 3);

    private static readonly ShiftDefinition Off = new() { Code = "O", Label = "Off", Kind = ShiftKind.Off };
    private static readonly ShiftDefinition Leave = new() { Code = "L", Label = "Leave", Kind = ShiftKind.Leave };
    private static readonly ShiftDefinition Trip = new() { Code = "TR", Label = "Trip", Kind = ShiftKind.Away };
    private static readonly ShiftDefinition StudioOne = new() { Code = "ST1", Label = "Studio 1", Kind = ShiftKind.Worked, Studio = Venue.Studio1, Start = new(13, 0), End = new(21, 0) };
    private static readonly ShiftDefinition Open = new() { Code = "OB", Label = "Outside broadcast", Kind = ShiftKind.Worked };
    private static readonly ShiftDefinition Day = new() { Code = "A1", Label = "Any", Kind = ShiftKind.Worked, Start = new(8, 0), End = new(17, 0) };
    private static readonly ShiftDefinition Night = new() { Code = "N", Label = "Night", Kind = ShiftKind.Worked, Start = new(19, 0), End = new(2, 0) };
    private static readonly ShiftDefinition TwoShifts = new()
    {
        Code = "ST2", Label = "Studio 2", Kind = ShiftKind.Worked,
        Start = new(5, 0), End = new(9, 0), SecondStart = new(13, 0), SecondEnd = new(22, 0)
    };

    private static DateTimeOffset At(int hour) => NbcTime.At(Monday, new(hour, 0));

    private static string? Reason(int fromHour, int toHour, bool forTrip, params ShiftDefinition[] shifts) =>
        RosterRules.Check(shifts, Monday, At(fromHour), At(toHour), forTrip)?.Reason;

    [Fact]
    public void A_blank_cell_is_never_a_clash()
    {
        Assert.Null(Reason(9, 11, false));
    }

    [Fact]
    public void A_day_off_is_a_warning_because_they_can_be_called_in()
    {
        var finding = RosterRules.Check([Off], Monday, At(9), At(11), false);

        Assert.Equal(ClashKind.CallIn, finding!.Kind);
        Assert.True(new Clash(finding.Kind, "", null, "", At(9), At(11)).IsWarning);
    }

    [Fact]
    public void Half_a_day_off_is_still_a_working_day()
    {
        Assert.Null(Reason(9, 11, false, Off, Day));
    }

    [Fact]
    public void Leave_is_not_a_warning_and_outranks_a_day_off()
    {
        var finding = RosterRules.Check([Off, Leave], Monday, At(9), At(11), false);

        Assert.Equal(ClashKind.OnLeave, finding!.Kind);
        Assert.False(new Clash(finding.Kind, "", null, "", At(9), At(11)).IsWarning);
    }

    [Fact]
    public void Rostered_to_a_studio_and_booked_in_another_is_a_warning()
    {
        Assert.Equal(ClashKind.RosteredElsewhere, RosterRules.Check([StudioOne], Monday, At(14), At(16), false, Venue.Studio2)!.Kind);
        Assert.Null(RosterRules.Check([StudioOne], Monday, At(14), At(16), false, Venue.Studio1));
    }

    [Fact]
    public void A_studio_shift_that_doesnt_touch_the_booking_doesnt_tie_them_to_it()
    {
        // A1 covers the morning booking. The Studio 1 shift only starts at 13h00.
        Assert.Null(RosterRules.Check([Day, StudioOne], Monday, At(9), At(11), false, Venue.Studio2));
    }

    [Fact]
    public void Someone_away_can_work_a_trip_but_nothing_else()
    {
        Assert.Contains("Away", Reason(9, 11, false, Trip));
        Assert.Null(Reason(9, 11, true, Trip));
    }

    [Fact]
    public void A_code_with_no_fixed_hours_fits_any_time()
    {
        Assert.Null(Reason(3, 4, false, Open));
    }

    [Fact]
    public void Inside_the_shift_is_fine()
    {
        Assert.Null(Reason(9, 11, false, Day));
    }

    [Fact]
    public void Running_past_the_end_of_a_shift_is_overtime_not_a_clash()
    {
        Assert.Null(Reason(16, 19, false, Day));
    }

    [Fact]
    public void Entirely_outside_the_shift_is_a_warning()
    {
        var finding = RosterRules.Check([Day], Monday, At(18), At(20), false);

        Assert.Equal(ClashKind.OutsideShift, finding!.Kind);
        Assert.True(new Clash(finding.Kind, "", null, "", At(18), At(20)).IsWarning);
    }

    [Fact]
    public void Either_of_two_shifts_on_a_day_will_do()
    {
        Assert.Null(Reason(6, 8, false, TwoShifts));
        Assert.Null(Reason(14, 16, false, TwoShifts));
        Assert.Contains("Not on shift", Reason(10, 12, false, TwoShifts));
    }

    [Fact]
    public void Two_codes_in_one_cell_count_together()
    {
        Assert.Null(Reason(20, 22, false, Day, Night));
    }

    [Fact]
    public void A_night_shift_runs_into_the_next_morning()
    {
        var (from, to) = (NbcTime.At(Monday.AddDays(1), new(0, 30)), NbcTime.At(Monday.AddDays(1), new(1, 30)));

        Assert.Null(RosterRules.Check([Night], Monday, from, to, false));
    }

    [Fact]
    public void A_production_past_midnight_belongs_to_the_day_it_started()
    {
        Assert.Equal([Monday], RosterRules.DaysCovered(At(22), At(22).AddHours(4)));
    }

    [Fact]
    public void A_trip_covers_every_day_it_lasts()
    {
        Assert.Equal([Monday, Monday.AddDays(1), Monday.AddDays(2)], RosterRules.DaysCovered(At(6), At(6).AddDays(2)));
    }

    [Fact]
    public void Dates_are_Windhoek_dates()
    {
        // 23h00 UTC on Sunday is 01h00 on Monday in Windhoek.
        var lateSundayUtc = new DateTimeOffset(2031, 3, 2, 23, 0, 0, TimeSpan.Zero);

        Assert.Equal(Monday, NbcTime.DateOf(lateSundayUtc));
    }

    [Fact]
    public void Cells_split_into_codes()
    {
        Assert.Equal(["N", "ST2"], RosterRules.SplitCodes(" N + ST2 "));
    }
}
