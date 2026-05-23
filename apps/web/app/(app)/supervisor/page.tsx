import Link from 'next/link';
import { LOCATION_TYPE_LABELS, type BookingStatus, type LocationType } from '@nbc/shared';
import { requireRole } from '@/lib/auth';
import { createClient } from '@/lib/supabase/server';
import { StatusBadge } from '@/components/StatusBadge';
import { ScheduleRealtime } from '@/components/ScheduleRealtime';
import { fmtDateTimeRange } from '@/lib/format';

export default async function SupervisorHome() {
  await requireRole(['supervisor', 'admin']);
  const supabase = await createClient();

  const { data: bookings } = await supabase
    .from('bookings')
    .select('id, title, status, location_type, venue, call_date, call_time, end_time, specialised_equipment')
    .in('status', ['ep_approved', 'crew_assigned'])
    .order('call_date', { ascending: true });

  return (
    <div className="mx-auto max-w-4xl">
      <ScheduleRealtime />
      <h1 className="mb-1 text-2xl font-semibold">Awaiting Crew</h1>
      <p className="mb-6 text-sm text-slate-600">
        Approved bookings ready for crew assignment. Open one to pick crew with live availability and
        conflict flagging.
      </p>

      {(!bookings || bookings.length === 0) && (
        <div className="card text-sm text-slate-600">Nothing awaiting crew right now.</div>
      )}

      <div className="space-y-3">
        {(bookings ?? []).map((b) => (
          <div key={b.id} className="card flex items-start justify-between gap-4">
            <div>
              <div className="flex items-center gap-3">
                <h3 className="font-medium">{b.title}</h3>
                <StatusBadge status={b.status as BookingStatus} />
              </div>
              <p className="mt-1 text-sm text-slate-600">
                {LOCATION_TYPE_LABELS[b.location_type as LocationType]}
                {b.venue ? ` · ${b.venue}` : ''} · {fmtDateTimeRange(b.call_date, b.call_time, b.end_time)}
              </p>
              {(b.specialised_equipment ?? []).length > 0 && (
                <p className="mt-1 text-xs text-slate-500">Gear: {b.specialised_equipment.join(', ')}</p>
              )}
            </div>
            <Link className="btn-primary shrink-0" href={`/supervisor/assign/${b.id}`}>
              {b.status === 'crew_assigned' ? 'Edit crew' : 'Assign crew'}
            </Link>
          </div>
        ))}
      </div>
    </div>
  );
}
