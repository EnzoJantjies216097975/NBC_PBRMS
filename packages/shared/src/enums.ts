/**
 * Domain enums — kept in sync with the Postgres enum types defined in
 * supabase/migrations/0001_init.sql. Each is a readonly tuple so we get both a
 * runtime value list (for dropdowns / validation) and a literal union type.
 */

export const USER_ROLES = [
  'operator',
  'supervisor',
  'producer',
  'executive_producer',
  'booking_officer',
  'manager',
  'admin',
] as const;
export type UserRole = (typeof USER_ROLES)[number];

export const EMPLOYMENT_TYPES = [
  'permanent',
  'contract',
  'freelance',
  'apprentice',
  'intern',
] as const;
export type EmploymentType = (typeof EMPLOYMENT_TYPES)[number];

/** operations = crew/technical depts; content = producer/editorial depts. */
export const DEPARTMENT_KINDS = ['operations', 'content'] as const;
export type DepartmentKind = (typeof DEPARTMENT_KINDS)[number];

export const LOCATION_TYPES = [
  'studio_1',
  'studio_2',
  'studio_3',
  'studio_4',
  'audio_post', // Audio Post Production (APP)
  'ob_van', // Outside Broadcast
  'fly_away', // Fly-Away broadcast
  'on_location', // requires a venue
  'streaming', // Kiloview / online stream
  'other',
] as const;
export type LocationType = (typeof LOCATION_TYPES)[number];

export const CHANNELS = ['nbc_1', 'nbc_2', 'nbc_3'] as const;
export type Channel = (typeof CHANNELS)[number];

/**
 * Booking lifecycle. The pipeline is:
 *   draft -> submitted -> ep_approved -> crew_assigned -> confirmed
 *                      \-> ep_changes_requested (back to producer)
 *                                       crew_assigned \-> supervisor_changes_requested (back to EP)
 *   confirmed -> in_progress -> completed ; cancelled from any active state.
 */
export const BOOKING_STATUSES = [
  'draft',
  'submitted',
  'ep_changes_requested',
  'ep_approved',
  'supervisor_changes_requested',
  'crew_assigned',
  'confirmed',
  'in_progress',
  'completed',
  'cancelled',
] as const;
export type BookingStatus = (typeof BOOKING_STATUSES)[number];

/** Per-crew-member assignment state on a booking. */
export const ASSIGNMENT_STATUSES = [
  'proposed',
  'confirmed',
  'declined',
  'stand_in',
  'cancelled',
] as const;
export type AssignmentStatus = (typeof ASSIGNMENT_STATUSES)[number];

export const SEVERITIES = ['low', 'medium', 'high', 'critical'] as const;
export type Severity = (typeof SEVERITIES)[number];

export const EQUIPMENT_STATUSES = ['available', 'booked_out', 'maintenance', 'retired'] as const;
export type EquipmentStatus = (typeof EQUIPMENT_STATUSES)[number];

export const EQUIPMENT_REQUEST_STATUSES = [
  'requested',
  'approved',
  'denied',
  'checked_out',
  'returned',
] as const;
export type EquipmentRequestStatus = (typeof EQUIPMENT_REQUEST_STATUSES)[number];

/**
 * Shift codes used on the monthly roster (see the April Sound roster).
 * Working shifts have fixed default windows; trip/ad-hoc shifts (TR, A1–A6)
 * are "as per booking". O = off, PH = public holiday, L = leave.
 */
export const SHIFT_CODES = [
  'FM', // Floor Managing 05:00–13:00
  'GM', // Good Morning 05:00–13:00
  'ST2', // Studio 2 14:00–23:00
  'ST4', // Studio 4 / Morning Blast 08:00–17:00
  'N', // News 13:00–21:00
  'APP', // Audio Post Production
  'TR', // Trips — as per booking
  'A1',
  'A2',
  'A3',
  'A4',
  'A5',
  'A6',
  'O', // Off
  'PH', // Public Holiday
  'L', // Leave
] as const;
export type ShiftCode = (typeof SHIFT_CODES)[number];

// ---------------------------------------------------------------------------
// Human-readable labels for UI rendering.
// ---------------------------------------------------------------------------

export const USER_ROLE_LABELS: Record<UserRole, string> = {
  operator: 'Operator',
  supervisor: 'Supervisor',
  producer: 'Producer',
  executive_producer: 'Executive Producer',
  booking_officer: 'Booking Officer',
  manager: 'Manager',
  admin: 'Administrator',
};

export const LOCATION_TYPE_LABELS: Record<LocationType, string> = {
  studio_1: 'Studio 1',
  studio_2: 'Studio 2',
  studio_3: 'Studio 3',
  studio_4: 'Studio 4',
  audio_post: 'Audio Post Production (APP)',
  ob_van: 'Outside Broadcast (OB Van)',
  fly_away: 'Fly-Away Broadcast',
  on_location: 'On Location',
  streaming: 'Streaming / Kiloview',
  other: 'Other',
};

export const CHANNEL_LABELS: Record<Channel, string> = {
  nbc_1: 'NBC 1',
  nbc_2: 'NBC 2',
  nbc_3: 'NBC 3',
};

export const BOOKING_STATUS_LABELS: Record<BookingStatus, string> = {
  draft: 'Draft',
  submitted: 'Submitted to Exec Producer',
  ep_changes_requested: 'Changes requested by Exec Producer',
  ep_approved: 'Approved — awaiting crew',
  supervisor_changes_requested: 'Changes requested by Supervisor',
  crew_assigned: 'Crew assigned',
  confirmed: 'Confirmed',
  in_progress: 'In progress',
  completed: 'Completed',
  cancelled: 'Cancelled',
};
