# Setup

## Prerequisites

- **Node.js ≥ 20** (Node 25 works for the web app; for Expo prefer an even-numbered LTS — 20/22/24).
- **pnpm** (`npm install -g pnpm`, or via Corepack).
- **Docker** (for running Supabase locally).
- **Supabase CLI** (`npm install -g supabase` or platform installer).

## Corporate network / firewall

NBC's network sits behind a **Fortinet FortiGate** firewall that intercepts TLS and (currently)
**blocks the npm registry** with an HTTP 403 block page. Two issues, two fixes:

### 1. TLS interception (`UNABLE_TO_VERIFY_LEAF_SIGNATURE`)

The firewall presents certificates signed by NBC's own CA (`FG100ETK19029083`), which Node doesn't
trust by default. Options, best first:

```powershell
# A) Trust the corporate CA (preferred). Get Fortinet_CA.cer from NBC IT, or extract the chain:
node scripts/extract-corp-ca.cjs "C:\Users\<you>\corp-ca.pem"
npm config set cafile "C:\Users\<you>\corp-ca.pem"
# also helps Expo / other tools:
setx NODE_EXTRA_CA_CERTS "C:\Users\<you>\corp-ca.pem"

# B) Relax verification (quick, less secure — package integrity is still hash-checked):
npm config set strict-ssl false --location=user
```

### 2. Registry blocked (HTTP 403 from FortiGuard)

Even with TLS fixed, the firewall may refuse `registry.npmjs.org` outright. To install dependencies:

- **Ask NBC IT to whitelist** `registry.npmjs.org` and the npm CDN hosts, **or**
- Install from a network that isn't filtered (home, mobile hotspot), **or**
- Point pnpm at an **internal registry mirror** (e.g. Verdaccio/Nexus) if NBC runs one:
  `npm config set registry https://<internal-mirror>/`

Everything in this repo except the dependency install works fully offline.

## Supabase

```bash
supabase start          # boots Postgres, Auth, Studio, etc. in Docker
supabase db reset       # applies migrations 0001–0008 (schema, RLS, seed)
```

Grab the local **API URL** and **anon key** from `supabase start` output (or `supabase status`) and
put them in the env files. For a hosted project, use the values from Project Settings → API.

**Generate DB types — required before `build`/`typecheck`:**

```bash
pnpm db:gen-types       # overwrites packages/shared/src/database.types.ts
```

The committed `database.types.ts` is a *partial stub*: it covers the booking-pipeline tables so
`pnpm dev` runs (Next.js doesn't type-check in dev), but `pnpm build` and `pnpm typecheck` need the
**full** generated types (every table + relationships) — so run `db:gen-types` after `db reset` and
re-run it whenever the schema changes.

## Environment files

| File                          | Keys                                                       |
| ----------------------------- | ---------------------------------------------------------- |
| `apps/web/.env.local`         | `NEXT_PUBLIC_SUPABASE_URL`, `NEXT_PUBLIC_SUPABASE_ANON_KEY`|
| `apps/mobile/.env`            | `EXPO_PUBLIC_SUPABASE_URL`, `EXPO_PUBLIC_SUPABASE_ANON_KEY`|

## Running

```bash
pnpm dev:web        # Next.js on http://localhost:3000
pnpm dev:mobile     # Expo dev server (scan QR with Expo Go, or run a simulator)
pnpm typecheck      # type-check every package
pnpm build          # build all
```

## Notifications & delivery

- **In-app** notifications work out of the box (the `notifications` table + RLS).
- **Live updates** use Supabase Realtime — migrations `0007`/`0008` add `notifications`, `bookings`,
  and `booking_crew` to the `supabase_realtime` publication, so the bell, inbox, and schedule views
  refresh without reloading.
- **Push + SMS** are delivered by the `notify-dispatch` Edge Function. To enable:
  1. `supabase functions deploy notify-dispatch`
  2. Set the SMS gateway secrets (`SMS_PROVIDER_URL`, `SMS_PROVIDER_API_KEY`, `SMS_PROVIDER_SENDER_ID`).
  3. Add a Database Webhook on `notifications` INSERT → `notify-dispatch` (or the `pg_net` trigger).
  See `supabase/functions/notify-dispatch/README.md`. Operators without a smartphone get SMS when
  their profile has `prefers_sms = true`; mobile devices self-register push tokens on login.

## Creating your first users

1. Sign up on the web app — this creates an `operator` profile by default.
2. Promote one user to `admin` (Supabase Studio → `profiles` table → set `role = admin`), then use
   the **People & Roles** screen / Studio to assign everyone else's role and department.
