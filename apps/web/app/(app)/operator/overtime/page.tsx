import { computeOvertimeFromMinutes, formatMinutes, fullName } from '@nbc/shared';
import { requireRole } from '@/lib/auth';
import { createClient } from '@/lib/supabase/server';
import { fmtDate } from '@/lib/format';
import { PrintButton } from '@/components/PrintButton';

interface LogRow {
  actual_start: string;
  actual_end: string;
  booking: { title: string } | { title: string }[] | null;
}

function hhmm(iso: string): string {
  return new Date(iso).toLocaleTimeString('en-GB', { hour: '2-digit', minute: '2-digit' });
}

export default async function OperatorOvertimePage() {
  const { userId, profile } = await requireRole(['operator', 'admin']);
  const supabase = await createClient();

  const { data: crew } = await supabase.from('booking_crew').select('booking_id').eq('profile_id', userId);
  const bookingIds = [...new Set((crew ?? []).map((c) => c.booking_id))];

  let logs: LogRow[] = [];
  if (bookingIds.length > 0) {
    const { data } = await supabase
      .from('production_logs')
      .select('actual_start, actual_end, booking:bookings(title)')
      .in('booking_id', bookingIds)
      .not('actual_end', 'is', null)
      .order('actual_start');
    logs = data ?? [];
  }

  // Per-production entries + per-day worked totals.
  const entries = logs.map((l) => {
    const start = new Date(l.actual_start);
    const end = new Date(l.actual_end);
    const worked = Math.max(0, Math.round((end.getTime() - start.getTime()) / 60_000));
    const bk = Array.isArray(l.booking) ? l.booking[0] : l.booking;
    return { date: l.actual_start.slice(0, 10), title: bk?.title ?? '—', start, end, worked };
  });

  const byDay = new Map<string, number>();
  for (const e of entries) byDay.set(e.date, (byDay.get(e.date) ?? 0) + e.worked);

  const daily = [...byDay.entries()]
    .sort((a, b) => a[0].localeCompare(b[0]))
    .map(([date, worked]) => {
      const ot = computeOvertimeFromMinutes(new Date(`${date}T12:00:00`), worked);
      return { date, worked, regular: ot.regularMinutes, overtime: ot.overtimeMinutes };
    });

  const totalOvertime = daily.reduce((s, d) => s + d.overtime, 0);

  return (
    <div className="mx-auto max-w-3xl">
      <div className="mb-4 flex items-center justify-between">
        <div>
          <h1 className="text-2xl font-semibold">My Overtime</h1>
          <p className="text-sm text-slate-600">Total overtime: {formatMinutes(totalOvertime)}</p>
        </div>
        <PrintButton label="Print overtime sheet" />
      </div>

      <div className="card print-sheet">
        <div className="mb-4 border-b border-slate-200 pb-3">
          <h2 className="text-lg font-bold">NBC — Overtime Sheet</h2>
          <p className="text-sm text-slate-600">
            Employee: <span className="font-medium">{fullName(profile)}</span>
          </p>
        </div>

        {entries.length === 0 ? (
          <p className="text-sm text-slate-600">No completed productions logged yet.</p>
        ) : (
          <>
            <table className="mb-6 w-full text-sm">
              <thead className="text-left text-slate-500">
                <tr className="border-b border-slate-200">
                  <th className="py-1">Date</th>
                  <th className="py-1">Production</th>
                  <th className="py-1">Start</th>
                  <th className="py-1">End</th>
                  <th className="py-1 text-right">Worked</th>
                </tr>
              </thead>
              <tbody>
                {entries.map((e, i) => (
                  <tr key={`${e.date}-${i}`} className="border-b border-slate-100">
                    <td className="py-1">{fmtDate(e.date)}</td>
                    <td className="py-1">{e.title}</td>
                    <td className="py-1">{hhmm(e.start.toISOString())}</td>
                    <td className="py-1">{hhmm(e.end.toISOString())}</td>
                    <td className="py-1 text-right">{formatMinutes(e.worked)}</td>
                  </tr>
                ))}
              </tbody>
            </table>

            <h3 className="mb-1 text-sm font-semibold">Daily overtime</h3>
            <table className="w-full text-sm">
              <thead className="text-left text-slate-500">
                <tr className="border-b border-slate-200">
                  <th className="py-1">Date</th>
                  <th className="py-1 text-right">Worked</th>
                  <th className="py-1 text-right">Regular</th>
                  <th className="py-1 text-right">Overtime</th>
                </tr>
              </thead>
              <tbody>
                {daily.map((d) => (
                  <tr key={d.date} className="border-b border-slate-100">
                    <td className="py-1">{fmtDate(d.date)}</td>
                    <td className="py-1 text-right">{formatMinutes(d.worked)}</td>
                    <td className="py-1 text-right">{formatMinutes(d.regular)}</td>
                    <td className="py-1 text-right font-medium">{formatMinutes(d.overtime)}</td>
                  </tr>
                ))}
                <tr>
                  <td className="py-2 font-semibold" colSpan={3}>
                    Total overtime
                  </td>
                  <td className="py-2 text-right font-semibold">{formatMinutes(totalOvertime)}</td>
                </tr>
              </tbody>
            </table>

            <div className="mt-10 flex justify-between text-sm">
              <div>
                <div className="h-10 w-48 border-b border-slate-400" />
                <span className="text-slate-500">Employee signature &amp; date</span>
              </div>
              <div>
                <div className="h-10 w-48 border-b border-slate-400" />
                <span className="text-slate-500">Manager: TV Operations</span>
              </div>
            </div>
          </>
        )}
      </div>
    </div>
  );
}
