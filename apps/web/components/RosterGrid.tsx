'use client';

import { useMemo, useState } from 'react';
import { SHIFTS_BY_CODE, SHIFT_CODES, type ShiftCode } from '@nbc/shared';
import { cellKey, type MonthDay } from '@/lib/roster';
import { saveRoster } from '@/app/(app)/supervisor/roster/actions';

interface RosterGridProps {
  rosterPeriodId: string;
  month: string;
  editable: boolean;
  operators: { id: string; name: string }[];
  days: MonthDay[];
  initial: Record<string, string>;
  highlightProfileId?: string;
}

function cellClass(code: string, isWeekend: boolean): string {
  const def = code ? SHIFTS_BY_CODE[code as ShiftCode] : undefined;
  if (def && !def.isWorking) return 'bg-amber-100 text-amber-900';
  if (def?.asPerBooking) return 'bg-sky-50 text-sky-900';
  if (code) return 'bg-white';
  return isWeekend ? 'bg-slate-50' : 'bg-white';
}

export function RosterGrid({
  rosterPeriodId,
  month,
  editable,
  operators,
  days,
  initial,
  highlightProfileId,
}: RosterGridProps) {
  const [cells, setCells] = useState<Record<string, string>>(initial);

  const set = (key: string, value: string) => setCells((prev) => ({ ...prev, [key]: value }));

  const payload = useMemo(() => {
    const out: { profileId: string; workDate: string; shiftCode: string | null }[] = [];
    for (const [key, value] of Object.entries(cells)) {
      if (value === (initial[key] ?? '')) continue;
      const sep = key.indexOf('|');
      out.push({ profileId: key.slice(0, sep), workDate: key.slice(sep + 1), shiftCode: value || null });
    }
    return out;
  }, [cells, initial]);

  const grid = (
    <div className="overflow-x-auto rounded-lg border border-slate-200 bg-white">
      <table className="border-collapse text-xs">
        <thead>
          <tr>
            <th className="sticky left-0 z-10 border-b border-r border-slate-200 bg-slate-50 px-3 py-2 text-left font-medium text-slate-600">
              Operator
            </th>
            {days.map((d) => (
              <th
                key={d.date}
                className={`min-w-9 border-b border-slate-200 px-1 py-1 text-center font-medium ${
                  d.isWeekend ? 'bg-slate-100 text-slate-500' : 'bg-slate-50 text-slate-600'
                }`}
              >
                <div>{d.day}</div>
                <div className="text-[9px] font-normal">{d.weekdayShort}</div>
              </th>
            ))}
          </tr>
        </thead>
        <tbody>
          {operators.map((op) => (
            <tr key={op.id} className={op.id === highlightProfileId ? 'bg-nbc/5' : ''}>
              <td className="sticky left-0 z-10 border-b border-r border-slate-200 bg-white px-3 py-1 font-medium text-slate-800">
                {op.name}
              </td>
              {days.map((d) => {
                const key = cellKey(op.id, d.date);
                const value = cells[key] ?? '';
                return (
                  <td key={key} className={`border-b border-r border-slate-100 p-0 text-center ${cellClass(value, d.isWeekend)}`}>
                    {editable ? (
                      <select
                        aria-label={`${op.name} ${d.date}`}
                        value={value}
                        onChange={(e) => set(key, e.target.value)}
                        className="w-11 bg-transparent px-0.5 py-1 text-center text-[11px] outline-none"
                      >
                        <option value=""></option>
                        {SHIFT_CODES.map((c) => (
                          <option key={c} value={c}>
                            {c}
                          </option>
                        ))}
                      </select>
                    ) : (
                      <span className="block px-0.5 py-1 text-[11px]">{value}</span>
                    )}
                  </td>
                );
              })}
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );

  if (!editable) return grid;

  return (
    <form action={saveRoster} className="space-y-3">
      <input type="hidden" name="roster_period_id" value={rosterPeriodId} />
      <input type="hidden" name="month" value={month} />
      <input type="hidden" name="payload" value={JSON.stringify(payload)} />
      {grid}
      <div className="flex items-center gap-3">
        <button className="btn-primary" type="submit" disabled={payload.length === 0}>
          Save changes
        </button>
        <span className="text-xs text-slate-500">{payload.length} cell(s) changed</span>
      </div>
    </form>
  );
}
