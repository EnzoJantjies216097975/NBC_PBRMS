/**
 * Supabase `Database` type.
 *
 * This is a hand-authored stub covering the first-focus tables (the booking
 * pipeline + roster + conflicts). Once the Supabase CLI is available, replace it
 * with generated output:
 *
 *     pnpm db:gen-types
 *
 * which regenerates this file from the live schema with every table.
 */

export type Json = string | number | boolean | null | { [key: string]: Json | undefined } | Json[];

import type {
  AssignmentStatus,
  BookingStatus,
  Channel,
  DepartmentKind,
  EmploymentType,
  LocationType,
  Severity,
  UserRole,
} from './enums';

type Timestamp = string; // ISO 8601
type DateString = string; // YYYY-MM-DD
type TimeString = string; // HH:MM[:SS]

export interface Database {
  public: {
    Tables: {
      departments: {
        Row: {
          id: string;
          name: string;
          kind: DepartmentKind;
          parent_id: string | null;
          created_at: Timestamp;
        };
        Insert: {
          id?: string;
          name: string;
          kind: DepartmentKind;
          parent_id?: string | null;
          created_at?: Timestamp;
        };
        Update: Partial<Database['public']['Tables']['departments']['Insert']>;
        Relationships: [];
      };
      profiles: {
        Row: {
          id: string;
          first_name: string;
          last_name: string;
          work_email: string;
          personal_email: string | null;
          date_of_birth: DateString | null;
          phone: string | null;
          physical_address: string | null;
          role: UserRole;
          employment_type: EmploymentType | null;
          department_id: string | null;
          supervisor_id: string | null;
          needs_transport: boolean;
          can_go_on_trips: boolean;
          is_news_camera: boolean;
          active: boolean;
          created_at: Timestamp;
          updated_at: Timestamp;
        };
        Insert: {
          id: string;
          first_name: string;
          last_name: string;
          work_email: string;
          personal_email?: string | null;
          date_of_birth?: DateString | null;
          phone?: string | null;
          physical_address?: string | null;
          role?: UserRole;
          employment_type?: EmploymentType | null;
          department_id?: string | null;
          supervisor_id?: string | null;
          needs_transport?: boolean;
          can_go_on_trips?: boolean;
          is_news_camera?: boolean;
          active?: boolean;
          created_at?: Timestamp;
          updated_at?: Timestamp;
        };
        Update: Partial<Database['public']['Tables']['profiles']['Insert']>;
        Relationships: [];
      };
      shifts: {
        Row: {
          code: string;
          name: string;
          start_time: TimeString | null;
          end_time: TimeString | null;
          as_per_booking: boolean;
          is_working: boolean;
          description: string | null;
        };
        Insert: {
          code: string;
          name: string;
          start_time?: TimeString | null;
          end_time?: TimeString | null;
          as_per_booking?: boolean;
          is_working?: boolean;
          description?: string | null;
        };
        Update: Partial<Database['public']['Tables']['shifts']['Insert']>;
        Relationships: [];
      };
      productions: {
        Row: {
          id: string;
          name: string;
          kind: string;
          content_department_id: string | null;
          default_location: LocationType | null;
          is_live: boolean;
          is_active: boolean;
          created_at: Timestamp;
        };
        Insert: {
          id?: string;
          name: string;
          kind: string;
          content_department_id?: string | null;
          default_location?: LocationType | null;
          is_live?: boolean;
          is_active?: boolean;
          created_at?: Timestamp;
        };
        Update: Partial<Database['public']['Tables']['productions']['Insert']>;
        Relationships: [];
      };
      bookings: {
        Row: {
          id: string;
          title: string;
          production_id: string | null;
          producer_id: string;
          content_department_id: string | null;
          executive_producer_id: string | null;
          channel: Channel | null;
          location_type: LocationType;
          venue: string | null;
          air_date: DateString | null;
          air_start: TimeString | null;
          air_end: TimeString | null;
          call_date: DateString;
          call_time: TimeString;
          end_date: DateString | null;
          end_time: TimeString | null;
          duration_minutes: number | null;
          needs_ob_van: boolean;
          is_streaming: boolean;
          uses_kiloview: boolean;
          is_once_off: boolean;
          is_ad_hoc: boolean;
          requires_car_booking: boolean;
          specialised_equipment: string[];
          status: BookingStatus;
          notes: string | null;
          ep_feedback: string | null;
          supervisor_feedback: string | null;
          cancelled_reason: string | null;
          created_at: Timestamp;
          updated_at: Timestamp;
          submitted_at: Timestamp | null;
          confirmed_at: Timestamp | null;
          cancelled_at: Timestamp | null;
        };
        Insert: {
          id?: string;
          title: string;
          production_id?: string | null;
          producer_id: string;
          content_department_id?: string | null;
          executive_producer_id?: string | null;
          channel?: Channel | null;
          location_type: LocationType;
          venue?: string | null;
          air_date?: DateString | null;
          air_start?: TimeString | null;
          air_end?: TimeString | null;
          call_date: DateString;
          call_time: TimeString;
          end_date?: DateString | null;
          end_time?: TimeString | null;
          duration_minutes?: number | null;
          needs_ob_van?: boolean;
          is_streaming?: boolean;
          uses_kiloview?: boolean;
          is_once_off?: boolean;
          is_ad_hoc?: boolean;
          requires_car_booking?: boolean;
          specialised_equipment?: string[];
          status?: BookingStatus;
          notes?: string | null;
          ep_feedback?: string | null;
          supervisor_feedback?: string | null;
          cancelled_reason?: string | null;
          created_at?: Timestamp;
          updated_at?: Timestamp;
          submitted_at?: Timestamp | null;
          confirmed_at?: Timestamp | null;
          cancelled_at?: Timestamp | null;
        };
        Update: Partial<Database['public']['Tables']['bookings']['Insert']>;
        Relationships: [];
      };
      booking_crew: {
        Row: {
          id: string;
          booking_id: string;
          profile_id: string;
          department_id: string | null;
          role_label: string;
          assigned_by: string | null;
          needs_car_booking: boolean;
          needs_transport: boolean;
          status: AssignmentStatus;
          stand_in_for: string | null;
          attended: boolean | null;
          attended_confirmed_by: string | null;
          attended_at: Timestamp | null;
          created_at: Timestamp;
          updated_at: Timestamp;
        };
        Insert: {
          id?: string;
          booking_id: string;
          profile_id: string;
          department_id?: string | null;
          role_label: string;
          assigned_by?: string | null;
          needs_car_booking?: boolean;
          needs_transport?: boolean;
          status?: AssignmentStatus;
          stand_in_for?: string | null;
          attended?: boolean | null;
          attended_confirmed_by?: string | null;
          attended_at?: Timestamp | null;
          created_at?: Timestamp;
          updated_at?: Timestamp;
        };
        Update: Partial<Database['public']['Tables']['booking_crew']['Insert']>;
        Relationships: [];
      };
      roster_periods: {
        Row: {
          id: string;
          department_id: string;
          year: number;
          month: number;
          status: string;
          created_by: string | null;
          published_at: Timestamp | null;
          created_at: Timestamp;
        };
        Insert: {
          id?: string;
          department_id: string;
          year: number;
          month: number;
          status?: string;
          created_by?: string | null;
          published_at?: Timestamp | null;
          created_at?: Timestamp;
        };
        Update: Partial<Database['public']['Tables']['roster_periods']['Insert']>;
        Relationships: [];
      };
      roster_assignments: {
        Row: {
          id: string;
          roster_period_id: string;
          profile_id: string;
          work_date: DateString;
          shift_code: string | null;
          note: string | null;
          created_by: string | null;
          updated_at: Timestamp;
        };
        Insert: {
          id?: string;
          roster_period_id: string;
          profile_id: string;
          work_date: DateString;
          shift_code?: string | null;
          note?: string | null;
          created_by?: string | null;
          updated_at?: Timestamp;
        };
        Update: Partial<Database['public']['Tables']['roster_assignments']['Insert']>;
        Relationships: [];
      };
      notifications: {
        Row: {
          id: string;
          recipient_id: string;
          type: string;
          title: string;
          body: string | null;
          booking_id: string | null;
          channel: string;
          read: boolean;
          created_at: Timestamp;
          sent_at: Timestamp | null;
          meta: Json | null;
        };
        Insert: {
          id?: string;
          recipient_id: string;
          type: string;
          title: string;
          body?: string | null;
          booking_id?: string | null;
          channel?: string;
          read?: boolean;
          created_at?: Timestamp;
          sent_at?: Timestamp | null;
          meta?: Json | null;
        };
        Update: Partial<Database['public']['Tables']['notifications']['Insert']>;
        Relationships: [];
      };
    };
    Views: Record<string, never>;
    Functions: {
      auth_user_role: {
        Args: Record<string, never>;
        Returns: UserRole;
      };
    };
    Enums: {
      user_role: UserRole;
      employment_type: EmploymentType;
      department_kind: DepartmentKind;
      location_type: LocationType;
      channel: Channel;
      booking_status: BookingStatus;
      assignment_status: AssignmentStatus;
      severity: Severity;
    };
    CompositeTypes: Record<string, never>;
  };
}

// Convenience helpers ------------------------------------------------------
export type Tables<T extends keyof Database['public']['Tables']> =
  Database['public']['Tables'][T]['Row'];
export type TablesInsert<T extends keyof Database['public']['Tables']> =
  Database['public']['Tables'][T]['Insert'];
export type TablesUpdate<T extends keyof Database['public']['Tables']> =
  Database['public']['Tables'][T]['Update'];
