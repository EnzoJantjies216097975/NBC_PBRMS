import { BOOKING_STATUS_LABELS, type BookingStatus } from '@nbc/shared';

const COLORS: Record<BookingStatus, string> = {
  draft: 'bg-slate-100 text-slate-600',
  submitted: 'bg-amber-100 text-amber-800',
  ep_changes_requested: 'bg-orange-100 text-orange-800',
  ep_approved: 'bg-sky-100 text-sky-800',
  supervisor_changes_requested: 'bg-orange-100 text-orange-800',
  crew_assigned: 'bg-indigo-100 text-indigo-800',
  confirmed: 'bg-emerald-100 text-emerald-800',
  in_progress: 'bg-blue-100 text-blue-800',
  completed: 'bg-slate-200 text-slate-700',
  cancelled: 'bg-red-100 text-red-700',
};

export function StatusBadge({ status }: { status: BookingStatus }) {
  return (
    <span className={`inline-block rounded-full px-2.5 py-0.5 text-xs font-medium ${COLORS[status]}`}>
      {BOOKING_STATUS_LABELS[status]}
    </span>
  );
}
