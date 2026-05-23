# NBC PBRMS — Roadmap

Phased so each step ships usable value. Phase 1 (this scaffold) establishes the spine; later phases
layer modules onto the same data model.

## Phase 1 — Foundation ✅ (this scaffold)

- Monorepo (pnpm + Turborepo), shared domain package.
- Full Postgres schema with RLS + seed data (departments, shifts, skills, production catalog).
- Overtime engine + conflict-detection logic (pure, tested-ready).
- Web: Supabase Auth, sign-up capturing profile fields, role-aware shell.
- **Booking pipeline slice**: Producer creates/submits a booking → Executive Producer
  approves (with channel) or requests changes.
- Mobile auth skeleton on the same backend.

## Phase 2 — Crewing & conflicts ✅

- ✅ Supervisor **crew picker**: assign Camera/Sound/Lighting (2 each) with availability classes
  (available / due-soon / busy), skills shown inline, **hard conflict blocking** + acknowledge-to-override.
- ✅ `conflicts.ts` wired into the picker, with authoritative server-side re-validation.
- ✅ Supervisor declines → back to EP with reasons; assign → notifications to operators + producer.
- ✅ Booking Officer **All Bookings** list → detail (crew + contacts, producer's department) →
  **confirm** (notifies producer + crew). Booking-crew status flips to `confirmed`.
- ✅ Booking Officer **dynamic studio-row daily calendar**: date nav, per-production time bars,
  inline clash highlighting (studio overlaps + operator double-bookings), requests panel on the
  right, available crew by Camera/Sound/Lighting, plus a "now" line for today. Logic extracted to a
  pure `buildDaySchedule`.
- ✅ Operator-recommendation **ranking** (`operatorScore` in `@nbc/shared`): picker orders crew by
  availability + ★ already-in-studio + ✓ requested-gear skill match, marks ✅ recommended, and
  highlights matched gear.

## Phase 3 — Roster (in progress)

- ✅ Monthly roster grid (operators × days, shift codes) editable by supervisors; per-department,
  per-month; weekend shading + shift-code colouring; save changed cells; **publish & notify** operators.
- ✅ Operator read-only roster view (own row highlighted) + nav links.
- ✅ Auto-fill generators: **copy previous month** (each operator's most-common shift per weekday)
  and **set weekends Off** — both non-destructive (blank cells only).
- ✅ **Standing productions overlay**: the daily calendar auto-shows recurring daily/weekend/flagship
  shows (from the catalog via `recurringProductionsForDate`) as dashed bars, de-duplicated against
  real bookings and included in studio-clash checks.
- ⏳ Promote a standing show into a real, crewable booking (one-click) — needs nullable producer or a
  system producer.
- ⏳ Month-end reminder to supervisors (a week before) — needs the scheduled-jobs/notifications layer.
- ⏳ Printable daily roster (the live daily calendar already exists in Phase 2).

## Phase 4 — Execution & overtime (in progress)

- ✅ Producer **run page**: confirm attendance, **signal a missing operator** (notify + phone shown),
  **start/end** logging (`production_logs`); overrun (≥30 min) flagged and the crew's supervisors notified.
- ✅ Operator **overtime view** computed daily from the logs (weekday 8h / Sat 5h / Sun all-OT) with a
  **printable Annexure-B sheet** (print CSS + `PrintButton`, shell hidden via `no-print`).
- ✅ Operator **production reports** with severity + an in-page severity help guide.
- ⏳ 1-hour-before reminders (needs the scheduled-jobs layer).
- ⏳ Stand-in logging; explicit ghost-booking / location-shoot abuse reports (attendance + start/end
  logging already capture the underlying data).

## Phase 5 — Storeroom, transport & APP ✅

- ✅ **Storeroom** (`/storeroom`, all roles): inventory by serial number with "held by", operator
  request → supervisor approve+checkout / deny → mark returned, and a **free-again waitlist notify**
  to the next requester on return.
- ✅ **Transport list** (`/booking-officer/transport`): printable per-date pickup list (name, phone,
  address, production, pickup/drop-off) derived from crew flagged `needs_transport`.
- ✅ **Car-booking visibility**: a 🚗 badge on the operator's schedule for assignments flagged
  `needs_car_booking` in the crew picker.
- ✅ **APP fast-track** (`/producer/app`): producer books a confirmed `audio_post` session that
  bypasses the pipeline, assigns the two APP operators (via a scoped RLS policy), and notifies the
  Sound Supervisor + Booking Officer. Sound Supervisor **APP usage log** (`/supervisor/app-log`)
  shows each session's production, when, operators, and logged duration.

## Phase 6 — Realtime, notifications, reports (in progress)

- ✅ In-app notifications inbox (`/notifications`) + header bell with unread badge + mark read/all.
- ✅ **Reports & analytics** (`/reports`, printable, month nav): overtime per operator + total,
  studio usage, trips & locations (with crew), cancellations, and underutilised-operator flags.
  Pure aggregation in `apps/web/lib/reports.ts`.
- ✅ **EP department calendar** (`/exec-producer/calendar`): whole-day clash view + the EP's own
  department's productions with crew + gear, date navigation.
- ✅ **Live in-app notifications** via Supabase Realtime (`0007_realtime.sql` + `RealtimeRefresh`):
  the bell/inbox update without a page refresh.
- ✅ **Live schedules**: `0008` adds bookings + crew to realtime; `ScheduleRealtime` refreshes the
  daily calendar, EP calendar, supervisor and operator schedules on any booking/crew change.
- ✅ **Off-app delivery scaffolded**: `notify-dispatch` Edge Function fans new notifications to
  **Expo push** (devices register in `device_tokens`) and **SMS** for operators flagged
  `prefers_sms`. Needs deploy + a Database Webhook + SMS-gateway config (see the function README).

## Phase 7 — Hardening & launch (in progress)

- ✅ **Role-assignment admin** (`/admin`): editable role + department + active per person.
- ✅ **Supervisor "My Operators"** (`/supervisor/operators`): tag skills/gear + flags
  (needs-transport, prefers-SMS, can-go-on-trips).
- ✅ **Audit-log viewer** (`/audit`, managers/admin): pipeline actions, overrides, admin changes.
- ✅ **Deployment guide** ([DEPLOY.md](DEPLOY.md)): Supabase + Vercel + Cloudflare/NameCheap + EAS.
- ✅ **Unit tests** (Vitest) for the pure logic — overtime, conflicts, recommendations, recurring
  productions, reports aggregation, and day-schedule clash detection (`pnpm test`).
- ⏳ Sentry wiring (`@sentry/nextjs` + `@sentry/react-native`; DSN placeholders in `.env.example`).
- ⏳ e2e tests (Playwright against the web app).
- ⏳ Actual deployment + data import of real crew/operators.
