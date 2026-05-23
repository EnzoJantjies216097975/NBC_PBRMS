import Link from 'next/link';
import { LOCATION_TYPE_LABELS, type BookingStatus, type LocationType } from '@nbc/shared';
import { requireRole } from '@/lib/auth';
import { createClient } from '@/lib/supabase/server';
import { StatusBadge } from '@/components/StatusBadge';
import { PeriodTabs } from '@/components/PeriodTabs';
import { fmtDateTimeRange } from '@/lib/format';
import { parseView, periodRange, shiftPeriod } from '@/lib/period';
import { todayIso } from '@/lib/schedule';

export default async function ProducerHome({
  searchParams,
}: {
  searchParams: Promise<{ view?: string; date?: string }>;
}) {
  const { userId } = await requireRole(['producer', 'executive_producer', 'admin']);
  const sp = await searchParams;
  const view = parseView(sp.view, 'all');
  const date = sp.date ?? todayIso();
  const range = periodRange(view, date);
  const supabase = await createClient();

  let query = supabase
    .from('bookings')
    .select('id, title, status, location_type, venue, call_date, call_time, end_time, ep_feedback')
    .eq('producer_id', userId);
  if (range.start && range.end) {
    query = query.gte('call_date', range.start).lte('call_date', range.end);
  }
  const { data: bookings } = await query.order('call_date', { ascending: true });

  return (
    <div className="mx-auto max-w-4xl">
      <div className="mb-4 flex items-center justify-between">
        <h1 className="text-2xl font-semibold">My Productions</h1>
        <Link href="/producer/new" className="btn-primary">
          + New Booking
        </Link>
      </div>

      <PeriodTabs
        basePath="/producer"
        view={view}
        date={date}
        rangeLabel={range.label}
        prevDate={shiftPeriod(view, date, -1)}
        nextDate={shiftPeriod(view, date, 1)}
      />

      {(!bookings || bookings.length === 0) && (
        <div className="card text-sm text-slate-600">
          {view === 'all'
            ? 'No bookings yet. Create your first one with New Booking.'
            : 'No productions in this period.'}
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
            {['confirmed', 'in_progress', 'completed'].includes(b.status) && (
              <Link href={`/producer/run/${b.id}`} className="btn-ghost shrink-0">
                Run
              </Link>
            )}
          </div>
        ))}
      </div>
    </div>
  );
}
