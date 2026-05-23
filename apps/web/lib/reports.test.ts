import { describe, it, expect } from 'vitest';
import { buildReport, type ReportBooking } from './reports';

const monday = '2026-05-18';

function booking(over: Partial<ReportBooking> = {}): ReportBooking {
  return {
    id: 'b',
    title: 'T',
    status: 'completed',
    location_type: 'studio_2',
    call_date: monday,
    call_time: '08:00',
    end_time: '17:00',
    crew: [],
    logs: [],
    ...over,
  };
}

describe('buildReport', () => {
  it('aggregates overtime per operator using the daily 8h rule', () => {
    const b = booking({
      crew: [{ profileId: 'p1', name: 'Op One' }],
      logs: [{ start: `${monday}T08:00:00`, end: `${monday}T18:00:00` }], // 10h
    });
    const r = buildReport([b], [
      { id: 'p1', name: 'Op One' },
      { id: 'p2', name: 'Op Two' },
    ]);
    expect(r.overtimeByOperator[0]).toMatchObject({ name: 'Op One', overtime: 120 });
    expect(r.totalOvertime).toBe(120);
  });

  it('counts studio usage and flags underutilised operators', () => {
    const b = booking({ crew: [{ profileId: 'p1', name: 'Op One' }] });
    const r = buildReport([b], [
      { id: 'p1', name: 'Op One' },
      { id: 'p2', name: 'Op Two' },
    ]);
    expect(r.studioUsage.find((s) => s.location === 'studio_2')?.sessions).toBe(1);
    expect(r.underutilised).toContain('Op Two');
    expect(r.underutilised).not.toContain('Op One');
  });

  it('lists trips and cancellations separately', () => {
    const trip = booking({ id: 't', location_type: 'on_location', crew: [{ profileId: 'p1', name: 'Op One' }] });
    const cancelled = booking({ id: 'c', status: 'cancelled' });
    const r = buildReport([trip, cancelled], []);
    expect(r.trips).toHaveLength(1);
    expect(r.trips[0]!.crew).toContain('Op One');
    expect(r.cancellations).toHaveLength(1);
    expect(r.totalSessions).toBe(1); // cancelled excluded from active
  });
});
