import Link from 'next/link';
import { LOCATION_TYPE_LABELS, type BookingStatus, type LocationType } from '@nbc/shared';
import { requireRole } from '@/lib/auth';
import { createClient } from '@/lib/supabase/server';
import { StatusBadge } from '@/components/StatusBadge';
import { fmtDateTimeRange } from '@/lib/format';

export default async function ProducerHome() {
  const { userId } = await requireRole(['producer', 'executive_producer', 'admin']);
  const supabase = await createClient();

  const { data: bookings } = await supabase
    .from('bookings')
    .select('id, title, status, location_type, venue, call_date, call_time, end_time, ep_feedback')
    .eq('producer_id', userId)
    .order('call_date', { ascending: true });

  return (
    <div className="mx-auto max-w-4xl">
      <div className="mb-6 flex items-center justify-between">
        <h1 className="text-2xl font-semibold">My Productions</h1>
        <Link href="/producer/new" className="btn-primary">
          + New Booking
        </Link>
      </div>

      {(!bookings || bookings.length === 0) && (
        <div className="card text-sm text-slate-600">
          No bookings yet. Create your first one with <strong>New Booking</strong>.
        </div>
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
              {b.status === 'ep_changes_requested' && b.ep_feedback && (
                <p className="mt-2 rounded-md bg-orange-50 px-3 py-2 text-sm text-orange-800">
                  Exec Producer needs: {b.ep_feedback}
                </p>
              )}
            </div>
          </div>
        ))}
      </div>
    </div>
  );
}
