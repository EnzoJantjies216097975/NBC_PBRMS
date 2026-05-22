import Link from 'next/link';
import type { BookingStatus } from '@nbc/shared';
import { HOUR_MARKS, AXIS_START_HOUR, AXIS_END_HOUR, AXIS_TOTAL_MIN } from '@/lib/schedule';

export interface CalendarBar {
  id: string;
  title: string;
  subtitle?: string;
  leftPct: number;
  widthPct: number;
  status: BookingStatus;
  clash: boolean;
  href: string;
  standing: boolean;
}

export interface CalendarRow {
  key: string;
  label: string;
  bars: CalendarBar[];
}

const BAR_STYLE: Record<BookingStatus, string> = {
  draft: 'bg-slate-300 text-slate-800',
  submitted: 'bg-amber-300 text-amber-950',
  ep_changes_requested: 'bg-orange-300 text-orange-950',
  ep_approved: 'bg-sky-300 text-sky-950',
  supervisor_changes_requested: 'bg-orange-300 text-orange-950',
  crew_assigned: 'bg-indigo-300 text-indigo-950',
  confirmed: 'bg-emerald-400 text-emerald-950',
  in_progress: 'bg-blue-400 text-blue-950',
  completed: 'bg-slate-300 text-slate-700',
  cancelled: 'bg-red-200 text-red-900',
};

const LABEL_COL = 'w-28 shrink-0';

export function DayCalendar({
  rows,
  nowLeftPct = null,
}: {
  rows: CalendarRow[];
  nowLeftPct?: number | null;
}) {
  return (
    <div className="overflow-x-auto rounded-lg border border-slate-200 bg-white">
      <div className="min-w-[760px]">
        {/* Hour header */}
        <div className="flex border-b border-slate-200">
          <div className={`${LABEL_COL} border-r border-slate-200 px-3 py-2 text-xs font-medium text-slate-500`}>
            Studio
          </div>
          <div className="relative h-7 flex-1">
            {HOUR_MARKS.map((h) => (
              <div
                key={h}
                className="absolute top-0 -translate-x-1/2 text-[10px] text-slate-400"
                style={{ left: `${((h - AXIS_START_HOUR) / (AXIS_END_HOUR - AXIS_START_HOUR)) * 100}%` }}
              >
                {String(h % 24).padStart(2, '0')}
              </div>
            ))}
          </div>
        </div>

        {/* Studio rows */}
        {rows.map((row) => (
          <div key={row.key} className="flex border-b border-slate-100 last:border-b-0">
            <div className={`${LABEL_COL} border-r border-slate-200 px-3 py-3 text-sm font-medium text-slate-700`}>
              {row.label}
            </div>
            <div className="relative h-14 flex-1">
              {/* hour gridlines */}
              {HOUR_MARKS.map((h) => (
                <div
                  key={h}
                  className="absolute top-0 h-full border-l border-slate-100"
                  style={{ left: `${((h - AXIS_START_HOUR) / (AXIS_END_HOUR - AXIS_START_HOUR)) * 100}%` }}
                />
              ))}
              {nowLeftPct != null && (
                <div
                  className="absolute top-0 z-10 h-full w-0.5 bg-red-500"
                  style={{ left: `${nowLeftPct}%` }}
                  title="Now"
                />
              )}
              {/* booking bars */}
              {row.bars.map((bar) => {
                const tooltip = [
                  bar.title,
                  bar.subtitle,
                  bar.standing ? 'Standing production' : null,
                  bar.clash ? 'CLASH' : null,
                ]
                  .filter(Boolean)
                  .join(' · ');
                const ring = bar.clash ? 'ring-2 ring-red-500' : '';
                const base =
                  'absolute top-1.5 flex h-11 flex-col justify-center overflow-hidden rounded px-2 text-[11px] leading-tight';
                const style = { left: `${bar.leftPct}%`, width: `${bar.widthPct}%` };
                const inner = (
                  <>
                    <span className="truncate font-semibold">{bar.title}</span>
                    {bar.subtitle && <span className="truncate opacity-80">{bar.subtitle}</span>}
                  </>
                );
                return bar.standing ? (
                  <div
                    key={bar.id}
                    title={tooltip}
                    className={`${base} border border-dashed border-slate-400 bg-white/70 text-slate-500 ${ring}`}
                    style={style}
                  >
                    {inner}
                  </div>
                ) : (
                  <Link
                    key={bar.id}
                    href={bar.href}
                    title={tooltip}
                    className={`${base} ${BAR_STYLE[bar.status]} ${ring}`}
                    style={style}
                  >
                    {inner}
                  </Link>
                );
              })}
            </div>
          </div>
        ))}
        <div className="sr-only">{AXIS_TOTAL_MIN} minute axis</div>
      </div>
    </div>
  );
}
