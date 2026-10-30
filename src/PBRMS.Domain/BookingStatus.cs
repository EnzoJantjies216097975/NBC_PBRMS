namespace PBRMS.Domain;

/// <summary>
/// Where a booking is on its way from the producer's draft to the production day.
/// Who moves it from one status to the next is defined in <see cref="BookingWorkflow"/>.
/// Source: App Flow Document, "Booking states".
/// </summary>
/// <remarks>
/// The App Flow Document lists "Submitted" and "PM review" as separate states. Both
/// mean "waiting for the Production Manager", so they are one status here: submitting
/// a draft puts it straight into <see cref="PmReview"/>.
/// </remarks>
public enum BookingStatus
{
    /// <summary>Being filled in by the producer, or sent back to them to fix.</summary>
    Draft,

    /// <summary>Waiting for the department's Production Manager.</summary>
    PmReview,

    /// <summary>Waiting for the department's Executive Producer, who checks air date and channel.</summary>
    EpReview,

    /// <summary>Waiting for the supervisor of every division named in the crew positions to assign crew.</summary>
    CrewAssignment,

    /// <summary>Waiting for the Booking Officer to settle studios and times and resolve clashes.</summary>
    Finalisation,

    /// <summary>Approved and crewed; producer, EP and crew have been told.</summary>
    Confirmed,

    /// <summary>The producer has confirmed the production started.</summary>
    InProgress,

    /// <summary>The producer has confirmed the production ended.</summary>
    Completed,

    /// <summary>Called off. Kept, never deleted, and still shown as cancelled on schedules (BR-14).</summary>
    Cancelled
}
