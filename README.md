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
| `src/PBRMS.Web` | The web app (Blazor Server). Screens only: everything goes through the API. |
| `tests/PBRMS.Tests` | Tests that run the real API in memory. |

The mobile app comes later.

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
| `PUT /bookings/{id}/crew` | `{ "assignments": [{ "slotId": "...", "operatorId": "..." }] }`. Studio crew: the Booking Officer or the division's supervisor. Outside crew: the division's supervisor. On a confirmed booking it replaces the operator. |
| `GET /bookings/{id}/clashes` | Everything the booking clashes with |
| `PUT /bookings/{id}/schedule` | The Booking Officer moves a booking at Finalisation to another studio or time |
| `GET /org-units/{id}/operators` | Active operators in a division, by name, for the crew picker and the monthly roster |
| `GET /operators/{id}/conflicts?from=&to=` | Why an operator can't work a period: other productions, and the published roster |
| `GET /shift-definitions/{divisionId}` | A division's legend of shift codes |
| `POST /shift-definitions/{divisionId}`, `PUT .../{id}` | Add or change a code (the division's supervisor or an Admin). Retire one with `isActive: false`. |
| `GET /rosters/{divisionId}/{yyyy-MM}` | The newest version for the division's supervisor and the Booking Officer. Everyone else gets what's published. |
| `PUT /rosters/{divisionId}/{yyyy-MM}` | Save changed cells: `{ "entries": [{ "operatorId": "...", "date": "2027-03-01", "code": "A1" }] }`. Editing a published roster starts a new draft. |
| `GET /rosters/{divisionId}/{yyyy-MM}/clashes` | Confirmed bookings this roster would upset (the division's supervisor and the Booking Officer) |
| `POST /rosters/{divisionId}/{yyyy-MM}/publish` | Put the draft in force. Clashes come back as warnings. |
| `GET /daily-roster/{yyyy-MM-dd}` | The sheet for a day, for everyone. The Booking Officer also gets what's left off it, and where a changed row differs from its booking. |
| `PUT /daily-roster/{yyyy-MM-dd}/announcement` | `{ "text": "..." }`, printed at the top (Booking Officer) |
| `PUT /daily-roster/{yyyy-MM-dd}/lines` | Change a production's row, leave it off with `hidden: true`, or add a row (Booking Officer). This changes the sheet only, not the crew. |
| `DELETE /daily-roster/{yyyy-MM-dd}/lines/{lineId}` | Undo a change, or remove an added row (Booking Officer) |
| `GET /daily-roster/{yyyy-MM-dd}/pdf` | The A4 printout (Booking Officer) |

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
| `editor@`, `app@` | Editor, APP operator (Sound) |
| `former.staff@` | Has left; refused |

If you change `DevSeed.cs`, drop the development database to pick up the change:
`dotnet ef database drop --project src/PBRMS.Data --startup-project src/PBRMS.Api`.


## Run the web app

The web app is a Blazor Server project with no rules or data of its own: everything goes through
the API. Start both, in two terminals:

```
dotnet run --project src/PBRMS.Api
dotnet run --project src/PBRMS.Web
```

Then open <http://localhost:5103> and pick who to be. Each browser tab signs in separately, so you
can open two tabs as different people and watch changes arrive live.

So far it has: My work, the booking form, booking pages (approve, crew, confirm), the daily roster
with row editing and its PDF, the monthly roster grid, shift codes, the APP room, reports, and notifications. On NBC's servers the sign-in page becomes the
Microsoft sign-in, and `Api:BaseUrl` must point at the API.

## How a booking moves

Producer → Executive Producer → crew → confirmed → production day. After the EP approves:

| | Studio production | Outside or trip |
| --- | --- | --- |
| Crew | Filled automatically from the published monthly roster. The Booking Officer books anyone still needed, and supervisors can change their division's crew as needed. | Decided by the supervisor of each division it needs. |
| In whose queue | The Booking Officer's | Only those supervisors' |
| Confirmed | By the Booking Officer | The moment the last supervisor decides, because they phone their operators first |
| Operators told | When it's confirmed | When it's confirmed |

If an outside production is still undecided when the daily roster is due (16h00 the day
before), the Booking Officer can decide it too, and it appears in their queue.

**The 15h00 cut-off.** Producers can't submit a booking for the next day from 15h00, so supervisors
and the Booking Officer can finalise it before the daily roster at 16h00. Today is always closed.
The refusal names the earliest date, and `GET /bookings/earliest-date` gives it to the form. Drafts can
be saved at any time. If a booking really can't wait, the Booking Officer lets it through with
`POST /bookings/{id}/allow-late` and a reason. The reason goes in its history, and the TV
Operations Manager is told. APP is softer: short notice is a warning, because urgent work happens.

**Booking Officer override.** Once outside crew reaches them at the deadline, the Booking Officer can
fill it while a studio is still short by adding `overrideReason` to `PUT /bookings/{id}/crew`. The
reason goes in the booking's history, and the supervisors and TV Operations Manager are told.
Nobody else can override this, and no other clash can be overridden.

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

**Studios are filled first.** Studio bookings must be fulfilled before outside productions can be booked:

- Crewing an outside production is refused while a studio at that time is still short of crew
  from the same division. The studio is named in the refusal, and it can't be overridden.
- A studio production can take an operator off an outside production that isn't confirmed yet.
  It's a warning first. Going ahead tells that production's supervisors and producer.
- A confirmed outside crew stays: the supervisor has already phoned them.
- An outside production can never take an operator off a studio, and studios can't take from each other.
- Studio productions come first in the Booking Officer's queue.

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

## APP bookings

APP operators work directly with producers and editors, so these skip the approval chain (BR-12):

NBC has one APP room, so this is the only way to book it: `App` is refused as a venue on the normal
booking form. Sessions can't overlap, whichever operator is on them. `GET /app-room/busy?from=&to=`
shows when the room is taken, so people can pick a free time rather than walking in.

1. A producer or editor books an operator: `POST /app-sessions`. `GET /app-operators` lists them.
2. The APP operator agrees: `POST /app-sessions/{id}/confirm`. They can't confirm over a production or another session.
3. Or they can't do that time: `POST /app-sessions/{id}/unavailable`. It goes to the Booking Officer (`GET /app-sessions/queue`).
4. The Booking Officer suggests a time the operator is free: `POST /app-sessions/{id}/suggest-time`.
5. The producer or editor takes it with `POST /app-sessions/{id}/accept`, or calls it off with `/cancel`.

**Is APP busy right now?** `GET /app-room/status` tells anyone signed in whether the room is in use,
for which production, until when, and when the next session starts. Open screens hear
`appRoomChanged` as soon as it changes. Keeping this right is never the operator's job:

- Someone who needs the room now checks themselves in: `POST /app-sessions/walk-in`. If the room is
  free it's confirmed straight away, and the operator is told but has nothing to do. Walk-ins are
  marked in the report, with a count per production and per person.
- They mark it finished with `POST /app-sessions/{id}/finish`, so it shows free. If they forget, it
  frees itself at the time they gave.

A confirmed session counts as the operator being busy, and shows on the daily roster.
`GET /reports/app-usage?from=&to=` shows supervisors, the TV Operations Manager and Admin which
productions, editors and producers used APP.

## Live updates

The apps keep a SignalR connection open to `/hubs/live`, so changes reach open screens straight
away. Send the sign-in token as usual; browsers pass it as `?access_token=` because they can't
set a header on a WebSocket. Two messages arrive:

- `notification`: a new notification, for that person only.
- `bookingChanged`: a booking changed, to everyone. It gives only the booking's id, and the app
  refreshes its own queue, so each person still sees only what they're allowed to.

Messages go out only after the change is committed. On IIS, turn on the WebSocket Protocol feature,
or SignalR falls back to slower long polling.

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
