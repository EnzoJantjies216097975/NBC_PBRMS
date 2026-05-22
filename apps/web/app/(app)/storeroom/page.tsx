import { fullName, type EquipmentStatus } from '@nbc/shared';
import { requireProfile } from '@/lib/auth';
import { createClient } from '@/lib/supabase/server';
import { approveCheckout, denyRequest, markReturned, requestEquipment } from './actions';

function one<T>(v: T | T[] | null | undefined): T | undefined {
  return Array.isArray(v) ? v[0] : (v ?? undefined);
}

const STATUS_STYLE: Record<EquipmentStatus, string> = {
  available: 'bg-emerald-100 text-emerald-800',
  booked_out: 'bg-amber-100 text-amber-800',
  maintenance: 'bg-slate-200 text-slate-700',
  retired: 'bg-red-100 text-red-700',
};

export default async function StoreroomPage({
  searchParams,
}: {
  searchParams: Promise<{ error?: string }>;
}) {
  const { userId, profile } = await requireProfile();
  const { error } = await searchParams;
  const supabase = await createClient();
  const canManage = profile.role === 'supervisor' || profile.role === 'admin';

  const { data: equipment } = await supabase
    .from('equipment')
    .select('id, name, category, serial_number, status, home_location')
    .order('category')
    .order('name');

  const { data: checkedOut } = await supabase
    .from('equipment_bookings')
    .select('id, equipment_id, requested_by, profile:profiles(first_name, last_name), booking:bookings(title)')
    .eq('status', 'checked_out');
  const holder = new Map<string, { name: string; production?: string }>();
  for (const c of checkedOut ?? []) {
    const p = one(c.profile);
    const bk = one(c.booking);
    holder.set(c.equipment_id, { name: p ? fullName(p) : 'Someone', production: bk?.title });
  }

  const { data: myRequests } = await supabase
    .from('equipment_bookings')
    .select('id, status, equipment:equipment(name), booking:bookings(title), created_at')
    .eq('requested_by', userId)
    .order('created_at', { ascending: false });

  const { data: crew } = await supabase
    .from('booking_crew')
    .select('booking:bookings(id, title)')
    .eq('profile_id', userId)
    .limit(50);
  const bookingOptions = (crew ?? [])
    .map((c) => one(c.booking))
    .filter((b): b is { id: string; title: string } => Boolean(b));

  const pending = canManage
    ? (
        await supabase
          .from('equipment_bookings')
          .select('id, equipment_id, requested_by, notes, equipment:equipment(name), profile:profiles(first_name, last_name)')
          .eq('status', 'requested')
          .order('created_at')
      ).data
    : null;

  const available = (equipment ?? []).filter((e) => e.status === 'available');

  return (
    <div className="mx-auto max-w-5xl">
      <h1 className="mb-1 text-2xl font-semibold">Storeroom</h1>
      <p className="mb-4 text-sm text-slate-600">
        Inventory by serial number. Operators request gear; supervisors approve and check it out.
      </p>
      {error && <p className="mb-4 rounded-md bg-red-50 px-3 py-2 text-sm text-red-700">{error}</p>}

      <section className="card mb-6">
        <h2 className="mb-3 font-medium">Request equipment</h2>
        <form action={requestEquipment} className="grid grid-cols-1 gap-3 sm:grid-cols-4">
          <select className="input" name="equipment_id" defaultValue="" required>
            <option value="" disabled>
              Item…
            </option>
            {available.map((e) => (
              <option key={e.id} value={e.id}>
                {e.name} ({e.serial_number})
              </option>
            ))}
          </select>
          <select className="input" name="booking_id" defaultValue="">
            <option value="">For production… (optional)</option>
            {bookingOptions.map((b) => (
              <option key={b.id} value={b.id}>
                {b.title}
              </option>
            ))}
          </select>
          <input className="input" name="due_back_at" type="date" title="Due back" />
          <button className="btn-primary" type="submit">
            Request
          </button>
          <input className="input sm:col-span-4" name="notes" placeholder="Notes (optional)" />
        </form>
      </section>

      <section className="mb-6 overflow-hidden rounded-lg border border-slate-200 bg-white">
        <table className="w-full text-sm">
          <thead className="bg-slate-50 text-left text-slate-500">
            <tr>
              <th className="px-4 py-2">Item</th>
              <th className="px-4 py-2">Serial</th>
              <th className="px-4 py-2">Category</th>
              <th className="px-4 py-2">Status</th>
              <th className="px-4 py-2">Held by</th>
            </tr>
          </thead>
          <tbody>
            {(equipment ?? []).map((e) => {
              const h = holder.get(e.id);
              return (
                <tr key={e.id} className="border-t border-slate-100">
                  <td className="px-4 py-2 font-medium">{e.name}</td>
                  <td className="px-4 py-2 text-slate-500">{e.serial_number}</td>
                  <td className="px-4 py-2 text-slate-600">{e.category}</td>
                  <td className="px-4 py-2">
                    <span className={`rounded-full px-2 py-0.5 text-xs font-medium ${STATUS_STYLE[e.status as EquipmentStatus]}`}>
                      {(e.status as string).replace('_', ' ')}
                    </span>
                  </td>
                  <td className="px-4 py-2 text-slate-600">
                    {h ? `${h.name}${h.production ? ` · ${h.production}` : ''}` : '—'}
                  </td>
                </tr>
              );
            })}
          </tbody>
        </table>
      </section>

      <div className="grid gap-6 lg:grid-cols-2">
        <section>
          <h2 className="mb-2 font-medium">My requests</h2>
          {(!myRequests || myRequests.length === 0) && (
            <div className="card text-sm text-slate-600">No requests yet.</div>
          )}
          <div className="space-y-2">
            {(myRequests ?? []).map((r) => {
              const eq = one(r.equipment);
              const bk = one(r.booking);
              return (
                <div key={r.id} className="card flex items-center justify-between py-3 text-sm">
                  <span>
                    {eq?.name ?? 'Item'}
                    {bk?.title ? <span className="text-slate-500"> · {bk.title}</span> : ''}
                  </span>
                  <span className="rounded-full bg-slate-100 px-2 py-0.5 text-xs capitalize">
                    {(r.status as string).replace('_', ' ')}
                  </span>
                </div>
              );
            })}
          </div>
        </section>

        {canManage && (
          <section>
            <h2 className="mb-2 font-medium">Pending approvals</h2>
            {(!pending || pending.length === 0) && (
              <div className="card text-sm text-slate-600">Nothing waiting for approval.</div>
            )}
            <div className="space-y-2">
              {(pending ?? []).map((r) => {
                const eq = one(r.equipment);
                const p = one(r.profile);
                return (
                  <div key={r.id} className="card text-sm">
                    <div className="mb-2">
                      <span className="font-medium">{eq?.name ?? 'Item'}</span> ·{' '}
                      {p ? fullName(p) : 'Operator'}
                      {r.notes ? <span className="text-slate-500"> — {r.notes}</span> : ''}
                    </div>
                    <div className="flex gap-2">
                      <form action={approveCheckout}>
                        <input type="hidden" name="request_id" value={r.id} />
                        <input type="hidden" name="equipment_id" value={r.equipment_id} />
                        <input type="hidden" name="requester_id" value={r.requested_by} />
                        <button className="btn-primary !px-3 !py-1" type="submit">
                          Approve &amp; check out
                        </button>
                      </form>
                      <form action={denyRequest}>
                        <input type="hidden" name="request_id" value={r.id} />
                        <input type="hidden" name="requester_id" value={r.requested_by} />
                        <button className="btn-ghost !px-3 !py-1" type="submit">
                          Deny
                        </button>
                      </form>
                    </div>
                  </div>
                );
              })}
            </div>

            {(checkedOut ?? []).length > 0 && (
              <>
                <h2 className="mb-2 mt-4 font-medium">Checked out</h2>
                <div className="space-y-2">
                  {(checkedOut ?? []).map((c) => {
                    const p = one(c.profile);
                    const eqName = (equipment ?? []).find((e) => e.id === c.equipment_id)?.name;
                    return (
                      <div key={c.id} className="card flex items-center justify-between py-3 text-sm">
                        <span>
                          {eqName ?? 'Item'} · {p ? fullName(p) : 'Someone'}
                        </span>
                        <form action={markReturned}>
                          <input type="hidden" name="request_id" value={c.id} />
                          <input type="hidden" name="equipment_id" value={c.equipment_id} />
                          <button className="btn-ghost !px-3 !py-1" type="submit">
                            Mark returned
                          </button>
                        </form>
                      </div>
                    );
                  })}
                </div>
              </>
            )}
          </section>
        )}
      </div>
    </div>
  );
}
