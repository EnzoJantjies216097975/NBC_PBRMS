using Microsoft.EntityFrameworkCore;
using PBRMS.Data;
using PBRMS.Domain;

namespace PBRMS.Api.Endpoints;

// The shapes of booking data the apps send and receive. They are kept separate from
// the database classes so the database can change without breaking the apps.

/// <summary>The booking form the producer fills in, used for both creating and editing a draft.</summary>
/// <param name="ProducerId">Leave empty to be the producer yourself. An EP or the Booking Officer can name another producer. Ignored when editing.</param>
/// <param name="CrewSlots">The full list of crew positions. When editing, it replaces the old list.</param>
public record BookingForm(
    string Title,
    Guid DepartmentId,
    Guid? ProducerId,
    ActivityType ActivityType,
    Venue Venue,
    string? VenueText,
    DateTimeOffset CallAt,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    bool IsTrip,
    bool IsOb,
    string? AllocationCode,
    string? Notes,
    List<CrewSlotForm>? CrewSlots);

/// <summary>One crew position on the form. Leave the times empty to use the booking's own times.</summary>
public record CrewSlotForm(Guid DivisionId, string PositionLabel, DateTimeOffset? StartsAt, DateTimeOffset? EndsAt, string? SpecialEquipment);

/// <summary>Move a booking on: submit, approve, send back or cancel.</summary>
/// <param name="Note">Why. Required when sending back or cancelling.</param>
/// <param name="Channel">The EP may set the channel when approving.</param>
/// <param name="AirAt">The EP may set the air date and time when approving.</param>
public record TransitionRequest(BookingAction Action, string? Note, Channel? Channel, DateTimeOffset? AirAt);

/// <summary>A booking as the apps see it, with its crew positions and history.</summary>
public record BookingResponse(
    Guid Id,
    int ProductionNo,
    string Title,
    BookingStatus Status,
    Guid DepartmentId,
    string Department,
    Guid ProducerId,
    string Producer,
    ActivityType ActivityType,
    Venue Venue,
    string? VenueText,
    DateTimeOffset CallAt,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    DateTimeOffset? AirAt,
    Channel? Channel,
    bool IsTrip,
    bool IsOb,
    string? AllocationCode,
    string? Notes,
    List<CrewSlotResponse> CrewSlots,
    List<StatusChangeResponse> History)
{
    /// <summary>Builds the response from a booking loaded with <see cref="BookingQueries.WithDetails"/>.</summary>
    public static BookingResponse From(Booking b) => new(
        b.Id, b.ProductionNo, b.Title, b.Status,
        b.DepartmentId, b.Department.Name,
        b.ProducerId, $"{b.Producer.FirstName} {b.Producer.Surname}",
        b.ActivityType, b.Venue, b.VenueText,
        b.CallAt, b.StartsAt, b.EndsAt, b.AirAt, b.Channel,
        b.IsTrip, b.IsOb, b.AllocationCode, b.Notes,
        b.CrewSlots
            .OrderBy(s => s.Division.Name).ThenBy(s => s.PositionLabel)
            .Select(s => new CrewSlotResponse(s.Id, s.DivisionId, s.Division.Name, s.PositionLabel, s.StartsAt, s.EndsAt, s.SpecialEquipment))
            .ToList(),
        b.History
            .OrderBy(h => h.At)
            .Select(h => new StatusChangeResponse(h.FromStatus, h.ToStatus, $"{h.Actor.FirstName} {h.Actor.Surname}", h.Note, h.At))
            .ToList());
}

public record CrewSlotResponse(Guid Id, Guid DivisionId, string Division, string PositionLabel, DateTimeOffset StartsAt, DateTimeOffset EndsAt, string? SpecialEquipment);

public record StatusChangeResponse(BookingStatus From, BookingStatus To, string By, string? Note, DateTimeOffset At);

/// <summary>Loading bookings with everything a <see cref="BookingResponse"/> shows.</summary>
public static class BookingQueries
{
    public static IQueryable<Booking> WithDetails(this IQueryable<Booking> bookings) => bookings
        .AsNoTracking()
        .Include(b => b.Department)
        .Include(b => b.Producer)
        .Include(b => b.CrewSlots).ThenInclude(s => s.Division)
        .Include(b => b.History).ThenInclude(h => h.Actor)
        .AsSplitQuery(); // one query per list instead of one huge joined result

    public static async Task<BookingResponse> LoadResponseAsync(PbrmsDbContext db, Guid bookingId) =>
        BookingResponse.From(await db.Bookings.WithDetails().SingleAsync(b => b.Id == bookingId));
}
