namespace PBRMS.Domain;

/// <summary>
/// A message to one person, shown in their PBRMS notifications. A notification is
/// saved in the same transaction as the change it reports, so nobody is told about a
/// change that didn't happen, and no change goes unreported.
/// </summary>
/// <remarks>
/// In-app only for now. Push and SMS delivery (FCM/APNs, SMS for SMS-only operators,
/// with retries and a delivery log) build on these rows (TR-05).
/// </remarks>
public class Notification
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }
    public User User { get; set; } = null!;

    public string Text { get; set; } = "";

    /// <summary>The booking it is about, if any, so the app can link to it.</summary>
    public Guid? BookingId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>When the person read it. Null while unread.</summary>
    public DateTimeOffset? ReadAt { get; set; }
}
