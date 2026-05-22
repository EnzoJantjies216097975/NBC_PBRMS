/**
 * Overtime engine.
 *
 * Rules (from spec):
 *   - Weekday (Mon–Fri): first 8h normal, beyond is overtime.
 *   - Saturday: first 5h normal, beyond is overtime.
 *   - Sunday: all hours are overtime.
 *   - A production that runs past its booked end by >= 30 min counts as overtime.
 *
 * All functions are pure so they can run on web, mobile, or in tests.
 */

import { OVERTIME_RULES } from './constants';

export type DayCategory = 'weekday' | 'saturday' | 'sunday';

export function dayCategory(date: Date): DayCategory {
  const d = date.getDay(); // 0 = Sunday … 6 = Saturday
  if (d === 0) return 'sunday';
  if (d === 6) return 'saturday';
  return 'weekday';
}

export function normalMinutesForDay(date: Date): number {
  switch (dayCategory(date)) {
    case 'sunday':
      return OVERTIME_RULES.sundayNormalMinutes;
    case 'saturday':
      return OVERTIME_RULES.saturdayNormalMinutes;
    default:
      return OVERTIME_RULES.weekdayNormalMinutes;
  }
}

export interface OvertimeResult {
  workedMinutes: number;
  regularMinutes: number;
  overtimeMinutes: number;
  isOvertime: boolean;
  dayCategory: DayCategory;
}

/** Split worked minutes into regular vs overtime for the given day. */
export function computeOvertimeFromMinutes(date: Date, workedMinutes: number): OvertimeResult {
  const worked = Math.max(0, Math.round(workedMinutes));
  const normal = normalMinutesForDay(date);
  const regularMinutes = Math.min(worked, normal);
  const overtimeMinutes = Math.max(0, worked - normal);
  return {
    workedMinutes: worked,
    regularMinutes,
    overtimeMinutes,
    isOvertime: overtimeMinutes > 0,
    dayCategory: dayCategory(date),
  };
}

/** Compute overtime from an actual start/end timestamp pair. */
export function computeOvertime(start: Date, end: Date): OvertimeResult {
  const workedMinutes = Math.max(0, Math.round((end.getTime() - start.getTime()) / 60_000));
  return computeOvertimeFromMinutes(start, workedMinutes);
}

export interface OverrunResult {
  minutesOver: number;
  isOvertime: boolean;
}

/**
 * Did a production run past its booked end time enough to count as overtime?
 * (spec: over the booked length by ~30–40 min.)
 */
export function computeProductionOverrun(bookedEnd: Date, actualEnd: Date): OverrunResult {
  const minutesOver = Math.max(0, Math.round((actualEnd.getTime() - bookedEnd.getTime()) / 60_000));
  return { minutesOver, isOvertime: minutesOver >= OVERTIME_RULES.productionOverrunGraceMinutes };
}

/** Format minutes as "8h 30m" / "45m" / "0m" for sheets and dashboards. */
export function formatMinutes(totalMinutes: number): string {
  const m = Math.max(0, Math.round(totalMinutes));
  const hours = Math.floor(m / 60);
  const mins = m % 60;
  if (hours === 0) return `${mins}m`;
  if (mins === 0) return `${hours}h`;
  return `${hours}h ${mins}m`;
}
