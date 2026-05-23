import Link from 'next/link';
import { LOCATION_TYPE_LABELS, fullName, type BookingStatus, type LocationType } from '@nbc/shared';
import { requireRole } from '@/lib/auth';
import { createClient } from '@/lib/supabase/server';
import { DayCalendar, type CalendarRow } from '@/components/DayCalendar';
import { ScheduleRealtime } from '@/components/ScheduleRealtime';
import { fmtDate, fmtTime } from '@/lib/format';
import { addDays, buildDaySchedule, todayIso, type ScheduleBooking } from '@/lib/schedule';

interface RawBooking {
  id: string;
  title: string;
  status: BookingStatus;
  location_type: LocationType;
  call_time: string;
  end_time: string | null;
  air_end: string | null;
  content_department_id: string | null;
  specialised_equipment: string[];
  booking_crew:
    | { profile_id: string; profile: { first_name: string; last_name: string } | { first_name: string; last_name: string }[] | null }[]
    | null;
}

function crewList(b: RawBooking): { profileId: string; name: string }[] {
  return (b.booking_crew ?? []).map((c) => {
    const p = Array.isArray(c.profile) ? c.profile[0] : c.profile;
    return { profileId: c.profile_id, name: p ? fullName(p) : 'Operator' };
  });
}

export default async function ExecProducerCalendarPage({
  searchParams,
}: {
  searchParams: Promise<{ date?: string }>;
}) {
  const { profile } = await requireRole(['executive_producer', 'admin']);
  const sp = await searchParams;
  const date = sp.date ?? todayIso();
  const supabase = await createClient();

  const { data: dept } = profile.department_id
    ? await supabase.from('departments').select('name').eq('id', profile.department_id).maybeSingle()
    : { data: null };

  const { data } = await supabase
    .from('bookings')
    .select(
      'id, title, status, location_type, call_time, end_time, air_end, content_department_id, specialised_equipment, ' +
        'booking_crew(profile_id, profile:profiles!booking_crew_profile_id_fkey(first_name, last_name))',
    )
    .eq('call_date', date)
    .not('status', 'in', '(draft,cancelled)')
    .order('call_time');
  const raw = (data ?? []) as unknown as RawBooking[];

  // Whole-day set drives clash detection; bars are non-clickable here.
  const scheduleBookings: ScheduleBooking[] = raw.map((b) => ({
    id: b.id,
    title: b.title,
    status: b.status,
    location_type: b.location_type,
    call_time: b.call_time,
    end_time: b.end_time,
    air_end: b.air_end,
    crew: crewList(b),
  }));
  const { rows, clashMessages } = buildDaySchedule(scheduleBookings);
  const calendarRows: CalendarRow[] = rows.map((r) => ({
    ...r,
    bars: r.bars.map((bar) => ({ ...bar, href: '' })),
  }));

  const mine = raw.filter((b) => b.content_department_id && b.content_department_id === profile.department_id);

  return (
    <div className="mx-auto max-w-6xl">
      <ScheduleRealtime />
      <div className="mb-4 flex flex-wrap items-center justify-between gap-3">
        <div>
          <h1 className="text-2xl font-semibold">Department Calendar</h1>
          <p className="text-sm text-slate-600">
            {dept?.name ?? 'Your department'} · {fmtDate(date)}
          </p>
        </div>
        <div className="flex items-center gap-2">
          <Link className="btn-ghost" href={`/exec-producer/calendar?date=${addDays(date, -1)}`}>
            ← Prev
          </Link>
          <Link className="btn-ghost" href="/exec-producer/calendar">
            Today
          </Link>
          <Link className="btn-ghost" href={`/exec-producer/calendar?date=${addDays(date, 1)}`}>
            Next →
          </Link>
        </div>
      </div>

      {clashMessages.length > 0 && (
        <div className="mb-4 rounded-md border border-red-200 bg-red-50 px-4 py-3 text-sm text-red-800">
          <p className="mb-1 font-medium">{clashMessages.length} clash(es) across the schedule:</p>
          <ul className="list-inside list-disc">
            {clashMessages.map((m) => (
              <li key={m}>{m}</li>
            ))}
          </ul>
        </div>
      )}

      <DayCalendar rows={calendarRows} />
      <p className="mb-6 mt-2 text-xs text-slate-500">
        Whole-day schedule (all departments) so you can spot studio &amp; crew clashes. Red outline =
        clash.
      </p>

      <h2 className="mb-2 font-medium">Your department today ({mine.length})</h2>
      {mine.length === 0 && (
        <div className="card text-sm text-slate-600">No productions for your department on this date.</div>
      )}
      <div className="space-y-2">
        {mine.map((b) => {
          const crew = crewList(b).map((c) => c.name);
          return (
            <div key={b.id} className="card text-sm">
              <div className="font-medium">{b.title}</div>
              <p className="text-slate-600">
                {LOCATION_TYPE_LABELS[b.location_type]} · {fmtTime(b.call_time)}–
                {fmtTime(b.end_time ?? b.air_end)}
              </p>
              <p className="text-slate-500">Crew: {crew.length ? crew.join(', ') : 'not yet assigned'}</p>
              {b.specialised_equipment.length > 0 && (
                <p className="text-slate-500">Gear: {b.specialised_equipment.join(', ')}</p>
              )}
            </div>
          );
        })}
      </div>
    </div>
  );
}
