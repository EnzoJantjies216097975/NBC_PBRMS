import { LOCATION_TYPE_LABELS, type LocationType } from '@nbc/shared';
import { requireRole } from '@/lib/auth';
import { createClient } from '@/lib/supabase/server';
import { fmtDateTimeRange } from '@/lib/format';
import { ScheduleRealtime } from '@/components/ScheduleRealtime';
import { PeriodTabs } from '@/components/PeriodTabs';
import { parseView, periodRange, shiftPeriod } from '@/lib/period';
import { todayIso } from '@/lib/schedule';

export default async function OperatorHome({
  searchParams,
}: {
  searchParams: Promise<{ view?: string; date?: string }>;
}) {
  const { userId } = await requireRole(['operator', 'admin']);
  const sp = await searchParams;
  const view = parseView(sp.view, 'week');
  const date = sp.date ?? todayIso();
  const range = periodRange(view, date);
  const supabase = await createClient();

  let query = supabase
    .from('booking_crew')
    .select(
      'id, role_label, status, needs_car_booking, booking:bookings!inner(id, title, status, location_type, venue, call_date, call_time, end_time)',
    )
    .eq('profile_id', userId);
  if (range.start && range.end) {
    query = query.gte('booking.call_date', range.start).lte('booking.call_date', range.end);
  }
  const { data: crew } = await query;

  const rows = (crew ?? [])
    .map((c) => ({ ...c, booking: Array.isArray(c.booking) ? c.booking[0] : c.booking }))
    .filter((c) => c.booking)
    .sort((a, b) => (a.booking!.call_date ?? '').localeCompare(b.booking!.call_date ?? ''));

  return (
    <div className="mx-auto max-w-4xl">
      <ScheduleRealtime />
      <h1 className="mb-1 text-2xl font-semibold">My Schedule</h1>
      <p className="mb-4 text-sm text-slate-600">Productions you are assigned to.</p>

      <PeriodTabs
        basePath="/operator"
        view={view}
        date={date}
        rangeLabel={range.label}
        prevDate={shiftPeriod(view, date, -1)}
        nextDate={shiftPeriod(view, date, 1)}
      />

      {rows.length === 0 && (
        <div className="card text-sm text-slate-600">
          {view === 'all' ? 'You have no assigned productions yet.' : 'Nothing in this period.'}
        </div>
      )}

      <div className="space-y-3">
        {rows.map((c) => (
          <div key={c.id} className={`card ${c.booking!.status === 'cancelled' ? 'opacity-60' : ''}`}>
            <div className="flex items-center gap-2">
              <h3 className="font-medium">{c.booking!.title}</h3>
              {c.booking!.status === 'cancelled' && (
                <span className="rounded-full bg-red-100 px-2 py-0.5 text-xs font-medium text-red-700">
                  Cancelled
                </span>
              )}
              {c.needs_car_booking && c.booking!.status !== 'cancelled' && (
                <span className="rounded-full bg-sky-100 px-2 py-0.5 text-xs font-medium text-sky-800">
                  🚗 vehicle booked
                </span>
              )}
            </div>
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
