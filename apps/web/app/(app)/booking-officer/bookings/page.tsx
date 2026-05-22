import Link from 'next/link';
import { LOCATION_TYPE_LABELS, type BookingStatus, type LocationType } from '@nbc/shared';
import { requireRole } from '@/lib/auth';
import { createClient } from '@/lib/supabase/server';
import { StatusBadge } from '@/components/StatusBadge';
import { fmtDateTimeRange } from '@/lib/format';

export default async function AllBookingsPage() {
  await requireRole(['booking_officer', 'manager', 'admin']);
  const supabase = await createClient();

  const { data: bookings } = await supabase
    .from('bookings')
    .select(
      'id, title, status, location_type, venue, call_date, call_time, end_time, content_department:departments!bookings_content_department_id_fkey(name)',
    )
    .order('call_date', { ascending: false })
    .limit(100);

  return (
    <div className="mx-auto max-w-5xl">
      <h1 className="mb-1 text-2xl font-semibold">All Bookings</h1>
      <p className="mb-6 text-sm text-slate-600">
        Every booking across departments. Open one to see the crew and the producer&apos;s
        department.
      </p>

      <div className="overflow-hidden rounded-lg border border-slate-200 bg-white">
        <table className="w-full text-sm">
          <thead className="bg-slate-50 text-left text-slate-500">
            <tr>
              <th className="px-4 py-2">When</th>
              <th className="px-4 py-2">Production</th>
              <th className="px-4 py-2">Department</th>
              <th className="px-4 py-2">Location</th>
              <th className="px-4 py-2">Status</th>
            </tr>
          </thead>
          <tbody>
            {(bookings ?? []).map((b) => {
              const dept = Array.isArray(b.content_department)
                ? b.content_department[0]
                : b.content_department;
              return (
                <tr key={b.id} className="border-t border-slate-100 hover:bg-slate-50">
                  <td className="px-4 py-2 text-slate-600">
                    {fmtDateTimeRange(b.call_date, b.call_time, b.end_time)}
                  </td>
                  <td className="px-4 py-2 font-medium">
                    <Link className="text-nbc hover:underline" href={`/booking-officer/bookings/${b.id}`}>
                      {b.title}
                    </Link>
                  </td>
                  <td className="px-4 py-2 text-slate-600">{dept?.name ?? '—'}</td>
                  <td className="px-4 py-2 text-slate-600">
                    {LOCATION_TYPE_LABELS[b.location_type as LocationType]}
                    {b.venue ? ` · ${b.venue}` : ''}
                  </td>
                  <td className="px-4 py-2">
                    <StatusBadge status={b.status as BookingStatus} />
                  </td>
                </tr>
              );
            })}
          </tbody>
        </table>
      </div>
    </div>
  );
}
