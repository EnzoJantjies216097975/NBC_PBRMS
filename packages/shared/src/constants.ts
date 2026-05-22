/**
 * Reference data and business-rule constants for NBC PBRMS.
 *
 * This mirrors the seed data in supabase/migrations/0003_seed.sql and the rules
 * described in the project spec. Keep the two in sync when either changes.
 */

import type { DepartmentKind, LocationType, ShiftCode } from './enums';

// ---------------------------------------------------------------------------
// Overtime rules
// ---------------------------------------------------------------------------

export const OVERTIME_RULES = {
  /** Weekdays (Mon–Fri): anything beyond 8h is overtime. */
  weekdayNormalMinutes: 8 * 60,
  /** Saturday: 5h is normal, beyond that is overtime. */
  saturdayNormalMinutes: 5 * 60,
  /** Sunday: all hours are overtime. */
  sundayNormalMinutes: 0,
  /**
   * If a production runs past its booked end time by this many minutes or more,
   * the overrun counts as overtime (spec: "over the given length by 30–40 min").
   */
  productionOverrunGraceMinutes: 30,
} as const;

// ---------------------------------------------------------------------------
// Shifts (see the April Sound roster + spec). `start`/`end` are 24h "HH:MM".
// asPerBooking shifts have no fixed window; isWorking=false are non-working.
// ---------------------------------------------------------------------------

export interface ShiftDef {
  code: ShiftCode;
  name: string;
  start: string | null;
  end: string | null;
  asPerBooking: boolean;
  isWorking: boolean;
}

export const SHIFTS: ShiftDef[] = [
  { code: 'FM', name: 'Floor Managing', start: '05:00', end: '13:00', asPerBooking: false, isWorking: true },
  { code: 'GM', name: 'Good Morning', start: '05:00', end: '13:00', asPerBooking: false, isWorking: true },
  { code: 'ST2', name: 'Studio 2', start: '14:00', end: '23:00', asPerBooking: false, isWorking: true },
  { code: 'ST4', name: 'Studio 4', start: '08:00', end: '17:00', asPerBooking: false, isWorking: true },
  { code: 'N', name: 'News', start: '13:00', end: '21:00', asPerBooking: false, isWorking: true },
  { code: 'APP', name: 'Audio Post Production', start: null, end: null, asPerBooking: true, isWorking: true },
  { code: 'TR', name: 'Trips', start: null, end: null, asPerBooking: true, isWorking: true },
  { code: 'A1', name: 'As Per Booking 1', start: null, end: null, asPerBooking: true, isWorking: true },
  { code: 'A2', name: 'As Per Booking 2', start: null, end: null, asPerBooking: true, isWorking: true },
  { code: 'A3', name: 'As Per Booking 3', start: null, end: null, asPerBooking: true, isWorking: true },
  { code: 'A4', name: 'As Per Booking 4', start: null, end: null, asPerBooking: true, isWorking: true },
  { code: 'A5', name: 'As Per Booking 5', start: null, end: null, asPerBooking: true, isWorking: true },
  { code: 'A6', name: 'As Per Booking 6', start: null, end: null, asPerBooking: true, isWorking: true },
  { code: 'O', name: 'Off', start: null, end: null, asPerBooking: false, isWorking: false },
  { code: 'PH', name: 'Public Holiday', start: null, end: null, asPerBooking: false, isWorking: false },
  { code: 'L', name: 'Leave', start: null, end: null, asPerBooking: false, isWorking: false },
];

export const SHIFTS_BY_CODE: Record<ShiftCode, ShiftDef> = Object.fromEntries(
  SHIFTS.map((s) => [s.code, s]),
) as Record<ShiftCode, ShiftDef>;

// ---------------------------------------------------------------------------
// Departments
// ---------------------------------------------------------------------------

export interface DepartmentSeed {
  name: string;
  kind: DepartmentKind;
  children?: string[];
}

export const DEPARTMENTS: DepartmentSeed[] = [
  // Operations (crew / technical) — each governed by a Supervisor.
  { name: 'Camera', kind: 'operations' },
  { name: 'Lighting', kind: 'operations' },
  { name: 'Sound', kind: 'operations' },
  { name: 'News Camera', kind: 'operations' }, // separate supervisor + roster
  { name: 'Production Officers', kind: 'operations' }, // Editors & Directors/Vision Mixers
  { name: 'Technicians', kind: 'operations' },
  { name: 'Final Control Centre', kind: 'operations' }, // FCC
  { name: 'OB Operations', kind: 'operations' }, // Outside Broadcast

  // Content / editorial — each governed by an Executive Producer.
  {
    name: 'Content Hub',
    kind: 'content',
    children: ['Education', 'Entertainment', 'Documentaries', 'AdHoc', 'Drama', 'Reality', 'Social Media'],
  },
  { name: 'News', kind: 'content' },
  { name: 'Current Affairs', kind: 'content' },
  { name: 'New Business', kind: 'content' },
  { name: 'Sports', kind: 'content' },
];

// ---------------------------------------------------------------------------
// Specialised equipment / skills frequently requested on bookings
// ---------------------------------------------------------------------------

export const SPECIALISED_EQUIPMENT = [
  'steadicam',
  'gimbal',
  'gopro',
  'drone',
  'pocket_cam',
  'jib',
  'wireless_mic_kit',
] as const;
export type SpecialisedEquipment = (typeof SPECIALISED_EQUIPMENT)[number];

export const SPECIALISED_EQUIPMENT_LABELS: Record<SpecialisedEquipment, string> = {
  steadicam: 'Steadicam',
  gimbal: 'Gimbal',
  gopro: 'GoPro',
  drone: 'Drone',
  pocket_cam: 'Pocket Cam',
  jib: 'Jib',
  wireless_mic_kit: 'Wireless Mic Kit',
};

// ---------------------------------------------------------------------------
// Production catalog — recurring/known shows from the spec. Used to seed the
// `productions` table, to pre-fill booking forms, and for recommendations.
// `day` uses ISO weekday (1=Mon … 7=Sun). null location = producer chooses.
// ---------------------------------------------------------------------------

export type ProductionKind = 'daily' | 'weekend' | 'flagship' | 'recorded' | 'location';

export interface ProductionSeed {
  name: string;
  kind: ProductionKind;
  location: LocationType | null;
  /** content department name (matches DEPARTMENTS) where relevant */
  department?: string;
  days?: number[]; // ISO weekdays the show runs
  start?: string; // "HH:MM"
  end?: string;
  isLive?: boolean;
}

const WEEKDAYS = [1, 2, 3, 4, 5];

export const PRODUCTIONS: ProductionSeed[] = [
  // ---- Daily (Mon–Fri) ----
  { name: 'Good Morning Namibia', kind: 'daily', location: 'studio_2', days: WEEKDAYS, start: '06:00', end: '09:00', isLive: true },
  { name: 'Namibia Connects', kind: 'daily', location: 'studio_1', days: WEEKDAYS, start: '10:00', end: '12:00' },
  { name: "1 o'Clock News", kind: 'daily', location: 'studio_1', department: 'News', days: WEEKDAYS, start: '13:00', end: '13:30', isLive: true },
  { name: 'Eye on SADC', kind: 'daily', location: 'studio_1', days: WEEKDAYS, start: '13:30', end: '14:00' },
  { name: 'Indigenous News', kind: 'daily', location: 'studio_1', department: 'News', days: WEEKDAYS, start: '14:00', end: '16:00' },
  { name: 'Daily Round-Up', kind: 'daily', location: 'studio_2', days: WEEKDAYS, start: '18:00', end: '19:00' },
  { name: "8 o'Clock News", kind: 'daily', location: 'studio_1', department: 'News', days: WEEKDAYS, start: '20:00', end: '21:00', isLive: true },
  { name: 'Sport News', kind: 'daily', location: 'studio_2', department: 'Sports', days: WEEKDAYS, start: '21:00', end: '21:30' },

  // ---- Weekend: Saturday (6) ----
  { name: 'Sports Breakfast Show', kind: 'weekend', location: 'studio_2', department: 'Sports', days: [6], start: '09:00', end: '10:00' },
  { name: 'Morning News Highlights', kind: 'weekend', location: 'studio_1', department: 'News', days: [6, 7], start: '11:00', end: '11:10' },
  { name: "1 o'Clock News (Weekend)", kind: 'weekend', location: 'studio_1', department: 'News', days: [6, 7], start: '13:00', end: '13:30' },
  { name: "4 o'Clock News Highlights", kind: 'weekend', location: 'studio_1', department: 'News', days: [6, 7], start: '16:00', end: '16:10' },
  { name: "6 o'Clock News Highlights", kind: 'weekend', location: 'studio_1', department: 'News', days: [6, 7], start: '18:00', end: '18:10' },
  { name: "8 o'Clock News (Weekend)", kind: 'weekend', location: 'studio_1', department: 'News', days: [6, 7], start: '20:00', end: '20:50' },
  { name: 'Sport News (Weekend)', kind: 'weekend', location: 'studio_2', department: 'Sports', days: [6, 7], start: '21:00', end: '21:30' },
  // ---- Weekend: Sunday (7) only ----
  { name: 'Wheels of Justice', kind: 'weekend', location: 'studio_2', days: [7], start: '19:00', end: '20:00' },

  // ---- Flagship live ----
  { name: 'Talk of the Nation', kind: 'flagship', location: 'studio_2', days: [1], start: '19:00', end: '20:00', isLive: true },
  { name: 'Business Today', kind: 'flagship', location: 'studio_2', days: [1, 3, 5], start: '21:30', end: '22:00', isLive: true },
  { name: 'Tupopyeni', kind: 'flagship', location: 'studio_2', days: [2], start: '19:00', end: '20:00', isLive: true },
  { name: 'Whatagwan', kind: 'flagship', location: 'studio_4', days: [3, 5], start: '18:00', end: '19:00', isLive: true },
  { name: 'Situation Kritical', kind: 'flagship', location: 'studio_2', days: [3], start: '19:00', end: '20:00', isLive: true },
  { name: 'Soccer Pitch', kind: 'flagship', location: 'studio_2', department: 'Sports', days: [4], start: '19:00', end: '20:00', isLive: true },

  // ---- Recorded: Current Affairs ----
  { name: 'Inside the Chambers', kind: 'recorded', location: 'studio_2', department: 'Current Affairs' },
  { name: 'One on One', kind: 'recorded', location: 'studio_2', department: 'Current Affairs' },
  { name: 'Public Service Corner', kind: 'recorded', location: 'studio_2', department: 'Current Affairs' },

  // ---- Recorded: Content Hub ----
  { name: 'Toucy T Show', kind: 'recorded', location: 'studio_2', department: 'Content Hub' },
  { name: 'Man Unfiltered', kind: 'recorded', location: 'studio_2', department: 'Content Hub' },
  { name: 'New Season', kind: 'recorded', location: null, department: 'Content Hub' }, // Studio 2 or 4
  { name: 'Unrooted', kind: 'recorded', location: null, department: 'Content Hub' }, // Studio 2 or 4
  { name: '@AM Saturday', kind: 'recorded', location: 'studio_4', department: 'Content Hub' },
  { name: '@AM Sunday', kind: 'recorded', location: 'studio_4', department: 'Content Hub' },
  { name: 'On My Playlist', kind: 'recorded', location: 'studio_2', department: 'Content Hub' },
  { name: 'Sunshine Club', kind: 'recorded', location: null, department: 'Content Hub' }, // Studio 2 or 4
  { name: 'Just Teenz', kind: 'recorded', location: null, department: 'Content Hub' },

  // ---- Recorded: Sports ----
  { name: 'Sports Essentials', kind: 'recorded', location: 'studio_2', department: 'Sports' },
  { name: 'Absolute Rugby', kind: 'recorded', location: 'studio_2', department: 'Sports' },

  // ---- Location shoots ----
  { name: 'Tutaleni', kind: 'location', location: 'on_location' },
  { name: 'Legends of Change', kind: 'location', location: 'on_location' },
  { name: 'Miss Namibia', kind: 'location', location: 'on_location' },
  { name: 'In The Community', kind: 'location', location: 'on_location' },
  { name: 'Sunshine Club (Location)', kind: 'location', location: 'on_location', department: 'Content Hub' },
  { name: 'Sports Uncovered', kind: 'location', location: 'on_location', department: 'Sports' },
  { name: 'Twin Turbo', kind: 'location', location: 'on_location' },
  { name: 'She Means Business', kind: 'location', location: 'on_location' },
  { name: 'Whatalifestyle', kind: 'location', location: 'on_location' },
];

// ---------------------------------------------------------------------------
// Recurring "standing" productions for a given date.
// ---------------------------------------------------------------------------

export interface RecurringInstance {
  name: string;
  location: LocationType | null;
  start: string; // HH:MM
  end: string; // HH:MM
  isLive: boolean;
  department?: string;
}

/** ISO weekday (1=Mon … 7=Sun) for a "YYYY-MM-DD" string, in local time. */
export function isoWeekday(dateISO: string): number {
  const d = new Date(`${dateISO}T00:00:00`).getDay(); // 0=Sun … 6=Sat
  return d === 0 ? 7 : d;
}

/**
 * The daily/weekend/flagship shows that run on the given date (those with a
 * fixed weekday + time in the catalog). Recorded/location shows are ad hoc and
 * are excluded.
 */
export function recurringProductionsForDate(dateISO: string): RecurringInstance[] {
  const weekday = isoWeekday(dateISO);
  return PRODUCTIONS.filter(
    (p) => p.days && p.start && p.end && p.days.includes(weekday),
  ).map((p) => ({
    name: p.name,
    location: p.location,
    start: p.start!,
    end: p.end!,
    isLive: p.isLive ?? false,
    department: p.department,
  }));
}
