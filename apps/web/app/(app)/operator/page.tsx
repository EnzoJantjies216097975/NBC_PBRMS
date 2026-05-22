import { LOCATION_TYPE_LABELS, type LocationType } from '@nbc/shared';
import { requireRole } from '@/lib/auth';
import { createClient } from '@/lib/supabase/server';
import { fmtDateTimeRange } from '@/lib/format';

export default async function OperatorHome() {
  const { userId } = await requireRole(['operator', 'admin']);
  const supabase = await createClient();

  const { data: crew } = await supabase
    .from('booking_crew')
    .select(
      'id, role_label, status, booking:bookings(id, title, location_type, venue, call_date, call_time, end_time)',
    )
    .eq('profile_id', userId);

  const rows = (crew ?? [])
    .map((c) => ({ ...c, booking: Array.isArray(c.booking) ? c.booking[0] : c.booking }))
    .filter((c) => c.booking)
    .sort((a, b) => (a.booking!.call_date ?? '').localeCompare(b.booking!.call_date ?? ''));

  return (
    <div className="mx-auto max-w-4xl">
      <h1 className="mb-1 text-2xl font-semibold">My Schedule</h1>
      <p className="mb-6 text-sm text-slate-600">Productions you are assigned to.</p>

      {rows.length === 0 && (
        <div className="card text-sm text-slate-600">You have no assigned productions yet.</div>
      )}

      <div className="space-y-3">
        {rows.map((c) => (
          <div key={c.id} className="card">
            <h3 className="font-medium">{c.booking!.title}</h3>
            <p className="mt-1 text-sm text-slate-600">
              {c.role_label || 'Crew'} ·{' '}
              {LOCATION_TYPE_LABELS[c.booking!.location_type as LocationType]}
              {c.booking!.venue ? ` · ${c.booking!.venue}` : ''}
            </p>
            <p className="text-sm text-slate-500">
              {fmtDateTimeRange(c.booking!.call_date, c.booking!.call_time, c.booking!.end_time)}
            </p>
          </div>
        ))}
      </div>
    </div>
  );
}
