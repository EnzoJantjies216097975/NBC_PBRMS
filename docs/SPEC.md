# NBC PBRMS — Specification

Distilled from stakeholder requirements. This is the source-of-truth for *what* the system does;
[ARCHITECTURE.md](ARCHITECTURE.md) covers *how*.

## Roles

| Role               | Responsibilities |
| ------------------ | ---------------- |
| **Operator**       | Crew member. Views their daily/weekly/monthly schedule, gets notified, logs production reports, books equipment, claims overtime. Types: permanent, contract, freelance, apprentice, intern. |
| **Supervisor**     | Governs one operations department. Builds the **monthly/weekly roster**, manages operators (skills/gear), assigns crew to approved bookings, approves equipment. May not edit the **daily** roster. |
| **Producer**       | Raises production bookings, adds scripts/rundowns, confirms attendance, logs start/end. Belongs to a content department. |
| **Executive Producer** | Validates/approves producer bookings, assigns channel (NBC 1–3), assigns producers, verifies overtime, sees department calendar + clashes. |
| **Booking Officer**| Owns day-to-day. **Only role that edits the daily roster.** Modifies bookings, resolves conflicts, prints daily rosters + transport lists. |
| **Manager / Admin**| Oversight, reporting, role assignment. |

## Operations departments (crew)

Camera · Lighting · Sound · News Camera · Production Officers (Editors & Directors) · Technicians ·
Final Control Centre (FCC) · OB Operations. **News Camera** has a separate supervisor + roster.
Floor Managers are typically Sound operators assisting on productions.

## Content departments (editorial)

Content Hub (Education, Entertainment, Documentaries, AdHoc, Drama, Reality, Social Media) · News ·
Current Affairs · New Business · Sports.

## The booking pipeline

```
Producer ──submit──▶ Executive Producer ──approve+channel──▶ Supervisor ──assign crew──▶ Booking Officer + Operators
            ▲                  │                                   │
            └── changes ◀──────┘                                   └── decline ◀── (back to EP with reasons)
```

- **Conflicts hard-block**: a booking cannot be confirmed while an operator double-booking exists.
  Conflicting operators are flagged (different colour); changing one warns which production they're
  already on and asks for confirmation.
- On confirmation: producer gets the crew list + contact details; operators get notified.
- **1 hour before**: everyone on the production is reminded to head to the studio.
- Producer confirms **attendance** (and can "signal" a missing operator, who is notified; the
  operator's phone is shown for a call). Producer confirms **start/end** → duration is logged.

## Rosters

- **Monthly** roster per operations department (operators × days, shift codes — see the April Sound
  roster). Owned by the supervisor; visible to their operators + the booking officer.
- Supervisors are **reminded a week before month-end** to build next month's roster.
- **Daily** roster is the booking officer's domain only. Rosters auto-generate but remain editable
  on the fly because bookings can change at any time.

## Shifts (8-hour, subject to change)

`FM` Floor Managing 05:00–13:00 · `GM` Good Morning 05:00–13:00 · `ST2` Studio 2 14:00–23:00 ·
`ST4` Studio 4 08:00–17:00 · `N` News 13:00–21:00 · `APP` Audio Post Production (bookable) ·
`TR` Trips / `A1`–`A6` as per booking. Non-working: `O` off, `PH` public holiday, `L` leave.
Anything beyond 8h in a shift is flagged as overtime.

## Overtime rules

- Weekday: first **8h** normal, beyond is overtime.
- Saturday: first **5h** normal, beyond is overtime.
- Sunday: **all** overtime.
- A production running **≥30 min** past its booked end counts as overtime.
- Operators can flag an overrun to the supervisor (who is notified) — guarded against abuse and
  logged. Operators view their overtime and export/print a signed sheet (Annexure B).

## Modules

- **Equipment / Storeroom** — every item has a serial number. Operators book gear out; supervisors
  / technicians approve based on availability. When gear returns, relevant operators are alerted
  (e.g. "Mic stand from Studio 2 now free for Studio 4"). All roles can view inventory; only
  supervisors assign.
- **Transport list** — crew who need a **pickup to get to work** (address, phone, pickup &
  drop-off times). Only flagged individuals. Printable for the Transport Office.
- **Car booking** — operators who need a **vehicle to go out** on a production. Shown on the
  booking form and on the operator's side. Distinct from the transport list.
- **Audio Post Production (APP)** — a bookable shift/studio with a fast-track flow that may bypass
  supervisor/booking-officer approval but keeps them informed; the Sound Supervisor logs which
  productions use APP, how long, and how often.
- **Production reports** — operators raise issues with severity (low/medium/high/critical, with a
  help guide) to the relevant supervisor.
- **Reports & analytics** (printable) — time at work, overtime per person + total, studio usage,
  cancellations, trips (who/how often/why), locations, underutilisation flags.

## Critical issues to address

1. Supervisors edit monthly/weekly rosters; **only the booking officer** edits the daily roster.
2. Rosters auto-generate but the booking officer can change bookings on a whim; the system
   recommends available operators and flags those who are free now but **due** for a production.
3. Some supervisors work field + studio but **cannot go on trips** (`can_go_on_trips = false`).
4. **Trip fairness** — log who goes on trips, how often, and why; producers may request operators
   with specific expertise (drone, steadicam, pocket cam) with notes; managers pull reports.
5. **Ghost bookings** — producers cancelling without telling crew, or booking all-day location
   crew that never goes out, leaving studios short-staffed. Mitigated via attendance + start/end
   logging, stand-in logging, and oversight reports.
6. **Favouritism** — fair assignment supported by availability/skills data + audit logging.

## Sign-up

Captures name, surname, work email, personal email, date of birth, phone, physical address.
Role + department are assigned afterwards by a supervisor / executive producer / admin, which adds
the person to the relevant department roster.

## Notifications

Real-time across web + mobile (push), even when operators are out on other productions, with **SMS
fallback** for operators without smartphones.

## Reference: known productions

The recurring catalog (daily, weekend, flagship live, recorded, location shoots) is encoded in
[`packages/shared/src/constants.ts`](../packages/shared/src/constants.ts) and seeded by
[`supabase/migrations/0003_seed.sql`](../supabase/migrations/0003_seed.sql).
