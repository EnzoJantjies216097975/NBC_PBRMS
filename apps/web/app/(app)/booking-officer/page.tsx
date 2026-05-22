import Link from 'next/link';
import {
  fullName,
  recurringProductionsForDate,
  type BookingStatus,
  type LocationType,
} from '@nbc/shared';
import { requireRole } from '@/lib/auth';
import { createClient } from '@/lib/supabase/server';
import { StatusBadge } from '@/components/StatusBadge';
import { DayCalendar, type CalendarRow } from '@/components/DayCalendar';
import { fmtDate, fmtTime } from '@/lib/format';
import {
  AXIS_END_HOUR,
  AXIS_START_HOUR,
  addDays,
  buildDaySchedule,
  todayIso,
  type ScheduleBooking,
} from '@/lib/schedule';

const CREW_DEPARTMENTS = ['Camera', 'Sound', 'Lighting'] as const;
const PENDING = new Set<BookingStatus>(['submitted', 'ep_approved', 'crew_assigned']);

interface CrewLite {
  profile_id: string;
  profile: { first_name: string; last_name: string } | { first_name: string; last_name: string }[] | null;
}
interface RawBooking {
  id: string;
  title: string;
  status: BookingStatus;
  location_type: LocationType;
  call_time: string;
  end_time: string | null;
  air_end: string | null;
  booking_crew: CrewLite[] | null;
}

export default async function BookingOfficerHome({
  searchParams,
}: {
  searchParams: Promise<{ date?: string }>;
}) {
  await requireRole(['booking_officer', 'manager', 'admin']);
  const supabase = await createClient();
  const sp = await searchParams;
  const date = sp.date ?? todayIso();

  const { data: raw } = await supabase
    .from('bookings')
    .select(
      'id, title, status, location_type, call_time, end_time, air_end, ' +
        'booking_crew(profile_id, profile:profiles(first_name, last_name))',
    )
    .eq('call_date', date)
    .not('status', 'in', '(draft,cancelled)')
    .order('call_time');

  const bookings: ScheduleBooking[] = ((raw ?? []) as unknown as RawBooking[]).map((b) => ({
    id: b.id,
    title: b.title,
    status: b.status,
    location_type: b.location_type,
    call_time: b.call_time,
    end_time: b.end_time,
    air_end: b.air_end,
    crew: (b.booking_crew ?? []).map((c) => {
      const p = Array.isArray(c.profile) ? c.profile[0] : c.profile;
      return { profileId: c.profile_id, name: p ? fullName(p) : 'Operator' };
    }),
  }));

  // Standing daily/flagship shows from the catalog, minus any already booked for real.
  const realTitles = new Set(bookings.map((b) => b.title.toLowerCase()));
  const standing: ScheduleBooking[] = recurringProductionsForDate(date)
    .filter((r) => !realTitles.has(r.name.toLowerCase()))
    .map((r) => ({
      id: `standing:${r.name}`,
      title: r.name,
      status: 'confirmed' as const,
      location_type: r.location ?? 'other',
      call_time: r.start,
      end_time: r.end,
      air_end: null,
      crew: [],
      standing: true,
    }));

  const { rows, clashMessages, bookedProfileIds } = buildDaySchedule([...bookings, ...standing]);
  const calendarRows: CalendarRow[] = rows.map((r) => ({
    ...r,
    bars: r.bars.map((bar) => ({
      ...bar,
      href: bar.standing ? '' : `/booking-officer/bookings/${bar.id}`,
    })),
  }));
  const requests = bookings.filter((b) => PENDING.has(b.status));

  // "Now" indicator — only when viewing today and within the visible axis.
  let nowLeftPct: number | null = null;
  if (date === todayIso()) {
    const now = new Date();
    const mins = now.getHours() * 60 + now.getMinutes();
    const startM = AXIS_START_HOUR * 60;
    const endM = AXIS_END_HOUR * 60;
    if (mins >= startM && mins <= endM) nowLeftPct = ((mins - startM) / (endM - startM)) * 100;
  }

  const { data: depts } = await supabase
    .from('departments')
    .select('id, name')
    .in('name', [...CREW_DEPARTMENTS])
    .is('parent_id', null);
  const deptIdByName: Record<string, string> = {};
  for (const d of depts ?? []) deptIdByName[d.name] = d.id;

  const { data: operators } = await supabase
    .from('profiles')
    .select('id, first_name, last_name, department_id')
    .eq('role', 'operator')
    .eq('active', true)
    .in('department_id', Object.values(deptIdByName))
    .order('last_name');

  return (
    <div className="mx-auto max-w-7xl">
      <div className="mb-4 flex flex-wrap items-center justify-between gap-3">
        <div>
          <h1 className="text-2xl font-semibold">Daily Schedule</h1>
          <p className="text-sm text-slate-600">{fmtDate(date)}</p>
        </div>
        <div className="flex items-center gap-2">
          <Link className="btn-ghost" href={`/booking-officer?date=${addDays(date, -1)}`}>
            ← Prev
          </Link>
          <Link className="btn-ghost" href="/booking-officer">
            Today
          </Link>
          <Link className="btn-ghost" href={`/booking-officer?date=${addDays(date, 1)}`}>
            Next →
          </Link>
        </div>
      </div>

      {clashMessages.length > 0 && (
        <div className="mb-4 rounded-md border border-red-200 bg-red-50 px-4 py-3 text-sm text-red-800">
          <p className="mb-1 font-medium">{clashMessages.length} clash(es) need resolving:</p>
          <ul className="list-inside list-disc">
            {clashMessages.map((m) => (
              <li key={m}>{m}</li>
            ))}
          </ul>
        </div>
      )}

      <div className="flex flex-col gap-4 lg:flex-row">
        <div className="min-w-0 flex-1">
          <DayCalendar rows={calendarRows} nowLeftPct={nowLeftPct} />
          <p className="mt-2 text-xs text-slate-500">
            Solid bars are bookings (colour = status); <span className="text-slate-400">dashed</span> bars
            are standing daily/flagship shows. Red outline = clash. Click a booking to open it.
          </p>
        </div>

        <aside className="w-full shrink-0 space-y-4 lg:w-80">
          <section className="card">
            <h2 className="mb-3 font-medium">Incoming requests ({requests.length})</h2>
            {requests.length === 0 && <p className="text-sm text-slate-500">Nothing pending today.</p>}
            <div className="space-y-2">
              {requests.map((b) => (
                <Link
                  key={b.id}
                  href={`/booking-officer/bookings/${b.id}`}
                  className="block rounded-md border border-slate-200 p-2 text-sm hover:bg-slate-50"
                >
                  <div className="flex items-center justify-between gap-2">
                    <span className="truncate font-medium">{b.title}</span>
                    <StatusBadge status={b.status} />
                  </div>
                  <span className="text-xs text-slate-500">
                    {fmtTime(b.call_time)}–{fmtTime(b.end_time ?? b.air_end)}
                  </span>
                </Link>
              ))}
            </div>
          </section>

          <section className="card">
            <h2 className="mb-3 font-medium">Available crew</h2>
            <div className="space-y-3">
              {CREW_DEPARTMENTS.map((deptName) => {
                const inDept = (operators ?? []).filter((o) => o.department_id === deptIdByName[deptName]);
                const available = inDept.filter((o) => !bookedProfileIds.has(o.id));
                const busy = inDept.length - available.length;
                return (
                  <div key={deptName}>
                    <p className="text-xs font-semibold uppercase tracking-wide text-slate-500">
                      {deptName} <span className="font-normal normal-case">· {busy} busy</span>
                    </p>
                    {available.length === 0 ? (
                      <p className="text-sm text-slate-400">None free</p>
                    ) : (
                      <div className="flex flex-wrap gap-1">
                        {available.map((o) => (
                          <span key={o.id} className="rounded bg-emerald-50 px-2 py-0.5 text-xs text-emerald-800">
                            {fullName(o)}
                          </span>
                        ))}
                      </div>
                    )}
                  </div>
                );
              })}
              <p className="pt-1 text-[11px] text-slate-400">
                Floor Managers are usually Sound operators assisting on productions.
              </p>
            </div>
          </section>
        </aside>
      </div>
    </div>
  );
}
