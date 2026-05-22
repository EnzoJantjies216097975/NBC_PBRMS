/** App-facing composite types built on top of the raw DB row types. */

import type { Tables } from './database.types';

export type Profile = Tables<'profiles'>;
export type Department = Tables<'departments'>;
export type Booking = Tables<'bookings'>;
export type BookingCrew = Tables<'booking_crew'>;
export type Shift = Tables<'shifts'>;
export type Production = Tables<'productions'>;
export type RosterPeriod = Tables<'roster_periods'>;
export type RosterAssignment = Tables<'roster_assignments'>;
export type AppNotification = Tables<'notifications'>;

export type ProfileSummary = Pick<
  Profile,
  'id' | 'first_name' | 'last_name' | 'phone' | 'role' | 'department_id'
>;

/** A booking joined with its crew, producer, and department for detail views. */
export interface BookingWithCrew extends Booking {
  crew: Array<BookingCrew & { profile?: ProfileSummary }>;
  producer?: ProfileSummary;
  content_department?: Pick<Department, 'id' | 'name' | 'kind'>;
}

export function fullName(p: Pick<Profile, 'first_name' | 'last_name'>): string {
  return `${p.first_name} ${p.last_name}`.trim();
}
