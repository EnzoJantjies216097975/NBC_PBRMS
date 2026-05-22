/**
 * Central capability checks keyed off the user's role. UI gates and (as a
 * second line of defence) server actions consult these; the real enforcement
 * lives in Postgres RLS (see supabase/migrations/0002_rls.sql).
 */

import type { UserRole } from './enums';

const has = (role: UserRole, roles: UserRole[]) => roles.includes(role);

export const can = {
  /** Only the Booking Officer (and admin) may edit the daily roster. */
  editDailyRoster: (r: UserRole) => has(r, ['booking_officer', 'admin']),
  /** Supervisors own the monthly/weekly roster for their department. */
  editMonthlyRoster: (r: UserRole) => has(r, ['supervisor', 'admin']),
  /** Producers raise bookings; EPs may also create on behalf of producers. */
  createBooking: (r: UserRole) => has(r, ['producer', 'executive_producer', 'admin']),
  /** EPs validate/approve and assign channel. */
  approveBooking: (r: UserRole) => has(r, ['executive_producer', 'admin']),
  /** Supervisors and the Booking Officer assign crew to bookings. */
  assignCrew: (r: UserRole) => has(r, ['supervisor', 'booking_officer', 'admin']),
  /** Supervisors manage their operators (add/remove/skills/gear). */
  manageOperators: (r: UserRole) => has(r, ['supervisor', 'admin']),
  /** Who may assign a user's role / link them to a department roster. */
  assignRoles: (r: UserRole) => has(r, ['supervisor', 'executive_producer', 'admin']),
  /** Reporting & analytics access. */
  viewReports: (r: UserRole) =>
    has(r, ['supervisor', 'booking_officer', 'manager', 'admin', 'executive_producer']),
  /** Full cross-department visibility of every booking. */
  viewAllBookings: (r: UserRole) => has(r, ['booking_officer', 'manager', 'admin']),
  /** Storeroom: only supervisors (and admin) assign gear; operators book out. */
  assignEquipment: (r: UserRole) => has(r, ['supervisor', 'admin']),
} as const;

/** The home route each role lands on after login (used by web + mobile). */
export const ROLE_HOME: Record<UserRole, string> = {
  operator: '/operator',
  supervisor: '/supervisor',
  producer: '/producer',
  executive_producer: '/exec-producer',
  booking_officer: '/booking-officer',
  manager: '/reports',
  admin: '/admin',
};
