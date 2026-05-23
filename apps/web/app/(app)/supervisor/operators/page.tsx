import { fullName } from '@nbc/shared';
import { requireRole } from '@/lib/auth';
import { createClient } from '@/lib/supabase/server';
import { updateOperator } from './actions';

interface OperatorRow {
  id: string;
  first_name: string;
  last_name: string;
  phone: string | null;
  needs_transport: boolean;
  prefers_sms: boolean;
  can_go_on_trips: boolean;
  profile_skills: { skill_id: string }[] | null;
}

export default async function SupervisorOperatorsPage() {
  const { userId } = await requireRole(['supervisor', 'admin']);
  const supabase = await createClient();

  const { data: profile } = await supabase
    .from('profiles')
    .select('department_id, department:departments(name)')
    .eq('id', userId)
    .single();
  const dept = Array.isArray(profile?.department) ? profile?.department[0] : profile?.department;

  const { data: skills } = await supabase.from('skills').select('id, name').order('name');

  const { data: operators } = profile?.department_id
    ? await supabase
        .from('profiles')
        .select('id, first_name, last_name, phone, needs_transport, prefers_sms, can_go_on_trips, profile_skills(skill_id)')
        .eq('role', 'operator')
        .eq('department_id', profile.department_id)
        .eq('active', true)
        .order('last_name')
    : { data: [] };

  const rows = (operators ?? []) as unknown as OperatorRow[];

  return (
    <div className="mx-auto max-w-4xl">
      <h1 className="mb-1 text-2xl font-semibold">My Operators</h1>
      <p className="mb-4 text-sm text-slate-600">
        {dept?.name ?? 'Your department'} · tag each operator&apos;s skills/gear and flags. New people
        sign up themselves; an admin assigns them to your department.
      </p>

      {rows.length === 0 && (
        <div className="card text-sm text-slate-600">
          No operators in your department yet. Ask an admin to assign them on People &amp; Roles.
        </div>
      )}

      <div className="space-y-3">
        {rows.map((op) => {
          const owned = new Set((op.profile_skills ?? []).map((s) => s.skill_id));
          return (
            <form key={op.id} action={updateOperator} className="card space-y-3">
              <input type="hidden" name="profile_id" value={op.id} />
              <div className="flex items-center justify-between">
                <div>
                  <span className="font-medium">{fullName(op)}</span>
                  {op.phone ? <span className="text-sm text-slate-500"> · {op.phone}</span> : ''}
                </div>
                <button className="btn-primary !px-3 !py-1" type="submit">
                  Save
                </button>
              </div>

              <div className="flex flex-wrap gap-x-6 gap-y-2 text-sm">
                <label className="flex items-center gap-2">
                  <input type="checkbox" name="needs_transport" defaultChecked={op.needs_transport} /> Needs
                  transport (pickup)
                </label>
                <label className="flex items-center gap-2">
                  <input type="checkbox" name="prefers_sms" defaultChecked={op.prefers_sms} /> SMS updates (no
                  smartphone)
                </label>
                <label className="flex items-center gap-2">
                  <input type="checkbox" name="can_go_on_trips" defaultChecked={op.can_go_on_trips} /> Can go
                  on trips
                </label>
              </div>

              <div>
                <p className="mb-1 text-xs font-semibold uppercase tracking-wide text-slate-500">
                  Skills &amp; gear
                </p>
                <div className="flex flex-wrap gap-x-4 gap-y-1 text-sm">
                  {(skills ?? []).map((s) => (
                    <label key={s.id} className="flex items-center gap-2">
                      <input type="checkbox" name="skill_ids" value={s.id} defaultChecked={owned.has(s.id)} />
                      {s.name}
                    </label>
                  ))}
                </div>
              </div>
            </form>
          );
        })}
      </div>
    </div>
  );
}
