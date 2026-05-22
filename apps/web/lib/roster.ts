/** Calendar helpers for the monthly roster grid (operators × days). */

export interface MonthDay {
  date: string; // YYYY-MM-DD
  day: number; // 1..31
  weekday: number; // 0=Sun … 6=Sat
  weekdayShort: string; // Su, Mo, …
  isWeekend: boolean;
}

const WEEKDAY_SHORT = ['Su', 'Mo', 'Tu', 'We', 'Th', 'Fr', 'Sa'];

export function monthDays(year: number, month: number): MonthDay[] {
  const count = new Date(year, month, 0).getDate(); // month is 1-based here
  const days: MonthDay[] = [];
  for (let d = 1; d <= count; d++) {
    const dt = new Date(year, month - 1, d);
    const weekday = dt.getDay();
    days.push({
      date: `${year}-${String(month).padStart(2, '0')}-${String(d).padStart(2, '0')}`,
      day: d,
      weekday,
      weekdayShort: WEEKDAY_SHORT[weekday]!,
      isWeekend: weekday === 0 || weekday === 6,
    });
  }
  return days;
}

export function parseMonth(s: string | undefined): { year: number; month: number } {
  if (s && /^\d{4}-\d{2}$/.test(s)) {
    const [year, month] = s.split('-').map(Number);
    return { year: year!, month: month! };
  }
  const now = new Date();
  return { year: now.getFullYear(), month: now.getMonth() + 1 };
}

export function monthParam(year: number, month: number): string {
  return `${year}-${String(month).padStart(2, '0')}`;
}

export function addMonths(year: number, month: number, delta: number): { year: number; month: number } {
  const d = new Date(year, month - 1 + delta, 1);
  return { year: d.getFullYear(), month: d.getMonth() + 1 };
}

export function monthLabel(year: number, month: number): string {
  return new Date(year, month - 1, 1).toLocaleDateString('en-GB', { month: 'long', year: 'numeric' });
}

/** key for an assignment cell */
export function cellKey(profileId: string, date: string): string {
  return `${profileId}|${date}`;
}
