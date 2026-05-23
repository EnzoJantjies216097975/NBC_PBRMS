import { describe, it, expect } from 'vitest';
import { classifyAvailability, findCrewConflicts, intervalsOverlap, toInterval } from './conflicts';

describe('intervalsOverlap', () => {
  it('detects overlapping intervals', () => {
    const a = toInterval('2026-05-18', '19:00', '20:00');
    const b = toInterval('2026-05-18', '19:30', '20:30');
    expect(intervalsOverlap(a, b)).toBe(true);
  });
  it('treats adjacent intervals as non-overlapping', () => {
    const a = toInterval('2026-05-18', '19:00', '20:00');
    const b = toInterval('2026-05-18', '20:00', '21:00');
    expect(intervalsOverlap(a, b)).toBe(false);
  });
  it('handles a window that crosses midnight', () => {
    const a = toInterval('2026-05-18', '23:00', '01:00');
    const b = toInterval('2026-05-18', '23:30', '23:45');
    expect(intervalsOverlap(a, b)).toBe(true);
  });
});

describe('findCrewConflicts', () => {
  const busy = [{ bookingId: 'b1', title: 'Show A', date: '2026-05-18', start: '19:00', end: '20:00' }];

  it('returns the overlapping booking', () => {
    const conflicts = findCrewConflicts(
      { profileId: 'p', date: '2026-05-18', start: '19:30', end: '21:00' },
      busy,
    );
    expect(conflicts).toHaveLength(1);
    expect(conflicts[0]!.title).toBe('Show A');
  });

  it('returns nothing when separate', () => {
    const conflicts = findCrewConflicts(
      { profileId: 'p', date: '2026-05-18', start: '21:00', end: '22:00' },
      busy,
    );
    expect(conflicts).toHaveLength(0);
  });
});

describe('classifyAvailability', () => {
  it('busy when overlapping', () => {
    const busy = [{ bookingId: 'b1', title: 'A', date: '2026-05-18', start: '19:00', end: '20:00' }];
    const r = classifyAvailability({ profileId: 'p', date: '2026-05-18', start: '19:30', end: '20:30' }, busy);
    expect(r.state).toBe('busy');
  });

  it('due_soon when a booking starts within the window', () => {
    const busy = [{ bookingId: 'b1', title: 'A', date: '2026-05-18', start: '13:00', end: '14:00' }];
    const r = classifyAvailability({ profileId: 'p', date: '2026-05-18', start: '11:30', end: '12:00' }, busy, 120);
    expect(r.state).toBe('due_soon');
    expect(r.nextUpcoming?.title).toBe('A');
  });

  it('available when free', () => {
    const r = classifyAvailability({ profileId: 'p', date: '2026-05-18', start: '06:00', end: '07:00' }, []);
    expect(r.state).toBe('available');
  });
});
