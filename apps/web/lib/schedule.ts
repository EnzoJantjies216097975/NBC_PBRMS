/** Geometry + helpers for the Booking Officer's daily studio-row calendar. */

import type { BookingStatus, LocationType } from '@nbc/shared';

export const AXIS_START_HOUR = 5; // 05:00
export const AXIS_END_HOUR = 24; // midnight
export const AXIS_TOTAL_MIN = (AXIS_END_HOUR - AXIS_START_HOUR) * 60;

export const HOUR_MARKS = Array.from(
  { length: AXIS_END_HOUR - AXIS_START_HOUR + 1 },
  (_, i) => AXIS_START_HOUR + i,
);

/** "HH:MM[:SS]" → minutes since midnight (null if missing). */
export function timeToMinutes(t: string | null | undefined): number | null {
  if (!t) return null;
  const [h, m] = t.split(':').map(Number);
  return (h ?? 0) * 60 + (m ?? 0);
}

/** left%/width% of a [startMin,endMin] block within the visible axis. */
export function barGeometry(startMin: number, endMin: number): { left: number; width: number } {
  const axisStart = AXIS_START_HOUR * 60;
  const axisEnd = AXIS_END_HOUR * 60;
  const start = Math.max(startMin, axisStart);
  const end = Math.min(Math.max(endMin, start + 1), axisEnd);
  const left = ((start - axisStart) / AXIS_TOTAL_MIN) * 100;
  const width = Math.max(((end - start) / AXIS_TOTAL_MIN) * 100, 1.5);
  return { left, width };
}

export function overlaps(aStart: number, aEnd: number, bStart: number, bEnd: number): boolean {
  return aStart < bEnd && bStart < aEnd;
}

/** Shift an ISO date string (YYYY-MM-DD) by n days. */
export function addDays(dateStr: string, n: number): string {
  const d = new Date(`${dateStr}T00:00:00`);
  d.setDate(d.getDate() + n);
  return d.toISOString().slice(0, 10);
}

export function todayIso(): string {
  return new Date().toISOString().slice(0, 10);
}

/**
 * Physical rows on the calendar. Studios + APP + OB clash on overlap; everything
 * else (location / fly-away / streaming) shares an "Other" row where overlapping
 * entries are different venues, so they are NOT treated as clashes.
 */
export const CALENDAR_ROWS: { key: string; label: string; clashable: boolean }[] = [
  { key: 'studio_1', label: 'Studio 1', clashable: true },
  { key: 'studio_2', label: 'Studio 2', clashable: true },
  { key: 'studio_3', label: 'Studio 3', clashable: true },
  { key: 'studio_4', label: 'Studio 4', clashable: true },
  { key: 'audio_post', label: 'APP', clashable: true },
  { key: 'ob_van', label: 'OB Van', clashable: true },
  { key: 'other', label: 'Location / Other', clashable: false },
];

const STUDIO_KEYS = new Set(['studio_1', 'studio_2', 'studio_3', 'studio_4', 'audio_post', 'ob_van']);

export function rowKeyForLocation(loc: LocationType): string {
  return STUDIO_KEYS.has(loc) ? loc : 'other';
}

// ---------------------------------------------------------------------------
// Day-schedule builder — pure: turns a day's bookings into calendar rows +
// clash diagnostics. Kept here so the page stays thin and this stays testable.
// ---------------------------------------------------------------------------

export interface ScheduleBooking {
  id: string;
  title: string;
  status: BookingStatus;
  location_type: LocationType;
  call_time: string;
  end_time: string | null;
  air_end: string | null;
  crew: { profileId: string; name: string }[];
  /** standing/recurring show derived from the catalog (not a real booking row) */
  standing?: boolean;
}

export interface ScheduleBar {
  id: string;
  title: string;
  subtitle: string;
  leftPct: number;
  widthPct: number;
  status: BookingStatus;
  clash: boolean;
  standing: boolean;
}

export interface ScheduleRow {
  key: string;
  label: string;
  bars: ScheduleBar[];
}

export interface DaySchedule {
  rows: ScheduleRow[];
  clashMessages: string[];
  clashIds: Set<string>;
  bookedProfileIds: Set<string>;
}

interface Window {
  start: number;
  end: number;
  rowKey: string;
}

interface DayItem {
  b: ScheduleBooking;
  w: Window;
}

function resolveWindow(b: ScheduleBooking): Window {
  const start = timeToMinutes(b.call_time) ?? 0;
  const endRaw = timeToMinutes(b.end_time) ?? timeToMinutes(b.air_end) ?? start + 60;
  return { start, end: endRaw <= start ? start + 60 : endRaw, rowKey: rowKeyForLocation(b.location_type) };
}

function hhmm(t: string | null): string {
  return t ? t.slice(0, 5) : '';
}

/** Invoke `cb` for every pair in `arr` whose windows overlap. */
function eachOverlappingPair<T>(arr: T[], win: (x: T) => Window, cb: (a: T, b: T) => void): void {
  for (let i = 0; i < arr.length; i++) {
    const a = arr[i];
    if (!a) continue;
    const aw = win(a);
    for (let j = i + 1; j < arr.length; j++) {
      const b = arr[j];
      if (b && overlaps(aw.start, aw.end, win(b).start, win(b).end)) cb(a, b);
    }
  }
}

function detectStudioClashes(items: DayItem[], clashIds: Set<string>, messages: string[]): void {
  for (const row of CALENDAR_ROWS.filter((r) => r.clashable)) {
    const inRow = items.filter((it) => it.w.rowKey === row.key);
    eachOverlappingPair(
      inRow,
      (it) => it.w,
      (a, c) => {
        clashIds.add(a.b.id);
        clashIds.add(c.b.id);
        messages.push(`${row.label}: "${a.b.title}" overlaps "${c.b.title}"`);
      },
    );
  }
}

function detectOperatorClashes(items: DayItem[], clashIds: Set<string>, messages: string[]): Set<string> {
  const byOperator = new Map<string, { name: string; item: DayItem }[]>();
  for (const it of items) {
    for (const c of it.b.crew) {
      const list = byOperator.get(c.profileId) ?? [];
      list.push({ name: c.name, item: it });
      byOperator.set(c.profileId, list);
    }
  }
  for (const entries of byOperator.values()) {
    eachOverlappingPair(
      entries,
      (e) => e.item.w,
      (a, c) => {
        clashIds.add(a.item.b.id);
        clashIds.add(c.item.b.id);
        messages.push(`${a.name} double-booked: "${a.item.b.title}" & "${c.item.b.title}"`);
      },
    );
  }
  return new Set(byOperator.keys());
}

export function buildDaySchedule(bookings: ScheduleBooking[]): DaySchedule {
  const items: DayItem[] = bookings.map((b) => ({ b, w: resolveWindow(b) }));

  const clashIds = new Set<string>();
  const clashMessages: string[] = [];
  detectStudioClashes(items, clashIds, clashMessages);
  const bookedProfileIds = detectOperatorClashes(items, clashIds, clashMessages);

  const rows: ScheduleRow[] = CALENDAR_ROWS.map((rowDef) => ({
    key: rowDef.key,
    label: rowDef.label,
    bars: items
      .filter((it) => it.w.rowKey === rowDef.key)
      .map(({ b, w }) => {
        const { left, width } = barGeometry(w.start, w.end);
        return {
          id: b.id,
          title: b.title,
          subtitle: `${hhmm(b.call_time)}–${hhmm(b.end_time ?? b.air_end)}`,
          leftPct: left,
          widthPct: width,
          status: b.status,
          clash: clashIds.has(b.id),
          standing: b.standing ?? false,
        };
      }),
  }));

  return { rows, clashMessages, clashIds, bookedProfileIds };
}
