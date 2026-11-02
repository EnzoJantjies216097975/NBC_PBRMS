// PBRMS API: the one backend the web and mobile apps both talk to.
// This file only wires things together. Sign-in is in Auth/ and the endpoints are in Endpoints/.

using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using PBRMS.Api;
using PBRMS.Api.Auth;
using PBRMS.Api.Endpoints;
using PBRMS.Api.Live;
using Microsoft.AspNetCore.SignalR;
using PBRMS.Data;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<PbrmsDbContext>((services, options) => options
    .UseSqlServer(builder.Configuration.GetConnectionString("Pbrms"))
    .AddInterceptors(
        services.GetRequiredService<AuditOnSave>(),
        services.GetRequiredService<LivePushOnSave>(),
        services.GetRequiredService<LivePushOnCommit>()));

// The audit log: every saved change, with who made it (see AuditOnSave.cs).
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<AuditOnSave>();

// Live updates to open screens (see Live/LiveHub.cs).
builder.Services.AddSignalR();
builder.Services.AddSingleton<IUserIdProvider, PbrmsUserIdProvider>();
builder.Services.AddScoped<LivePush>();
builder.Services.AddScoped<LivePushOnSave>();
builder.Services.AddScoped<LivePushOnCommit>();

builder.Services.AddScoped<ClashChecker>();
builder.Services.AddScoped<Notifier>();
builder.Services.AddScoped<OffRosterLog>();
builder.Services.AddScoped<StudioPrecedence>();
builder.Services.AddScoped<CrewProgress>();
builder.Services.AddScoped<OvertimeWatcher>();
builder.Services.AddScoped<CrewDeadlineWatcher>();
builder.Services.AddScoped<ProductionDayWatcher>();
builder.Services.AddScoped<DailyRosterComposer>();
builder.Services.AddHostedService<MinuteChecksService>();

builder.AddPbrmsSignIn();

// Send enums as text ("Supervisor") rather than numbers, so the apps don't need a lookup table.
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

// Errors go out in the standard "problem details" JSON format (RFC 7807), as the schema document asks.
builder.Services.AddProblemDetails();

// Describes every endpoint at /openapi/v1.json (Development only), for building the apps against.
builder.Services.AddOpenApi();

var app = builder.Build();

// First-time setup on a new server: PBRMS.Api setup <email> <first name> <surname>
// Creates or updates the database and adds the first Admin, then stops. See README.md.
if (args is ["setup", var email, var firstName, var surname])
{
    using var setupScope = app.Services.CreateScope();
    var setupDb = setupScope.ServiceProvider.GetRequiredService<PbrmsDbContext>();
    await setupDb.Database.MigrateAsync();

    var problem = await FirstAdmin.AddAsync(setupDb, email, firstName, surname);
    Console.WriteLine(problem ?? $"{firstName} {surname} is set up as the first Admin.");
    return problem is null ? 0 : 1;
}

app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}
else
{
    // On the server: tell browsers to use HTTPS only, and move any plain HTTP request to HTTPS.
    app.UseHsts();
    app.UseHttpsRedirection();
}

app.UseAuthentication();
app.UseAuthorization();

app.MapHealthEndpoints();
app.MapMeEndpoints();
app.MapOrgUnitEndpoints();
app.MapUserEndpoints();
app.MapBookingFormEndpoints();
app.MapBookingQueryEndpoints();
app.MapBookingTransitionEndpoints();
app.MapBookingCrewEndpoints();
app.MapBookingFinalisationEndpoints();
app.MapBookingDayEndpoints();
app.MapOperatorEndpoints();
app.MapShiftDefinitionEndpoints();
app.MapRosterEndpoints();
app.MapNotificationEndpoints();
app.MapOffRosterEndpoints();
app.MapConfirmationEndpoints();
app.MapAuditEndpoints();
app.MapDailyRosterEndpoints();
app.MapAppSessionEndpoints();
app.MapHub<LiveHub>("/hubs/live");

if (SignIn.IsDevelopmentMode(app.Configuration))
    app.MapDevLoginEndpoints();

// Development only: create or update the database and add sample people, so the
// API works straight after "dotnet run".
if (app.Environment.IsDevelopment() && app.Configuration.GetValue<bool>("Database:PrepareOnStartup"))
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<PbrmsDbContext>();
    await db.Database.MigrateAsync();
    await DevSeed.RunAsync(db);
}

await app.RunAsync();
return 0;
