using System.Globalization;
using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Tables;
using MigraDoc.Rendering;
using PBRMS.Domain;
using PdfSharp.Fonts;

namespace PBRMS.Api;

/// <summary>
/// Prints the daily roster: A4 landscape, a row per area and a line per production, with
/// a column for each trade, in the layout of the board's floor sheet (RosterPrint.jsx).
/// Uses PDFsharp/MigraDoc 6.2.4, the last MIT-licensed version (see PBRMS.Api.csproj).
/// </summary>
public static class DailyRosterPdf
{
    private const string Font = "Arial";

    /// <summary>The sheet's file name, as the board names it: Roster_Tue_15092026.pdf.</summary>
    public static string FileName(DateOnly date) =>
        $"Roster_{date.ToString("ddd_ddMMyyyy", CultureInfo.InvariantCulture)}.pdf";

    public static byte[] Render(DailyRosterView roster)
    {
        // The API runs on Windows Server, so the fonts come from Windows.
        GlobalFontSettings.UseWindowsFontsUnderWindows = true;

        var document = new Document();
        document.Styles[StyleNames.Normal]!.Font.Name = Font;
        document.Styles[StyleNames.Normal]!.Font.Size = 8;

        var section = document.AddSection();
        section.PageSetup.PageFormat = PageFormat.A4;
        section.PageSetup.Orientation = Orientation.Landscape;
        section.PageSetup.TopMargin = Unit.FromCentimeter(1.2);
        section.PageSetup.BottomMargin = Unit.FromCentimeter(1.2);
        section.PageSetup.LeftMargin = Unit.FromCentimeter(1);
        section.PageSetup.RightMargin = Unit.FromCentimeter(1);

        AddHeading(section, roster);
        AddTable(section, roster);
        AddUndecided(section, roster);

        section.AddParagraph("This roster is subject to change.").Format.Font.Italic = true;

        var renderer = new PdfDocumentRenderer { Document = document };
        renderer.RenderDocument();

        using var file = new MemoryStream();
        renderer.PdfDocument.Save(file, false);
        return file.ToArray();
    }

    private static void AddHeading(Section section, DailyRosterView roster)
    {
        var heading = section.AddParagraph($"NBC Daily Roster: {roster.Date.ToString("dddd d MMMM yyyy", CultureInfo.InvariantCulture)}");
        heading.Format.Font.Size = 14;
        heading.Format.Font.Bold = true;
        heading.Format.SpaceAfter = Unit.FromPoint(4);

        if (!string.IsNullOrWhiteSpace(roster.Announcement))
        {
            var announcement = section.AddParagraph(roster.Announcement);
            announcement.Format.Font.Bold = true;
            announcement.Format.SpaceAfter = Unit.FromPoint(6);
        }
    }

    private static void AddTable(Section section, DailyRosterView roster)
    {
        var trades = roster.Trades();

        var table = section.AddTable();
        table.Borders.Width = 0.5;
        table.TopPadding = Unit.FromPoint(1.5);
        table.BottomPadding = Unit.FromPoint(1.5);

        // A4 landscape less the margins is 27.7 cm.
        table.AddColumn(Unit.FromCentimeter(2.4));
        table.AddColumn(Unit.FromCentimeter(2.3));
        table.AddColumn(Unit.FromCentimeter(7));
        var tradeWidth = (27.7 - 2.4 - 2.3 - 7) / Math.Max(trades.Count, 1);
        foreach (var _ in trades)
            table.AddColumn(Unit.FromCentimeter(tradeWidth));

        var header = table.AddRow();
        header.HeadingFormat = true;
        header.Format.Font.Bold = true;
        header.Shading.Color = Colors.LightGray;
        string[] headings = ["Area", "Time", "Production", .. trades];
        for (var i = 0; i < headings.Length; i++)
            header.Cells[i].AddParagraph(headings[i]);

        string? lastArea = null;
        foreach (var line in roster.Lines)
        {
            var row = table.AddRow();
            row.KeepWith = 0;

            // The area is named once, on the first line of its block.
            var area = row.Cells[0].AddParagraph(line.Area == lastArea ? "" : line.Area);
            area.Format.Font.Bold = true;
            lastArea = line.Area;

            row.Cells[1].AddParagraph(TimeOf(line));

            var title = row.Cells[2].AddParagraph(line.Title);
            title.Format.Font.Bold = line.BookingId is not null;
            if (!string.IsNullOrEmpty(line.Detail))
                row.Cells[2].AddParagraph(line.Detail);

            for (var i = 0; i < trades.Count; i++)
            {
                var names = line.Crew.Where(c => c.Division == trades[i]).Select(c => c.Name);
                row.Cells[3 + i].AddParagraph(string.Join("\n", names));
            }
        }
    }

    /// <summary>Productions not confirmed yet, so nobody reads the sheet as complete.</summary>
    private static void AddUndecided(Section section, DailyRosterView roster)
    {
        if (roster.Undecided.Count == 0)
            return;

        var heading = section.AddParagraph("Not confirmed yet");
        heading.Format.SpaceBefore = Unit.FromPoint(8);
        heading.Format.Font.Bold = true;

        foreach (var booking in roster.Undecided)
        {
            var open = booking.OpenPositions.Count == 0 ? "" : $": needs {string.Join(", ", booking.OpenPositions)}";
            section.AddParagraph($"#{booking.ProductionNo} {booking.Title} ({NbcTime.Text(booking.StartsAt)}){open}");
        }
    }

    private static string TimeOf(DailyLine line) =>
        line.StartsAt is { } start && line.EndsAt is { } end ? $"{Clock(start)}–{Clock(end)}" : "";

    private static string Clock(DateTimeOffset moment) => moment.ToOffset(NbcTime.Offset).ToString("HH'h'mm");
}
