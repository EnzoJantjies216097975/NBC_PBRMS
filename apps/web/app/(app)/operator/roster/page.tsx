import Link from 'next/link';
import { SHIFTS, fullName } from '@nbc/shared';
import { requireRole } from '@/lib/auth';
import { createClient } from '@/lib/supabase/server';
import { addMonths, cellKey, monthDays, monthLabel, monthParam, parseMonth } from '@/lib/roster';
import { RosterGrid } from '@/components/RosterGrid';

export default async function OperatorRosterPage({
  searchParams,
}: {
  searchParams: Promise<{ month?: string }>;
}) {
  const { userId } = await requireRole(['operator', 'admin']);
  const sp = await searchParams;
  const { year, month } = parseMonth(sp.month);
  const supabase = await createClient();

  const { data: profile } = await supabase
    .from('profiles')
    .select('department_id, department:departments(name)')
    .eq('id', userId)
    .single();
  const dept = Array.isArray(profile?.department) ? profile?.department[0] : profile?.department;

  const prev = addMonths(year, month, -1);
  const next = addMonths(year, month, 1);

  const header = (
    <div className="mb-4 flex flex-wrap items-center justify-between gap-3">
      <div>
        <h1 className="text-2xl font-semibold">My Roster</h1>
        <p className="text-sm text-slate-600">
          {dept?.name ?? 'Your department'} · {monthLabel(year, month)}
        </p>
      </div>
      <div className="flex items-center gap-2">
        <Link className="btn-ghost" href={`/operator/roster?month=${monthParam(prev.year, prev.month)}`}>
          ← {monthLabel(prev.year, prev.month)}
        </Link>
        <Link className="btn-ghost" href={`/operator/roster?month=${monthParam(next.year, next.month)}`}>
          {monthLabel(next.year, next.month)} →
        </Link>
      </div>
    </div>
  );

  const period = profile?.department_id
    ? (
        await supabase
          .from('roster_periods')
          .select('id, status')
          .eq('department_id', profile.department_id)
          .eq('year', year)
          .eq('month', month)
          .maybeSingle()
      ).data
    : null;

  if (!period) {
    return (
      <div className="mx-auto max-w-full">
        {header}
        <div className="card text-sm text-slate-600">
          No roster published for {monthLabel(year, month)} yet.
        </div>
      </div>
    );
  }

  const { data: operators } = await supabase
    .from('profiles')
    .select('id, first_name, last_name')
    .eq('role', 'operator')
    .eq('department_id', profile!.department_id!)
    .eq('active', true)
    .order('last_name');

  const { data: assignments } = await supabase
    .from('roster_assignments')
    .select('profile_id, work_date, shift_code')
    .eq('roster_period_id', period.id);

  const initial: Record<string, string> = {};
  for (const a of assignments ?? []) {
    if (a.shift_code) initial[cellKey(a.profile_id, a.work_date)] = a.shift_code;
  }

  return (
    <div className="mx-auto max-w-full">
      {header}
      {period.status !== 'published' && (
        <p className="mb-3 text-xs text-amber-700">This roster is still a draft and may change.</p>
      )}
      <RosterGrid
        rosterPeriodId={period.id}
        month={monthParam(year, month)}
        editable={false}
        operators={(operators ?? []).map((o) => ({ id: o.id, name: fullName(o) }))}
        days={monthDays(year, month)}
        initial={initial}
        highlightProfileId={userId}
      />
      <div className="mt-4 flex flex-wrap gap-x-4 gap-y-1 text-xs text-slate-500">
        {SHIFTS.map((s) => (
          <span key={s.code}>
            <span className="font-semibold">{s.code}</span> {s.name}
          </span>
        ))}
      </div>
    </div>
  );
}
