import { requireRole } from '@/lib/auth';
import { Placeholder } from '@/components/Placeholder';

export default async function ReportsPage() {
  await requireRole(['supervisor', 'booking_officer', 'manager', 'admin', 'executive_producer']);
  return (
    <Placeholder title="Reports & Analytics">
      Printable reports — overtime per person &amp; total, studio usage, cancellations, trips
      fairness, and underutilisation flags — are scaffolded in the data model and arrive here in a
      later phase.
    </Placeholder>
  );
}
