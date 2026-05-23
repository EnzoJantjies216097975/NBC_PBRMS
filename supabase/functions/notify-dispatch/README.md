# notify-dispatch

Fans every new row in `notifications` out to **Expo push** and, for operators
flagged `profiles.prefers_sms`, to **SMS**. In-app delivery already happens via
the `notifications` table + Realtime; this function adds the off-app channels.

## Deploy

```bash
supabase functions deploy notify-dispatch

# Secrets (SUPABASE_URL + SUPABASE_SERVICE_ROLE_KEY are injected automatically):
supabase secrets set SMS_PROVIDER_URL="https://<your-namibian-gateway>/send"
supabase secrets set SMS_PROVIDER_API_KEY="..."
supabase secrets set SMS_PROVIDER_SENDER_ID="NBC"
```

> The SMS request body in `index.ts` (`{ to, from, message }`) is a placeholder —
> adapt it to your gateway's API.

## Trigger it on new notifications

**Option A — Database Webhook (recommended, no SQL):** Supabase Dashboard →
Database → Webhooks → *Create* → table `notifications`, event `INSERT`, type
*Supabase Edge Function*, select `notify-dispatch`.

**Option B — `pg_net` trigger:**

```sql
create extension if not exists pg_net;

create or replace function dispatch_notification()
returns trigger language plpgsql security definer as $$
begin
  perform net.http_post(
    url     := '<FUNCTIONS_URL>/notify-dispatch',
    headers := jsonb_build_object('Content-Type', 'application/json',
                                  'Authorization', 'Bearer <ANON_OR_SERVICE_KEY>'),
    body    := jsonb_build_object('record', to_jsonb(new))
  );
  return new;
end;
$$;

create trigger trg_dispatch_notification
  after insert on notifications
  for each row execute function dispatch_notification();
```

Mobile devices register their Expo push token into `device_tokens` on login
(see `apps/mobile/lib/push.ts`).
