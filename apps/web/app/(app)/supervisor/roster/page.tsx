import Link from 'next/link';
import { SHIFTS, fullName } from '@nbc/shared';
import { requireRole } from '@/lib/auth';
import { createClient } from '@/lib/supabase/server';
import {
  addMonths,
  cellKey,
  monthDays,
  monthLabel,
  monthParam,
  parseMonth,
} from '@/lib/roster';
import { RosterGrid } from '@/components/RosterGrid';
import { copyPreviousMonth, createRosterPeriod, fillWeekendsOff, publishRoster } from './actions';

export default async function SupervisorRosterPage({
  searchParams,
}: {
  searchParams: Promise<{ month?: string; error?: string }>;
}) {
  const { userId } = await requireRole(['supervisor', 'admin']);
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

  const nav = (
    <div className="flex items-center gap-2">
      <Link className="btn-ghost" href={`/supervisor/roster?month=${monthParam(prev.year, prev.month)}`}>
        ← {monthLabel(prev.year, prev.month)}
      </Link>
      <Link className="btn-ghost" href={`/supervisor/roster?month=${monthParam(next.year, next.month)}`}>
        {monthLabel(next.year, next.month)} →
      </Link>
    </div>
  );

  const header = (
    <div className="mb-4 flex flex-wrap items-center justify-between gap-3">
      <div>
        <h1 className="text-2xl font-semibold">Monthly Roster</h1>
        <p className="text-sm text-slate-600">
          {dept?.name ?? 'Your department'} · {monthLabel(year, month)}
        </p>
      </div>
      {nav}
    </div>
  );

  if (!profile?.department_id) {
    return (
      <div className="mx-auto max-w-5xl">
        {header}
        <div className="card text-sm text-red-700">
          Your profile has no department set, so there is no roster to manage. Ask an admin to assign
          your department.
        </div>
      </div>
    );
  }

  const { data: period } = await supabase
    .from('roster_periods')
    .select('id, status')
    .eq('department_id', profile.department_id)
    .eq('year', year)
    .eq('month', month)
    .maybeSingle();

  if (!period) {
    return (
      <div className="mx-auto max-w-5xl">
        {header}
        {sp.error && <p className="mb-4 rounded-md bg-red-50 px-3 py-2 text-sm text-red-700">{sp.error}</p>}
        <div className="card">
          <p className="mb-3 text-sm text-slate-600">
            No roster exists for {monthLabel(year, month)} yet.
          </p>
          <form action={createRosterPeriod}>
            <input type="hidden" name="year" value={year} />
            <input type="hidden" name="month" value={month} />
            <button className="btn-primary" type="submit">
              Create roster for {monthLabel(year, month)}
            </button>
          </form>
        </div>
      </div>
    );
  }

  const { data: operators } = await supabase
    .from('profiles')
    .select('id, first_name, last_name')
    .eq('role', 'operator')
    .eq('department_id', profile.department_id)
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

  const days = monthDays(year, month);

  return (
    <div className="mx-auto max-w-full">
      {header}
      {sp.error && <p className="mb-4 rounded-md bg-red-50 px-3 py-2 text-sm text-red-700">{sp.error}</p>}

      <div className="mb-3 flex items-center gap-3">
        <span
          className={`rounded-full px-2.5 py-0.5 text-xs font-medium ${
            period.status === 'published' ? 'bg-emerald-100 text-emerald-800' : 'bg-amber-100 text-amber-800'
          }`}
        >
          {period.status === 'published' ? 'Published' : 'Draft'}
        </span>
        <form action={publishRoster}>
          <input type="hidden" name="roster_period_id" value={period.id} />
          <input type="hidden" name="month" value={monthParam(year, month)} />
          <button className="btn-ghost" type="submit">
            {period.status === 'published' ? 'Re-publish & notify' : 'Publish & notify operators'}
          </button>
        </form>
      </div>

      <div className="mb-3 flex flex-wrap items-center gap-2 rounded-md border border-slate-200 bg-slate-50 px-3 py-2 text-sm">
        <span className="font-medium text-slate-600">Auto-fill empty cells:</span>
        <form action={copyPreviousMonth}>
          <input type="hidden" name="roster_period_id" value={period.id} />
          <input type="hidden" name="year" value={year} />
          <input type="hidden" name="month" value={month} />
          <button className="btn-ghost" type="submit">
            Copy {monthLabel(addMonths(year, month, -1).year, addMonths(year, month, -1).month)} pattern
          </button>
        </form>
        <form action={fillWeekendsOff}>
          <input type="hidden" name="roster_period_id" value={period.id} />
          <input type="hidden" name="year" value={year} />
          <input type="hidden" name="month" value={month} />
          <button className="btn-ghost" type="submit">
            Set weekends Off
          </button>
        </form>
        <span className="text-xs text-slate-400">Only fills blank cells — your edits stay.</span>
      </div>

      {(!operators || operators.length === 0) ? (
        <div className="card text-sm text-slate-600">
          No operators in {dept?.name ?? 'your department'} yet. Add them under{' '}
          <Link className="text-nbc hover:underline" href="/supervisor/operators">
            My Operators
          </Link>
          .
        </div>
      ) : (
        <RosterGrid
          rosterPeriodId={period.id}
          month={monthParam(year, month)}
          editable
          operators={(operators ?? []).map((o) => ({ id: o.id, name: fullName(o) }))}
          days={days}
          initial={initial}
        />
      )}

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
