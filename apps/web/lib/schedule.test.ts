import { describe, it, expect } from 'vitest';
import { barGeometry, buildDaySchedule, timeToMinutes, type ScheduleBooking } from './schedule';

function bk(over: Partial<ScheduleBooking>): ScheduleBooking {
  return {
    id: 'b',
    title: 'T',
    status: 'confirmed',
    location_type: 'studio_2',
    call_time: '10:00',
    end_time: '12:00',
    air_end: null,
    crew: [],
    ...over,
  };
}

describe('timeToMinutes', () => {
  it('parses HH:MM and handles null', () => {
    expect(timeToMinutes('05:30')).toBe(330);
    expect(timeToMinutes(null)).toBeNull();
  });
});

describe('buildDaySchedule', () => {
  it('flags two productions overlapping in the same studio', () => {
    const a = bk({ id: 'a', call_time: '10:00', end_time: '12:00' });
    const b = bk({ id: 'b', call_time: '11:00', end_time: '13:00' });
    const r = buildDaySchedule([a, b]);
    expect(r.clashMessages.length).toBeGreaterThan(0);
    expect(r.clashIds.has('a')).toBe(true);
    expect(r.clashIds.has('b')).toBe(true);
  });

  it('flags an operator double-booked across different studios', () => {
    const a = bk({ id: 'a', location_type: 'studio_1', call_time: '10:00', end_time: '12:00', crew: [{ profileId: 'p', name: 'P' }] });
    const b = bk({ id: 'b', location_type: 'studio_2', call_time: '11:00', end_time: '13:00', crew: [{ profileId: 'p', name: 'P' }] });
    const r = buildDaySchedule([a, b]);
    expect(r.clashIds.has('a')).toBe(true);
    expect(r.clashIds.has('b')).toBe(true);
  });

  it('does not flag a clash for non-overlapping bookings', () => {
    const a = bk({ id: 'a', call_time: '08:00', end_time: '09:00' });
    const b = bk({ id: 'b', call_time: '10:00', end_time: '11:00' });
    expect(buildDaySchedule([a, b]).clashMessages).toHaveLength(0);
  });

  it('does not treat the Location/Other row as a studio clash', () => {
    const a = bk({ id: 'a', location_type: 'on_location', call_time: '10:00', end_time: '14:00' });
    const b = bk({ id: 'b', location_type: 'fly_away', call_time: '11:00', end_time: '13:00' });
    expect(buildDaySchedule([a, b]).clashMessages).toHaveLength(0);
  });
});

describe('barGeometry', () => {
  it('positions a block at the start of the axis', () => {
    const g = barGeometry(5 * 60, 6 * 60); // 05:00–06:00, axis starts at 05:00
    expect(g.left).toBe(0);
    expect(g.width).toBeGreaterThan(0);
  });
});
