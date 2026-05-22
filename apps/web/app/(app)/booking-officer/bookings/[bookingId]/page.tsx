import Link from 'next/link';
import { notFound } from 'next/navigation';
import {
  CHANNEL_LABELS,
  LOCATION_TYPE_LABELS,
  fullName,
  type BookingStatus,
  type Channel,
  type LocationType,
} from '@nbc/shared';
import { requireRole } from '@/lib/auth';
import { createClient } from '@/lib/supabase/server';
import { StatusBadge } from '@/components/StatusBadge';
import { fmtDateTimeRange } from '@/lib/format';
import { confirmBooking } from './actions';

function one<T>(v: T | T[] | null | undefined): T | undefined {
  return Array.isArray(v) ? v[0] : (v ?? undefined);
}

export default async function BookingDetailPage({
  params,
  searchParams,
}: {
  params: Promise<{ bookingId: string }>;
  searchParams: Promise<{ error?: string }>;
}) {
  await requireRole(['booking_officer', 'manager', 'admin']);
  const { bookingId } = await params;
  const { error } = await searchParams;
  const supabase = await createClient();

  const { data: booking } = await supabase
    .from('bookings')
    .select(
      'id, title, status, channel, location_type, venue, call_date, call_time, end_time, notes, specialised_equipment, ' +
        'producer:profiles!bookings_producer_id_fkey(first_name, last_name, phone, department:departments(name))',
    )
    .eq('id', bookingId)
    .maybeSingle();
  if (!booking) notFound();

  const { data: crew } = await supabase
    .from('booking_crew')
    .select(
      'id, role_label, status, needs_car_booking, needs_transport, profile:profiles(first_name, last_name, phone)',
    )
    .eq('booking_id', bookingId);

  const producer = one(booking.producer);
  const producerDept = one(producer?.department);

  return (
    <div className="mx-auto max-w-3xl">
      <Link href="/booking-officer/bookings" className="text-sm text-nbc hover:underline">
        ← All Bookings
      </Link>
      <div className="mt-2 flex items-center justify-between">
        <h1 className="text-2xl font-semibold">{booking.title}</h1>
        <StatusBadge status={booking.status as BookingStatus} />
      </div>
      {error && <p className="my-3 rounded-md bg-red-50 px-3 py-2 text-sm text-red-700">{error}</p>}

      <div className="card mt-4 space-y-1 text-sm">
        <p>
          <span className="font-medium">Producer:</span>{' '}
          {producer ? fullName(producer) : '—'}
          {producerDept ? ` · ${producerDept.name}` : ''}
          {producer?.phone ? ` · ${producer.phone}` : ''}
        </p>
        <p>
          <span className="font-medium">Where:</span>{' '}
          {LOCATION_TYPE_LABELS[booking.location_type as LocationType]}
          {booking.venue ? ` · ${booking.venue}` : ''}
        </p>
        <p>
          <span className="font-medium">When:</span>{' '}
          {fmtDateTimeRange(booking.call_date, booking.call_time, booking.end_time)}
        </p>
        <p>
          <span className="font-medium">Channel:</span>{' '}
          {booking.channel ? CHANNEL_LABELS[booking.channel as Channel] : '—'}
        </p>
        {booking.specialised_equipment.length > 0 && (
          <p>
            <span className="font-medium">Gear:</span> {booking.specialised_equipment.join(', ')}
          </p>
        )}
        {booking.notes && (
          <p>
            <span className="font-medium">Notes:</span> {booking.notes}
          </p>
        )}
      </div>

      <h2 className="mb-2 mt-6 font-medium">Crew ({crew?.length ?? 0})</h2>
      {(!crew || crew.length === 0) && (
        <div className="card text-sm text-slate-600">No crew assigned yet.</div>
      )}
      <div className="space-y-2">
        {(crew ?? []).map((c) => {
          const p = one(c.profile);
          return (
            <div key={c.id} className="card flex items-center justify-between py-3 text-sm">
              <div>
                <span className="font-medium">{c.role_label || 'Crew'}:</span>{' '}
                {p ? fullName(p) : '—'}
                {p?.phone ? ` · ${p.phone}` : ''}
              </div>
              <div className="flex gap-2 text-xs text-slate-500">
                {c.needs_car_booking && <span className="rounded bg-slate-100 px-2 py-0.5">vehicle</span>}
                {c.needs_transport && <span className="rounded bg-slate-100 px-2 py-0.5">pickup</span>}
                <span className="rounded bg-slate-100 px-2 py-0.5">{c.status}</span>
              </div>
            </div>
          );
        })}
      </div>

      {booking.status === 'crew_assigned' && (
        <form action={confirmBooking} className="mt-6">
          <input type="hidden" name="booking_id" value={bookingId} />
          <button className="btn-primary" type="submit">
            Confirm booking &amp; notify everyone
          </button>
        </form>
      )}
    </div>
  );
}
