import { describe, it, expect } from 'vitest';
import { matchedSkillNames, operatorScore } from './recommend';

describe('operatorScore', () => {
  it('ranks available > due_soon > busy', () => {
    const available = operatorScore({ state: 'available', inStudio: false, matchedSkillCount: 0 });
    const dueSoon = operatorScore({ state: 'due_soon', inStudio: false, matchedSkillCount: 0 });
    const busy = operatorScore({ state: 'busy', inStudio: false, matchedSkillCount: 0 });
    expect(available).toBeGreaterThan(dueSoon);
    expect(dueSoon).toBeGreaterThan(busy);
  });

  it('boosts in-studio operators', () => {
    expect(operatorScore({ state: 'available', inStudio: true, matchedSkillCount: 0 })).toBeGreaterThan(
      operatorScore({ state: 'available', inStudio: false, matchedSkillCount: 0 }),
    );
  });

  it('weights skill matches highly', () => {
    expect(operatorScore({ state: 'available', inStudio: false, matchedSkillCount: 2 })).toBeGreaterThan(
      operatorScore({ state: 'available', inStudio: true, matchedSkillCount: 0 }),
    );
  });
});

describe('matchedSkillNames', () => {
  it('matches case-insensitively', () => {
    expect(matchedSkillNames(['Drone', 'Steadicam'], ['drone', 'Gimbal'])).toEqual(['drone']);
  });
  it('returns empty when there is nothing to match', () => {
    expect(matchedSkillNames([], ['Drone'])).toEqual([]);
    expect(matchedSkillNames(['Drone'], [])).toEqual([]);
  });
});
