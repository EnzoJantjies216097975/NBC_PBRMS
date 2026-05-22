import Link from 'next/link';
import { notFound } from 'next/navigation';
import {
  LOCATION_TYPE_LABELS,
  SPECIALISED_EQUIPMENT_LABELS,
  classifyAvailability,
  fullName,
  matchedSkillNames,
  operatorScore,
  type CrewBusyBlock,
  type LocationType,
  type SpecialisedEquipment,
} from '@nbc/shared';
import { requireRole } from '@/lib/auth';
import { createClient } from '@/lib/supabase/server';
import { fmtDateTimeRange } from '@/lib/format';
import { CrewPicker, type CandidateOperator } from '@/components/CrewPicker';
import { declineBooking } from './actions';

const CREW_DEPARTMENTS = ['Camera', 'Sound', 'Lighting'] as const;

interface OperatorRow {
  id: string;
  first_name: string;
  last_name: string;
  phone: string | null;
  department_id: string | null;
  profile_skills: { skills: { name: string } | { name: string }[] | null }[] | null;
}

export default async function AssignPage({
  params,
  searchParams,
}: {
  params: Promise<{ bookingId: string }>;
  searchParams: Promise<{ error?: string }>;
}) {
  await requireRole(['supervisor', 'admin']);
  const { bookingId } = await params;
  const { error } = await searchParams;
  const supabase = await createClient();

  const { data: booking } = await supabase
    .from('bookings')
    .select(
      'id, title, status, location_type, venue, call_date, call_time, end_time, air_end, specialised_equipment',
    )
    .eq('id', bookingId)
    .maybeSingle();
  if (!booking) notFound();

  const windowEnd = booking.end_time ?? booking.air_end ?? booking.call_time;

  // Department ids for the crew groups.
  const { data: depts } = await supabase
    .from('departments')
    .select('id, name')
    .in('name', [...CREW_DEPARTMENTS])
    .is('parent_id', null);
  const deptIdByName: Record<string, string> = {};
  for (const d of depts ?? []) deptIdByName[d.name] = d.id;
  const deptIds = Object.values(deptIdByName);

  // Storeroom availability for the booking's requested specialised gear, so the
  // supervisor can tell whether the production is actually possible.
  let gear: { name: string; status: string }[] = [];
  if (booking.specialised_equipment.length > 0) {
    const { data } = await supabase.from('equipment').select('name, status');
    gear = data ?? [];
  }
  const gearAvailability = booking.specialised_equipment.map((key) => {
    const token = key.split('_')[0] ?? key;
    return {
      label: SPECIALISED_EQUIPMENT_LABELS[key as SpecialisedEquipment] ?? key,
      matches: gear.filter((g) => g.name.toLowerCase().includes(token)),
    };
  });

  // Operators in those departments, with their skills.
  const { data: operators } = await supabase
    .from('profiles')
    .select('id, first_name, last_name, phone, department_id, profile_skills(skills(name))')
    .eq('role', 'operator')
    .eq('active', true)
    .in('department_id', deptIds)
    .order('last_name');

  // Everyone's busy blocks on this date (for availability + conflicts).
  const opIds = (operators ?? []).map((o) => o.id);
  const { data: busyRows } = opIds.length
    ? await supabase
        .from('booking_crew')
        .select('profile_id, bookings!inner(id, title, call_date, call_time, end_time, air_end, status, location_type)')
        .in('profile_id', opIds)
        .in('status', ['proposed', 'confirmed', 'stand_in'])
        .eq('bookings.call_date', booking.call_date)
        .neq('booking_id', bookingId)
        .not('bookings.status', 'in', '(cancelled,draft)')
    : { data: [] };

  const busyByProfile = new Map<string, CrewBusyBlock[]>();
  const inStudio = new Set<string>();
  for (const row of busyRows ?? []) {
    const bk = Array.isArray(row.bookings) ? row.bookings[0] : row.bookings;
    if (!bk) continue;
    const list = busyByProfile.get(row.profile_id) ?? [];
    list.push({
      bookingId: bk.id,
      title: bk.title,
      date: bk.call_date,
      start: bk.call_time,
      end: bk.end_time ?? bk.air_end ?? bk.call_time,
    });
    busyByProfile.set(row.profile_id, list);
    if (bk.location_type === booking.location_type) inStudio.add(row.profile_id);
  }

  const requestedSkillLabels = booking.specialised_equipment.map(
    (k) => SPECIALISED_EQUIPMENT_LABELS[k as SpecialisedEquipment] ?? k,
  );

  const toCandidate = (o: OperatorRow): CandidateOperator => {
    const avail = classifyAvailability(
      { profileId: o.id, date: booking.call_date, start: booking.call_time, end: windowEnd },
      busyByProfile.get(o.id) ?? [],
    );
    const skills = (o.profile_skills ?? [])
      .map((ps) => (Array.isArray(ps.skills) ? ps.skills[0]?.name : ps.skills?.name))
      .filter((n): n is string => Boolean(n));
    const inStudioFlag = inStudio.has(o.id);
    const matchedSkills = matchedSkillNames(requestedSkillLabels, skills);
    return {
      id: o.id,
      name: fullName(o),
      phone: o.phone,
      skills,
      matchedSkills,
      recommended: avail.state === 'available' && (matchedSkills.length > 0 || inStudioFlag),
      state: avail.state,
      conflictTitle: avail.conflicts[0]?.title ?? avail.nextUpcoming?.title ?? null,
      inStudio: inStudioFlag,
    };
  };

  const rankOf = (c: CandidateOperator) =>
    operatorScore({ state: c.state, inStudio: c.inStudio, matchedSkillCount: c.matchedSkills.length });

  const operatorsByDept: Record<string, CandidateOperator[]> = {};
  for (const name of CREW_DEPARTMENTS) {
    operatorsByDept[name] = (operators ?? [])
      .filter((o) => o.department_id === deptIdByName[name])
      .map(toCandidate)
      .sort((a, b) => rankOf(b) - rankOf(a));
  }

  // Existing assignments (prefill when re-opening).
  const { data: existing } = await supabase
    .from('booking_crew')
    .select('profile_id, role_label, department_id, needs_car_booking, needs_transport')
    .eq('booking_id', bookingId);

  return (
    <div className="mx-auto max-w-4xl">
      <Link href="/supervisor" className="text-sm text-nbc hover:underline">
        ← Back to Awaiting Crew
      </Link>
      <h1 className="mt-2 text-2xl font-semibold">Assign crew · {booking.title}</h1>
      <p className="mb-1 text-sm text-slate-600">
        {LOCATION_TYPE_LABELS[booking.location_type as LocationType]}
        {booking.venue ? ` · ${booking.venue}` : ''} ·{' '}
        {fmtDateTimeRange(booking.call_date, booking.call_time, booking.end_time)}
      </p>
      {gearAvailability.length > 0 && (
        <div className="mb-4 rounded-md border border-slate-200 bg-slate-50 px-3 py-2 text-sm">
          <p className="mb-1 font-medium text-slate-600">Requested gear — storeroom availability:</p>
          <ul className="space-y-0.5">
            {gearAvailability.map((g) => (
              <li key={g.label} className="text-slate-600">
                <span className="font-medium">{g.label}:</span>{' '}
                {g.matches.length === 0 ? (
                  <span className="text-slate-400">not tracked in storeroom</span>
                ) : (
                  g.matches.map((m, i) => (
                    <span key={m.name}>
                      {i > 0 ? ', ' : ''}
                      {m.name}{' '}
                      <span className={m.status === 'available' ? 'text-emerald-700' : 'text-red-600'}>
                        ({m.status.replace('_', ' ')})
                      </span>
                    </span>
                  ))
                )}
              </li>
            ))}
          </ul>
        </div>
      )}
      {error && <p className="mb-4 rounded-md bg-red-50 px-3 py-2 text-sm text-red-700">{error}</p>}

      <CrewPicker
        bookingId={bookingId}
        groups={CREW_DEPARTMENTS.map((name) => ({
          name,
          deptId: deptIdByName[name] ?? null,
          count: 2,
          operators: operatorsByDept[name] ?? [],
        }))}
        existing={(existing ?? []).map((e) => ({
          profileId: e.profile_id,
          roleLabel: e.role_label,
          deptId: e.department_id,
          needsCar: e.needs_car_booking,
          needsTransport: e.needs_transport,
        }))}
      />

      <section className="card mt-6 border-red-100">
        <h2 className="font-medium">Can&apos;t crew this booking?</h2>
        <p className="mb-3 text-sm text-slate-600">
          Send it back to the Executive Producer with the reason (e.g. no operators available,
          equipment unavailable).
        </p>
        <form action={declineBooking} className="flex items-end gap-2">
          <input type="hidden" name="booking_id" value={bookingId} />
          <div className="flex-1">
            <label className="label">Reason</label>
            <input className="input" name="supervisor_feedback" placeholder="Why it can't be crewed" />
          </div>
          <button className="btn-ghost" type="submit">
            Send back to EP
          </button>
        </form>
      </section>
    </div>
  );
}
