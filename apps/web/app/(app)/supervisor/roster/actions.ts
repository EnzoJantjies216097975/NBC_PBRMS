'use server';

import { redirect } from 'next/navigation';
import { revalidatePath } from 'next/cache';
import { createClient } from '@/lib/supabase/server';
import { notify } from '@/lib/notify';
import { addMonths, monthDays, monthParam, type MonthDay } from '@/lib/roster';

interface CellEdit {
  profileId: string;
  workDate: string;
  shiftCode: string | null;
}

export async function createRosterPeriod(formData: FormData) {
  const supabase = await createClient();
  const {
    data: { user },
  } = await supabase.auth.getUser();
  if (!user) redirect('/login');

  const year = Number(formData.get('year'));
  const month = Number(formData.get('month'));

  const { data: profile } = await supabase
    .from('profiles')
    .select('department_id')
    .eq('id', user.id)
    .single();
  if (!profile?.department_id) {
    redirect('/supervisor/roster?error=' + encodeURIComponent('Your profile has no department set.'));
  }

  await supabase.from('roster_periods').upsert(
    { department_id: profile!.department_id!, year, month, created_by: user.id, status: 'draft' },
    { onConflict: 'department_id,year,month', ignoreDuplicates: true },
  );

  revalidatePath('/supervisor/roster');
  redirect(`/supervisor/roster?month=${monthParam(year, month)}`);
}

export async function saveRoster(formData: FormData) {
  const supabase = await createClient();
  const {
    data: { user },
  } = await supabase.auth.getUser();
  if (!user) redirect('/login');

  const rosterPeriodId = String(formData.get('roster_period_id') ?? '');
  const month = String(formData.get('month') ?? '');
  let edits: CellEdit[] = [];
  try {
    edits = JSON.parse(String(formData.get('payload') ?? '[]')) as CellEdit[];
  } catch {
    edits = [];
  }

  if (rosterPeriodId && edits.length > 0) {
    const { error } = await supabase.from('roster_assignments').upsert(
      edits.map((e) => ({
        roster_period_id: rosterPeriodId,
        profile_id: e.profileId,
        work_date: e.workDate,
        shift_code: e.shiftCode || null,
        created_by: user.id,
      })),
      { onConflict: 'roster_period_id,profile_id,work_date' },
    );
    if (error) redirect(`/supervisor/roster?month=${month}&error=${encodeURIComponent(error.message)}`);
  }

  revalidatePath('/supervisor/roster');
  redirect(`/supervisor/roster?month=${month}`);
}

type AssignmentRow = { roster_period_id: string; profile_id: string; work_date: string; shift_code: string; created_by: string };

/** Resolve the caller, their department's operators, the target days, and which
 * cells are already filled — shared setup for the auto-fill generators. */
async function autoFillContext(rosterPeriodId: string, year: number, month: number) {
  const supabase = await createClient();
  const {
    data: { user },
  } = await supabase.auth.getUser();
  if (!user) redirect('/login');

  const { data: profile } = await supabase
    .from('profiles')
    .select('department_id')
    .eq('id', user.id)
    .single();

  const { data: operators } = await supabase
    .from('profiles')
    .select('id')
    .eq('role', 'operator')
    .eq('department_id', profile?.department_id ?? '')
    .eq('active', true);

  const { data: current } = await supabase
    .from('roster_assignments')
    .select('profile_id, work_date, shift_code')
    .eq('roster_period_id', rosterPeriodId);
  const filled = new Set(
    (current ?? []).filter((c) => c.shift_code).map((c) => `${c.profile_id}|${c.work_date}`),
  );

  return { supabase, user, deptId: profile?.department_id ?? null, operators: operators ?? [], filled };
}

function mostFrequent(counts: Map<string, number>): string | null {
  let best: string | null = null;
  let bestN = 0;
  for (const [code, n] of counts) {
    if (n > bestN) {
      best = code;
      bestN = n;
    }
  }
  return best;
}

type PrevAssignment = { profile_id: string; work_date: string; shift_code: string | null };

/** Build `${profileId}|${weekday}` -> (shiftCode -> count) from a month's rows. */
function buildWeekdayPattern(assignments: PrevAssignment[]): Map<string, Map<string, number>> {
  const pattern = new Map<string, Map<string, number>>();
  for (const a of assignments) {
    if (!a.shift_code) continue;
    const weekday = new Date(`${a.work_date}T00:00:00`).getDay();
    const key = `${a.profile_id}|${weekday}`;
    const counts = pattern.get(key) ?? new Map<string, number>();
    counts.set(a.shift_code, (counts.get(a.shift_code) ?? 0) + 1);
    pattern.set(key, counts);
  }
  return pattern;
}

/** Generate upsert rows for empty cells from a weekday pattern. */
function rowsFromPattern(
  operators: { id: string }[],
  days: MonthDay[],
  pattern: Map<string, Map<string, number>>,
  filled: Set<string>,
  rosterPeriodId: string,
  userId: string,
): AssignmentRow[] {
  const rows: AssignmentRow[] = [];
  for (const op of operators) {
    for (const d of days) {
      if (filled.has(`${op.id}|${d.date}`)) continue;
      const counts = pattern.get(`${op.id}|${d.weekday}`);
      const code = counts ? mostFrequent(counts) : null;
      if (code) {
        rows.push({ roster_period_id: rosterPeriodId, profile_id: op.id, work_date: d.date, shift_code: code, created_by: userId });
      }
    }
  }
  return rows;
}

/** Seed the month from the previous month's per-operator, per-weekday pattern. */
export async function copyPreviousMonth(formData: FormData) {
  const rosterPeriodId = String(formData.get('roster_period_id') ?? '');
  const year = Number(formData.get('year'));
  const month = Number(formData.get('month'));
  const back = `/supervisor/roster?month=${monthParam(year, month)}`;
  if (!rosterPeriodId) redirect('/supervisor/roster');

  const { supabase, user, deptId, operators, filled } = await autoFillContext(rosterPeriodId, year, month);
  const prev = addMonths(year, month, -1);

  const { data: prevPeriod } = await supabase
    .from('roster_periods')
    .select('id')
    .eq('department_id', deptId ?? '')
    .eq('year', prev.year)
    .eq('month', prev.month)
    .maybeSingle();
  if (!prevPeriod) {
    redirect(`${back}&error=${encodeURIComponent('No previous-month roster to copy from.')}`);
  }

  const { data: prevAssignments } = await supabase
    .from('roster_assignments')
    .select('profile_id, work_date, shift_code')
    .eq('roster_period_id', prevPeriod!.id);

  const pattern = buildWeekdayPattern(prevAssignments ?? []);
  const rows = rowsFromPattern(operators, monthDays(year, month), pattern, filled, rosterPeriodId, user.id);

  if (rows.length > 0) {
    await supabase.from('roster_assignments').upsert(rows, { onConflict: 'roster_period_id,profile_id,work_date' });
  }

  revalidatePath('/supervisor/roster');
  redirect(rows.length > 0 ? back : `${back}&error=${encodeURIComponent('Nothing to copy (previous month was empty).')}`);
}

/** Mark every empty weekend cell as Off — a common starting point. */
export async function fillWeekendsOff(formData: FormData) {
  const rosterPeriodId = String(formData.get('roster_period_id') ?? '');
  const year = Number(formData.get('year'));
  const month = Number(formData.get('month'));
  const back = `/supervisor/roster?month=${monthParam(year, month)}`;
  if (!rosterPeriodId) redirect('/supervisor/roster');

  const { supabase, user, operators, filled } = await autoFillContext(rosterPeriodId, year, month);

  const rows: AssignmentRow[] = [];
  for (const op of operators) {
    for (const d of monthDays(year, month)) {
      if (!d.isWeekend) continue;
      const cell = `${op.id}|${d.date}`;
      if (filled.has(cell)) continue;
      rows.push({ roster_period_id: rosterPeriodId, profile_id: op.id, work_date: d.date, shift_code: 'O', created_by: user.id });
    }
  }

  if (rows.length > 0) {
    await supabase.from('roster_assignments').upsert(rows, { onConflict: 'roster_period_id,profile_id,work_date' });
  }

  revalidatePath('/supervisor/roster');
  redirect(back);
}

export async function publishRoster(formData: FormData) {
  const supabase = await createClient();
  const {
    data: { user },
  } = await supabase.auth.getUser();
  if (!user) redirect('/login');

  const rosterPeriodId = String(formData.get('roster_period_id') ?? '');
  const month = String(formData.get('month') ?? '');
  if (!rosterPeriodId) redirect('/supervisor/roster');

  const { data: period } = await supabase
    .from('roster_periods')
    .select('id, department_id')
    .eq('id', rosterPeriodId)
    .single();

  await supabase
    .from('roster_periods')
    .update({ status: 'published', published_at: new Date().toISOString() })
    .eq('id', rosterPeriodId);

  if (period?.department_id) {
    const { data: ops } = await supabase
      .from('profiles')
      .select('id')
      .eq('role', 'operator')
      .eq('department_id', period.department_id)
      .eq('active', true);
    await notify(
      supabase,
      (ops ?? []).map((o) => ({
        recipient_id: o.id,
        type: 'roster_published',
        title: 'Monthly roster published',
        body: `The roster for ${month} is now available.`,
      })),
    );
  }

  revalidatePath('/supervisor/roster');
  redirect(`/supervisor/roster?month=${month}`);
}
