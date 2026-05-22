import { USER_ROLE_LABELS, fullName, type UserRole } from '@nbc/shared';
import { requireRole } from '@/lib/auth';
import { createClient } from '@/lib/supabase/server';

export default async function AdminHome() {
  await requireRole(['admin']);
  const supabase = await createClient();

  const { data: people } = await supabase
    .from('profiles')
    .select('id, first_name, last_name, work_email, role, active')
    .order('last_name');

  return (
    <div className="mx-auto max-w-5xl">
      <h1 className="mb-1 text-2xl font-semibold">People &amp; Roles</h1>
      <p className="mb-6 text-sm text-slate-600">
        Everyone in the system. Inline role/department assignment UI is next; for now roles can be
        set in Supabase or via a follow-up phase.
      </p>

      <div className="overflow-hidden rounded-lg border border-slate-200 bg-white">
        <table className="w-full text-sm">
          <thead className="bg-slate-50 text-left text-slate-500">
            <tr>
              <th className="px-4 py-2">Name</th>
              <th className="px-4 py-2">Work email</th>
              <th className="px-4 py-2">Role</th>
              <th className="px-4 py-2">Active</th>
            </tr>
          </thead>
          <tbody>
            {(people ?? []).map((p) => (
              <tr key={p.id} className="border-t border-slate-100">
                <td className="px-4 py-2 font-medium">{fullName(p)}</td>
                <td className="px-4 py-2 text-slate-600">{p.work_email}</td>
                <td className="px-4 py-2">{USER_ROLE_LABELS[p.role as UserRole]}</td>
                <td className="px-4 py-2">{p.active ? 'Yes' : 'No'}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </div>
  );
}
