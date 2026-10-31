using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using PBRMS.Api.Auth;
using PBRMS.Data;
using PBRMS.Domain;

namespace PBRMS.Api.Endpoints;

/// <summary>
/// APP bookings (see <see cref="AppSession"/>), and the report on who used APP.
/// </summary>
public static class AppSessionEndpoints
{
    public record AppSessionForm(Guid OperatorId, string Production, DateTimeOffset StartsAt, DateTimeOffset EndsAt, string? Note);

    public record AppNoteRequest(string? Note);

    public record AppSuggestionRequest(DateTimeOffset StartsAt, DateTimeOffset EndsAt, string? Note);

    public record AppSessionResponse(
        Guid Id, string Production, string Requester, string Operator, DateTimeOffset StartsAt, DateTimeOffset EndsAt,
        AppSessionStatus Status, string? Note, DateTimeOffset? SuggestedStartsAt, DateTimeOffset? SuggestedEndsAt, bool IsWalkIn);

    public record AppOperatorResponse(Guid Id, string Name);

    public record AppBusyTime(DateTimeOffset StartsAt, DateTimeOffset EndsAt, string Operator);

    /// <summary>Checking in to the room now. EndsAt is when they expect to finish.</summary>
    public record AppWalkInForm(Guid OperatorId, string Production, DateTimeOffset EndsAt, string? Note);

    /// <summary>Is the room in use right now? The details are empty when it's free. NextStartsAt is the next session after this one.</summary>
    public record AppRoomStatus(bool Busy, string? Production, string? BookedBy, string? Operator, DateTimeOffset? Until, DateTimeOffset? NextStartsAt);

    public record AppUsageRow(string Production, string Requester, Role RequesterRole, string Operator, DateTimeOffset StartsAt, DateTimeOffset EndsAt, int Minutes, bool WalkIn);

    public record AppUsageTotal(string Name, int Sessions, int Minutes, int WalkIns);

    /// <summary>Every confirmed session, with totals by production and by the person who asked.</summary>
    public record AppUsageReport(List<AppUsageRow> Sessions, List<AppUsageTotal> ByProduction, List<AppUsageTotal> ByRequester);

    public static void MapAppSessionEndpoints(this WebApplication app)
    {
        string[] requesters = [nameof(Role.Producer), nameof(Role.Editor)];
        var bookingOfficer = nameof(Role.BookingOfficer);

        app.MapGet("/app-operators", AppOperators).RequireAuthorization();
        app.MapGet("/app-room/busy", RoomBusy).RequireAuthorization();
        app.MapGet("/app-room/status", RoomStatus).RequireAuthorization();
        app.MapPost("/app-sessions/walk-in", WalkIn).RequireAuthorization(p => p.RequireRole(requesters));
        app.MapPost("/app-sessions/{id:guid}/finish", Finish).RequireAuthorization();
        app.MapPost("/app-sessions", Request).RequireAuthorization(p => p.RequireRole(requesters));
        app.MapGet("/app-sessions/mine", Mine).RequireAuthorization();
        app.MapGet("/app-sessions/queue", WaitingForATime).RequireAuthorization(p => p.RequireRole(bookingOfficer));
        app.MapPost("/app-sessions/{id:guid}/confirm", Confirm).RequireAuthorization();
        app.MapPost("/app-sessions/{id:guid}/unavailable", Unavailable).RequireAuthorization();
        app.MapPost("/app-sessions/{id:guid}/suggest-time", SuggestTime).RequireAuthorization(p => p.RequireRole(bookingOfficer));
        app.MapPost("/app-sessions/{id:guid}/accept", Accept).RequireAuthorization();
        app.MapPost("/app-sessions/{id:guid}/cancel", Cancel).RequireAuthorization();
        app.MapGet("/reports/app-usage", Usage).RequireAuthorization(p => p.RequireRole(
            nameof(Role.Supervisor), nameof(Role.TvOperationsManager), nameof(Role.Admin)));
    }

    /// <summary>The APP operators, for the producer or editor to choose from.</summary>
    private static async Task<List<AppOperatorResponse>> AppOperators(PbrmsDbContext db) =>
        await db.RoleAssignments.AsNoTracking()
            .Where(r => r.Role == Role.AppOperator && r.User.IsActive)
            .OrderBy(r => r.User.Surname)
            .Select(r => new AppOperatorResponse(r.UserId, r.User.FirstName + " " + r.User.Surname))
            .ToListAsync();

    /// <summary>
    /// When the room is taken, so people can pick a free time and book it, rather than
    /// walking in on an operator who has had no time to plan or finish their work.
    /// </summary>
    private static async Task<List<AppBusyTime>> RoomBusy(DateOnly from, DateOnly to, PbrmsDbContext db)
    {
        var (start, end) = (NbcTime.At(from, TimeOnly.MinValue), NbcTime.At(to.AddDays(1), TimeOnly.MinValue));

        return await db.AppSessions.AsNoTracking()
            .Where(s => s.Status == AppSessionStatus.Confirmed && s.StartsAt < end && start < s.EndsAt)
            .OrderBy(s => s.StartsAt)
            .Select(s => new AppBusyTime(s.StartsAt, s.EndsAt, s.Operator.FirstName + " " + s.Operator.Surname))
            .ToListAsync();
    }

    /// <summary>What's happening in the room right now, for any producer or editor wondering whether to go down.</summary>
    private static async Task<AppRoomStatus> RoomStatus(PbrmsDbContext db)
    {
        var now = DateTimeOffset.UtcNow;

        var sessions = await db.AppSessions.AsNoTracking()
            .Where(s => s.Status == AppSessionStatus.Confirmed && s.EndsAt > now)
            .OrderBy(s => s.StartsAt)
            .Take(2)
            .Select(s => new
            {
                s.Production,
                BookedBy = s.Requester.FirstName + " " + s.Requester.Surname,
                Operator = s.Operator.FirstName + " " + s.Operator.Surname,
                s.StartsAt,
                s.EndsAt
            })
            .ToListAsync();

        var current = sessions.FirstOrDefault(s => s.StartsAt <= now);
        var next = sessions.FirstOrDefault(s => s.StartsAt > now);

        return new AppRoomStatus(current is not null, current?.Production, current?.BookedBy, current?.Operator, current?.EndsAt, next?.StartsAt);
    }

    /// <summary>
    /// A producer or editor needs the room now and checks themselves in. If it's free the
    /// session is confirmed straight away. The operator is told but doesn't have to do
    /// anything: keeping the room's status right mustn't become the operator's job.
    /// </summary>
    private static async Task<IResult> WalkIn(AppWalkInForm form, ClaimsPrincipal principal, PbrmsDbContext db, ClashChecker clashChecker, Notifier notifier)
    {
        var now = DateTimeOffset.UtcNow;

        if (string.IsNullOrWhiteSpace(form.Production) || form.Production.Trim().Length > 160)
            return Problems.Invalid("production", "Name the production in up to 160 characters.");
        if (form.EndsAt <= now)
            return Problems.Invalid("endsAt", "Say when you expect to finish.");
        if (form.Note?.Trim().Length > 500)
            return Problems.Invalid("note", "Keep the note to 500 characters.");
        if (!await db.RoleAssignments.AnyAsync(r => r.UserId == form.OperatorId && r.Role == Role.AppOperator && r.User.IsActive))
            return Problems.Invalid("operatorId", "That person isn't an active APP operator.");

        await using var transaction = await db.Database.BeginTransactionAsync();

        var session = new AppSession
        {
            Production = form.Production.Trim(),
            RequesterId = PbrmsClaims.UserIdOf(principal),
            OperatorId = form.OperatorId,
            StartsAt = now,
            EndsAt = form.EndsAt,
            Note = form.Note?.Trim(),
            Status = AppSessionStatus.Confirmed,
            IsWalkIn = true,
            CreatedAt = now,
            ConfirmedAt = now
        };

        if (await BusyAsync(session, now, form.EndsAt, db, clashChecker) is { } busy)
            return busy;

        db.AppSessions.Add(session);
        notifier.Send([session.OperatorId], $"{session.Production} is in APP now, until {NbcTime.Text(session.EndsAt)}.");

        await db.SaveChangesAsync();
        await transaction.CommitAsync();

        return Results.Created($"/app-sessions/{session.Id}", await LoadAsync(db, session.Id));
    }

    /// <summary>
    /// The person using the room says they've finished, so it shows as free. If they forget,
    /// it frees itself at the time they gave. The Booking Officer can do this for them.
    /// </summary>
    private static async Task<IResult> Finish(Guid id, ClaimsPrincipal principal, PbrmsDbContext db)
    {
        var session = await db.AppSessions.FirstOrDefaultAsync(s => s.Id == id);
        if (session is null)
            return Results.NotFound();

        var caller = await Caller.LoadAsync(principal, db);
        if (session.RequesterId != caller.UserId && !caller.Holds(Role.BookingOfficer))
            return Problems.Forbidden("Only the person who booked this session or the Booking Officer can finish it.");

        var now = DateTimeOffset.UtcNow;
        if (session.Status != AppSessionStatus.Confirmed || session.StartsAt > now || session.EndsAt <= now)
            return Problems.Conflict("This session isn't running now.");

        session.EndsAt = now;
        return await SaveAsync(id, db);
    }

    private static async Task<IResult> Request(AppSessionForm form, ClaimsPrincipal principal, PbrmsDbContext db, ClashChecker clashChecker, Notifier notifier)
    {
        if (string.IsNullOrWhiteSpace(form.Production) || form.Production.Trim().Length > 160)
            return Problems.Invalid("production", "Name the production in up to 160 characters.");
        if (form.EndsAt <= form.StartsAt)
            return Problems.Invalid("endsAt", "The end must be after the start.");
        if (form.Note?.Trim().Length > 500)
            return Problems.Invalid("note", "Keep the note to 500 characters.");
        if (!await db.RoleAssignments.AnyAsync(r => r.UserId == form.OperatorId && r.Role == Role.AppOperator && r.User.IsActive))
            return Problems.Invalid("operatorId", "That person isn't an active APP operator.");

        // Say so now rather than after the operator has looked at it.
        var taken = await clashChecker.AppRoomAsync(form.StartsAt, form.EndsAt);
        if (taken.Count > 0)
            return Problems.Clashes("The APP room is booked at that time. See GET /app-room/busy for when it's free.", taken);

        var session = new AppSession
        {
            Production = form.Production.Trim(),
            RequesterId = PbrmsClaims.UserIdOf(principal),
            OperatorId = form.OperatorId,
            StartsAt = form.StartsAt,
            EndsAt = form.EndsAt,
            Note = form.Note?.Trim(),
            CreatedAt = DateTimeOffset.UtcNow
        };
        db.AppSessions.Add(session);

        notifier.Send([session.OperatorId], $"APP booking for {session.Production} at {NbcTime.Text(session.StartsAt)}. Please confirm it.");
        await db.SaveChangesAsync();

        return Results.Created($"/app-sessions/{session.Id}", await LoadAsync(db, session.Id));
    }

    /// <summary>The sessions the signed-in person asked for, or is the operator on.</summary>
    private static async Task<List<AppSessionResponse>> Mine(ClaimsPrincipal principal, PbrmsDbContext db)
    {
        var userId = PbrmsClaims.UserIdOf(principal);
        return await Sessions(db).Where(s => s.RequesterId == userId || s.OperatorId == userId).OrderBy(s => s.StartsAt).Select(Show).ToListAsync();
    }

    /// <summary>Sessions waiting for the Booking Officer to suggest a time.</summary>
    private static async Task<List<AppSessionResponse>> WaitingForATime(PbrmsDbContext db) =>
        await Sessions(db).Where(s => s.Status == AppSessionStatus.Unavailable).OrderBy(s => s.StartsAt).Select(Show).ToListAsync();

    /// <summary>The APP operator agrees to the time. They can't confirm over a production they're on.</summary>
    private static async Task<IResult> Confirm(Guid id, ClaimsPrincipal principal, PbrmsDbContext db, ClashChecker clashChecker, Notifier notifier)
    {
        await using var transaction = await db.Database.BeginTransactionAsync();

        var session = await db.AppSessions.FirstOrDefaultAsync(s => s.Id == id);
        if (session is null)
            return Results.NotFound();
        if (session.OperatorId != PbrmsClaims.UserIdOf(principal))
            return Problems.Forbidden("Only the APP operator on this booking can confirm it.");
        if (session.Status != AppSessionStatus.Requested)
            return Problems.Conflict($"This booking is {session.Status}, so it can't be confirmed.");

        if (await BusyAsync(session, session.StartsAt, session.EndsAt, db, clashChecker) is { } busy)
            return busy;

        await MarkConfirmedAsync(session, db, notifier);
        return await SaveAsync(id, db, transaction);
    }

    /// <summary>The APP operator can't do that time, so the Booking Officer is asked to suggest one.</summary>
    private static async Task<IResult> Unavailable(Guid id, AppNoteRequest request, ClaimsPrincipal principal, PbrmsDbContext db, Notifier notifier)
    {
        var session = await db.AppSessions.FirstOrDefaultAsync(s => s.Id == id);
        if (session is null)
            return Results.NotFound();
        if (session.OperatorId != PbrmsClaims.UserIdOf(principal))
            return Problems.Forbidden("Only the APP operator on this booking can say they're unavailable.");
        if (session.Status != AppSessionStatus.Requested)
            return Problems.Conflict($"This booking is {session.Status}, so that can't be done.");

        session.Status = AppSessionStatus.Unavailable;
        session.Note = request.Note?.Trim() ?? session.Note;

        var bookingOfficers = await notifier.PeopleWithRoleAsync(Role.BookingOfficer);
        notifier.Send(bookingOfficers, $"APP booking for {session.Production} at {NbcTime.Text(session.StartsAt)} can't be done then. Please suggest a suitable time.");
        notifier.Send([session.RequesterId], $"Your APP booking for {session.Production} at {NbcTime.Text(session.StartsAt)} isn't available at that time. The Booking Officer will suggest another.");

        return await SaveAsync(id, db);
    }

    /// <summary>The Booking Officer suggests a time the operator is free.</summary>
    private static async Task<IResult> SuggestTime(Guid id, AppSuggestionRequest request, ClaimsPrincipal principal, PbrmsDbContext db, ClashChecker clashChecker, Notifier notifier)
    {
        var session = await db.AppSessions.FirstOrDefaultAsync(s => s.Id == id);
        if (session is null)
            return Results.NotFound();
        if (session.Status is not (AppSessionStatus.Unavailable or AppSessionStatus.TimeSuggested))
            return Problems.Conflict($"This booking is {session.Status}, so a time can't be suggested.");
        if (request.EndsAt <= request.StartsAt)
            return Problems.Invalid("endsAt", "The end must be after the start.");

        if (await BusyAsync(session, request.StartsAt, request.EndsAt, db, clashChecker) is { } busy)
            return busy;

        session.Status = AppSessionStatus.TimeSuggested;
        session.SuggestedStartsAt = request.StartsAt;
        session.SuggestedEndsAt = request.EndsAt;
        session.SuggestedById = PbrmsClaims.UserIdOf(principal);
        session.Note = request.Note?.Trim() ?? session.Note;

        notifier.Send([session.RequesterId], $"Suggested time for your APP booking for {session.Production}: {NbcTime.Text(request.StartsAt)}. Please accept it or cancel.");

        return await SaveAsync(id, db);
    }

    /// <summary>The producer or editor takes the suggested time, which confirms the booking.</summary>
    private static async Task<IResult> Accept(Guid id, ClaimsPrincipal principal, PbrmsDbContext db, ClashChecker clashChecker, Notifier notifier)
    {
        await using var transaction = await db.Database.BeginTransactionAsync();

        var session = await db.AppSessions.FirstOrDefaultAsync(s => s.Id == id);
        if (session is null)
            return Results.NotFound();
        if (session.RequesterId != PbrmsClaims.UserIdOf(principal))
            return Problems.Forbidden("Only the person who made this booking can accept the time.");
        if (session.Status != AppSessionStatus.TimeSuggested)
            return Problems.Conflict("There is no suggested time to accept.");

        // The operator may have been booked since the time was suggested.
        var (start, end) = (session.SuggestedStartsAt!.Value, session.SuggestedEndsAt!.Value);
        if (await BusyAsync(session, start, end, db, clashChecker) is { } busy)
            return busy;

        session.StartsAt = start;
        session.EndsAt = end;
        await MarkConfirmedAsync(session, db, notifier);

        return await SaveAsync(id, db, transaction);
    }

    private static async Task<IResult> Cancel(Guid id, AppNoteRequest request, ClaimsPrincipal principal, PbrmsDbContext db, Notifier notifier)
    {
        var session = await db.AppSessions.FirstOrDefaultAsync(s => s.Id == id);
        if (session is null)
            return Results.NotFound();

        var caller = await Caller.LoadAsync(principal, db);
        var involved = session.RequesterId == caller.UserId || session.OperatorId == caller.UserId || caller.Holds(Role.BookingOfficer);
        if (!involved)
            return Problems.Forbidden("Only the people on this booking or the Booking Officer can cancel it.");
        if (session.Status == AppSessionStatus.Cancelled)
            return Problems.Conflict("This booking is already cancelled.");

        session.Status = AppSessionStatus.Cancelled;
        session.Note = request.Note?.Trim() ?? session.Note;

        notifier.Send(
            new[] { session.RequesterId, session.OperatorId }.Where(person => person != caller.UserId),
            $"The APP booking for {session.Production} at {NbcTime.Text(session.StartsAt)} has been cancelled.");

        return await SaveAsync(id, db);
    }

    /// <summary>Which productions, editors and producers used APP (confirmed sessions in the period).</summary>
    private static async Task<AppUsageReport> Usage(DateOnly from, DateOnly to, PbrmsDbContext db)
    {
        var (start, end) = (NbcTime.At(from, TimeOnly.MinValue), NbcTime.At(to.AddDays(1), TimeOnly.MinValue));

        var sessions = await db.AppSessions.AsNoTracking()
            .Where(s => s.Status == AppSessionStatus.Confirmed && s.StartsAt >= start && s.StartsAt < end)
            .OrderBy(s => s.StartsAt)
            .Select(s => new
            {
                s.Production,
                Requester = s.Requester.FirstName + " " + s.Requester.Surname,
                IsEditor = s.Requester.Roles.Any(r => r.Role == Role.Editor),
                s.IsWalkIn,
                Operator = s.Operator.FirstName + " " + s.Operator.Surname,
                s.StartsAt,
                s.EndsAt
            })
            .ToListAsync();

        var rows = sessions
            .Select(s => new AppUsageRow(
                s.Production, s.Requester, s.IsEditor ? Role.Editor : Role.Producer, s.Operator,
                s.StartsAt, s.EndsAt, (int)(s.EndsAt - s.StartsAt).TotalMinutes, s.IsWalkIn))
            .ToList();

        return new AppUsageReport(rows, Totals(rows, r => r.Production), Totals(rows, r => r.Requester));
    }

    private static List<AppUsageTotal> Totals(List<AppUsageRow> rows, Func<AppUsageRow, string> key) =>
        rows.GroupBy(key)
            .Select(g => new AppUsageTotal(g.Key, g.Count(), g.Sum(r => r.Minutes), g.Count(r => r.WalkIn)))
            .OrderByDescending(t => t.Minutes)
            .ToList();

    /// <summary>
    /// Is the APP room or the operator booked at that time? Returns the problem if so. NBC
    /// has one room, so a session clashes with any other, whichever operator is on it. A day
    /// off is only a warning elsewhere, so it doesn't stop a session either. Takes the clash
    /// locks when there's a transaction, so two confirmations can't both pass.
    /// </summary>
    private static async Task<IResult?> BusyAsync(AppSession session, DateTimeOffset from, DateTimeOffset to, PbrmsDbContext db, ClashChecker clashChecker)
    {
        if (db.Database.CurrentTransaction is not null)
            await ClashLocks.TakeAsync(db, [ClashLocks.ForOperator(session.OperatorId), ClashLocks.ForVenue(Venue.App)]);

        var room = await clashChecker.AppRoomAsync(from, to);
        if (room.Count > 0)
            return Problems.Clashes("The APP room is booked at that time.", room);

        var clashes = await clashChecker.ForOperatorAsync(session.OperatorId, from, to);
        var blocking = clashes.Where(c => !c.IsWarning).ToList();
        return blocking.Count == 0 ? null : Problems.Clashes("The APP operator is busy at that time.", blocking);
    }

    /// <summary>Confirmed: the person who asked, the operator, their supervisors and the Booking Officer are told (App Flow Document, "APP session").</summary>
    private static async Task MarkConfirmedAsync(AppSession session, PbrmsDbContext db, Notifier notifier)
    {
        session.Status = AppSessionStatus.Confirmed;
        session.ConfirmedAt = DateTimeOffset.UtcNow;

        var divisions = await db.RoleAssignments.AsNoTracking()
            .Where(r => r.UserId == session.OperatorId && r.Role == Role.AppOperator && r.OrgUnitId != null)
            .Select(r => r.OrgUnitId!.Value)
            .ToListAsync();
        var supervisors = await notifier.PeopleWithRoleAsync(Role.Supervisor, divisions);
        var bookingOfficers = await notifier.PeopleWithRoleAsync(Role.BookingOfficer);

        notifier.Send(
            supervisors.Concat(bookingOfficers).Append(session.RequesterId).Append(session.OperatorId),
            $"APP booking for {session.Production} is confirmed for {NbcTime.Text(session.StartsAt)}.");
    }

    private static async Task<IResult> SaveAsync(Guid id, PbrmsDbContext db, Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction? transaction = null)
    {
        try
        {
            await db.SaveChangesAsync();
            if (transaction is not null)
                await transaction.CommitAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            return Problems.Conflict("Someone else changed this booking at the same moment. Reload it and try again.");
        }

        return Results.Ok(await LoadAsync(db, id));
    }

    private static IQueryable<AppSession> Sessions(PbrmsDbContext db) => db.AppSessions.AsNoTracking();

    private static readonly System.Linq.Expressions.Expression<Func<AppSession, AppSessionResponse>> Show = s => new AppSessionResponse(
        s.Id, s.Production,
        s.Requester.FirstName + " " + s.Requester.Surname,
        s.Operator.FirstName + " " + s.Operator.Surname,
        s.StartsAt, s.EndsAt, s.Status, s.Note, s.SuggestedStartsAt, s.SuggestedEndsAt, s.IsWalkIn);

    private static Task<AppSessionResponse> LoadAsync(PbrmsDbContext db, Guid id) =>
        Sessions(db).Where(s => s.Id == id).Select(Show).SingleAsync();
}
