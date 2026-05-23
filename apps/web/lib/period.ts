/** Day / Week / Month period helpers for the producer + operator schedule views. */

import { fmtDate } from './format';
import { addDays } from './schedule';
import { addMonths, monthLabel } from './roster';

export type PeriodView = 'all' | 'day' | 'week' | 'month';

export function parseView(s: string | undefined, fallback: PeriodView): PeriodView {
  return s === 'all' || s === 'day' || s === 'week' || s === 'month' ? s : fallback;
}

/** Monday-of-week for the given ISO date. */
function weekStart(dateISO: string): string {
  const day = new Date(`${dateISO}T00:00:00`).getDay(); // 0 Sun … 6 Sat
  return addDays(dateISO, day === 0 ? -6 : 1 - day);
}

export interface PeriodRange {
  start: string | null; // null = no date filter (all)
  end: string | null;
  label: string;
}

export function periodRange(view: PeriodView, dateISO: string): PeriodRange {
  if (view === 'all') return { start: null, end: null, label: 'All productions' };
  if (view === 'day') return { start: dateISO, end: dateISO, label: fmtDate(dateISO) };
  if (view === 'week') {
    const start = weekStart(dateISO);
    const end = addDays(start, 6);
    return { start, end, label: `${fmtDate(start)} – ${fmtDate(end)}` };
  }
  const d = new Date(`${dateISO}T00:00:00`);
  const year = d.getFullYear();
  const month = d.getMonth() + 1;
  const mm = String(month).padStart(2, '0');
  const lastDay = new Date(year, month, 0).getDate();
  return {
    start: `${year}-${mm}-01`,
    end: `${year}-${mm}-${String(lastDay).padStart(2, '0')}`,
    label: monthLabel(year, month),
  };
}

/** Shift the anchor date by one period in the given direction. */
export function shiftPeriod(view: PeriodView, dateISO: string, dir: 1 | -1): string {
  if (view === 'day') return addDays(dateISO, dir);
  if (view === 'week') return addDays(dateISO, dir * 7);
  if (view === 'month') {
    const d = new Date(`${dateISO}T00:00:00`);
    const r = addMonths(d.getFullYear(), d.getMonth() + 1, dir);
    return `${r.year}-${String(r.month).padStart(2, '0')}-01`;
  }
  return dateISO;
}
