/**
 * Conflict detection & availability classification.
 *
 * Used by Supervisors / Booking Officers when assigning crew so that an operator
 * already booked in an overlapping window is flagged (spec: "marked in a
 * different colour" + "warn which production they are already booked on").
 * Bookings cannot be confirmed while a hard conflict exists.
 */

export interface TimeInterval {
  start: Date;
  end: Date;
}

/** Half-open overlap test: [aStart,aEnd) intersects [bStart,bEnd). */
export function intervalsOverlap(a: TimeInterval, b: TimeInterval): boolean {
  return a.start < b.end && b.start < a.end;
}

/**
 * Build an interval from an ISO date ("YYYY-MM-DD") and 24h times ("HH:MM").
 * If end <= start the block is treated as crossing midnight (end next day).
 */
export function toInterval(date: string, start: string, end: string): TimeInterval {
  const startAt = new Date(`${date}T${start}:00`);
  let endAt = new Date(`${date}T${end}:00`);
  if (endAt.getTime() <= startAt.getTime()) {
    endAt = new Date(endAt.getTime() + 24 * 60 * 60_000);
  }
  return { start: startAt, end: endAt };
}

/** A window during which an operator is already committed. */
export interface CrewBusyBlock {
  bookingId: string;
  title: string;
  date: string; // YYYY-MM-DD
  start: string; // HH:MM
  end: string; // HH:MM
  status?: string;
}

export interface ConflictCandidate {
  profileId: string;
  date: string;
  start: string;
  end: string;
}

/** Return every existing block that overlaps the candidate window. */
export function findCrewConflicts(
  candidate: ConflictCandidate,
  busy: CrewBusyBlock[],
): CrewBusyBlock[] {
  const cand = toInterval(candidate.date, candidate.start, candidate.end);
  return busy.filter((b) => intervalsOverlap(cand, toInterval(b.date, b.start, b.end)));
}

export type AvailabilityState = 'available' | 'due_soon' | 'busy';

export interface AvailabilityResult {
  state: AvailabilityState;
  conflicts: CrewBusyBlock[];
  /** the next upcoming block on the same day, if any (drives "due_soon") */
  nextUpcoming?: CrewBusyBlock;
}

/**
 * Classify an operator for a candidate slot:
 *  - `busy`      → overlapping booking exists (hard conflict).
 *  - `due_soon`  → free now but has a booking starting within `dueSoonMinutes`.
 *  - `available` → otherwise free.
 */
export function classifyAvailability(
  candidate: ConflictCandidate,
  busy: CrewBusyBlock[],
  dueSoonMinutes = 120,
): AvailabilityResult {
  const conflicts = findCrewConflicts(candidate, busy);
  if (conflicts.length > 0) {
    return { state: 'busy', conflicts };
  }

  const cand = toInterval(candidate.date, candidate.start, candidate.end);
  const upcoming = busy
    .map((b) => ({ block: b, interval: toInterval(b.date, b.start, b.end) }))
    .filter((x) => x.interval.start.getTime() >= cand.end.getTime())
    .sort((a, b) => a.interval.start.getTime() - b.interval.start.getTime());

  const next = upcoming[0];
  if (next) {
    const gapMinutes = (next.interval.start.getTime() - cand.end.getTime()) / 60_000;
    if (gapMinutes <= dueSoonMinutes) {
      return { state: 'due_soon', conflicts: [], nextUpcoming: next.block };
    }
  }
  return { state: 'available', conflicts: [] };
}
