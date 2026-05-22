/** Pure aggregation for the management Reports & analytics page. */

import { computeOvertimeFromMinutes, type LocationType } from '@nbc/shared';

export interface ReportBooking {
  id: string;
  title: string;
  status: string;
  location_type: LocationType;
  call_date: string;
  call_time: string;
  end_time: string | null;
  crew: { profileId: string; name: string }[];
  logs: { start: string | null; end: string | null }[];
}

export interface OperatorOvertime {
  profileId: string;
  name: string;
  overtime: number;
}
export interface StudioUsage {
  location: LocationType;
  sessions: number;
  minutes: number;
}
export interface TripEntry {
  id: string;
  title: string;
  date: string;
  location: LocationType;
  crew: string[];
}
export interface ReportResult {
  overtimeByOperator: OperatorOvertime[];
  totalOvertime: number;
  studioUsage: StudioUsage[];
  cancellations: { id: string; title: string; date: string }[];
  trips: TripEntry[];
  underutilised: string[];
  totalSessions: number;
}

const TRIP_LOCATIONS = new Set<LocationType>(['on_location', 'ob_van', 'fly_away']);

function logMinutes(logs: ReportBooking['logs']): number {
  let total = 0;
  for (const l of logs) {
    if (l.start && l.end) {
      total += Math.max(0, Math.round((new Date(l.end).getTime() - new Date(l.start).getTime()) / 60_000));
    }
  }
  return total;
}

function scheduledMinutes(call: string, end: string | null): number {
  if (!end) return 0;
  const [h1, m1] = call.split(':').map(Number);
  const [h2, m2] = end.split(':').map(Number);
  let diff = (h2 ?? 0) * 60 + (m2 ?? 0) - ((h1 ?? 0) * 60 + (m1 ?? 0));
  if (diff < 0) diff += 24 * 60;
  return diff;
}

function computeOvertime(bookings: ReportBooking[]): { list: OperatorOvertime[]; total: number } {
  const dayWorked = new Map<string, number>(); // `${profileId}|${date}` -> minutes
  const names = new Map<string, string>();
  for (const b of bookings) {
    const worked = logMinutes(b.logs);
    if (worked <= 0) continue;
    for (const c of b.crew) {
      names.set(c.profileId, c.name);
      const key = `${c.profileId}|${b.call_date}`;
      dayWorked.set(key, (dayWorked.get(key) ?? 0) + worked);
    }
  }

  const perOperator = new Map<string, number>();
  for (const [key, mins] of dayWorked) {
    const sep = key.indexOf('|');
    const profileId = key.slice(0, sep);
    const date = key.slice(sep + 1);
    const ot = computeOvertimeFromMinutes(new Date(`${date}T12:00:00`), mins).overtimeMinutes;
    perOperator.set(profileId, (perOperator.get(profileId) ?? 0) + ot);
  }

  const list = [...perOperator.entries()]
    .map(([profileId, overtime]) => ({ profileId, name: names.get(profileId) ?? 'Operator', overtime }))
    .filter((o) => o.overtime > 0)
    .sort((a, b) => b.overtime - a.overtime);
  return { list, total: list.reduce((s, o) => s + o.overtime, 0) };
}

function computeStudioUsage(active: ReportBooking[]): StudioUsage[] {
  const usage = new Map<LocationType, { sessions: number; minutes: number }>();
  for (const b of active) {
    const u = usage.get(b.location_type) ?? { sessions: 0, minutes: 0 };
    u.sessions += 1;
    u.minutes += scheduledMinutes(b.call_time, b.end_time);
    usage.set(b.location_type, u);
  }
  return [...usage.entries()]
    .map(([location, u]) => ({ location, ...u }))
    .sort((a, b) => b.minutes - a.minutes);
}

export function buildReport(bookings: ReportBooking[], operators: { id: string; name: string }[]): ReportResult {
  const active = bookings.filter((b) => b.status !== 'cancelled' && b.status !== 'draft');

  const { list: overtimeByOperator, total: totalOvertime } = computeOvertime(bookings);

  const cancellations = bookings
    .filter((b) => b.status === 'cancelled')
    .map((b) => ({ id: b.id, title: b.title, date: b.call_date }));

  const trips: TripEntry[] = active
    .filter((b) => TRIP_LOCATIONS.has(b.location_type))
    .map((b) => ({ id: b.id, title: b.title, date: b.call_date, location: b.location_type, crew: b.crew.map((c) => c.name) }));

  const assigned = new Set<string>();
  for (const b of active) for (const c of b.crew) assigned.add(c.profileId);
  const underutilised = operators.filter((o) => !assigned.has(o.id)).map((o) => o.name);

  return {
    overtimeByOperator,
    totalOvertime,
    studioUsage: computeStudioUsage(active),
    cancellations,
    trips,
    underutilised,
    totalSessions: active.length,
  };
}
