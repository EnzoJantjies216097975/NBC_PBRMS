using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using PBRMS.Api.Auth;

namespace PBRMS.Api.Live;

/// <summary>
/// /hubs/live: the connection the web and mobile apps keep open, so changes reach open
/// screens straight away (TRD: "within 3 s"). The apps only listen:
///
///   "notification"   A new notification for this person only: <see cref="LiveNotification"/>.
///   "appRoomChanged" The APP room's bookings changed, sent to everyone: refresh /app-room/status.
///   "bookingChanged" Some booking changed, sent to everyone: <see cref="LiveBookingChange"/>.
///                    It says only which booking, and the app refreshes its own queue, so
///                    each person still sees only what the API allows them to.
///   "rosterChanged"  A division's monthly roster was saved, sent to everyone, the same way:
///                    <see cref="LiveRosterChange"/>.
/// </summary>
[Authorize]
public class LiveHub : Hub;

public record LiveNotification(Guid Id, string Text, Guid? BookingId);

public record LiveBookingChange(Guid Id);

public record LiveRosterChange(Guid DivisionId);

/// <summary>Tells SignalR that a connection's user is their PBRMS user, so a message can go to one person.</summary>
public class PbrmsUserIdProvider : IUserIdProvider
{
    public string? GetUserId(HubConnectionContext connection) =>
        connection.User.FindFirstValue(PbrmsClaims.UserId);
}
