import {
  CHANNELS,
  CHANNEL_LABELS,
  LOCATION_TYPE_LABELS,
  fullName,
  type BookingStatus,
  type LocationType,
} from '@nbc/shared';
import { requireRole } from '@/lib/auth';
import { createClient } from '@/lib/supabase/server';
import { StatusBadge } from '@/components/StatusBadge';
import { fmtDateTimeRange } from '@/lib/format';
import { approveBooking, requestChanges } from './actions';

export default async function ExecProducerHome({
  searchParams,
}: {
  searchParams: Promise<{ error?: string }>;
}) {
  await requireRole(['executive_producer', 'admin']);
  const { error } = await searchParams;
  const supabase = await createClient();

  const { data: bookings } = await supabase
    .from('bookings')
    .select(
      'id, title, status, location_type, venue, call_date, call_time, end_time, notes, specialised_equipment, producer:profiles!bookings_producer_id_fkey(first_name, last_name, phone)',
    )
    .eq('status', 'submitted')
    .order('call_date', { ascending: true });

  return (
    <div className="mx-auto max-w-4xl">
      <h1 className="mb-1 text-2xl font-semibold">Incoming Bookings</h1>
      <p className="mb-6 text-sm text-slate-600">
        Validate each request and assign a channel, or send it back stating what&apos;s missing.
      </p>
      {error && <p className="mb-4 rounded-md bg-red-50 px-3 py-2 text-sm text-red-700">{error}</p>}

      {(!bookings || bookings.length === 0) && (
        <div className="card text-sm text-slate-600">No bookings awaiting your review.</div>
      )}

      <div className="space-y-4">
        {(bookings ?? []).map((b) => {
          const producer = Array.isArray(b.producer) ? b.producer[0] : b.producer;
          return (
            <div key={b.id} className="card space-y-4">
              <div className="flex items-center justify-between">
                <h3 className="font-medium">{b.title}</h3>
                <StatusBadge status={b.status as BookingStatus} />
              </div>
              <dl className="grid grid-cols-2 gap-x-6 gap-y-1 text-sm text-slate-600">
                <div>
                  <dt className="inline font-medium text-slate-700">Producer: </dt>
                  <dd className="inline">{producer ? fullName(producer) : '—'}</dd>
                </div>
                <div>
                  <dt className="inline font-medium text-slate-700">Where: </dt>
                  <dd className="inline">
                    {LOCATION_TYPE_LABELS[b.location_type as LocationType]}
                    {b.venue ? ` · ${b.venue}` : ''}
                  </dd>
                </div>
                <div>
                  <dt className="inline font-medium text-slate-700">When: </dt>
                  <dd className="inline">{fmtDateTimeRange(b.call_date, b.call_time, b.end_time)}</dd>
                </div>
                <div>
                  <dt className="inline font-medium text-slate-700">Gear: </dt>
                  <dd className="inline">
                    {b.specialised_equipment?.length ? b.specialised_equipment.join(', ') : 'None'}
                  </dd>
                </div>
              </dl>
              {b.notes && <p className="rounded-md bg-slate-50 px-3 py-2 text-sm">{b.notes}</p>}

              <div className="flex flex-wrap items-end gap-6 border-t border-slate-100 pt-4">
                <form action={approveBooking} className="flex items-end gap-2">
                  <input type="hidden" name="booking_id" value={b.id} />
                  <div>
                    <label className="label">Channel</label>
                    <select className="input" name="channel" defaultValue="nbc_1">
                      {CHANNELS.map((c) => (
                        <option key={c} value={c}>
                          {CHANNEL_LABELS[c]}
                        </option>
                      ))}
                    </select>
                  </div>
                  <button className="btn-primary" type="submit">
                    Approve
                  </button>
                </form>

                <form action={requestChanges} className="flex flex-1 items-end gap-2">
                  <input type="hidden" name="booking_id" value={b.id} />
                  <div className="flex-1">
                    <label className="label">Request changes — what&apos;s missing?</label>
                    <input className="input" name="ep_feedback" placeholder="e.g. Confirm air date and crew call time" />
                  </div>
                  <button className="btn-ghost" type="submit">
                    Send back
                  </button>
                </form>
              </div>
            </div>
          );
        })}
      </div>
    </div>
  );
}
