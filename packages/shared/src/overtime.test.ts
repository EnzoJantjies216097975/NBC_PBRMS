import { describe, it, expect } from 'vitest';
import { computeOvertimeFromMinutes, computeProductionOverrun, dayCategory, formatMinutes } from './overtime';

// 2026-05-18 = Monday, 2026-05-23 = Saturday, 2026-05-24 = Sunday
const monday = new Date('2026-05-18T12:00:00');
const saturday = new Date('2026-05-23T12:00:00');
const sunday = new Date('2026-05-24T12:00:00');

describe('dayCategory', () => {
  it('classifies weekday / saturday / sunday', () => {
    expect(dayCategory(monday)).toBe('weekday');
    expect(dayCategory(saturday)).toBe('saturday');
    expect(dayCategory(sunday)).toBe('sunday');
  });
});

describe('computeOvertimeFromMinutes', () => {
  it('weekday: first 8h normal, rest overtime', () => {
    const r = computeOvertimeFromMinutes(monday, 600); // 10h
    expect(r.regularMinutes).toBe(480);
    expect(r.overtimeMinutes).toBe(120);
    expect(r.isOvertime).toBe(true);
  });

  it('weekday under 8h: no overtime', () => {
    expect(computeOvertimeFromMinutes(monday, 400).overtimeMinutes).toBe(0);
  });

  it('saturday: first 5h normal', () => {
    const r = computeOvertimeFromMinutes(saturday, 360); // 6h
    expect(r.regularMinutes).toBe(300);
    expect(r.overtimeMinutes).toBe(60);
  });

  it('sunday: all overtime', () => {
    const r = computeOvertimeFromMinutes(sunday, 120);
    expect(r.regularMinutes).toBe(0);
    expect(r.overtimeMinutes).toBe(120);
  });
});

describe('computeProductionOverrun', () => {
  const bookedEnd = new Date('2026-05-18T20:00:00');
  it('flags an overrun of >= 30 minutes', () => {
    expect(computeProductionOverrun(bookedEnd, new Date('2026-05-18T20:30:00')).isOvertime).toBe(true);
  });
  it('does not flag a short overrun', () => {
    expect(computeProductionOverrun(bookedEnd, new Date('2026-05-18T20:20:00')).isOvertime).toBe(false);
  });
});

describe('formatMinutes', () => {
  it('formats hours and minutes', () => {
    expect(formatMinutes(0)).toBe('0m');
    expect(formatMinutes(45)).toBe('45m');
    expect(formatMinutes(90)).toBe('1h 30m');
    expect(formatMinutes(120)).toBe('2h');
  });
});
