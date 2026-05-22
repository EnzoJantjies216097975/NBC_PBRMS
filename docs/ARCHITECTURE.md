# NBC PBRMS — Architecture

## Tech decisions

- **Monorepo (pnpm + Turborepo)** with `apps/web`, `apps/mobile`, and `packages/shared`. Domain
  rules live once in `@nbc/shared` and are imported by both apps.
- **Next.js (App Router)** for web — most NBC staff are on Windows desktops, and the heavy
  surfaces here (dashboards, calendars, large tables, **printing** of rosters/overtime/transport
  sheets) are far better served by a real web app with React Server Components.
- **Expo / React Native** for mobile — push notifications and on-the-go schedule access for crew
  (Android + iOS).
- **Supabase** for the backend: Postgres (data + business constraints), Auth (chosen over Clerk so
  role-based **row-level security** is native, free, and has fewer moving parts), Realtime (live
  updates), and Storage (scripts/rundowns).
- **Supabase Auth + RLS** is the security backbone: every table has RLS enabled and policies keyed
  off the signed-in user's role/department. The `@nbc/shared` `permissions` helper mirrors these
  for UI gating, but Postgres is the enforcement point.

## Shared package (`@nbc/shared`)

- `enums.ts` — domain enums + display labels (mirror the Postgres enum types).
- `constants.ts` — shifts, overtime thresholds, departments, specialised gear, production catalog.
- `overtime.ts` — pure overtime engine (weekday/Saturday/Sunday rules + production overrun).
- `conflicts.ts` — interval-overlap conflict detection + availability classification
  (`available` / `due_soon` / `busy`).
- `permissions.ts` — capability checks (`can.editDailyRoster`, etc.) + per-role home routes.
- `database.types.ts` — typed `Database` (regenerate with `pnpm db:gen-types`).
- `supabase.ts` — typed client factory.

## Data model

See [`supabase/migrations/0001_init.sql`](../supabase/migrations/0001_init.sql). Core entities:

| Table | Purpose |
| ----- | ------- |
| `departments` | Operations + content depts, with `parent_id` sub-divisions. |
| `profiles` | 1:1 with `auth.users`; role, employment type, department, supervisor, `needs_transport`, `can_go_on_trips`, `is_news_camera`. |
| `skills` / `profile_skills` | Operator capabilities (drone, steadicam…). |
| `shifts` | Roster cell codes (FM, GM, ST2, ST4, N, APP, TR, A1–A6, O, PH, L). |
| `productions` | Catalog of recurring/known shows. |
| `bookings` | **Central pipeline object** — one production instance, with full status machine. |
| `booking_crew` | Crew assignments (role label, attendance, stand-in, car/transport flags). |
| `booking_documents` | Scripts, rundowns, plans (addable any time). |
| `roster_periods` / `roster_assignments` | Monthly roster per department. |
| `production_logs` | Actual start/end → duration + overtime. |
| `overtime_entries` | Per-operator overtime for sheets/reports. |
| `production_reports` | Operator-raised issues (severity). |
| `equipment` / `equipment_bookings` | Storeroom with serial numbers + checkout flow. |
| `transport_requests` | Pickup-to-work list. |
| `car_bookings` | Vehicle-to-go-out reservations. |
| `notifications` | In-app / push / SMS. |
| `audit_log` | Status changes, stand-ins, overrides, cancellations — oversight & anti-abuse. |

Plus the `v_crew_schedule` view exposing per-operator busy windows for conflict detection.

## Booking state machine

```
draft ─submit─▶ submitted ─approve─▶ ep_approved ─assign─▶ crew_assigned ─confirm─▶ confirmed
                    │                      ▲                      │
        ep_changes_requested ◀────────────┘       supervisor_changes_requested
                    │ (producer edits, resubmits)                 │ (back to EP)
                                                      confirmed ─▶ in_progress ─▶ completed
                                          (cancelled reachable from any active state)
```

Each transition writes to `audit_log`. Conflict checks run before `confirmed`.

## Security (RLS) summary

- Reference data (`departments`, `shifts`, `skills`, `productions`) — readable by all authenticated
  users; writes restricted (admin, or supervisors for skills, content roles for productions).
- `bookings` — visible to the producer, the assigned EP, the content department, oversight roles,
  and assigned crew. Producers edit their own drafts; staff roles drive the pipeline transitions.
- `booking_crew` — crew see their own rows; supervisors/booking-officer manage; the producer of a
  booking can confirm attendance.
- `roster_*` — readable by all; **only supervisors** (their own department) + admin write — the
  booking officer is deliberately excluded from monthly-roster edits.
- `overtime_entries` — operator sees own; supervisors/managers/EP verify.
- `notifications` — recipient-scoped.

## Cross-cutting (later phases)

- **Realtime** — Supabase Realtime channels on `bookings`, `booking_crew`, `notifications` for live
  updates across roles.
- **Notifications + SMS** — Expo push for mobile; a Supabase Edge Function fans out to a Namibian
  SMS gateway for operators without smartphones.
- **Printing** — server-rendered print routes in the web app for daily rosters, overtime sheets
  (Annexure B), transport lists, and the artists' fees/royalties claim.
- **Recommendations** — operator suggestions ranked by availability, skill/gear match, and trip
  fairness; Pinecone only if semantic matching is ever needed.
