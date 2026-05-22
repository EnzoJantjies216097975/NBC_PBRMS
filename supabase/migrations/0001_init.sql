-- ===========================================================================
-- NBC PBRMS — 0001 init
-- Core schema: identity, departments, shifts, productions, the booking pipeline,
-- crew assignments, the monthly roster, plus supporting tables. RLS is enabled
-- here and policies are defined in 0002_rls.sql. Seed data is in 0003_seed.sql.
-- ===========================================================================

create extension if not exists pgcrypto;

-- ---------------------------------------------------------------------------
-- Enum types (kept in sync with packages/shared/src/enums.ts)
-- ---------------------------------------------------------------------------
create type user_role as enum (
  'operator', 'supervisor', 'producer', 'executive_producer',
  'booking_officer', 'manager', 'admin'
);
create type employment_type as enum (
  'permanent', 'contract', 'freelance', 'apprentice', 'intern'
);
create type department_kind as enum ('operations', 'content');
create type location_type as enum (
  'studio_1', 'studio_2', 'studio_3', 'studio_4', 'audio_post',
  'ob_van', 'fly_away', 'on_location', 'streaming', 'other'
);
create type channel as enum ('nbc_1', 'nbc_2', 'nbc_3');
create type booking_status as enum (
  'draft', 'submitted', 'ep_changes_requested', 'ep_approved',
  'supervisor_changes_requested', 'crew_assigned', 'confirmed',
  'in_progress', 'completed', 'cancelled'
);
create type assignment_status as enum (
  'proposed', 'confirmed', 'declined', 'stand_in', 'cancelled'
);
create type severity as enum ('low', 'medium', 'high', 'critical');
create type equipment_status as enum ('available', 'booked_out', 'maintenance', 'retired');
create type equipment_request_status as enum (
  'requested', 'approved', 'denied', 'checked_out', 'returned'
);

-- ---------------------------------------------------------------------------
-- updated_at helper
-- ---------------------------------------------------------------------------
create or replace function set_updated_at()
returns trigger language plpgsql as $$
begin
  new.updated_at = now();
  return new;
end;
$$;

-- ---------------------------------------------------------------------------
-- Departments (operations crew depts + content/editorial depts, with sub-divs)
-- ---------------------------------------------------------------------------
create table departments (
  id          uuid primary key default gen_random_uuid(),
  name        text not null,
  kind        department_kind not null,
  parent_id   uuid references departments(id) on delete set null,
  created_at  timestamptz not null default now(),
  unique (name, parent_id)
);

-- ---------------------------------------------------------------------------
-- Profiles (1:1 with auth.users)
-- ---------------------------------------------------------------------------
create table profiles (
  id                uuid primary key references auth.users(id) on delete cascade,
  first_name        text not null default '',
  last_name         text not null default '',
  work_email        text not null,
  personal_email    text,
  date_of_birth     date,
  phone             text,
  physical_address  text,
  role              user_role not null default 'operator',
  employment_type   employment_type,
  department_id     uuid references departments(id) on delete set null,
  supervisor_id     uuid references profiles(id) on delete set null,
  needs_transport   boolean not null default false,  -- needs a pickup to get to work
  can_go_on_trips   boolean not null default true,   -- some supervisors may not travel
  is_news_camera    boolean not null default false,  -- separate supervisor + roster
  active            boolean not null default true,
  created_at        timestamptz not null default now(),
  updated_at        timestamptz not null default now()
);
create index idx_profiles_role on profiles(role);
create index idx_profiles_department on profiles(department_id);
create index idx_profiles_supervisor on profiles(supervisor_id);
create trigger trg_profiles_updated before update on profiles
  for each row execute function set_updated_at();

-- ---------------------------------------------------------------------------
-- Skills & specialised gear capability per operator
-- ---------------------------------------------------------------------------
create table skills (
  id          uuid primary key default gen_random_uuid(),
  name        text not null unique,
  description text
);
create table profile_skills (
  profile_id  uuid not null references profiles(id) on delete cascade,
  skill_id    uuid not null references skills(id) on delete cascade,
  primary key (profile_id, skill_id)
);

-- ---------------------------------------------------------------------------
-- Shifts (monthly roster cell codes)
-- ---------------------------------------------------------------------------
create table shifts (
  code            text primary key,
  name            text not null,
  start_time      time,
  end_time        time,
  as_per_booking  boolean not null default false,
  is_working      boolean not null default true,
  description     text
);

-- ---------------------------------------------------------------------------
-- Productions catalog (recurring/known shows)
-- ---------------------------------------------------------------------------
create table productions (
  id                    uuid primary key default gen_random_uuid(),
  name                  text not null,
  kind                  text not null default 'recorded',
  content_department_id uuid references departments(id) on delete set null,
  default_location      location_type,
  is_live               boolean not null default false,
  is_active             boolean not null default true,
  created_at            timestamptz not null default now()
);

-- ---------------------------------------------------------------------------
-- Bookings — the central pipeline object (one production instance/event)
-- ---------------------------------------------------------------------------
create table bookings (
  id                     uuid primary key default gen_random_uuid(),
  title                  text not null,
  production_id          uuid references productions(id) on delete set null,
  producer_id            uuid not null references profiles(id) on delete restrict,
  content_department_id  uuid references departments(id) on delete set null,
  executive_producer_id  uuid references profiles(id) on delete set null,
  channel                channel,
  location_type          location_type not null,
  venue                  text,                        -- for on_location / ob / fly_away
  air_date               date,
  air_start              time,
  air_end                time,
  call_date              date not null,               -- when crew should arrive
  call_time              time not null,
  end_date               date,
  end_time               time,
  duration_minutes       integer,
  needs_ob_van           boolean not null default false,
  is_streaming           boolean not null default false,
  uses_kiloview          boolean not null default false,
  is_once_off            boolean not null default false,
  is_ad_hoc              boolean not null default false,
  requires_car_booking   boolean not null default false,  -- crew need a vehicle to go out
  specialised_equipment  text[] not null default '{}',
  status                 booking_status not null default 'draft',
  notes                  text,
  ep_feedback            text,   -- EP's missing-info / rejection reasons
  supervisor_feedback    text,   -- supervisor's decline reasons
  cancelled_reason       text,
  created_at             timestamptz not null default now(),
  updated_at             timestamptz not null default now(),
  submitted_at           timestamptz,
  confirmed_at           timestamptz,
  cancelled_at           timestamptz
);
create index idx_bookings_status on bookings(status);
create index idx_bookings_producer on bookings(producer_id);
create index idx_bookings_ep on bookings(executive_producer_id);
create index idx_bookings_call_date on bookings(call_date);
create index idx_bookings_location on bookings(location_type, call_date);
create trigger trg_bookings_updated before update on bookings
  for each row execute function set_updated_at();

-- ---------------------------------------------------------------------------
-- Crew assignments on a booking
-- ---------------------------------------------------------------------------
create table booking_crew (
  id                     uuid primary key default gen_random_uuid(),
  booking_id             uuid not null references bookings(id) on delete cascade,
  profile_id             uuid not null references profiles(id) on delete restrict,
  department_id          uuid references departments(id) on delete set null,
  role_label             text not null default '',     -- "Camera 1", "Sound 1", "Floor Manager"...
  assigned_by            uuid references profiles(id) on delete set null,
  needs_car_booking      boolean not null default false,  -- this person needs a vehicle
  needs_transport        boolean not null default false,  -- this person needs a pickup
  status                 assignment_status not null default 'proposed',
  stand_in_for           uuid references profiles(id) on delete set null,
  attended               boolean,
  attended_confirmed_by  uuid references profiles(id) on delete set null,
  attended_at            timestamptz,
  created_at             timestamptz not null default now(),
  updated_at             timestamptz not null default now(),
  unique (booking_id, profile_id, role_label)
);
create index idx_booking_crew_booking on booking_crew(booking_id);
create index idx_booking_crew_profile on booking_crew(profile_id);
create trigger trg_booking_crew_updated before update on booking_crew
  for each row execute function set_updated_at();

-- ---------------------------------------------------------------------------
-- Booking documents (scripts, rundowns, plans — addable any time)
-- ---------------------------------------------------------------------------
create table booking_documents (
  id           uuid primary key default gen_random_uuid(),
  booking_id   uuid not null references bookings(id) on delete cascade,
  uploaded_by  uuid references profiles(id) on delete set null,
  file_name    text not null,
  storage_path text not null,
  doc_type     text not null default 'other',  -- script | rundown | plan | note | other
  created_at   timestamptz not null default now()
);
create index idx_booking_documents_booking on booking_documents(booking_id);

-- ---------------------------------------------------------------------------
-- Monthly roster (per operations department per month)
-- ---------------------------------------------------------------------------
create table roster_periods (
  id            uuid primary key default gen_random_uuid(),
  department_id uuid not null references departments(id) on delete cascade,
  year          integer not null,
  month         integer not null check (month between 1 and 12),
  status        text not null default 'draft',  -- draft | published
  created_by    uuid references profiles(id) on delete set null,
  published_at  timestamptz,
  created_at    timestamptz not null default now(),
  unique (department_id, year, month)
);
create table roster_assignments (
  id               uuid primary key default gen_random_uuid(),
  roster_period_id uuid not null references roster_periods(id) on delete cascade,
  profile_id       uuid not null references profiles(id) on delete cascade,
  work_date        date not null,
  shift_code       text references shifts(code) on delete set null,
  note             text,
  created_by       uuid references profiles(id) on delete set null,
  updated_at       timestamptz not null default now(),
  unique (roster_period_id, profile_id, work_date)
);
create index idx_roster_assign_profile_date on roster_assignments(profile_id, work_date);
create trigger trg_roster_assign_updated before update on roster_assignments
  for each row execute function set_updated_at();

-- ---------------------------------------------------------------------------
-- Production run logs (actual start/end -> duration & overtime)
-- ---------------------------------------------------------------------------
create table production_logs (
  id                uuid primary key default gen_random_uuid(),
  booking_id        uuid not null references bookings(id) on delete cascade,
  actual_start      timestamptz,
  actual_end        timestamptz,
  started_by        uuid references profiles(id) on delete set null,
  ended_by          uuid references profiles(id) on delete set null,
  is_overtime       boolean not null default false,
  overtime_minutes  integer not null default 0,
  note              text,
  created_at        timestamptz not null default now()
);
create index idx_production_logs_booking on production_logs(booking_id);

-- ---------------------------------------------------------------------------
-- Overtime entries (per operator, drives sheets + reports)
-- ---------------------------------------------------------------------------
create table overtime_entries (
  id                uuid primary key default gen_random_uuid(),
  profile_id        uuid not null references profiles(id) on delete cascade,
  booking_id        uuid references bookings(id) on delete set null,
  work_date         date not null,
  regular_minutes   integer not null default 0,
  overtime_minutes  integer not null default 0,
  reason            text,
  source            text not null default 'auto',  -- auto | manual
  approved_by       uuid references profiles(id) on delete set null,
  approved_at       timestamptz,
  created_at        timestamptz not null default now()
);
create index idx_overtime_profile_date on overtime_entries(profile_id, work_date);

-- ---------------------------------------------------------------------------
-- Operator production reports (issues raised to supervisors)
-- ---------------------------------------------------------------------------
create table production_reports (
  id               uuid primary key default gen_random_uuid(),
  booking_id       uuid references bookings(id) on delete set null,
  author_id        uuid not null references profiles(id) on delete cascade,
  department_id    uuid references departments(id) on delete set null,
  severity         severity not null default 'low',
  title            text not null,
  body             text,
  status           text not null default 'open',  -- open | acknowledged | resolved
  acknowledged_by  uuid references profiles(id) on delete set null,
  acknowledged_at  timestamptz,
  created_at       timestamptz not null default now()
);
create index idx_production_reports_author on production_reports(author_id);

-- ---------------------------------------------------------------------------
-- Storeroom / equipment
-- ---------------------------------------------------------------------------
create table equipment (
  id             uuid primary key default gen_random_uuid(),
  name           text not null,
  category       text,
  serial_number  text unique,
  status         equipment_status not null default 'available',
  home_location  location_type,
  notes          text,
  created_at     timestamptz not null default now()
);
create table equipment_bookings (
  id              uuid primary key default gen_random_uuid(),
  equipment_id    uuid not null references equipment(id) on delete cascade,
  booking_id      uuid references bookings(id) on delete set null,
  requested_by    uuid not null references profiles(id) on delete cascade,
  approved_by     uuid references profiles(id) on delete set null,
  status          equipment_request_status not null default 'requested',
  checked_out_at  timestamptz,
  due_back_at     timestamptz,
  returned_at     timestamptz,
  notes           text,
  created_at      timestamptz not null default now()
);
create index idx_equipment_bookings_equipment on equipment_bookings(equipment_id);

-- ---------------------------------------------------------------------------
-- Transport (pickups to work) and car bookings (vehicles to go out)
-- ---------------------------------------------------------------------------
create table transport_requests (
  id               uuid primary key default gen_random_uuid(),
  profile_id       uuid not null references profiles(id) on delete cascade,
  booking_id       uuid references bookings(id) on delete set null,
  work_date        date not null,
  pickup_time      time,
  pickup_address   text,
  dropoff_time     time,
  dropoff_address  text,
  phone            text,
  status           text not null default 'requested',
  created_by       uuid references profiles(id) on delete set null,
  created_at       timestamptz not null default now()
);
create table car_bookings (
  id            uuid primary key default gen_random_uuid(),
  booking_id    uuid references bookings(id) on delete cascade,
  profile_id    uuid references profiles(id) on delete set null,
  needed_from   timestamptz,
  needed_to     timestamptz,
  purpose       text,
  status        text not null default 'requested',
  created_by    uuid references profiles(id) on delete set null,
  created_at    timestamptz not null default now()
);

-- ---------------------------------------------------------------------------
-- Notifications & audit log
-- ---------------------------------------------------------------------------
create table notifications (
  id            uuid primary key default gen_random_uuid(),
  recipient_id  uuid not null references profiles(id) on delete cascade,
  type          text not null,
  title         text not null,
  body          text,
  booking_id    uuid references bookings(id) on delete set null,
  channel       text not null default 'in_app',  -- in_app | push | sms
  read          boolean not null default false,
  created_at    timestamptz not null default now(),
  sent_at       timestamptz,
  meta          jsonb
);
create index idx_notifications_recipient on notifications(recipient_id, read);

create table audit_log (
  id          uuid primary key default gen_random_uuid(),
  booking_id  uuid references bookings(id) on delete set null,
  actor_id    uuid references profiles(id) on delete set null,
  action      text not null,
  detail      jsonb,
  created_at  timestamptz not null default now()
);
create index idx_audit_booking on audit_log(booking_id);

-- ---------------------------------------------------------------------------
-- View: per-operator busy windows (feeds conflict detection in the app)
-- ---------------------------------------------------------------------------
create or replace view v_crew_schedule with (security_invoker = true) as
select
  bc.profile_id,
  bc.booking_id,
  b.title,
  b.status,
  b.location_type,
  b.call_date                                   as work_date,
  b.call_time                                   as start_time,
  coalesce(b.end_time, b.air_end, b.call_time)  as end_time
from booking_crew bc
join bookings b on b.id = bc.booking_id
where bc.status in ('proposed', 'confirmed', 'stand_in')
  and b.status not in ('cancelled', 'draft');

-- ---------------------------------------------------------------------------
-- Enable RLS on every table (policies follow in 0002_rls.sql).
-- Tables without policies are deny-by-default to authenticated users.
-- ---------------------------------------------------------------------------
alter table departments          enable row level security;
alter table profiles             enable row level security;
alter table skills               enable row level security;
alter table profile_skills       enable row level security;
alter table shifts               enable row level security;
alter table productions          enable row level security;
alter table bookings             enable row level security;
alter table booking_crew         enable row level security;
alter table booking_documents    enable row level security;
alter table roster_periods       enable row level security;
alter table roster_assignments   enable row level security;
alter table production_logs      enable row level security;
alter table overtime_entries     enable row level security;
alter table production_reports   enable row level security;
alter table equipment            enable row level security;
alter table equipment_bookings   enable row level security;
alter table transport_requests   enable row level security;
alter table car_bookings         enable row level security;
alter table notifications        enable row level security;
alter table audit_log            enable row level security;
