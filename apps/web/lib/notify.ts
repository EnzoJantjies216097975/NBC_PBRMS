import type { TablesInsert } from '@nbc/shared';
import type { createClient } from '@/lib/supabase/server';

type SupabaseServerClient = Awaited<ReturnType<typeof createClient>>;
type NewNotification = Omit<TablesInsert<'notifications'>, 'channel'> & { channel?: string };

/**
 * Fire-and-collect in-app notifications. Phase 6 adds push + SMS fan-out via an
 * Edge Function; for now these land in the `notifications` table and surface in
 * each user's feed (recipient-scoped by RLS).
 */
export async function notify(supabase: SupabaseServerClient, rows: NewNotification[]) {
  const payload = rows.filter((r) => r.recipient_id);
  if (payload.length === 0) return;
  await supabase
    .from('notifications')
    .insert(payload.map((r) => ({ channel: 'in_app', ...r })));
}
