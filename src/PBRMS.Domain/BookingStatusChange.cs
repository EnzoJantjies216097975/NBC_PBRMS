namespace PBRMS.Domain;

/// <summary>
/// One step in a booking's history: who moved it from which status to which, when,
/// and their note. Written for every status change (TR-03, BR-18), so anyone can see
/// why a booking was sent back or cancelled. Never changed or deleted.
/// </summary>
public class BookingStatusChange
{
    public Guid Id { get; set; }

    public Guid BookingId { get; set; }

    public BookingStatus FromStatus { get; set; }
    public BookingStatus ToStatus { get; set; }

    public Guid ActorId { get; set; }
    public User Actor { get; set; } = null!;

    /// <summary>The reason given. Required when sending back or cancelling.</summary>
    public string? Note { get; set; }

    public DateTimeOffset At { get; set; }
}
