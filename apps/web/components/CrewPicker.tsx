'use client';

import { useMemo, useState } from 'react';
import type { AvailabilityState } from '@nbc/shared';
import { assignCrew } from '@/app/(app)/supervisor/assign/[bookingId]/actions';

export interface CandidateOperator {
  id: string;
  name: string;
  phone: string | null;
  skills: string[];
  matchedSkills: string[];
  recommended: boolean;
  state: AvailabilityState;
  conflictTitle: string | null;
  inStudio: boolean;
}

export interface CrewGroup {
  name: string;
  deptId: string | null;
  count: number;
  operators: CandidateOperator[];
}

interface ExistingAssignment {
  profileId: string;
  roleLabel: string;
  deptId: string | null;
  needsCar: boolean;
  needsTransport: boolean;
}

interface SlotState {
  key: string;
  groupName: string;
  deptId: string | null;
  roleLabel: string;
  profileId: string;
  needsCar: boolean;
  needsTransport: boolean;
}

const STATE_LABEL: Record<AvailabilityState, string> = {
  available: 'available',
  due_soon: 'due soon',
  busy: 'busy',
};

const STATE_STYLE: Record<AvailabilityState, string> = {
  available: 'text-emerald-700',
  due_soon: 'text-amber-700',
  busy: 'text-red-700',
};

function prefixFor(o: CandidateOperator): string {
  if (o.recommended) return '✅ ';
  if (o.inStudio) return '★ ';
  return '';
}

function optionLabel(o: CandidateOperator): string {
  const bits = [`${prefixFor(o)}${o.name}`];
  if (o.matchedSkills.length) bits.push(`✓ ${o.matchedSkills.join('/')}`);
  else if (o.skills.length) bits.push(`[${o.skills.join(', ')}]`);
  bits.push(o.state === 'busy' && o.conflictTitle ? `busy: ${o.conflictTitle}` : STATE_LABEL[o.state]);
  return bits.join(' — ');
}

function availabilityText(o: CandidateOperator): string {
  if (o.state === 'busy' && o.conflictTitle) return `Clash — already on “${o.conflictTitle}”.`;
  if (o.state === 'due_soon' && o.conflictTitle) return `Free now, but due on “${o.conflictTitle}” soon.`;
  return 'Available';
}

export function CrewPicker({
  bookingId,
  groups,
  existing,
}: {
  bookingId: string;
  groups: CrewGroup[];
  existing: ExistingAssignment[];
}) {
  const opById = useMemo(() => {
    const m = new Map<string, CandidateOperator>();
    for (const g of groups) for (const o of g.operators) m.set(o.id, o);
    return m;
  }, [groups]);

  const [slots, setSlots] = useState<SlotState[]>(() =>
    groups.flatMap((g) =>
      Array.from({ length: g.count }, (_, i) => {
        const roleLabel = `${g.name} ${i + 1}`;
        const prior = existing.find((e) => e.roleLabel === roleLabel);
        return {
          key: roleLabel,
          groupName: g.name,
          deptId: g.deptId,
          roleLabel,
          profileId: prior?.profileId ?? '',
          needsCar: prior?.needsCar ?? false,
          needsTransport: prior?.needsTransport ?? false,
        };
      }),
    ),
  );

  const update = (key: string, patch: Partial<SlotState>) =>
    setSlots((prev) => prev.map((s) => (s.key === key ? { ...s, ...patch } : s)));

  const filled = slots.filter((s) => s.profileId);
  const selectedIds = filled.map((s) => s.profileId);
  const hasDuplicates = new Set(selectedIds).size !== selectedIds.length;
  const busySelected = filled.filter((s) => opById.get(s.profileId)?.state === 'busy');
  const [ack, setAck] = useState(false);

  const blocked = filled.length === 0 || hasDuplicates || (busySelected.length > 0 && !ack);
  const payload = JSON.stringify(
    filled.map((s) => ({
      profileId: s.profileId,
      roleLabel: s.roleLabel,
      deptId: s.deptId,
      needsCar: s.needsCar,
      needsTransport: s.needsTransport,
    })),
  );

  const groupKeys = groups.map((g) => g.name);

  return (
    <form action={assignCrew} className="space-y-5">
      <input type="hidden" name="booking_id" value={bookingId} />
      <input type="hidden" name="payload" value={payload} />
      <input type="hidden" name="force" value={busySelected.length > 0 && ack ? '1' : '0'} />

      {groupKeys.map((groupName) => {
        const group = groups.find((g) => g.name === groupName)!;
        return (
          <section key={groupName} className="card space-y-3">
            <h2 className="font-medium">{groupName}</h2>
            {slots
              .filter((s) => s.groupName === groupName)
              .map((s) => {
                const op = s.profileId ? opById.get(s.profileId) : undefined;
                return (
                  <div key={s.key} className="rounded-md border border-slate-200 p-3">
                    <label className="label">{s.roleLabel}</label>
                    <select
                      className="input"
                      value={s.profileId}
                      onChange={(e) => update(s.key, { profileId: e.target.value })}
                    >
                      <option value="">— Unassigned —</option>
                      {group.operators.map((o) => (
                        <option key={o.id} value={o.id}>
                          {optionLabel(o)}
                        </option>
                      ))}
                    </select>

                    {op && (
                      <p className={`mt-1 text-xs ${STATE_STYLE[op.state]}`}>
                        {availabilityText(op)}
                        {op.matchedSkills.length ? ` · matches: ${op.matchedSkills.join(', ')}` : ''}
                        {op.phone ? ` · ${op.phone}` : ''}
                        {op.inStudio ? ' · ★ in this studio today' : ''}
                      </p>
                    )}

                    <div className="mt-2 flex gap-6 text-sm">
                      <label className="flex items-center gap-2">
                        <input
                          type="checkbox"
                          checked={s.needsCar}
                          onChange={(e) => update(s.key, { needsCar: e.target.checked })}
                        />
                        Book a vehicle (going out)
                      </label>
                      <label className="flex items-center gap-2">
                        <input
                          type="checkbox"
                          checked={s.needsTransport}
                          onChange={(e) => update(s.key, { needsTransport: e.target.checked })}
                        />
                        Needs pickup
                      </label>
                    </div>
                  </div>
                );
              })}
          </section>
        );
      })}

      {hasDuplicates && (
        <p className="rounded-md bg-red-50 px-3 py-2 text-sm text-red-700">
          The same operator is selected in more than one slot.
        </p>
      )}

      {busySelected.length > 0 && (
        <label className="flex items-start gap-2 rounded-md bg-amber-50 px-3 py-2 text-sm text-amber-900">
          <input type="checkbox" checked={ack} onChange={(e) => setAck(e.target.checked)} className="mt-1" />
          <span>
            {busySelected.length} selected operator(s) have a clashing booking. I&apos;ve checked with
            them and want to assign anyway (this is logged).
          </span>
        </label>
      )}

      <div className="flex items-center gap-3">
        <button className="btn-primary" type="submit" disabled={blocked}>
          Assign crew &amp; notify
        </button>
        <span className="text-xs text-slate-500">
          ✅ recommended · ★ in this studio today · ✓ has requested gear. Conflicts must be
          acknowledged before assigning.
        </span>
      </div>
    </form>
  );
}
