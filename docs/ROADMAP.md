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

## Phase 5 — Storeroom, transport & APP

- Equipment inventory by serial number; book-out → approve → checkout → return + free-again alerts.
- Transport list (pickups) + car bookings (vehicles to go out), both printable.
- APP fast-track flow with Sound Supervisor logging.

## Phase 6 — Realtime, notifications, reports

- ✅ In-app notifications inbox (`/notifications`) + header bell with unread badge + mark read/all.
- Supabase Realtime everywhere; Expo push; **SMS fallback** via Edge Function + Namibian gateway.
- Reports & analytics dashboards, all printable (overtime, studio usage, cancellations, trip
  fairness, underutilisation). Department calendar + clash view for EPs.

## Phase 7 — Hardening & launch

- Sentry, audit-log review UI, role-assignment admin UI, e2e tests, Vercel + Cloudflare + NameCheap
  deployment, data import of existing crew/operators.
