using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using PBRMS.Data;
using PBRMS.Domain;

namespace PBRMS.Api.Endpoints;

/// <summary>
/// GET /reports/audit: the audit log, newest first, for Admin. It only reads: nothing in
/// PBRMS can change or remove a line (see <see cref="AuditEntry"/>).
/// </summary>
public static partial class AuditEndpoints
{
    private const int DefaultLimit = 500;

    /// <param name="Who">The person, or "System" for PBRMS's own work.</param>
    /// <param name="What">The kind of thing: "Booking", "Role Assignment".</param>
    /// <param name="Item">Which one, if it has a name: a person, a unit or a production.</param>
    public record AuditRow(DateTimeOffset At, string Who, string What, string? Item, AuditAction Action, string Summary);

    /// <param name="More">True if the period holds more lines than these, so it should be narrowed.</param>
    public record AuditReport(List<AuditRow> Rows, bool More);

    public static void MapAuditEndpoints(this WebApplication app)
    {
        app.MapGet("/reports/audit", Report).RequireAuthorization(p => p.RequireRole(nameof(Role.Admin)));
    }

    private static async Task<AuditReport> Report(DateOnly from, DateOnly to, int? limit, PbrmsDbContext db)
    {
        var (start, end) = (NbcTime.At(from, TimeOnly.MinValue), NbcTime.At(to.AddDays(1), TimeOnly.MinValue));
        var most = Math.Clamp(limit ?? DefaultLimit, 1, DefaultLimit);

        // One more than asked for, which says whether there's more without counting them all.
        var lines = await db.AuditEntries.AsNoTracking()
            .Where(a => a.At >= start && a.At < end)
            .OrderByDescending(a => a.At)
            .Take(most + 1)
            .Select(a => new
            {
                a.At,
                Who = a.Actor == null ? "System" : a.Actor.FirstName + " " + a.Actor.Surname,
                a.What,
                a.ItemId,
                a.Action,
                a.Summary
            })
            .ToListAsync();

        var names = await NamesAsync(lines.SelectMany(l => new[] { l.ItemId, l.Summary }), db);

        var rows = lines
            .Take(most)
            .Select(l => new AuditRow(l.At, l.Who, l.What, names.GetValueOrDefault(l.ItemId.ToLowerInvariant()), l.Action, Named(l.Summary, names)))
            .ToList();

        return new AuditReport(rows, lines.Count > most);
    }

    /// <summary>The people, units and productions behind the ids in this text, so the log reads in names.</summary>
    private static async Task<Dictionary<string, string>> NamesAsync(IEnumerable<string> texts, PbrmsDbContext db)
    {
        var ids = texts
            .SelectMany(t => AnId().Matches(t).Select(m => Guid.Parse(m.Value)))
            .Distinct()
            .ToList();

        var people = await db.Users.AsNoTracking().Where(u => ids.Contains(u.Id))
            .Select(u => new { u.Id, Name = u.FirstName + " " + u.Surname }).ToListAsync();
        var units = await db.OrgUnits.AsNoTracking().Where(u => ids.Contains(u.Id))
            .Select(u => new { u.Id, u.Name }).ToListAsync();
        var bookings = await db.Bookings.AsNoTracking().Where(b => ids.Contains(b.Id))
            .Select(b => new { b.Id, Name = "#" + b.ProductionNo + " " + b.Title }).ToListAsync();

        return people.Concat(units).Concat(bookings).ToDictionary(n => n.Id.ToString(), n => n.Name);
    }

    /// <summary>Swaps ids for names. An id with no name, such as a crew position's, just says so.</summary>
    private static string Named(string text, Dictionary<string, string> names) =>
        AnId().Replace(text, m => names.GetValueOrDefault(m.Value.ToLowerInvariant(), "(record)"));

    [GeneratedRegex("[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}")]
    private static partial Regex AnId();
}
