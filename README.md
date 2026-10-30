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
people (`src/PBRMS.Data/DevSeed.cs`). Then open `src/PBRMS.Api/PBRMS.Api.http` to try it.

| Endpoint | What it does |
| --- | --- |
| `GET /health` | Is the API up, and can it reach the database? |
| `POST /auth/dev-login` | `{ "email": "producer@pbrms.test" }` returns a token (Development only, no password) |
| `GET /me` | Your profile and roles |
| `GET /org-units` | Departments and TV Ops divisions, for the booking form's pickers |
| `POST /bookings` | Create a draft booking |
| `PUT /bookings/{id}` | Edit your draft (also after it was sent back) |
| `GET /bookings/{id}` | One booking with its crew positions and history |
| `GET /bookings/mine` | Your bookings, as a producer |
| `GET /bookings/queue` | Bookings waiting for you to approve or send back |
| `POST /bookings/{id}/transition` | `{ "action": "...", "note": "..." }`. Actions: Submit, Approve, SendBack, Start, End, Cancel. |
| `GET /reports/off-roster-work?from=&to=` | Who was booked on a day off or outside their shift. The TV Operations Manager, Management and Admin see everyone; supervisors see their own divisions. Add `divisionId=` for one division. |
| `GET /notifications` | Your newest notifications |
| `POST /notifications/{id}/read` | Mark one as read |
| `PUT /bookings/{id}/crew` | `{ "assignments": [{ "slotId": "...", "operatorId": "..." }] }`. The Booking Officer for studios, supervisors outside. On a confirmed booking it replaces the operator. |
| `GET /bookings/{id}/clashes` | Everything the booking clashes with |
| `PUT /bookings/{id}/schedule` | The Booking Officer moves a booking at Finalisation to another studio or time |
| `GET /org-units/{id}/operators` | Active operators in a division, for the crew picker |
| `GET /operators/{id}/conflicts?from=&to=` | Why an operator can't work a period: other productions, and the published roster |
| `GET /shift-definitions/{divisionId}` | A division's legend of shift codes |
| `POST /shift-definitions/{divisionId}`, `PUT .../{id}` | Add or change a code (the division's supervisor or an Admin). Retire one with `isActive: false`. |
| `GET /rosters/{divisionId}/{yyyy-MM}` | The newest roster version for a month |
| `PUT /rosters/{divisionId}/{yyyy-MM}` | Save changed cells: `{ "entries": [{ "operatorId": "...", "date": "2027-03-01", "code": "A1" }] }`. Editing a published roster starts a new draft. |
| `GET /rosters/{divisionId}/{yyyy-MM}/clashes` | Confirmed bookings this roster would upset |
| `POST /rosters/{divisionId}/{yyyy-MM}/publish` | Put the draft in force. Clashes come back as warnings. |

Send every request except health and dev-login with `Authorization: Bearer <token>`.

**Sample users** (all `@pbrms.test`):

| Email | Role |
| --- | --- |
| `producer@` | Producer, News |
| `ep@` | Executive Producer, News |
| `supervisor@` | Supervisor and Operator, Camera |
| `supervisor.sound@` | Supervisor, Sound |
| `operator@` | Operator, Sound |
| `camera@` | Operator, Camera |
| `producer.sports@` | Producer, Sports |
| `booking@`, `admin@`, `management@` | Booking Officer, Admin, Management (NBC-wide) |
| `tvops.manager@` | TV Operations Manager |
| `former.staff@` | Has left; refused |

If you change `DevSeed.cs`, drop the development database to pick up the change:
`dotnet ef database drop --project src/PBRMS.Data --startup-project src/PBRMS.Api`.


## How a booking moves

Producer → Executive Producer → crew → confirmed → production day. After the EP approves:

| | Studio production | Outside or trip |
| --- | --- | --- |
| Crew | Filled automatically from the published monthly roster. The Booking Officer books anyone still needed. | Decided by the supervisor of each division it needs. |
| Who sees it | The Booking Officer | Only those supervisors |
| Confirmed | By the Booking Officer | The moment the last supervisor decides, because they phone their operators first |
| Operators told | When it's confirmed | When it's confirmed |

If an outside production is still undecided when the daily roster is due (16h00 the day
before), the Booking Officer can decide it too, and it appears in their queue.

Admin can see who decided each position and who confirmed each booking at
`GET /reports/confirmations?from=&to=`.

## What the roster does to crew assignments

| The roster says | What happens |
| --- | --- |
| Leave | Refused, and it can't be overridden. Book someone else. |
| Away (trip) | Refused, unless the booking is a trip. |
| Not on shift at that time | A warning, and logged. Going ahead notifies the operator, the division's supervisors, the TV Operations Manager and the Booking Officer. |
| Day off | The same: a warning, logged, and the same people notified. |
| Rostered to another studio | A warning. Going ahead tells the supervisors to sort out the cover. |
| Rostered to a studio in use, booked on non-studio work | Refused: studio productions have priority. |
| Rostered to an idle studio, booked on non-studio work | A warning, as above. |
| Blank cell, or a draft roster | Nothing: unknown is never a clash. |

A warning comes back as a 409 with `needsAcknowledgement: true`, and nothing is saved.
Send the same request again with `"acknowledgeWarnings": true` to go ahead. The Booking
Officer sees any remaining warnings the same way when confirming.

**Studios take precedence.** Studios are normally filled first, so:

- Studio productions come first in a supervisor's queue.
- Crewing an outside production while a studio at that time is short of crew gives a warning.
- A studio production can take an operator off an outside production that hasn't started.
  It's a warning first. Going ahead sends the outside production back to crew assignment,
  even if it was confirmed, and tells its supervisors, its producer and the Booking Officer.
- An outside production can never take an operator off a studio, and studios can't take from each other.

A production still running 30 minutes after its booked end counts as overtime. The
supervisors of its crew divisions are told once.
The API checks every minute, so on IIS the site must be set to Always Running.

## The daily roster

`GET /daily-roster/{yyyy-MM-dd}` builds the sheet fresh each time from confirmed bookings and
the published monthly rosters, so a late booking appears by itself. People rostered but not on a
production are listed too; days off and leave aren't. Bookings not yet confirmed are listed
separately. Everyone can read it.

The Booking Officer can change it, and only they can (BR-05):

| Endpoint | What it does |
| --- | --- |
| `PUT /daily-roster/{date}/announcement` | The announcement at the top of the sheet |
| `PUT /daily-roster/{date}/lines` | Change a production's row (give `bookingId`), hide it (`hidden: true`), or add a row of your own |
| `DELETE /daily-roster/{date}/lines/{id}` | Discard a change: the row goes back to what was generated |
| `GET /daily-roster/{date}/pdf` | The A4 landscape printout, named like `Roster_Tue_10112026.pdf` |

Editing a row changes the sheet only. To change who is really on a production, change its crew,
which keeps the clash checks and tells the people affected.

At 16h00 the day before, the Booking Officer is told about any outside production still
undecided, and can decide it.

**PDF library:** PDFsharp/MigraDoc is pinned at exactly 6.2.4, the last MIT version. From 6.5.0 it
is AGPL-3.0. Don't upgrade it without reading the licence; a test fails if the version changes.

## Run the tests

```
dotnet test
```

The tests run against SQL Server on this machine, in a database called `PBRMS_Tests`.
It is created on the first run, then wiped and refilled at the start of each run.

**If SQL Server is slow** (creating a database takes tens of seconds, or tests time
out): add SQL Server's data folder and `sqlservr.exe` to your antivirus exclusions,
as Microsoft recommends, and keep a few GB free on C:.

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
