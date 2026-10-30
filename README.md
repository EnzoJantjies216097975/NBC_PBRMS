# PBRMS

Production Booking and Resource Management System for NBC TV Operations.
One .NET API serves the web and mobile apps; data lives in SQL Server.

The specification (PRD, TRD, schema, SRS) is in `D:\nbc`. The documents in `docs/`
describe the current Production Board, which PBRMS replaces.

## What's in the solution

| Project | What it holds |
| --- | --- |
| `src/PBRMS.Domain` | The things PBRMS is about (users, org units, roles) and, later, the business rules. No database code. |
| `src/PBRMS.Data` | The SQL Server database: `PbrmsDbContext`, migrations, sample data for development. |
| `src/PBRMS.Api` | The web API: sign-in (`Auth/`) and endpoints (`Endpoints/`). `Program.cs` wires it together. |
| `tests/PBRMS.Tests` | Tests that run the real API in memory. |

The web (Blazor) and mobile projects come later.

## Set up a development machine

1. **.NET 10 SDK**: <https://dotnet.microsoft.com/download/dotnet/10.0>
2. **SQL Server 2022 Developer** (free): <https://www.microsoft.com/sql-server/sql-server-downloads>.
   Choose the **Basic** install. It creates the default instance that
   `appsettings.Development.json` points at (`Server=localhost`).
   SQL Server Management Studio (SSMS) is optional and lets you look at the tables.
3. **EF Core tools**, for migrations: `dotnet tool install -g dotnet-ef`

## Run it

```
dotnet run --project src/PBRMS.Api
```

On first start in Development the API creates the `PBRMS` database and adds sample
people (`src/PBRMS.Data/DevSeed.cs`). Then open `src/PBRMS.Api/PBRMS.Api.http` to try it:

- `GET /health`: is the API up, and can it reach the database?
- `POST /auth/dev-login` with `{ "email": "supervisor@pbrms.test" }`: returns a token (Development only, no password)
- `GET /me` with `Authorization: Bearer <token>`: your profile and roles

Sample users: `producer@`, `pm@`, `ep@`, `supervisor@`, `booking@`, `operator@`,
`admin@` and `management@pbrms.test`. `former.staff@pbrms.test` has left and is refused.

## Run the tests

```
dotnet test
```

The tests use an in-memory SQLite database, so they need no SQL Server.

## Change the database

1. Edit or add classes in `src/PBRMS.Domain`, and their rules in `PbrmsDbContext.OnModelCreating`.
2. Create a migration:
   ```
   dotnet ef migrations add <WhatChanged> --project src/PBRMS.Data --startup-project src/PBRMS.Api
   ```
3. Read the generated file in `src/PBRMS.Data/Migrations`. It's applied on the next `dotnet run`.

## Sign-in

People sign in with their NBC email. `Auth:Mode` in appsettings picks how:

| Mode | Where | How |
| --- | --- | --- |
| `Development` | Your machine | `POST /auth/dev-login`, any sample email, no password. Refused anywhere but Development. |
| `Microsoft` | NBC's servers | The apps sign in through Microsoft (Entra ID). The API only checks Microsoft's token. |

Either way, the email must belong to an **active PBRMS user**, and roles come from
the PBRMS database. A valid NBC account with no PBRMS user gets 403 Forbidden.

**To switch to Microsoft sign-in, NBC IT needs to:**

1. Register an app for the PBRMS API in NBC's Microsoft Entra tenant and expose an API scope.
2. Give you the **Tenant ID** and the API's **Client ID**.
3. Set these on the server (environment variables or IIS config, not in this repository):
   `Auth__Mode=Microsoft`, `Auth__Microsoft__TenantId=...`, `Auth__Microsoft__ClientId=...`

This assumes NBC email runs on Microsoft 365. If NBC uses an on-premises Exchange
server instead, sign-in needs a different approach; see `Auth/SignIn.cs`.
