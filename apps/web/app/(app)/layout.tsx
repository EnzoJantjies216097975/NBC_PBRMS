import Link from 'next/link';
import { USER_ROLE_LABELS, fullName } from '@nbc/shared';
import { requireProfile } from '@/lib/auth';
import { createClient } from '@/lib/supabase/server';
import { NAV_BY_ROLE } from '@/lib/nav';
import { RealtimeRefresh } from '@/components/RealtimeRefresh';
import { signOut } from '../(auth)/actions';

export default async function AppLayout({ children }: { children: React.ReactNode }) {
  const { userId, profile } = await requireProfile();
  const nav = NAV_BY_ROLE[profile.role] ?? [];

  const supabase = await createClient();
  const { count } = await supabase
    .from('notifications')
    .select('id', { count: 'exact', head: true })
    .eq('recipient_id', userId)
    .eq('read', false);
  const unread = count ?? 0;
  const bellLabel = unread > 0 ? `Notifications, ${unread} unread` : 'Notifications';

  return (
    <div className="flex min-h-screen">
      <RealtimeRefresh userId={userId} />
      <aside className="no-print hidden w-64 shrink-0 flex-col bg-nbc-dark text-white md:flex">
        <div className="border-b border-white/10 px-5 py-4">
          <div className="text-lg font-bold">NBC PBRMS</div>
          <div className="text-xs text-white/70">{USER_ROLE_LABELS[profile.role]}</div>
        </div>
        <nav className="flex-1 space-y-1 px-3 py-4">
          {nav.map((item) => (
            <Link
              key={item.href}
              href={item.href}
              className="block rounded-md px-3 py-2 text-sm text-white/90 hover:bg-white/10"
            >
              {item.label}
            </Link>
          ))}
        </nav>
        <div className="border-t border-white/10 px-5 py-4 text-xs text-white/70">
          {fullName(profile)}
        </div>
      </aside>

      <div className="flex flex-1 flex-col">
        <header className="no-print flex items-center justify-between border-b border-slate-200 bg-white px-6 py-3">
          <div className="text-sm text-slate-500">
            {USER_ROLE_LABELS[profile.role]} &middot; {fullName(profile)}
          </div>
          <div className="flex items-center gap-3">
            <Link
              href="/notifications"
              className="relative rounded-md p-2 hover:bg-slate-100"
              title={bellLabel}
              aria-label={bellLabel}
            >
              <span className="text-lg" aria-hidden>
                🔔
              </span>
              {unread > 0 && (
                <span className="absolute -right-0.5 -top-0.5 flex h-4 min-w-4 items-center justify-center rounded-full bg-nbc-accent px-1 text-[10px] font-semibold text-white">
                  {unread > 9 ? '9+' : unread}
                </span>
              )}
            </Link>
            <form action={signOut}>
              <button className="btn-ghost" type="submit">
                Sign out
              </button>
            </form>
          </div>
        </header>
        <main className="flex-1 p-6">{children}</main>
      </div>
    </div>
  );
}
