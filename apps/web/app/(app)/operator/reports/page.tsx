import { SEVERITIES, type Severity } from '@nbc/shared';
import { requireRole } from '@/lib/auth';
import { createClient } from '@/lib/supabase/server';
import { fmtTimestamp } from '@/lib/format';
import { submitReport } from './actions';

const SEVERITY_STYLE: Record<Severity, string> = {
  low: 'bg-slate-100 text-slate-700',
  medium: 'bg-amber-100 text-amber-800',
  high: 'bg-orange-100 text-orange-800',
  critical: 'bg-red-100 text-red-700',
};

const SEVERITY_GUIDE: Record<Severity, string> = {
  low: 'Minor — noted for the record; production was unaffected (e.g. a spare cable was used).',
  medium: 'Noticeable — caused delay or a workaround, but the production still went ahead.',
  high: 'Serious — significantly disrupted the production or risked equipment/safety.',
  critical: 'Severe — production failed/stopped, or there was a safety incident. Needs urgent attention.',
};

export default async function OperatorReportsPage({
  searchParams,
}: {
  searchParams: Promise<{ error?: string }>;
}) {
  const { userId } = await requireRole(['operator', 'admin']);
  const { error } = await searchParams;
  const supabase = await createClient();

  const { data: crew } = await supabase
    .from('booking_crew')
    .select('booking:bookings(id, title)')
    .eq('profile_id', userId)
    .limit(50);
  const bookingOptions = (crew ?? [])
    .map((c) => (Array.isArray(c.booking) ? c.booking[0] : c.booking))
    .filter((b): b is { id: string; title: string } => Boolean(b));

  const { data: reports } = await supabase
    .from('production_reports')
    .select('id, title, body, severity, status, created_at, booking:bookings(title)')
    .eq('author_id', userId)
    .order('created_at', { ascending: false });

  return (
    <div className="mx-auto max-w-2xl">
      <h1 className="mb-1 text-2xl font-semibold">Production Reports</h1>
      <p className="mb-4 text-sm text-slate-600">
        Raise an issue to your supervisor. Pick the severity that best fits.
      </p>
      {error && <p className="mb-4 rounded-md bg-red-50 px-3 py-2 text-sm text-red-700">{error}</p>}

      <details className="card mb-4">
        <summary className="cursor-pointer text-sm font-medium">What do the severity levels mean?</summary>
        <ul className="mt-3 space-y-2 text-sm">
          {SEVERITIES.map((s) => (
            <li key={s} className="flex gap-2">
              <span className={`h-fit rounded-full px-2 py-0.5 text-xs font-medium capitalize ${SEVERITY_STYLE[s]}`}>
                {s}
              </span>
              <span className="text-slate-600">{SEVERITY_GUIDE[s]}</span>
            </li>
          ))}
        </ul>
      </details>

      <form action={submitReport} className="card mb-6 space-y-4">
        <div>
          <label className="label" htmlFor="title">
            What happened?
          </label>
          <input className="input" id="title" name="title" required placeholder="Short summary" />
        </div>
        <div className="grid grid-cols-2 gap-3">
          <div>
            <label className="label" htmlFor="severity">
              Severity
            </label>
            <select className="input capitalize" id="severity" name="severity" defaultValue="low">
              {SEVERITIES.map((s) => (
                <option key={s} value={s}>
                  {s}
                </option>
              ))}
            </select>
          </div>
          <div>
            <label className="label" htmlFor="booking_id">
              Production (optional)
            </label>
            <select className="input" id="booking_id" name="booking_id" defaultValue="">
              <option value="">— Not specific —</option>
              {bookingOptions.map((b) => (
                <option key={b.id} value={b.id}>
                  {b.title}
                </option>
              ))}
            </select>
          </div>
        </div>
        <div>
          <label className="label" htmlFor="body">
            Details
          </label>
          <textarea className="input min-h-24" id="body" name="body" placeholder="What went wrong, impact, what you did…" />
        </div>
        <button className="btn-primary" type="submit">
          Submit report
        </button>
      </form>

      <h2 className="mb-2 font-medium">My reports</h2>
      {(!reports || reports.length === 0) && (
        <div className="card text-sm text-slate-600">No reports submitted yet.</div>
      )}
      <div className="space-y-2">
        {(reports ?? []).map((r) => {
          const bk = Array.isArray(r.booking) ? r.booking[0] : r.booking;
          return (
            <div key={r.id} className="card">
              <div className="flex items-center justify-between gap-3">
                <span className="font-medium">{r.title}</span>
                <span className={`rounded-full px-2 py-0.5 text-xs font-medium capitalize ${SEVERITY_STYLE[r.severity as Severity]}`}>
                  {r.severity}
                </span>
              </div>
              {r.body && <p className="mt-1 text-sm text-slate-600">{r.body}</p>}
              <p className="mt-1 text-xs text-slate-400">
                {bk?.title ? `${bk.title} · ` : ''}
                {r.status} · {fmtTimestamp(r.created_at)}
              </p>
            </div>
          );
        })}
      </div>
    </div>
  );
}
