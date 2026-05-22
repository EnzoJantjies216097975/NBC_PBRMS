import { requireProfile } from '@/lib/auth';
import { createClient } from '@/lib/supabase/server';
import { fmtTimestamp } from '@/lib/format';
import { markAllRead, markRead } from './actions';

export default async function NotificationsPage() {
  const { userId } = await requireProfile();
  const supabase = await createClient();

  const { data: notifications } = await supabase
    .from('notifications')
    .select('id, type, title, body, read, created_at')
    .eq('recipient_id', userId)
    .order('created_at', { ascending: false })
    .limit(100);

  const items = notifications ?? [];
  const unread = items.filter((n) => !n.read).length;

  return (
    <div className="mx-auto max-w-2xl">
      <div className="mb-4 flex items-center justify-between">
        <div>
          <h1 className="text-2xl font-semibold">Notifications</h1>
          <p className="text-sm text-slate-600">{unread} unread</p>
        </div>
        {unread > 0 && (
          <form action={markAllRead}>
            <button className="btn-ghost" type="submit">
              Mark all read
            </button>
          </form>
        )}
      </div>

      {items.length === 0 && (
        <div className="card text-sm text-slate-600">You have no notifications yet.</div>
      )}

      <div className="space-y-2">
        {items.map((n) => {
          const body = (
            <>
              <div className="flex items-center justify-between gap-3">
                <span className={`font-medium ${n.read ? 'text-slate-600' : 'text-slate-900'}`}>
                  {!n.read && <span className="mr-2 inline-block h-2 w-2 rounded-full bg-nbc align-middle" />}
                  {n.title}
                </span>
                <span className="shrink-0 text-xs text-slate-400">{fmtTimestamp(n.created_at)}</span>
              </div>
              {n.body && <p className="mt-1 text-sm text-slate-600">{n.body}</p>}
            </>
          );

          return n.read ? (
            <div key={n.id} className="card bg-slate-50">
              {body}
            </div>
          ) : (
            <form key={n.id} action={markRead}>
              <input type="hidden" name="id" value={n.id} />
              <button
                type="submit"
                className="card block w-full border-nbc/30 text-left transition hover:bg-slate-50"
                title="Mark as read"
              >
                {body}
              </button>
            </form>
          );
        })}
      </div>
    </div>
  );
}
