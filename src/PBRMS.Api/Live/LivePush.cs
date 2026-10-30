using System.Data.Common;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using PBRMS.Data;
using PBRMS.Domain;

namespace PBRMS.Api.Live;

/// <summary>
/// Sends live updates for whatever the database just saved, so no endpoint has to remember
/// to. It watches for new notifications and changed bookings.
///
/// Updates go out only after the data is really committed: an app refreshing on the message
/// must find the new data, and nobody must hear about a change that was rolled back. One per request.
/// </summary>
public class LivePush(IHubContext<LiveHub> hub, ILogger<LivePush> logger)
{
    private readonly List<LiveNotification> notifications = [];
    private readonly Dictionary<Guid, Guid> notificationUsers = [];
    private readonly HashSet<Guid> bookings = [];

    /// <summary>Notes what's about to be saved. The ids exist by now; the messages wait for the commit.</summary>
    public void Collect(DbContext context)
    {
        foreach (var entry in context.ChangeTracker.Entries<Notification>().Where(e => e.State == EntityState.Added))
        {
            var notification = entry.Entity;
            notifications.Add(new LiveNotification(notification.Id, notification.Text, notification.BookingId));
            notificationUsers[notification.Id] = notification.UserId;
        }

        foreach (var entry in context.ChangeTracker.Entries<Booking>().Where(e => e.State is EntityState.Added or EntityState.Modified))
            bookings.Add(entry.Entity.Id);
    }

    /// <summary>Sends everything collected. A failed push must never fail the save behind it: apps also refresh on opening.</summary>
    public async Task SendAsync()
    {
        try
        {
            foreach (var notification in notifications)
                await hub.Clients.User(notificationUsers[notification.Id].ToString()).SendAsync("notification", notification);

            foreach (var booking in bookings)
                await hub.Clients.All.SendAsync("bookingChanged", new LiveBookingChange(booking));
        }
        catch (Exception error)
        {
            logger.LogWarning(error, "A live update could not be sent.");
        }

        notifications.Clear();
        notificationUsers.Clear();
        bookings.Clear();
    }
}

/// <summary>Hands each save to <see cref="LivePush"/>, and sends straight away if there's no transaction to wait for.</summary>
public class LivePushOnSave(LivePush push) : SaveChangesInterceptor
{
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        if (eventData.Context is { } context)
            push.Collect(context);

        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    public override async ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
    {
        if (eventData.Context?.Database.CurrentTransaction is null)
            await push.SendAsync();

        return result;
    }
}

/// <summary>Sends once a transaction has committed. A transaction that rolls back never gets here.</summary>
public class LivePushOnCommit(LivePush push) : DbTransactionInterceptor
{
    public override async Task TransactionCommittedAsync(
        DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default) =>
        await push.SendAsync();
}
