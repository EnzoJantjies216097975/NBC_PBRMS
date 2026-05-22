import { fullName, formatMinutes, type BookingStatus } from '@nbc/shared';
import { requireRole } from '@/lib/auth';
import { createClient } from '@/lib/supabase/server';
import { StatusBadge } from '@/components/StatusBadge';
import { fmtDateTimeRange } from '@/lib/format';

interface AppRow {
  id: string;
  title: string;
  status: BookingStatus;
  call_date: string;
  call_time: string;
  end_time: string | null;
  booking_crew: { profile: { first_name: string; last_name: string } | { first_name: string; last_name: string }[] | null }[] | null;
  production_logs: { actual_start: string | null; actual_end: string | null }[] | null;
}

function loggedMinutes(logs: AppRow['production_logs']): number {
  let total = 0;
  for (const l of logs ?? []) {
    if (l.actual_start && l.actual_end) {
      total += Math.max(0, Math.round((new Date(l.actual_end).getTime() - new Date(l.actual_start).getTime()) / 60_000));
    }
  }
  return total;
}

export default async function AppLogPage() {
  await requireRole(['supervisor', 'admin']);
  const supabase = await createClient();

  const { data } = await supabase
    .from('bookings')
    .select(
      'id, title, status, call_date, call_time, end_time, ' +
        'booking_crew(profile:profiles(first_name, last_name)), ' +
        'production_logs(actual_start, actual_end)',
    )
    .eq('location_type', 'audio_post')
    .order('call_date', { ascending: false });
  const sessions = (data ?? []) as unknown as AppRow[];

  const totalMinutes = sessions.reduce((s, r) => s + loggedMinutes(r.production_logs), 0);

  return (
    <div className="mx-auto max-w-4xl">
      <h1 className="mb-1 text-2xl font-semibold">APP Usage Log</h1>
      <p className="mb-4 text-sm text-slate-600">
        Every Audio Post Production session — {sessions.length} total · {formatMinutes(totalMinutes)}{' '}
        logged.
      </p>

      {sessions.length === 0 ? (
        <div className="card text-sm text-slate-600">No APP sessions booked yet.</div>
      ) : (
        <div className="overflow-hidden rounded-lg border border-slate-200 bg-white">
          <table className="w-full text-sm">
            <thead className="bg-slate-50 text-left text-slate-500">
              <tr>
                <th className="px-4 py-2">Production</th>
                <th className="px-4 py-2">When</th>
                <th className="px-4 py-2">Operators</th>
                <th className="px-4 py-2">Logged</th>
                <th className="px-4 py-2">Status</th>
              </tr>
            </thead>
            <tbody>
              {sessions.map((r) => {
                const ops = (r.booking_crew ?? [])
                  .map((c) => (Array.isArray(c.profile) ? c.profile[0] : c.profile))
                  .filter((p): p is { first_name: string; last_name: string } => Boolean(p))
                  .map((p) => fullName(p))
                  .join(', ');
                const mins = loggedMinutes(r.production_logs);
                return (
                  <tr key={r.id} className="border-t border-slate-100">
                    <td className="px-4 py-2 font-medium">{r.title}</td>
                    <td className="px-4 py-2 text-slate-600">
                      {fmtDateTimeRange(r.call_date, r.call_time, r.end_time)}
                    </td>
                    <td className="px-4 py-2 text-slate-600">{ops || '—'}</td>
                    <td className="px-4 py-2">{mins > 0 ? formatMinutes(mins) : '—'}</td>
                    <td className="px-4 py-2">
                      <StatusBadge status={r.status} />
                    </td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        </div>
      )}
    </div>
  );
}
