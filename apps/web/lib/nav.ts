import type { UserRole } from '@nbc/shared';

export interface NavItem {
  href: string;
  label: string;
}

/** Sidebar navigation per role. Extend as new modules land. */
export const NAV_BY_ROLE: Record<UserRole, NavItem[]> = {
  producer: [
    { href: '/producer', label: 'My Productions' },
    { href: '/producer/new', label: 'New Booking' },
  ],
  executive_producer: [
    { href: '/exec-producer', label: 'Incoming Bookings' },
    { href: '/exec-producer/calendar', label: 'Department Calendar' },
    { href: '/reports', label: 'Reports' },
  ],
  supervisor: [
    { href: '/supervisor', label: 'Awaiting Crew' },
    { href: '/supervisor/operators', label: 'My Operators' },
    { href: '/supervisor/roster', label: 'Monthly Roster' },
    { href: '/reports', label: 'Reports' },
  ],
  booking_officer: [
    { href: '/booking-officer', label: 'Daily Schedule' },
    { href: '/booking-officer/bookings', label: 'All Bookings' },
    { href: '/booking-officer/transport', label: 'Transport List' },
    { href: '/reports', label: 'Reports' },
  ],
  operator: [
    { href: '/operator', label: 'My Schedule' },
    { href: '/operator/roster', label: 'My Roster' },
    { href: '/operator/overtime', label: 'My Overtime' },
    { href: '/operator/reports', label: 'Production Reports' },
    { href: '/operator/storeroom', label: 'Storeroom' },
  ],
  manager: [
    { href: '/reports', label: 'Reports' },
    { href: '/booking-officer/bookings', label: 'All Bookings' },
  ],
  admin: [
    { href: '/admin', label: 'People & Roles' },
    { href: '/reports', label: 'Reports' },
  ],
};
