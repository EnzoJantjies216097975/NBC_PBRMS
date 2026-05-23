import Link from 'next/link';
import { notFound } from 'next/navigation';
import { LOCATION_TYPE_LABELS, fullName, type BookingStatus, type LocationType } from '@nbc/shared';
import { requireRole } from '@/lib/auth';
import { createClient } from '@/lib/supabase/server';
import { StatusBadge } from '@/components/StatusBadge';
import { fmtDateTimeRange, fmtTimestamp } from '@/lib/format';
import { confirmPresent, endProduction, signalOperator, startProduction } from './actions';

function one<T>(v: T | T[] | null | undefined): T | undefined {
  return Array.isArray(v) ? v[0] : (v ?? undefined);
}

export default async function RunPage({ params }: { params: Promise<{ bookingId: string }> }) {
  await requireRole(['producer', 'admin']);
  const { bookingId } = await params;
  const supabase = await createClient();

  const { data: booking } = await supabase
    .from('bookings')
    .select(
      'id, title, status, location_type, venue, call_date, call_time, end_time, ' +
        'booking_crew(id, role_label, attended, profile:profiles!booking_crew_profile_id_fkey(first_name, last_name, phone)), ' +
        'production_logs(actual_start, actual_end, overtime_minutes, is_overtime)',
    )
    .eq('id', bookingId)
    .maybeSingle();
  if (!booking) notFound();

  const status = booking.status as BookingStatus;
  const crew = booking.booking_crew ?? [];
  const logs = (booking.production_logs ?? []).slice().sort((a, b) =>
    (a.actual_start ?? '').localeCompare(b.actual_start ?? ''),
  );
  const latestLog = logs[logs.length - 1];

  return (
    <div className="mx-auto max-w-2xl">
      <Link href="/producer" className="text-sm text-nbc hover:underline">
        ← My Productions
      </Link>
      <div className="mt-2 flex items-center justify-between">
        <h1 className="text-2xl font-semibold">Run · {booking.title}</h1>
        <StatusBadge status={status} />
      </div>
      <p className="mb-4 text-sm text-slate-600">
        {LOCATION_TYPE_LABELS[booking.location_type as LocationType]}
        {booking.venue ? ` · ${booking.venue}` : ''} ·{' '}
        {fmtDateTimeRange(booking.call_date, booking.call_time, booking.end_time)}
      </p>

      <section className="card mb-6">
        <h2 className="mb-3 font-medium">Production timing</h2>
        {status === 'confirmed' && (
          <form action={startProduction}>
            <input type="hidden" name="booking_id" value={bookingId} />
            <button className="btn-primary" type="submit">
              Start production
            </button>
          </form>
        )}
        {status === 'in_progress' && (
          <div className="space-y-3">
            <p className="text-sm text-slate-600">
              Started {fmtTimestamp(latestLog?.actual_start)}.
            </p>
            <form action={endProduction}>
              <input type="hidden" name="booking_id" value={bookingId} />
              <button className="btn-primary" type="submit">
                End production
              </button>
            </form>
          </div>
        )}
        {status === 'completed' && latestLog && (
          <div className="text-sm text-slate-600">
            <p>Started {fmtTimestamp(latestLog.actual_start)}</p>
            <p>Ended {fmtTimestamp(latestLog.actual_end)}</p>
            {latestLog.is_overtime && (
              <p className="mt-1 font-medium text-amber-700">
                Ran {latestLog.overtime_minutes} min over — overtime flagged.
              </p>
            )}
          </div>
        )}
        {status !== 'confirmed' && status !== 'in_progress' && status !== 'completed' && (
          <p className="text-sm text-slate-500">Timing controls open once the booking is confirmed.</p>
        )}
      </section>

      <h2 className="mb-2 font-medium">Crew attendance</h2>
      {crew.length === 0 && <div className="card text-sm text-slate-600">No crew assigned.</div>}
      <div className="space-y-2">
        {crew.map((c) => {
          const p = one(c.profile);
          return (
            <div key={c.id} className="card flex items-center justify-between gap-3 py-3 text-sm">
              <div>
                <span className="font-medium">{c.role_label || 'Crew'}:</span> {p ? fullName(p) : '—'}
                {p?.phone ? <span className="text-slate-500"> · {p.phone}</span> : ''}
              </div>
              {c.attended ? (
                <span className="rounded-full bg-emerald-100 px-2.5 py-0.5 text-xs font-medium text-emerald-800">
                  Present ✓
                </span>
              ) : (
                <div className="flex gap-2">
                  <form action={confirmPresent}>
                    <input type="hidden" name="crew_id" value={c.id} />
                    <input type="hidden" name="booking_id" value={bookingId} />
                    <button className="btn-primary !px-3 !py-1" type="submit">
                      Present
                    </button>
                  </form>
                  <form action={signalOperator}>
                    <input type="hidden" name="crew_id" value={c.id} />
                    <input type="hidden" name="booking_id" value={bookingId} />
                    <button className="btn-ghost !px-3 !py-1" type="submit">
                      Signal
                    </button>
                  </form>
                </div>
              )}
            </div>
          );
        })}
      </div>
    </div>
  );
}
