namespace PBRMS.Domain;

/// <summary>
/// Where a booking is on its way from the producer's draft to the production day.
/// Who moves it from one status to the next is defined in <see cref="BookingWorkflow"/>.
/// Source: App Flow Document, "Booking states".
/// </summary>
/// <remarks>
/// The App Flow Document also has "Submitted" and "PM review" states. NBC has no Production
/// Managers, so submitting a draft puts it straight into <see cref="EpReview"/>.
/// </remarks>
public enum BookingStatus
{
    /// <summary>Being filled in by the producer, or sent back to them to fix.</summary>
    Draft,

    /// <summary>Waiting for the department's Executive Producer, who checks air date and channel.</summary>
    EpReview,

    /// <summary>
    /// With the supervisors and the Booking Officer at the same time, while crew is assigned.
    /// Who leads depends on the venue: see <see cref="BookingWorkflow.CrewLead"/>.
    /// </summary>
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
