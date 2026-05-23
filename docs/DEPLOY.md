# Deployment

End-to-end production setup. Do these once, then redeploys are automatic on push.

## 1. Supabase (backend)

```bash
supabase link --project-ref <your-ref>
supabase db push                      # applies migrations 0001–0008
supabase functions deploy notify-dispatch
supabase secrets set SMS_PROVIDER_URL=... SMS_PROVIDER_API_KEY=... SMS_PROVIDER_SENDER_ID=NBC
```

Then add a **Database Webhook** on `notifications` INSERT → `notify-dispatch`
(see `supabase/functions/notify-dispatch/README.md`).

## 2. Web app on Vercel

Create a Vercel project from the GitHub repo and set:

- **Root Directory:** `apps/web` (Vercel auto-detects Next.js + Turborepo and builds
  the `@nbc/shared` workspace dep).
- **Environment variables:** `NEXT_PUBLIC_SUPABASE_URL`, `NEXT_PUBLIC_SUPABASE_ANON_KEY`
  (+ `NEXT_PUBLIC_SENTRY_DSN` once Sentry is added).

Every push to `main` deploys; preview deploys per PR.

## 3. Domain & DNS (NameCheap + Cloudflare)

1. Add the domain to **Cloudflare**; point NameCheap's nameservers at Cloudflare.
2. In Cloudflare DNS, add the `CNAME`/`A` records Vercel shows for your domain.
3. Add the custom domain in the Vercel project; let it issue the certificate.

## 4. Mobile app (Expo / EAS)

```bash
cd apps/mobile
npx eas build --platform all          # requires an Expo account + EAS config
```

Set `EXPO_PUBLIC_SUPABASE_URL` / `EXPO_PUBLIC_SUPABASE_ANON_KEY` as EAS secrets.
Push needs an EAS `projectId` in `app.json` for `getExpoPushTokenAsync` to return a token.

## Still to harden (remaining Phase 7)

- **Sentry** — install `@sentry/nextjs` (web) + `@sentry/react-native` (mobile), wire the DSNs
  from env (placeholders already in `.env.example`).
- **e2e tests** — Playwright against the web app; unit tests for the pure libs in `@nbc/shared`
  (`overtime`, `conflicts`, `recommend`) and `apps/web/lib` (`reports`, `schedule`).
- **Data import** — seed real crew/operators and the recurring-production catalog per studio.

> Corporate-network note: `pnpm install`, `supabase`, and `git push` may all hit NBC's Fortinet
> TLS interception / registry block — see [SETUP.md](SETUP.md#corporate-network--firewall). Deploy
> from an unblocked network or via CI.
