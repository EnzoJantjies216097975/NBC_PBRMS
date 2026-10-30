// PBRMS API: the one backend the web and mobile apps both talk to.
// This file only wires things together. Sign-in is in Auth/ and the endpoints are in Endpoints/.

using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using PBRMS.Api.Auth;
using PBRMS.Api.Endpoints;
using PBRMS.Data;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<PbrmsDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("Pbrms")));

builder.AddPbrmsSignIn();

// Send enums as text ("Supervisor") rather than numbers, so the apps don't need a lookup table.
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

// Errors go out in the standard "problem details" JSON format (RFC 7807), as the schema document asks.
builder.Services.AddProblemDetails();

// Describes every endpoint at /openapi/v1.json (Development only), for building the apps against.
builder.Services.AddOpenApi();

var app = builder.Build();

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

if (SignIn.IsDevelopmentMode(app.Configuration))
    app.MapDevLoginEndpoints();

// Development only: create or update the database and add sample people, so the
// API works straight after "dotnet run". Tests turn this off and set up their own database.
if (app.Environment.IsDevelopment() && app.Configuration.GetValue<bool>("Database:PrepareOnStartup"))
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<PbrmsDbContext>();
    await db.Database.MigrateAsync();
    await DevSeed.RunAsync(db);
}

app.Run();
