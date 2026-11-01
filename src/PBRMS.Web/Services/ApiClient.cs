using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;
using PBRMS.Domain;

namespace PBRMS.Web.Services;

/// <summary>
/// How the web app talks to the PBRMS API. There's one per signed-in browser tab, and it holds
/// the sign-in token, which stays on the server and never reaches the browser's own code.
/// All the rules live in the API: this class only asks, and reports back what it said.
/// </summary>
public class ApiClient(HttpClient http, ProtectedSessionStorage storage, LiveUpdates live)
{
    // The API sends enums as text ("Draft").
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private const string TokenKey = "pbrms-token";

    private string? token;

    /// <summary>Who's signed in, or null if nobody is.</summary>
    public MeResponse? Me { get; private set; }

    public bool Has(params Role[] roles) => Me?.Roles.Any(r => roles.Contains(r.Role)) == true;

    public bool Has(Role role, string unit) => Me?.Roles.Any(r => r.Role == role && r.Unit == unit) == true;

    // ---- Sign-in ----

    /// <summary>
    /// Development sign-in, with no password. On NBC's servers this becomes the Microsoft
    /// sign-in, and the rest of the app won't need to change.
    /// </summary>
    public async Task<ApiResult> SignInAsync(string email)
    {
        var response = await http.PostAsJsonAsync("auth/dev-login", new { email });
        if (!response.IsSuccessStatusCode)
            return await ProblemAsync(response);

        var body = await response.Content.ReadFromJsonAsync<TokenResponse>(Json);
        await UseTokenAsync(body!.Token);
        await storage.SetAsync(TokenKey, body.Token);
        return ApiResult.Success;
    }

    /// <summary>Picks the sign-in back up after the page is reloaded, if this tab has one.</summary>
    public async Task RestoreAsync()
    {
        var saved = await storage.GetAsync<string>(TokenKey);
        if (saved is { Success: true, Value: { } value })
            await UseTokenAsync(value);
    }

    public async Task SignOutAsync()
    {
        token = null;
        Me = null;
        await storage.DeleteAsync(TokenKey);
        await live.StopAsync();
    }

    private async Task UseTokenAsync(string value)
    {
        token = value;
        var me = await GetAsync<MeResponse>("me");
        Me = me.Value;

        if (Me is null)
            token = null;
        else
            await live.StartAsync(value);
    }

    // ---- Bookings ----

    public Task<ApiResult<List<BookingResponse>>> QueueAsync() => GetAsync<List<BookingResponse>>("bookings/queue");
    public Task<ApiResult<List<BookingResponse>>> MyBookingsAsync() => GetAsync<List<BookingResponse>>("bookings/mine");
    public Task<ApiResult<BookingResponse>> BookingAsync(Guid id) => GetAsync<BookingResponse>($"bookings/{id}");
    public Task<ApiResult<EarliestDate>> EarliestDateAsync() => GetAsync<EarliestDate>("bookings/earliest-date");
    public Task<ApiResult<List<OrgUnitRow>>> OrgUnitsAsync() => GetAsync<List<OrgUnitRow>>("org-units");
    public Task<ApiResult<List<OperatorRow>>> OperatorsAsync(Guid division) => GetAsync<List<OperatorRow>>($"org-units/{division}/operators");

    public Task<ApiResult<BookingResponse>> CreateBookingAsync(BookingForm form) => PostAsync<BookingResponse>("bookings", form);

    public Task<ApiResult<BookingResponse>> UpdateBookingAsync(Guid id, BookingForm form) =>
        SendAsync<BookingResponse>(HttpMethod.Put, $"bookings/{id}", form);

    public Task<ApiResult<BookingResponse>> TransitionAsync(Guid id, TransitionRequest request) =>
        PostAsync<BookingResponse>($"bookings/{id}/transition", request);

    public Task<ApiResult<BookingResponse>> AllowLateAsync(Guid id, string reason) =>
        PostAsync<BookingResponse>($"bookings/{id}/allow-late", new OverrideRequest(reason));

    public Task<ApiResult<BookingResponse>> SaveCrewAsync(Guid id, CrewRequest request) =>
        SendAsync<BookingResponse>(HttpMethod.Put, $"bookings/{id}/crew", request);

    // ---- Monthly roster and shift codes ----

    public Task<ApiResult<RosterView>> RosterAsync(Guid division, string month) =>
        GetAsync<RosterView>($"rosters/{division}/{month}");

    public Task<ApiResult<List<Clash>>> RosterClashesAsync(Guid division, string month) =>
        GetAsync<List<Clash>>($"rosters/{division}/{month}/clashes");

    public Task<ApiResult<RosterView>> SaveRosterCellsAsync(Guid division, string month, List<RosterCell> entries) =>
        SendAsync<RosterView>(HttpMethod.Put, $"rosters/{division}/{month}", new { entries });

    public Task<ApiResult<RosterPublished>> PublishRosterAsync(Guid division, string month) =>
        PostAsync<RosterPublished>($"rosters/{division}/{month}/publish", null);

    public Task<ApiResult<List<ShiftCode>>> ShiftCodesAsync(Guid division) =>
        GetAsync<List<ShiftCode>>($"shift-definitions/{division}");

    public Task<ApiResult<ShiftCode>> AddShiftCodeAsync(Guid division, ShiftCode code) =>
        PostAsync<ShiftCode>($"shift-definitions/{division}", code);

    public Task<ApiResult<ShiftCode>> ChangeShiftCodeAsync(Guid division, ShiftCode code) =>
        SendAsync<ShiftCode>(HttpMethod.Put, $"shift-definitions/{division}/{code.Id}", code);

    // ---- Daily roster ----

    public Task<ApiResult<DailyRosterView>> DailyRosterAsync(DateOnly date) => GetAsync<DailyRosterView>($"daily-roster/{date:yyyy-MM-dd}");

    public Task<ApiResult<DailyRosterView>> SetAnnouncementAsync(DateOnly date, string? text) =>
        SendAsync<DailyRosterView>(HttpMethod.Put, $"daily-roster/{date:yyyy-MM-dd}/announcement", new { text });

    public Task<ApiResult<DailyRosterView>> SaveDailyLineAsync(DateOnly date, DailyLineForm form) =>
        SendAsync<DailyRosterView>(HttpMethod.Put, $"daily-roster/{date:yyyy-MM-dd}/lines", form);

    /// <summary>Takes a change off the sheet: a production goes back to what was generated, and an added row goes away.</summary>
    public Task<ApiResult<DailyRosterView>> DiscardDailyLineAsync(DateOnly date, Guid lineId) =>
        SendAsync<DailyRosterView>(HttpMethod.Delete, $"daily-roster/{date:yyyy-MM-dd}/lines/{lineId}", null);

    /// <summary>The A4 printout, or null if the API refused.</summary>
    public async Task<byte[]?> DailyRosterPdfAsync(DateOnly date)
    {
        using var request = Request(HttpMethod.Get, $"daily-roster/{date:yyyy-MM-dd}/pdf");
        using var response = await http.SendAsync(request);
        return response.IsSuccessStatusCode ? await response.Content.ReadAsByteArrayAsync() : null;
    }

    // ---- APP ----

    public Task<ApiResult<AppRoomStatus>> AppStatusAsync() => GetAsync<AppRoomStatus>("app-room/status");
    public Task<ApiResult<List<AppOperatorRow>>> AppOperatorsAsync() => GetAsync<List<AppOperatorRow>>("app-operators");
    public Task<ApiResult<List<AppSessionRow>>> MySessionsAsync() => GetAsync<List<AppSessionRow>>("app-sessions/mine");
    public Task<ApiResult<List<AppSessionRow>>> SessionsNeedingATimeAsync() => GetAsync<List<AppSessionRow>>("app-sessions/queue");

    public Task<ApiResult<List<AppBusyRow>>> AppBusyAsync(DateOnly from, DateOnly to) =>
        GetAsync<List<AppBusyRow>>($"app-room/busy?from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}");

    public Task<ApiResult<AppSessionRow>> BookAppAsync(object form) => PostAsync<AppSessionRow>("app-sessions", form);
    public Task<ApiResult<AppSessionRow>> WalkInAsync(object form) => PostAsync<AppSessionRow>("app-sessions/walk-in", form);

    /// <summary>confirm, unavailable, accept, finish or cancel.</summary>
    public Task<ApiResult<AppSessionRow>> SessionActionAsync(Guid id, string action, string? note = null) =>
        PostAsync<AppSessionRow>($"app-sessions/{id}/{action}", action is "confirm" or "accept" or "finish" ? null : new { note });

    public Task<ApiResult<AppSessionRow>> SuggestTimeAsync(Guid id, DateTimeOffset startsAt, DateTimeOffset endsAt) =>
        PostAsync<AppSessionRow>($"app-sessions/{id}/suggest-time", new { startsAt, endsAt });

    // ---- Reports ----

    public Task<ApiResult<List<OffRosterRow>>> OffRosterReportAsync(DateOnly from, DateOnly to, Guid? division) =>
        GetAsync<List<OffRosterRow>>($"reports/off-roster-work?from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}{(division is { } id ? $"&divisionId={id}" : "")}");

    public Task<ApiResult<AppUsageReport>> AppUsageReportAsync(DateOnly from, DateOnly to) =>
        GetAsync<AppUsageReport>($"reports/app-usage?from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}");

    public Task<ApiResult<List<ConfirmationRow>>> ConfirmationsReportAsync(DateOnly from, DateOnly to) =>
        GetAsync<List<ConfirmationRow>>($"reports/confirmations?from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}");

    // ---- Notifications ----

    public Task<ApiResult<List<NotificationRow>>> NotificationsAsync() => GetAsync<List<NotificationRow>>("notifications");

    public async Task MarkReadAsync(Guid id)
    {
        using var request = Request(HttpMethod.Post, $"notifications/{id}/read");
        using var _ = await http.SendAsync(request);
    }

    // ---- Calling the API ----

    private async Task<ApiResult<T>> GetAsync<T>(string url) => await SendAsync<T>(HttpMethod.Get, url, null);

    private Task<ApiResult<T>> PostAsync<T>(string url, object? body) => SendAsync<T>(HttpMethod.Post, url, body);

    private async Task<ApiResult<T>> SendAsync<T>(HttpMethod method, string url, object? body)
    {
        using var request = Request(method, url);
        if (body is not null)
            request.Content = JsonContent.Create(body, options: Json);

        using var response = await http.SendAsync(request);
        if (response.IsSuccessStatusCode)
            return ApiResult<T>.From(await response.Content.ReadFromJsonAsync<T>(Json));

        return ApiResult<T>.From(await ProblemAsync(response));
    }

    private HttpRequestMessage Request(HttpMethod method, string url)
    {
        var request = new HttpRequestMessage(method, url);
        if (token is not null)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }

    /// <summary>Reads the API's refusal: its reason in plain English, and any clashes or warnings.</summary>
    private static async Task<ApiResult> ProblemAsync(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        if (string.IsNullOrWhiteSpace(text))
            return new ApiResult(false, $"The request was refused ({(int)response.StatusCode}).", [], false);

        using var problem = JsonDocument.Parse(text);
        var root = problem.RootElement;

        // A form problem names each field; the first reason is enough to show.
        var reason = root.TryGetProperty("errors", out var errors)
            ? errors.EnumerateObject().First().Value[0].GetString()
            : root.TryGetProperty("detail", out var detail) ? detail.GetString()
            : root.GetProperty("title").GetString();

        var clashes = root.TryGetProperty("clashes", out var found) ? found.Deserialize<List<Clash>>(Json) ?? [] : [];
        var needsAcknowledgement = root.TryGetProperty("needsAcknowledgement", out var flag) && flag.GetBoolean();

        return new ApiResult(false, reason, clashes, needsAcknowledgement);
    }

    private record TokenResponse(string Token);
}
