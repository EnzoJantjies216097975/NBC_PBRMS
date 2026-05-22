/**
 * Operator recommendation scoring.
 *
 * Spec: when crewing, recommend operators who are available, who are already in
 * that studio that day, or who have the specific skill/equipment requested
 * (steadicam, drone, gimbal…). Higher score = stronger recommendation.
 *
 * Pure so it can run on web, mobile, or in tests.
 */

import type { AvailabilityState } from './conflicts';

export interface RankableOperator {
  state: AvailabilityState;
  inStudio: boolean;
  matchedSkillCount: number;
}

const STATE_SCORE: Record<AvailabilityState, number> = {
  available: 100,
  due_soon: 50,
  busy: 0,
};

export function operatorScore(o: RankableOperator): number {
  return STATE_SCORE[o.state] + (o.inStudio ? 30 : 0) + o.matchedSkillCount * 40;
}

/** Skill names the operator has that satisfy the requested gear (case-insensitive). */
export function matchedSkillNames(requestedLabels: string[], operatorSkills: string[]): string[] {
  if (requestedLabels.length === 0 || operatorSkills.length === 0) return [];
  const want = new Set(requestedLabels.map((s) => s.trim().toLowerCase()));
  return operatorSkills.filter((s) => want.has(s.trim().toLowerCase()));
}
