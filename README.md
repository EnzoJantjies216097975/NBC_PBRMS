# NBC PBRMS

**Production Booking & Roster Management System** for the Namibian Broadcasting Corporation.

A web + mobile platform that lets producers raise production bookings, executive producers
validate them and assign channels, TV-Operations supervisors crew them (with availability and
conflict detection), and the booking officer run the day-to-day — with rosters, overtime tracking,
equipment/storeroom, transport lists, real-time notifications (with SMS fallback), and printable
oversight reports.

> Status: **Phase 1 scaffold.** Monorepo, shared domain layer, full database schema with RLS +
> seed data, web auth + role-aware shell, the Producer → Executive Producer booking pipeline, and
> a mobile auth skeleton are in place. See [docs/ROADMAP.md](docs/ROADMAP.md) for what's next.

## Tech stack

| Concern        | Choice                                             |
| -------------- | -------------------------------------------------- |
| Web            | Next.js (App Router, React Server Components)       |
| Mobile         | Expo / React Native                                |
| Shared logic   | `@nbc/shared` TypeScript package                   |
| Backend / DB   | Supabase (Postgres + Auth + RLS + Realtime + Storage) |
| Monorepo       | pnpm workspaces + Turborepo                        |
| Hosting / DNS  | Vercel (web), Cloudflare (DNS), NameCheap (domain) |
| Errors         | Sentry                                             |
| Vector search  | Pinecone (only if/when needed)                     |

## Repository layout

```
NBC_PBRMS/
├─ apps/
│  ├─ web/        Next.js web app (dashboards, forms, printing)
│  └─ mobile/     Expo app (notifications, on-the-go schedule)
├─ packages/
│  └─ shared/     Domain types, enums, business rules (overtime, conflicts), permissions
├─ supabase/
│  ├─ migrations/ 0001 schema · 0002 RLS · 0003 seed
│  └─ config.toml
├─ scripts/       Setup helpers (e.g. corporate CA extraction)
└─ docs/          SPEC · ARCHITECTURE · SETUP · ROADMAP
```

## Quick start

> ⚠️ **NBC corporate network blocks the npm registry** (Fortinet firewall returns HTTP 403).
> Dependency installs must run from a network that can reach `registry.npmjs.org`, or IT must
> whitelist it. See [docs/SETUP.md](docs/SETUP.md#corporate-network--firewall) — this is the only
> step that needs the internet; everything else is already written.

```bash
# 1. Install dependencies (from an unblocked network)
pnpm install

# 2. Start Supabase locally (needs Docker) and apply migrations + seed
supabase start
supabase db reset           # runs migrations 0001–0003

# 3. Configure env
cp .env.example .env
cp apps/web/.env.local.example apps/web/.env.local       # fill in Supabase URL + anon key
cp apps/mobile/.env.example apps/mobile/.env

# 4. Run
pnpm dev:web        # http://localhost:3000
pnpm dev:mobile     # Expo dev server
```

## Documentation

- [docs/SPEC.md](docs/SPEC.md) — what the system must do (roles, rules, modules).
- [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) — tech decisions, data model, workflows.
- [docs/SETUP.md](docs/SETUP.md) — local setup, Supabase, and the corporate-firewall fix.
- [docs/ROADMAP.md](docs/ROADMAP.md) — phased delivery plan.
