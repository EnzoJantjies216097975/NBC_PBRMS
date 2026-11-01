// PBRMS web app: the screens people use on their desktops. It has no rules or data of its
// own: everything goes through the PBRMS API, so the web and mobile apps can't disagree.

using PBRMS.Web.Components;
using PBRMS.Web.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddHttpClient("api", client =>
    client.BaseAddress = new Uri(builder.Configuration["Api:BaseUrl"]!));

// One of each per browser tab, because the API client holds that person's sign-in.
builder.Services.AddScoped<LiveUpdates>();
builder.Services.AddScoped(services => new ApiClient(
    services.GetRequiredService<IHttpClientFactory>().CreateClient("api"),
    services.GetRequiredService<Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage.ProtectedSessionStorage>(),
    services.GetRequiredService<LiveUpdates>()));

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
    app.UseHttpsRedirection();
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

await app.RunAsync();
