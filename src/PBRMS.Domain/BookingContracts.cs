namespace PBRMS.Domain;

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

/// <summary>Move a booking on: submit, approve, send back, start, end or cancel.</summary>
/// <param name="Note">Why. Required when sending back or cancelling.</param>
/// <param name="Channel">The EP may set the channel when approving.</param>
/// <param name="AirAt">The EP may set the air date and time when approving.</param>
/// <param name="AcknowledgeWarnings">The Booking Officer has seen the warnings on this booking and wants to confirm anyway.</param>
public record TransitionRequest(BookingAction Action, string? Note, Channel? Channel, DateTimeOffset? AirAt, bool AcknowledgeWarnings = false);

/// <summary>Fill or empty crew positions on a booking. Positions not listed stay as they are.</summary>
/// <param name="AcknowledgeWarnings">
/// The person has seen the warnings (a day off, another studio) and wants to go ahead.
/// Without it, a warning comes back as a 409 listing them, and nothing is saved.
/// </param>
/// <param name="OverrideReason">
/// The Booking Officer's reason for filling outside crew while a studio is still short, only if
/// needed. It's recorded in the booking's history. Other clashes can't be overridden.
/// </param>
public record CrewRequest(List<CrewChange> Assignments, bool AcknowledgeWarnings = false, string? OverrideReason = null);

/// <summary>The producer marks whether the operator on this position has arrived.</summary>
public record AttendanceRequest(Guid SlotId, bool Arrived);

/// <summary>The producer urgently signals the operator on this position, who hasn't arrived.</summary>
public record SignalRequest(Guid SlotId);

/// <summary>Why the Booking Officer is making an exception.</summary>
public record OverrideRequest(string? Reason);

/// <summary>Put this operator on this slot. An empty OperatorId takes the current operator off it.</summary>
public record CrewChange(Guid SlotId, Guid? OperatorId);

/// <summary>The Booking Officer's changes to where and when a booking happens.</summary>
public record ScheduleRequest(Venue Venue, string? VenueText, DateTimeOffset CallAt, DateTimeOffset StartsAt, DateTimeOffset EndsAt);

/// <summary>A booking as the apps see it, with its crew positions and history. CrewLead says who is deciding while crew is being assigned.</summary>
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
    DateTimeOffset? ActualStartAt,
    DateTimeOffset? ActualEndAt,
    bool IsTrip,
    bool IsOb,
    string? AllocationCode,
    string? Notes,
    List<CrewSlotResponse> CrewSlots,
    List<StatusChangeResponse> History,
    Role? CrewLead,
    bool LeavesTheBuilding,
    DateTimeOffset? DepartedAt,
    DateTimeOffset? DepartureMissedAt)
{
    /// <summary>Builds the response from a booking loaded with its crew, producer and history.</summary>
    public static BookingResponse From(Booking b) => new(
        b.Id, b.ProductionNo, b.Title, b.Status,
        b.DepartmentId, b.Department.Name,
        b.ProducerId, $"{b.Producer.FirstName} {b.Producer.Surname}",
        b.ActivityType, b.Venue, b.VenueText,
        b.CallAt, b.StartsAt, b.EndsAt, b.AirAt, b.Channel, b.ActualStartAt, b.ActualEndAt,
        b.IsTrip, b.IsOb, b.AllocationCode, b.Notes,
        b.CrewSlots
            .OrderBy(s => s.Division.Name).ThenBy(s => s.PositionLabel)
            .Select(s => new CrewSlotResponse(
                s.Id, s.DivisionId, s.Division.Name, s.PositionLabel, s.StartsAt, s.EndsAt, s.SpecialEquipment,
                s.Current?.OperatorId,
                s.Current is null ? null : $"{s.Current.Operator.FirstName} {s.Current.Operator.Surname}",
                // Phone numbers only once the crew is final: the producer's confirmed crew list (E1-03).
                b.Status is BookingStatus.Confirmed or BookingStatus.InProgress ? s.Current?.Operator.Phone : null,
                s.Current?.ArrivedAt,
                s.Current?.SignalledAt))
            .ToList(),
        b.History
            .OrderBy(h => h.At)
            .Select(h => new StatusChangeResponse(h.FromStatus, h.ToStatus, $"{h.Actor.FirstName} {h.Actor.Surname}", h.Note, h.At))
            .ToList(),
        b.Status == BookingStatus.CrewAssignment ? BookingWorkflow.CrewLead(b.Venue) : null,
        BookingWorkflow.LeavesTheBuilding(b),
        b.DepartedAt,
        b.DepartureMissedAt);
}

/// <summary>A crew position and who is on it. The operator fields are null while it is empty.</summary>
public record CrewSlotResponse(
    Guid Id,
    Guid DivisionId,
    string Division,
    string PositionLabel,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    string? SpecialEquipment,
    Guid? OperatorId,
    string? Operator,
    string? OperatorPhone,
    DateTimeOffset? ArrivedAt,
    DateTimeOffset? SignalledAt);

public record StatusChangeResponse(BookingStatus From, BookingStatus To, string By, string? Note, DateTimeOffset At);

