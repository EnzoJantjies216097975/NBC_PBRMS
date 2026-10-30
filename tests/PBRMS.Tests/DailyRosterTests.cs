using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Xml.Linq;
using Microsoft.Extensions.DependencyInjection;
using PBRMS.Data;
using PBRMS.Domain;
using static PBRMS.Api.Endpoints.DailyRosterEndpoints;
using static PBRMS.Api.Endpoints.RosterEndpoints;
using static PBRMS.Tests.TestSteps;

namespace PBRMS.Tests;

/// <summary>
/// The daily roster through the real API: built from confirmed bookings and the monthly
/// roster, changed only by the Booking Officer, and printed as a PDF. Each test has its
/// own day, in a year no other test uses.
/// </summary>
[Collection("API")]
public class DailyRosterTests(TestApi api)
{
    private const string BookingOfficer = "booking@pbrms.test";
    private const string Supervisor = "supervisor@pbrms.test"; // of Camera

    [Fact]
    public async Task A_confirmed_studio_production_is_on_the_sheet_with_its_crew()
    {
        var day = NewRosterDay();
        var booking = await api.AtFinalisationAsync(day, Venue.Studio1);
        await api.TransitionAsync(BookingOfficer, booking.Id, BookingAction.Approve);

        var sheet = await SheetAsync(day);

        var line = sheet.Lines.Single(l => l.BookingId == booking.Id);
        Assert.Equal("Studio 1", line.Area);
        Assert.Equal("Camera", Assert.Single(line.Crew).Division);
    }

    [Fact]
    public async Task A_booking_confirmed_later_appears_on_the_sheet_by_itself()
    {
        var day = NewRosterDay();
        var booking = await api.AtFinalisationAsync(day, Venue.Studio2);
        Assert.DoesNotContain((await SheetAsync(day)).Lines, l => l.BookingId == booking.Id);

        await api.TransitionAsync(BookingOfficer, booking.Id, BookingAction.Approve);

        Assert.Contains((await SheetAsync(day)).Lines, l => l.BookingId == booking.Id);
    }

    [Fact]
    public async Task An_unconfirmed_booking_is_listed_with_the_positions_it_still_needs()
    {
        var day = NewRosterDay();
        var booking = await api.AtCrewStageAsync(day, Venue.Location, "Camera");

        var undecided = Assert.Single((await SheetAsync(day)).Undecided, u => u.BookingId == booking.Id);

        Assert.Equal(["Camera 1"], undecided.OpenPositions);
    }

    [Fact]
    public async Task Outside_productions_are_numbered_down_the_sheet_after_the_studios()
    {
        var day = NewRosterDay();
        var studio = await api.AtFinalisationAsync(day, Venue.Studio4);
        await api.TransitionAsync(BookingOfficer, studio.Id, BookingAction.Approve);
        var early = await api.AtCrewStageAsync(day, Venue.Location, "Camera");
        await api.AssignOkAsync(early, "Camera", await api.NewOperatorAsync("Camera"));
        var late = await api.AtCrewStageAsync(day.AddHours(3), Venue.Location, "Camera");
        await api.AssignOkAsync(late, "Camera", await api.NewOperatorAsync("Camera"));

        var sheet = await SheetAsync(day);

        Assert.Equal("Production 1", sheet.Lines.Single(l => l.BookingId == early.Id).Area);
        Assert.Equal("Production 2", sheet.Lines.Single(l => l.BookingId == late.Id).Area);
        var areas = sheet.Lines.Select(l => l.Area).ToList();
        Assert.True(areas.IndexOf("Studio 4") < areas.IndexOf("Production 1"));
    }

    [Fact]
    public async Task A_studio_with_nothing_in_it_says_so()
    {
        var sheet = await SheetAsync(NewRosterDay());

        Assert.Equal(DailyRosterRules.NobodyOn, sheet.Lines.Single(l => l.Area == "Studio 3").Title);
    }

    [Fact]
    public async Task People_rostered_with_no_production_are_shown_and_people_off_are_not()
    {
        var day = NewRosterDay();
        var (onDuty, inStudio, off) = (await api.NewOperatorAsync("Camera"), await api.NewOperatorAsync("Camera"), await api.NewOperatorAsync("Camera"));
        var date = NbcTime.DateOf(day);
        await PublishRosterAsync(day, new RosterCell(onDuty, date, "A1"), new RosterCell(inStudio, date, "ST1"), new RosterCell(off, date, "O"));

        var sheet = await SheetAsync(day);

        var onShift = sheet.Lines.Single(l => l.Area == DailyRosterRules.OnShiftArea);
        Assert.StartsWith("A1", onShift.Title);
        Assert.Single(onShift.Crew);

        var studio = sheet.Lines.Single(l => l.Area == "Studio 1");
        Assert.Equal(DailyRosterRules.OnDutyTitle, studio.Title);
        Assert.Single(studio.Crew);
    }

    [Fact]
    public async Task Someone_on_a_production_isnt_also_listed_as_on_shift()
    {
        var day = NewRosterDay();
        var op = await api.NewOperatorAsync("Camera");
        await PublishRosterAsync(day, new RosterCell(op, NbcTime.DateOf(day), "A1"));
        var booking = await api.AtCrewStageAsync(day, Venue.Location, "Camera");
        await api.AssignOkAsync(booking, "Camera", op);

        var sheet = await SheetAsync(day);

        Assert.DoesNotContain(sheet.Lines, l => l.Area == DailyRosterRules.OnShiftArea);
    }

    [Fact]
    public async Task The_booking_officer_can_change_a_line_and_it_stays_changed()
    {
        var day = NewRosterDay();
        var booking = await api.AtFinalisationAsync(day, Venue.Studio1);
        await api.TransitionAsync(BookingOfficer, booking.Id, BookingAction.Approve);
        var client = await api.SignedInClientAsync(BookingOfficer);

        var form = new DailyLineForm(null, booking.Id, false, "Studio 1", day, day.AddHours(1), "Morning Show (shortened)", "Call the gallery", [new("Camera", "Stand In")]);
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync(LinesUrl(day), form, Json)).StatusCode);

        var line = (await SheetAsync(day)).Lines.Single(l => l.BookingId == booking.Id);
        Assert.Equal("Morning Show (shortened)", line.Title);
        Assert.Equal("Stand In", Assert.Single(line.Crew).Name);

        // Discarding the change brings back what was generated.
        var discard = await client.DeleteAsync($"{LinesUrl(day)}/{line.LineId}");
        Assert.Equal(HttpStatusCode.OK, discard.StatusCode);
        Assert.StartsWith($"#{booking.ProductionNo}", (await SheetAsync(day)).Lines.Single(l => l.BookingId == booking.Id).Title);
    }

    [Fact]
    public async Task The_booking_officer_can_add_a_line_and_hide_a_production()
    {
        var day = NewRosterDay();
        var booking = await api.AtFinalisationAsync(day, Venue.Studio2);
        await api.TransitionAsync(BookingOfficer, booking.Id, BookingAction.Approve);
        var client = await api.SignedInClientAsync(BookingOfficer);

        await client.PutAsJsonAsync(LinesUrl(day), new DailyLineForm(null, null, false, "Studio 3", day, day.AddHours(2), "Rig check", null, null), Json);
        await client.PutAsJsonAsync(LinesUrl(day), new DailyLineForm(null, booking.Id, true, "Studio 2", null, null, "Hidden", null, null), Json);

        var sheet = await SheetAsync(day);
        Assert.Contains(sheet.Lines, l => l is { Area: "Studio 3", Title: "Rig check" });
        Assert.DoesNotContain(sheet.Lines, l => l.BookingId == booking.Id);
    }

    [Fact]
    public async Task The_announcement_shows_on_the_sheet()
    {
        var day = NewRosterDay();
        var client = await api.SignedInClientAsync(BookingOfficer);

        await client.PutAsJsonAsync($"/daily-roster/{Date(day)}/announcement", new AnnouncementRequest("Fire drill at 10h00"), Json);

        Assert.Equal("Fire drill at 10h00", (await SheetAsync(day)).Announcement);
    }

    [Fact]
    public async Task Only_the_booking_officer_can_change_or_print_the_sheet_but_everyone_can_read_it()
    {
        var day = NewRosterDay();
        var supervisor = await api.SignedInClientAsync(Supervisor);
        var operatorClient = await api.SignedInClientAsync("operator@pbrms.test");

        Assert.Equal(HttpStatusCode.OK, (await operatorClient.GetAsync($"/daily-roster/{Date(day)}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await supervisor.PutAsJsonAsync($"/daily-roster/{Date(day)}/announcement", new AnnouncementRequest("No"), Json)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await supervisor.GetAsync($"/daily-roster/{Date(day)}/pdf")).StatusCode);
    }

    [Fact]
    public async Task The_sheet_prints_as_an_A4_landscape_pdf_with_the_boards_file_name()
    {
        var day = NewRosterDay();
        var booking = await api.AtFinalisationAsync(day, Venue.Studio1);
        await api.TransitionAsync(BookingOfficer, booking.Id, BookingAction.Approve);
        var client = await api.SignedInClientAsync(BookingOfficer);
        await client.PutAsJsonAsync($"/daily-roster/{Date(day)}/announcement", new AnnouncementRequest("Fire drill at 10h00"), Json);

        var response = await client.GetAsync($"/daily-roster/{Date(day)}/pdf");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/pdf", response.Content.Headers.ContentType!.MediaType);
        Assert.Matches(@"Roster_[A-Z][a-z]{2}_\d{8}\.pdf", response.Content.Headers.ContentDisposition!.FileName);

        var pdf = await response.Content.ReadAsByteArrayAsync();
        var text = Encoding.Latin1.GetString(pdf);
        Assert.StartsWith("%PDF-", text);
        Assert.Contains("/MediaBox[0 0 841.89 595.276]", text); // A4 landscape, in points
    }

    [Fact]
    public async Task The_booking_officer_is_told_once_about_outside_crew_still_undecided_at_the_deadline()
    {
        var day = NewRosterDay();
        var booking = await api.AtCrewStageAsync(day, Venue.Location, "Camera");
        var deadline = BookingWorkflow.CrewDeadline(day);

        await CheckDeadlineAtAsync(deadline.AddMinutes(-1));
        Assert.DoesNotContain(await api.NotificationsAsync(BookingOfficer), n => n.BookingId == booking.Id);

        await CheckDeadlineAtAsync(deadline);
        await CheckDeadlineAtAsync(deadline.AddHours(1));
        Assert.Single(await api.NotificationsAsync(BookingOfficer), n => n.BookingId == booking.Id);
    }

    [Fact]
    public void The_pdf_library_stays_on_its_MIT_version()
    {
        // From 6.5.0 PDFsharp is AGPL-3.0, which has obligations for software run as a
        // service. If this fails, don't change the test: read the licence first.
        var project = XDocument.Load(Path.Combine(RepoRoot(), "src", "PBRMS.Api", "PBRMS.Api.csproj"));
        var version = project.Descendants("PackageReference").Single(p => p.Attribute("Include")?.Value == "PDFsharp-MigraDoc").Attribute("Version")!.Value;

        Assert.Equal("[6.2.4]", version);
    }

    // ---- Helpers ----

    private static int dayCounter;

    /// <summary>10h00 on a day no other test uses.</summary>
    private static DateTimeOffset NewRosterDay() =>
        new DateTimeOffset(2035, 1, 1, 10, 0, 0, NbcTime.Offset).AddDays(Interlocked.Increment(ref dayCounter));

    private static string Date(DateTimeOffset day) => $"{day:yyyy-MM-dd}";

    private static string LinesUrl(DateTimeOffset day) => $"/daily-roster/{Date(day)}/lines";

    private async Task<DailyRosterView> SheetAsync(DateTimeOffset day)
    {
        var client = await api.SignedInClientAsync(BookingOfficer);
        return (await client.GetFromJsonAsync<DailyRosterView>($"/daily-roster/{Date(day)}", Json))!;
    }

    /// <summary>Adds these cells to the Camera roster for that month and publishes it.</summary>
    private async Task PublishRosterAsync(DateTimeOffset day, params RosterCell[] cells)
    {
        var units = await api.UnitsAsync();
        var client = await api.SignedInClientAsync(Supervisor);
        var url = $"/rosters/{units["Camera"]}/{day:yyyy-MM}";

        (await client.PutAsJsonAsync(url, new RosterRequest([.. cells]), Json)).EnsureSuccessStatusCode();
        (await client.PostAsync($"{url}/publish", null)).EnsureSuccessStatusCode();
    }

    private async Task CheckDeadlineAtAsync(DateTimeOffset now)
    {
        using var scope = api.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<CrewDeadlineWatcher>().CheckAsync(now);
    }

    /// <summary>The repository's top folder, found by walking up from where the tests run.</summary>
    private static string RepoRoot()
    {
        var folder = new DirectoryInfo(AppContext.BaseDirectory);
        while (!File.Exists(Path.Combine(folder!.FullName, "PBRMS.slnx")))
            folder = folder.Parent;
        return folder.FullName;
    }
}
