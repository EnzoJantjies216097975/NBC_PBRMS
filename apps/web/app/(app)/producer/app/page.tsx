import Link from 'next/link';
import { fullName, type BookingStatus } from '@nbc/shared';
import { requireRole } from '@/lib/auth';
import { createClient } from '@/lib/supabase/server';
import { StatusBadge } from '@/components/StatusBadge';
import { fmtDateTimeRange } from '@/lib/format';
import { bookApp } from './actions';

export default async function ProducerAppPage({
  searchParams,
}: {
  searchParams: Promise<{ error?: string }>;
}) {
  const { userId } = await requireRole(['producer', 'admin']);
  const { error } = await searchParams;
  const supabase = await createClient();

  const { data: soundDept } = await supabase
    .from('departments')
    .select('id')
    .eq('name', 'Sound')
    .is('parent_id', null)
    .maybeSingle();

  const { data: soundOps } = soundDept
    ? await supabase
        .from('profiles')
        .select('id, first_name, last_name')
        .eq('role', 'operator')
        .eq('department_id', soundDept.id)
        .eq('active', true)
        .order('last_name')
    : { data: [] };

  const { data: sessions } = await supabase
    .from('bookings')
    .select('id, title, status, call_date, call_time, end_time')
    .eq('producer_id', userId)
    .eq('location_type', 'audio_post')
    .order('call_date', { ascending: false });

  const opOptions = (
    <>
      <option value="">— None —</option>
      {(soundOps ?? []).map((o) => (
        <option key={o.id} value={o.id}>
          {fullName(o)}
        </option>
      ))}
    </>
  );

  return (
    <div className="mx-auto max-w-2xl">
      <h1 className="mb-1 text-2xl font-semibold">Audio Post Production</h1>
      <p className="mb-4 text-sm text-slate-600">
        APP sessions are fast-tracked — booked directly without the full pipeline. The Sound
        Supervisor and Booking Officer are notified automatically.
      </p>
      {error && <p className="mb-4 rounded-md bg-red-50 px-3 py-2 text-sm text-red-700">{error}</p>}

      <form action={bookApp} className="card mb-6 space-y-4">
        <div>
          <label className="label" htmlFor="title">
            Production name
          </label>
          <input className="input" id="title" name="title" required placeholder="What's being post-produced" />
        </div>
        <div className="grid grid-cols-3 gap-3">
          <div>
            <label className="label" htmlFor="call_date">Date</label>
            <input className="input" id="call_date" name="call_date" type="date" required />
          </div>
          <div>
            <label className="label" htmlFor="call_time">Start</label>
            <input className="input" id="call_time" name="call_time" type="time" required />
          </div>
          <div>
            <label className="label" htmlFor="end_time">End</label>
            <input className="input" id="end_time" name="end_time" type="time" />
          </div>
        </div>
        <div className="grid grid-cols-2 gap-3">
          <div>
            <label className="label" htmlFor="op1">APP operator 1</label>
            <select className="input" id="op1" name="op1" defaultValue="">
              {opOptions}
            </select>
          </div>
          <div>
            <label className="label" htmlFor="op2">APP operator 2</label>
            <select className="input" id="op2" name="op2" defaultValue="">
              {opOptions}
            </select>
          </div>
        </div>
        <div>
          <label className="label" htmlFor="notes">Notes</label>
          <input className="input" id="notes" name="notes" placeholder="Optional" />
        </div>
        <button className="btn-primary" type="submit">
          Book APP session
        </button>
      </form>

      <h2 className="mb-2 font-medium">My APP sessions</h2>
      {(!sessions || sessions.length === 0) && (
        <div className="card text-sm text-slate-600">No APP sessions yet.</div>
      )}
      <div className="space-y-2">
        {(sessions ?? []).map((s) => (
          <div key={s.id} className="card flex items-center justify-between gap-3">
            <div>
              <div className="flex items-center gap-2">
                <span className="font-medium">{s.title}</span>
                <StatusBadge status={s.status as BookingStatus} />
              </div>
              <p className="text-sm text-slate-500">
                {fmtDateTimeRange(s.call_date, s.call_time, s.end_time)}
              </p>
            </div>
            <Link href={`/producer/run/${s.id}`} className="btn-ghost shrink-0">
              Run
            </Link>
          </div>
        ))}
      </div>
    </div>
  );
}
