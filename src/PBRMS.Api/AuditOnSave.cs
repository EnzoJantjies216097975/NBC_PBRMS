using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Metadata;
using PBRMS.Api.Auth;
using PBRMS.Domain;

namespace PBRMS.Api;

/// <summary>
/// Writes the audit log (BR-18, TR-04). It watches every save, so no endpoint has to remember
/// to, and adds its lines to that same save: if the change is refused or rolled back, so are
/// they. Notifications are left out: they're messages about changes, not changes.
/// </summary>
public partial class AuditOnSave(IHttpContextAccessor http) : SaveChangesInterceptor
{
    private const int LongestValue = 200;
    private const int LongestSummary = 4000;

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        if (eventData.Context is { } context)
            WriteLines(context);

        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void WriteLines(DbContext context)
    {
        // Worked out before adding anything, because the lines are themselves new entries.
        var lines = context.ChangeTracker.Entries()
            .Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted
                        && e.Entity is not (Notification or AuditEntry))
            .Select(LineFor)
            .OfType<AuditEntry>()
            .ToList();

        context.AddRange(lines);
    }

    private AuditEntry? LineFor(EntityEntry entry)
    {
        var action = entry.State switch
        {
            EntityState.Added => AuditAction.Added,
            EntityState.Deleted => AuditAction.Removed,
            _ => AuditAction.Changed
        };

        var summary = action == AuditAction.Changed ? WhatMoved(entry) : EverythingIn(entry);
        if (action == AuditAction.Changed && summary.Length == 0)
            return null;

        return new AuditEntry
        {
            At = DateTimeOffset.UtcNow,
            ActorId = ActorId(),
            What = Words(entry.Metadata.ClrType.Name),
            ItemId = string.Join(",", entry.Properties.Where(p => p.Metadata.IsPrimaryKey()).Select(p => p.CurrentValue)),
            Action = action,
            Summary = summary.Length > LongestSummary ? $"{summary[..LongestSummary]}…" : summary
        };
    }

    /// <summary>Whoever is signed in to this request. Nobody, for the minute checks or setup.</summary>
    private Guid? ActorId() =>
        Guid.TryParse(http.HttpContext?.User.FindFirst(PbrmsClaims.UserId)?.Value, out var id) ? id : null;

    private static string WhatMoved(EntityEntry entry) => string.Join("; ", Recorded(entry)
        .Where(p => p.IsModified && !Equals(p.OriginalValue, p.CurrentValue))
        .Select(p => $"{Words(p.Metadata.Name)}: {Text(p.OriginalValue)} → {Text(p.CurrentValue)}"));

    // On a new row, a value SQL Server fills in isn't known yet: a production number would read as 0.
    private static string EverythingIn(EntityEntry entry) => string.Join("; ", Recorded(entry)
        .Where(p => entry.State != EntityState.Added || p.Metadata.ValueGenerated == ValueGenerated.Never)
        .Select(p => entry.State == EntityState.Deleted ? (p.Metadata, Value: p.OriginalValue) : (p.Metadata, Value: p.CurrentValue))
        .Where(p => p.Value is not null)
        .Select(p => $"{Words(p.Metadata.Name)}: {Text(p.Value)}"));

    /// <summary>The values worth writing down: not the row's own id, or the version stamp SQL Server keeps.</summary>
    private static IEnumerable<PropertyEntry> Recorded(EntityEntry entry) =>
        entry.Properties.Where(p => !p.Metadata.IsPrimaryKey() && !p.Metadata.IsConcurrencyToken);

    private static string Text(object? value) => value switch
    {
        null => "empty",
        DateTimeOffset moment => NbcTime.Text(moment),
        string text when text.Length > LongestValue => $"{text[..LongestValue]}…",
        _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? ""
    };

    /// <summary>RoleAssignment reads as "Role Assignment", and OrgUnitId as "Org Unit".</summary>
    private static string Words(string name) =>
        CapitalLetter().Replace(name.EndsWith("Id", StringComparison.Ordinal) && name.Length > 2 ? name[..^2] : name, " ");

    [GeneratedRegex("(?<=[a-z])(?=[A-Z])")]
    private static partial Regex CapitalLetter();
}
