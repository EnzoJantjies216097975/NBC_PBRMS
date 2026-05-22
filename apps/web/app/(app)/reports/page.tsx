import Link from 'next/link';
import { LOCATION_TYPE_LABELS, formatMinutes, fullName } from '@nbc/shared';
import { requireRole } from '@/lib/auth';
import { createClient } from '@/lib/supabase/server';
import { fmtDate } from '@/lib/format';
import { addMonths, monthDays, monthLabel, monthParam, parseMonth } from '@/lib/roster';
import { buildReport, type ReportBooking } from '@/lib/reports';
import { PrintButton } from '@/components/PrintButton';

export default async function ReportsPage({
  searchParams,
}: {
  searchParams: Promise<{ month?: string }>;
}) {
  await requireRole(['supervisor', 'booking_officer', 'manager', 'admin', 'executive_producer']);
  const sp = await searchParams;
  const { year, month } = parseMonth(sp.month);
  const supabase = await createClient();

  const days = monthDays(year, month);
  const start = `${monthParam(year, month)}-01`;
  const end = days.at(-1)?.date ?? start;

  const { data } = await supabase
    .from('bookings')
    .select(
      'id, title, status, location_type, call_date, call_time, end_time, ' +
        'booking_crew(profile_id, profile:profiles(first_name, last_name)), ' +
        'production_logs(actual_start, actual_end)',
    )
    .gte('call_date', start)
    .lte('call_date', end)
    .order('call_date');

  const bookings: ReportBooking[] = (data ?? []).map((b) => ({
    id: b.id,
    title: b.title,
    status: b.status,
    location_type: b.location_type,
    call_date: b.call_date,
    call_time: b.call_time,
    end_time: b.end_time,
    crew: (b.booking_crew ?? []).map((c) => {
      const p = Array.isArray(c.profile) ? c.profile[0] : c.profile;
      return { profileId: c.profile_id, name: p ? fullName(p) : 'Operator' };
    }),
    logs: (b.production_logs ?? []).map((l) => ({ start: l.actual_start, end: l.actual_end })),
  }));

  const { data: operators } = await supabase
    .from('profiles')
    .select('id, first_name, last_name')
    .eq('role', 'operator')
    .eq('active', true);

  const report = buildReport(
    bookings,
    (operators ?? []).map((o) => ({ id: o.id, name: fullName(o) })),
  );

  const prev = addMonths(year, month, -1);
  const next = addMonths(year, month, 1);

  return (
    <div className="mx-auto max-w-5xl">
      <div className="mb-4 flex flex-wrap items-center justify-between gap-3">
        <div>
          <h1 className="text-2xl font-semibold">Reports &amp; Analytics</h1>
          <p className="text-sm text-slate-600">
            {monthLabel(year, month)} · {report.totalSessions} sessions ·{' '}
            {formatMinutes(report.totalOvertime)} overtime
          </p>
        </div>
        <div className="flex items-center gap-2">
          <Link className="btn-ghost no-print" href={`/reports?month=${monthParam(prev.year, prev.month)}`}>
            ← Prev
          </Link>
          <Link className="btn-ghost no-print" href={`/reports?month=${monthParam(next.year, next.month)}`}>
            Next →
          </Link>
          <PrintButton label="Print report" />
        </div>
      </div>

      <div className="print-sheet space-y-6">
        <ReportSection title={`Overtime by operator — total ${formatMinutes(report.totalOvertime)}`}>
          {report.overtimeByOperator.length === 0 ? (
            <Empty>No overtime logged this month.</Empty>
          ) : (
            <table className="w-full text-sm">
              <thead className="text-left text-slate-500">
                <tr className="border-b border-slate-200">
                  <th className="py-1">Operator</th>
                  <th className="py-1 text-right">Overtime</th>
                </tr>
              </thead>
              <tbody>
                {report.overtimeByOperator.map((o) => (
                  <tr key={o.profileId} className="border-b border-slate-100">
                    <td className="py-1">{o.name}</td>
                    <td className="py-1 text-right font-medium">{formatMinutes(o.overtime)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}
        </ReportSection>

        <ReportSection title="Studio usage">
          {report.studioUsage.length === 0 ? (
            <Empty>No bookings this month.</Empty>
          ) : (
            <table className="w-full text-sm">
              <thead className="text-left text-slate-500">
                <tr className="border-b border-slate-200">
                  <th className="py-1">Location</th>
                  <th className="py-1 text-right">Sessions</th>
                  <th className="py-1 text-right">Scheduled time</th>
                </tr>
              </thead>
              <tbody>
                {report.studioUsage.map((s) => (
                  <tr key={s.location} className="border-b border-slate-100">
                    <td className="py-1">{LOCATION_TYPE_LABELS[s.location]}</td>
                    <td className="py-1 text-right">{s.sessions}</td>
                    <td className="py-1 text-right">{formatMinutes(s.minutes)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}
        </ReportSection>

        <ReportSection title={`Trips & locations (${report.trips.length})`}>
          {report.trips.length === 0 ? (
            <Empty>No location/OB/fly-away productions this month.</Empty>
          ) : (
            <table className="w-full text-sm">
              <thead className="text-left text-slate-500">
                <tr className="border-b border-slate-200">
                  <th className="py-1">Production</th>
                  <th className="py-1">Date</th>
                  <th className="py-1">Type</th>
                  <th className="py-1">Crew</th>
                </tr>
              </thead>
              <tbody>
                {report.trips.map((t) => (
                  <tr key={t.id} className="border-b border-slate-100">
                    <td className="py-1 font-medium">{t.title}</td>
                    <td className="py-1">{fmtDate(t.date)}</td>
                    <td className="py-1">{LOCATION_TYPE_LABELS[t.location]}</td>
                    <td className="py-1 text-slate-600">{t.crew.join(', ') || '—'}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}
        </ReportSection>

        <ReportSection title={`Cancellations (${report.cancellations.length})`}>
          {report.cancellations.length === 0 ? (
            <Empty>No cancellations this month.</Empty>
          ) : (
            <ul className="space-y-1 text-sm">
              {report.cancellations.map((c) => (
                <li key={c.id}>
                  {c.title} <span className="text-slate-500">· {fmtDate(c.date)}</span>
                </li>
              ))}
            </ul>
          )}
        </ReportSection>

        <ReportSection title={`Underutilised operators (${report.underutilised.length})`}>
          {report.underutilised.length === 0 ? (
            <Empty>Every active operator had at least one assignment.</Empty>
          ) : (
            <div className="flex flex-wrap gap-1">
              {report.underutilised.map((n) => (
                <span key={n} className="rounded bg-amber-50 px-2 py-0.5 text-xs text-amber-800">
                  {n}
                </span>
              ))}
            </div>
          )}
        </ReportSection>
      </div>
    </div>
  );
}

function ReportSection({ title, children }: { title: string; children: React.ReactNode }) {
  return (
    <section className="card">
      <h2 className="mb-3 font-medium">{title}</h2>
      {children}
    </section>
  );
}

function Empty({ children }: { children: React.ReactNode }) {
  return <p className="text-sm text-slate-500">{children}</p>;
}
