import { USER_ROLES, USER_ROLE_LABELS, fullName, type UserRole } from '@nbc/shared';
import { requireRole } from '@/lib/auth';
import { createClient } from '@/lib/supabase/server';
import { updateMember } from './actions';

export default async function AdminHome({
  searchParams,
}: {
  searchParams: Promise<{ error?: string }>;
}) {
  await requireRole(['admin']);
  const { error } = await searchParams;
  const supabase = await createClient();

  const { data: people } = await supabase
    .from('profiles')
    .select('id, first_name, last_name, work_email, role, department_id, active')
    .order('last_name');

  const { data: departments } = await supabase
    .from('departments')
    .select('id, name, kind')
    .order('kind')
    .order('name');
  const opsDepts = (departments ?? []).filter((d) => d.kind === 'operations');
  const contentDepts = (departments ?? []).filter((d) => d.kind === 'content');

  return (
    <div className="mx-auto max-w-6xl">
      <h1 className="mb-1 text-2xl font-semibold">People &amp; Roles</h1>
      <p className="mb-4 text-sm text-slate-600">
        Assign each person their role and department. Setting a department adds them to that
        department&apos;s roster automatically.
      </p>
      {error && <p className="mb-4 rounded-md bg-red-50 px-3 py-2 text-sm text-red-700">{error}</p>}

      <div className="overflow-x-auto rounded-lg border border-slate-200 bg-white">
        <table className="w-full text-sm">
          <thead className="bg-slate-50 text-left text-slate-500">
            <tr>
              <th className="px-3 py-2">Name</th>
              <th className="px-3 py-2">Role</th>
              <th className="px-3 py-2">Department</th>
              <th className="px-3 py-2">Active</th>
              <th className="px-3 py-2"></th>
            </tr>
          </thead>
          <tbody>
            {(people ?? []).map((p) => (
              <tr key={p.id} className="border-t border-slate-100 align-middle">
                <td className="px-3 py-2">
                  <div className="font-medium">{fullName(p)}</div>
                  <div className="text-xs text-slate-500">{p.work_email}</div>
                </td>
                <td className="px-3 py-2">
                  <select form={`m-${p.id}`} name="role" defaultValue={p.role} className="input !py-1">
                    {USER_ROLES.map((r) => (
                      <option key={r} value={r}>
                        {USER_ROLE_LABELS[r as UserRole]}
                      </option>
                    ))}
                  </select>
                </td>
                <td className="px-3 py-2">
                  <select
                    form={`m-${p.id}`}
                    name="department_id"
                    defaultValue={p.department_id ?? ''}
                    className="input !py-1"
                  >
                    <option value="">— None —</option>
                    <optgroup label="Operations">
                      {opsDepts.map((d) => (
                        <option key={d.id} value={d.id}>
                          {d.name}
                        </option>
                      ))}
                    </optgroup>
                    <optgroup label="Content">
                      {contentDepts.map((d) => (
                        <option key={d.id} value={d.id}>
                          {d.name}
                        </option>
                      ))}
                    </optgroup>
                  </select>
                </td>
                <td className="px-3 py-2">
                  <input form={`m-${p.id}`} type="checkbox" name="active" defaultChecked={p.active} />
                </td>
                <td className="px-3 py-2">
                  <form id={`m-${p.id}`} action={updateMember}>
                    <input type="hidden" name="profile_id" value={p.id} />
                    <button className="btn-primary !px-3 !py-1" type="submit">
                      Save
                    </button>
                  </form>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </div>
  );
}
