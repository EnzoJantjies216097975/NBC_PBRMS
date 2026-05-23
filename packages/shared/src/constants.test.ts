import { describe, it, expect } from 'vitest';
import { isoWeekday, recurringProductionsForDate } from './constants';

describe('isoWeekday', () => {
  it('returns Mon=1 … Sun=7', () => {
    expect(isoWeekday('2026-05-18')).toBe(1); // Monday
    expect(isoWeekday('2026-05-23')).toBe(6); // Saturday
    expect(isoWeekday('2026-05-24')).toBe(7); // Sunday
  });
});

describe('recurringProductionsForDate', () => {
  it('includes weekday dailies + Monday flagship', () => {
    const names = recurringProductionsForDate('2026-05-18').map((p) => p.name);
    expect(names).toContain('Good Morning Namibia');
    expect(names).toContain('Talk of the Nation');
  });

  it('excludes weekday-only shows on Sunday, includes Sunday shows', () => {
    const names = recurringProductionsForDate('2026-05-24').map((p) => p.name);
    expect(names).not.toContain('Good Morning Namibia');
    expect(names).toContain('Wheels of Justice');
  });
});
