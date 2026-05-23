import Link from 'next/link';
import type { PeriodView } from '@/lib/period';

const ALL_VIEWS: { key: PeriodView; label: string }[] = [
  { key: 'all', label: 'All' },
  { key: 'day', label: 'Day' },
  { key: 'week', label: 'Week' },
  { key: 'month', label: 'Month' },
];

export function PeriodTabs({
  basePath,
  view,
  date,
  rangeLabel,
  prevDate,
  nextDate,
  showAll = true,
}: {
  basePath: string;
  view: PeriodView;
  date: string;
  rangeLabel: string;
  prevDate: string;
  nextDate: string;
  showAll?: boolean;
}) {
  const views = showAll ? ALL_VIEWS : ALL_VIEWS.filter((v) => v.key !== 'all');
  const tab = (active: boolean) =>
    `rounded-md px-3 py-1 text-sm ${
      active ? 'bg-nbc text-white' : 'border border-slate-300 bg-white text-slate-700 hover:bg-slate-100'
    }`;

  return (
    <div className="mb-4 flex flex-wrap items-center justify-between gap-3">
      <div className="flex gap-1">
        {views.map((v) => (
          <Link key={v.key} href={`${basePath}?view=${v.key}&date=${date}`} className={tab(v.key === view)}>
            {v.label}
          </Link>
        ))}
      </div>
      {view !== 'all' && (
        <div className="flex items-center gap-2 text-sm">
          <Link className="btn-ghost" href={`${basePath}?view=${view}&date=${prevDate}`}>
            ←
          </Link>
          <span className="min-w-40 text-center text-slate-600">{rangeLabel}</span>
          <Link className="btn-ghost" href={`${basePath}?view=${view}&date=${nextDate}`}>
            →
          </Link>
        </div>
      )}
    </div>
  );
}
