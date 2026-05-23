// Supabase Edge Function (Deno) — fans a new notification out to Expo push and,
// for operators without a smartphone (profiles.prefers_sms), to SMS.
//
// Invoke via a Database Webhook on `notifications` INSERT (see ./README.md).
// Deploy:  supabase functions deploy notify-dispatch
import { createClient } from 'jsr:@supabase/supabase-js@2';

interface NotificationRecord {
  recipient_id: string;
  title: string;
  body: string | null;
}

Deno.serve(async (req) => {
  try {
    const payload = await req.json();
    const record: NotificationRecord | undefined = payload?.record;
    if (!record?.recipient_id) return new Response('no record', { status: 200 });

    const supabase = createClient(
      Deno.env.get('SUPABASE_URL')!,
      Deno.env.get('SUPABASE_SERVICE_ROLE_KEY')!,
    );

    const [{ data: profile }, { data: tokens }] = await Promise.all([
      supabase.from('profiles').select('phone, prefers_sms').eq('id', record.recipient_id).maybeSingle(),
      supabase.from('device_tokens').select('token').eq('profile_id', record.recipient_id),
    ]);

    const message = record.body ? `${record.title} — ${record.body}` : record.title;

    // 1) Expo push to all the recipient's registered devices.
    const pushTokens = (tokens ?? [])
      .map((t: { token: string }) => t.token)
      .filter((t: string) => t.startsWith('ExponentPushToken'));
    if (pushTokens.length > 0) {
      await fetch('https://exp.host/--/api/v2/push/send', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(
          pushTokens.map((to: string) => ({ to, title: record.title, body: record.body ?? '' })),
        ),
      });
    }

    // 2) SMS fallback for operators flagged prefers_sms (no smartphone).
    //    Body shape is provider-specific — adapt to your Namibian SMS gateway.
    if (profile?.prefers_sms && profile.phone) {
      const smsUrl = Deno.env.get('SMS_PROVIDER_URL');
      const smsKey = Deno.env.get('SMS_PROVIDER_API_KEY');
      const sender = Deno.env.get('SMS_PROVIDER_SENDER_ID') ?? 'NBC';
      if (smsUrl && smsKey) {
        await fetch(smsUrl, {
          method: 'POST',
          headers: { 'Content-Type': 'application/json', Authorization: `Bearer ${smsKey}` },
          body: JSON.stringify({ to: profile.phone, from: sender, message }),
        });
      }
    }

    return new Response('ok', { status: 200 });
  } catch (e) {
    // Always 200 so the webhook isn't retried forever; log for debugging.
    console.error('notify-dispatch error', e);
    return new Response('handled', { status: 200 });
  }
});
