import Link from 'next/link';
import { fullName } from '@nbc/shared';
import { requireRole } from '@/lib/auth';
import { createClient } from '@/lib/supabase/server';
import { fmtDate, fmtTime } from '@/lib/format';
import { addDays, todayIso } from '@/lib/schedule';
import { PrintButton } from '@/components/PrintButton';

function one<T>(v: T | T[] | null | undefined): T | undefined {
  return Array.isArray(v) ? v[0] : (v ?? undefined);
}

export default async function TransportListPage({
  searchParams,
}: {
  searchParams: Promise<{ date?: string }>;
}) {
  await requireRole(['booking_officer', 'manager', 'admin']);
  const sp = await searchParams;
  const date = sp.date ?? todayIso();
  const supabase = await createClient();

  const { data: rows } = await supabase
    .from('booking_crew')
    .select(
      'id, profile:profiles!booking_crew_profile_id_fkey(first_name, last_name, phone, physical_address), ' +
        'booking:bookings!inner(title, call_date, call_time, end_time, air_end, status)',
    )
    .eq('needs_transport', true)
    .eq('booking.call_date', date)
    .not('booking.status', 'in', '(draft,cancelled)');

  const list = (rows ?? [])
    .map((r) => {
      const p = one(r.profile);
      const bk = one(r.booking);
      return {
        id: r.id,
        name: p ? fullName(p) : '—',
        phone: p?.phone ?? '—',
        address: p?.physical_address ?? '—',
        production: bk?.title ?? '—',
        pickup: bk?.call_time ?? null,
        dropoff: bk?.end_time ?? bk?.air_end ?? null,
      };
    })
    .sort((a, b) => (a.pickup ?? '').localeCompare(b.pickup ?? ''));

  return (
    <div className="mx-auto max-w-4xl">
      <div className="mb-4 flex flex-wrap items-center justify-between gap-3">
        <div>
          <h1 className="text-2xl font-semibold">Transport List</h1>
          <p className="text-sm text-slate-600">Crew needing a pickup · {fmtDate(date)}</p>
        </div>
        <div className="flex items-center gap-2">
          <Link className="btn-ghost no-print" href={`/booking-officer/transport?date=${addDays(date, -1)}`}>
            ← Prev
          </Link>
          <Link className="btn-ghost no-print" href="/booking-officer/transport">
            Today
          </Link>
          <Link className="btn-ghost no-print" href={`/booking-officer/transport?date=${addDays(date, 1)}`}>
            Next →
          </Link>
          <PrintButton label="Print list" />
        </div>
      </div>

      <div className="card print-sheet">
        <div className="mb-3 border-b border-slate-200 pb-2">
          <h2 className="text-lg font-bold">NBC — Transport Pickup List</h2>
          <p className="text-sm text-slate-600">{fmtDate(date)}</p>
        </div>
        {list.length === 0 ? (
          <p className="text-sm text-slate-600">No crew flagged for transport on this date.</p>
        ) : (
          <table className="w-full text-sm">
            <thead className="text-left text-slate-500">
              <tr className="border-b border-slate-200">
                <th className="py-1">Name</th>
                <th className="py-1">Phone</th>
                <th className="py-1">Address</th>
                <th className="py-1">Production</th>
                <th className="py-1">Pickup</th>
                <th className="py-1">Drop-off</th>
              </tr>
            </thead>
            <tbody>
              {list.map((r) => (
                <tr key={r.id} className="border-b border-slate-100">
                  <td className="py-1 font-medium">{r.name}</td>
                  <td className="py-1">{r.phone}</td>
                  <td className="py-1 text-slate-600">{r.address}</td>
                  <td className="py-1">{r.production}</td>
                  <td className="py-1">{fmtTime(r.pickup)}</td>
                  <td className="py-1">{fmtTime(r.dropoff)}</td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </div>
    </div>
  );
}
