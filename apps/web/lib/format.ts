/** Small formatting helpers for dates/times coming back from Postgres. */

export function fmtDate(d: string | null | undefined): string {
  if (!d) return '—';
  const date = new Date(`${d}T00:00:00`);
  return date.toLocaleDateString('en-GB', { day: '2-digit', month: 'short', year: 'numeric' });
}

export function fmtTime(t: string | null | undefined): string {
  if (!t) return '—';
  return t.slice(0, 5); // HH:MM
}

export function fmtTimestamp(iso: string | null | undefined): string {
  if (!iso) return '—';
  return new Date(iso).toLocaleString('en-GB', {
    day: '2-digit',
    month: 'short',
    hour: '2-digit',
    minute: '2-digit',
  });
}

export function fmtDateTimeRange(
  date: string | null,
  start: string | null,
  end: string | null,
): string {
  if (!date) return '—';
  const range = [fmtTime(start), end ? fmtTime(end) : null].filter(Boolean).join('–');
  const suffix = range ? ` · ${range}` : '';
  return `${fmtDate(date)}${suffix}`;
}
