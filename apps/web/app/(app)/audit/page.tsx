import { fullName } from '@nbc/shared';
import { requireRole } from '@/lib/auth';
import { createClient } from '@/lib/supabase/server';
import { fmtTimestamp } from '@/lib/format';

function one<T>(v: T | T[] | null | undefined): T | undefined {
  return Array.isArray(v) ? v[0] : (v ?? undefined);
}

const ACTION_LABELS: Record<string, string> = {
  ep_approved: 'Approved by Exec Producer',
  ep_changes_requested: 'EP requested changes',
  crew_assigned: 'Crew assigned',
  supervisor_changes_requested: 'Supervisor sent back',
  confirmed: 'Booking confirmed',
  cancelled: 'Production cancelled',
  production_started: 'Production started',
  production_ended: 'Production ended',
  app_booked: 'APP session booked',
  member_updated: 'Member role/department updated',
};

export default async function AuditPage() {
  await requireRole(['supervisor', 'booking_officer', 'manager', 'admin']);
  const supabase = await createClient();

  const { data: entries } = await supabase
    .from('audit_log')
    .select('id, action, detail, created_at, actor:profiles(first_name, last_name), booking:bookings(title)')
    .order('created_at', { ascending: false })
    .limit(200);

  return (
    <div className="mx-auto max-w-5xl">
      <h1 className="mb-1 text-2xl font-semibold">Audit Log</h1>
      <p className="mb-4 text-sm text-slate-600">
        Pipeline actions, overrides, and admin changes — most recent first.
      </p>

      {(!entries || entries.length === 0) && (
        <div className="card text-sm text-slate-600">No audit entries yet.</div>
      )}

      <div className="overflow-x-auto rounded-lg border border-slate-200 bg-white">
        <table className="w-full text-sm">
          <thead className="bg-slate-50 text-left text-slate-500">
            <tr>
              <th className="px-3 py-2">When</th>
              <th className="px-3 py-2">Who</th>
              <th className="px-3 py-2">Action</th>
              <th className="px-3 py-2">Production</th>
              <th className="px-3 py-2">Detail</th>
            </tr>
          </thead>
          <tbody>
            {(entries ?? []).map((e) => {
              const actor = one(e.actor);
              const booking = one(e.booking);
              return (
                <tr key={e.id} className="border-t border-slate-100 align-top">
                  <td className="px-3 py-2 text-slate-500">{fmtTimestamp(e.created_at)}</td>
                  <td className="px-3 py-2">{actor ? fullName(actor) : 'System'}</td>
                  <td className="px-3 py-2">{ACTION_LABELS[e.action] ?? e.action}</td>
                  <td className="px-3 py-2 text-slate-600">{booking?.title ?? '—'}</td>
                  <td className="px-3 py-2 text-xs text-slate-400">
                    {e.detail ? JSON.stringify(e.detail) : ''}
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
