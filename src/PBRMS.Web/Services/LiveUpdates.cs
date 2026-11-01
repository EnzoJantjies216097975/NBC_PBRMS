using System.Text.Json;
using Microsoft.AspNetCore.SignalR.Client;

namespace PBRMS.Web.Services;

/// <summary>
/// Listens to the API's live connection, so screens refresh themselves when a booking
/// comes in or the APP room changes. Pages subscribe to <see cref="Changed"/> and reload
/// their own data, so each person still sees only what the API allows them to.
/// </summary>
public sealed class LiveUpdates(IConfiguration config) : IAsyncDisposable
{
    private HubConnection? connection;

    /// <summary>Something changed. Reload what's on screen.</summary>
    public event Func<Task>? Changed;

    public async Task StartAsync(string token)
    {
        await StopAsync();

        connection = new HubConnectionBuilder()
            .WithUrl(new Uri(new Uri(config["Api:BaseUrl"]!), "hubs/live"), options =>
                options.AccessTokenProvider = () => Task.FromResult<string?>(token))
            .WithAutomaticReconnect()
            .Build();

        foreach (var message in new[] { "notification", "bookingChanged" })
            connection.On<JsonElement>(message, _ => TellPagesAsync());
        connection.On("appRoomChanged", TellPagesAsync);

        // If this fails the app still works: the screens just won't refresh by themselves.
        try
        {
            await connection.StartAsync();
        }
        catch (HttpRequestException)
        {
        }
    }

    public async Task StopAsync()
    {
        if (connection is not null)
            await connection.DisposeAsync();
        connection = null;
    }

    private Task TellPagesAsync() => Changed?.Invoke() ?? Task.CompletedTask;

    public ValueTask DisposeAsync() => new(StopAsync());
}
