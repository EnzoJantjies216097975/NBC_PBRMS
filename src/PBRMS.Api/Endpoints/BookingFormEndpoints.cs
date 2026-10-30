using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using PBRMS.Api.Auth;
using PBRMS.Data;
using PBRMS.Domain;

namespace PBRMS.Api.Endpoints;

/// <summary>
/// The booking form: create a draft, and edit it while it is still a draft,
/// including after it was sent back to be fixed (E1-01).
/// </summary>
public static class BookingFormEndpoints
{
    public static void MapBookingFormEndpoints(this WebApplication app)
    {
        // Schema document: Producers create bookings; an EP or the Booking Officer can too, on a producer's behalf.
        app.MapPost("/bookings", CreateDraft)
            .RequireAuthorization(p => p.RequireRole(nameof(Role.Producer), nameof(Role.ExecutiveProducer), nameof(Role.BookingOfficer)));

        app.MapPut("/bookings/{id:guid}", EditDraft).RequireAuthorization();
    }

    private static async Task<IResult> CreateDraft(BookingForm form, ClaimsPrincipal principal, PbrmsDbContext db)
    {
        var caller = await Caller.LoadAsync(principal, db);
        var units = await db.OrgUnits.AsNoTracking().ToListAsync();

        var errors = Validate(form, units);
        if (errors.Count > 0)
            return Results.ValidationProblem(errors);

        // The caller is the producer, unless an EP or the Booking Officer names someone else.
        var producerId = form.ProducerId ?? caller.UserId;
        if (producerId != caller.UserId && !caller.Holds(Role.ExecutiveProducer, Role.BookingOfficer))
            return Problems.Forbidden("Only an Executive Producer or the Booking Officer can book on another producer's behalf.");

        if (!await IsProducerOfAsync(db, producerId, form.DepartmentId, units))
            return Problems.Forbidden("The producer must be an active Producer in the booking's department.");

        var booking = new Booking { ProducerId = producerId };
        Fill(booking, form);
        db.Bookings.Add(booking);
        await db.SaveChangesAsync();

        return Results.Created($"/bookings/{booking.Id}", await BookingQueries.LoadResponseAsync(db, booking.Id));
    }

    private static async Task<IResult> EditDraft(Guid id, BookingForm form, ClaimsPrincipal principal, PbrmsDbContext db)
    {
        var booking = await db.Bookings.Include(b => b.CrewSlots).FirstOrDefaultAsync(b => b.Id == id);
        if (booking is null)
            return Results.NotFound();

        if (booking.ProducerId != PbrmsClaims.UserIdOf(principal))
            return Problems.Forbidden("Only the booking's producer can edit it.");
        if (booking.Status != BookingStatus.Draft)
            return Problems.Conflict($"Only drafts can be edited, and this booking is in {booking.Status}.");

        var units = await db.OrgUnits.AsNoTracking().ToListAsync();
        var errors = Validate(form, units);
        if (errors.Count > 0)
            return Results.ValidationProblem(errors);

        // The producer may have moved the booking to another department; they must belong to that one too.
        if (!await IsProducerOfAsync(db, booking.ProducerId, form.DepartmentId, units))
            return Problems.Forbidden("You must be a Producer in the booking's department.");

        Fill(booking, form);
        await db.SaveChangesAsync();

        return Results.Ok(await BookingQueries.LoadResponseAsync(db, id));
    }

    /// <summary>Is this person an active Producer in the department, or in a unit above it?</summary>
    private static async Task<bool> IsProducerOfAsync(PbrmsDbContext db, Guid userId, Guid departmentId, List<OrgUnit> units)
    {
        var roles = await db.RoleAssignments.AsNoTracking()
            .Where(r => r.UserId == userId && r.User.IsActive)
            .ToListAsync();
        return BookingWorkflow.HoldsRoleIn(roles, Role.Producer, OrgUnit.ChainUp(departmentId, units));
    }

    /// <summary>Copies the form onto the booking. The crew list is replaced as a whole, because the form always carries the full list.</summary>
    private static void Fill(Booking booking, BookingForm form)
    {
        booking.Title = form.Title.Trim();
        booking.DepartmentId = form.DepartmentId;
        booking.ActivityType = form.ActivityType;
        booking.Venue = form.Venue;
        booking.VenueText = form.VenueText?.Trim();
        booking.CallAt = form.CallAt;
        booking.StartsAt = form.StartsAt;
        booking.EndsAt = form.EndsAt;
        booking.IsTrip = form.IsTrip;
        booking.IsOb = form.IsOb;
        booking.AllocationCode = form.AllocationCode?.Trim();
        booking.Notes = form.Notes?.Trim();

        booking.CrewSlots.Clear();
        booking.CrewSlots.AddRange((form.CrewSlots ?? []).Select(slot => new CrewSlot
        {
            DivisionId = slot.DivisionId,
            PositionLabel = slot.PositionLabel.Trim(),
            StartsAt = slot.StartsAt ?? form.StartsAt,
            EndsAt = slot.EndsAt ?? form.EndsAt,
            SpecialEquipment = slot.SpecialEquipment?.Trim()
        }));
    }

    /// <summary>Checks the form. Returns each field that needs fixing, with why; an empty result means it is fine.</summary>
    private static Dictionary<string, string[]> Validate(BookingForm form, List<OrgUnit> units)
    {
        var errors = new Dictionary<string, string[]>();
        void Error(string field, string message) => errors[field] = [message];
        void MaxLength(string field, string? value, int max)
        {
            if (value?.Trim().Length > max) Error(field, $"Keep this to {max} characters.");
        }

        if (string.IsNullOrWhiteSpace(form.Title))
            Error("title", "Give the production a title.");
        MaxLength("title", form.Title, 160);
        MaxLength("venueText", form.VenueText, 160);
        MaxLength("allocationCode", form.AllocationCode, 30);
        MaxLength("notes", form.Notes, 2000);

        if (form.EndsAt <= form.StartsAt)
            Error("endsAt", "The end must be after the start.");
        if (form.CallAt > form.StartsAt)
            Error("callAt", "The crew call must be at or before the start.");

        var department = units.FirstOrDefault(u => u.Id == form.DepartmentId);
        if (department is null || department.Kind == OrgUnitKind.TvOpsDivision)
            Error("departmentId", "Choose the content department the production is for.");

        var slots = form.CrewSlots ?? [];
        for (var i = 0; i < slots.Count; i++)
        {
            var slot = slots[i];
            var field = $"crewSlots[{i}]";

            if (units.FirstOrDefault(u => u.Id == slot.DivisionId)?.Kind != OrgUnitKind.TvOpsDivision)
                Error($"{field}.divisionId", "Choose the TV Ops division that supplies this position.");
            if (string.IsNullOrWhiteSpace(slot.PositionLabel))
                Error($"{field}.positionLabel", "Name the position, e.g. \"Camera 2\".");
            MaxLength($"{field}.positionLabel", slot.PositionLabel, 60);
            MaxLength($"{field}.specialEquipment", slot.SpecialEquipment, 200);
            if ((slot.EndsAt ?? form.EndsAt) <= (slot.StartsAt ?? form.StartsAt))
                Error($"{field}.endsAt", "The end must be after the start.");
        }

        return errors;
    }
}
