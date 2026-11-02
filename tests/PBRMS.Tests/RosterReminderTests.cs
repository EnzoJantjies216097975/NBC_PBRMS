using Microsoft.Extensions.DependencyInjection;
using PBRMS.Data;
using PBRMS.Domain;

namespace PBRMS.Tests;

/// <summary>
/// The reminder to publish next month's roster. Every test has its own division and
/// tests a date far from any other test's, because a division is reminded once a day.
/// </summary>
[Collection("API")]
public class RosterReminderTests(TestApi api)
{
    private static int yearCounter;

    /// <summary>A year no other test uses, so nothing else has published a roster in it.</summary>
    private static int NewYear() => 2040 + Interlocked.Increment(ref yearCounter);

    private static DateTimeOffset At(int year, int month, int day, int hour = 8) => NbcTime.At(new DateOnly(year, month, day), new TimeOnly(hour, 0));

    private async Task CheckAtAsync(DateTimeOffset now)
    {
        using var scope = api.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<RosterReminderWatcher>().CheckAsync(now);
    }

    /// <summary>A division, with its supervisor's sign-in email. It has one operator unless asked otherwise.</summary>
    private async Task<(Guid Id, string Name, string Supervisor)> NewDivisionAsync(bool withOperator = true)
    {
        var id = Guid.NewGuid();
        var name = $"Reminders {id:N}";
        var email = $"{id:N}@pbrms.test";

        await api.WithDatabaseAsync(async db =>
        {
            db.OrgUnits.Add(new OrgUnit { Id = id, Name = name, Kind = OrgUnitKind.TvOpsDivision });
            db.Users.Add(new User { FirstName = "Temp", Surname = "Supervisor", WorkEmail = email, Roles = [new() { Role = Role.Supervisor, OrgUnitId = id }] });
            await db.SaveChangesAsync();
        });

        if (withOperator)
            await api.NewOperatorAsync(name);

        return (id, name, email);
    }

    private async Task PublishAsync(Guid divisionId, int year, int month)
    {
        await api.WithDatabaseAsync(async db =>
        {
            db.MonthlyRosters.Add(new MonthlyRoster
            {
                DivisionId = divisionId,
                Month = new DateOnly(year, month, 1),
                Version = 1,
                Status = RosterStatus.Published,
                UpdatedAt = DateTimeOffset.UtcNow,
                PublishedAt = DateTimeOffset.UtcNow
            });
            await db.SaveChangesAsync();
        });
    }

    private async Task<List<string>> RemindersAsync(string email) =>
        (await api.NotificationsAsync(email)).Where(n => n.Text.Contains("roster for")).Select(n => n.Text).ToList();

    [Theory]
    [InlineData(23, true)]  // 7 days before the end of a 30-day month
    [InlineData(22, false)]
    [InlineData(30, true)]
    public void Next_month_falls_due_seven_days_before_this_one_ends(int day, bool nextMonthDue)
    {
        var months = RosterRules.MonthsDue(new DateOnly(2031, 11, day));

        Assert.Equal(nextMonthDue, months.Contains(new DateOnly(2031, 12, 1)));
    }

    [Fact]
    public void A_year_end_rolls_over_to_january()
    {
        Assert.Equal([new DateOnly(2031, 12, 1), new DateOnly(2032, 1, 1)], RosterRules.MonthsDue(new DateOnly(2031, 12, 28)));
    }

    [Fact]
    public async Task A_supervisor_is_reminded_about_the_roster_that_is_due()
    {
        var year = NewYear();
        var division = await NewDivisionAsync();
        await PublishAsync(division.Id, year, 11);

        await CheckAtAsync(At(year, 11, 24));

        Assert.Equal([$"The December {year} roster for {division.Name} isn't published yet."], await RemindersAsync(division.Supervisor));
    }

    [Fact]
    public async Task Nothing_is_sent_before_08h00_or_twice_in_a_day()
    {
        var year = NewYear();
        var division = await NewDivisionAsync();
        await PublishAsync(division.Id, year, 11);

        await CheckAtAsync(At(year, 11, 24, hour: 7));
        Assert.Empty(await RemindersAsync(division.Supervisor));

        await CheckAtAsync(At(year, 11, 24, hour: 8));
        await CheckAtAsync(At(year, 11, 24, hour: 9));
        Assert.Single(await RemindersAsync(division.Supervisor));
    }

    [Fact]
    public async Task It_comes_back_every_day_until_the_roster_is_published()
    {
        var year = NewYear();
        var division = await NewDivisionAsync();
        await PublishAsync(division.Id, year, 11);

        await CheckAtAsync(At(year, 11, 24));
        await CheckAtAsync(At(year, 11, 25));
        Assert.Equal(2, (await RemindersAsync(division.Supervisor)).Count);

        await PublishAsync(division.Id, year, 12);
        await CheckAtAsync(At(year, 11, 26));
        Assert.Equal(2, (await RemindersAsync(division.Supervisor)).Count);
    }

    [Fact]
    public async Task A_late_roster_is_still_chased_once_its_month_has_started()
    {
        var year = NewYear();
        var division = await NewDivisionAsync();

        await CheckAtAsync(At(year, 12, 3));

        Assert.Contains($"The December {year} roster", Assert.Single(await RemindersAsync(division.Supervisor)));
    }

    [Fact]
    public async Task Nothing_is_sent_early_in_a_month_with_its_roster_published()
    {
        var year = NewYear();
        var division = await NewDivisionAsync();
        await PublishAsync(division.Id, year, 11);

        await CheckAtAsync(At(year, 11, 10));

        Assert.Empty(await RemindersAsync(division.Supervisor));
    }

    [Fact]
    public async Task A_division_with_no_operators_has_nothing_to_roster()
    {
        var year = NewYear();
        var division = await NewDivisionAsync(withOperator: false);

        await CheckAtAsync(At(year, 11, 24));

        Assert.Empty(await RemindersAsync(division.Supervisor));
    }
}
