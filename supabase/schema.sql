-- ============================================================
-- supabase/migrations/0001_init.sql
-- ============================================================
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


-- ============================================================
-- supabase/migrations/0002_rls.sql
-- ============================================================
-- ===========================================================================
-- NBC PBRMS — 0002 row-level security
-- Helper functions + policies. Helpers are SECURITY DEFINER so they can read
-- `profiles` without tripping the table's own RLS (avoids recursion).
-- ===========================================================================

create or replace function auth_user_role()
returns user_role
language sql stable security definer set search_path = public as $$
  select role from profiles where id = auth.uid();
$$;

create or replace function auth_user_department()
returns uuid
language sql stable security definer set search_path = public as $$
  select department_id from profiles where id = auth.uid();
$$;

grant execute on function auth_user_role() to authenticated;
grant execute on function auth_user_department() to authenticated;

-- ---------------------------------------------------------------------------
-- profiles
-- ---------------------------------------------------------------------------
create policy "profiles readable by authenticated" on profiles
  for select to authenticated using (true);
create policy "users insert own profile" on profiles
  for insert to authenticated with check (id = auth.uid());
create policy "users update own profile" on profiles
  for update to authenticated using (id = auth.uid()) with check (id = auth.uid());
create policy "elevated update any profile" on profiles
  for update to authenticated
  using (auth_user_role() in ('admin', 'supervisor', 'executive_producer'))
  with check (auth_user_role() in ('admin', 'supervisor', 'executive_producer'));
create policy "admin delete profile" on profiles
  for delete to authenticated using (auth_user_role() = 'admin');

-- ---------------------------------------------------------------------------
-- departments / shifts / skills / productions (mostly reference data)
-- ---------------------------------------------------------------------------
create policy "departments readable" on departments
  for select to authenticated using (true);
create policy "departments managed by admin" on departments
  for all to authenticated
  using (auth_user_role() = 'admin') with check (auth_user_role() = 'admin');

create policy "shifts readable" on shifts
  for select to authenticated using (true);
create policy "shifts managed by admin" on shifts
  for all to authenticated
  using (auth_user_role() = 'admin') with check (auth_user_role() = 'admin');

create policy "skills readable" on skills
  for select to authenticated using (true);
create policy "skills managed by supervisor" on skills
  for all to authenticated
  using (auth_user_role() in ('supervisor', 'admin'))
  with check (auth_user_role() in ('supervisor', 'admin'));

create policy "profile_skills readable" on profile_skills
  for select to authenticated using (true);
create policy "profile_skills managed by supervisor or owner" on profile_skills
  for all to authenticated
  using (auth_user_role() in ('supervisor', 'admin') or profile_id = auth.uid())
  with check (auth_user_role() in ('supervisor', 'admin') or profile_id = auth.uid());

create policy "productions readable" on productions
  for select to authenticated using (true);
create policy "productions managed by content roles" on productions
  for all to authenticated
  using (auth_user_role() in ('producer', 'executive_producer', 'admin'))
  with check (auth_user_role() in ('producer', 'executive_producer', 'admin'));

-- ---------------------------------------------------------------------------
-- bookings
-- ---------------------------------------------------------------------------
create policy "bookings visible to stakeholders" on bookings
  for select to authenticated using (
    producer_id = auth.uid()
    or executive_producer_id = auth.uid()
    or content_department_id = auth_user_department()
    or auth_user_role() in ('supervisor', 'booking_officer', 'manager', 'admin')
    or exists (
      select 1 from booking_crew bc
      where bc.booking_id = bookings.id and bc.profile_id = auth.uid()
    )
  );
create policy "producers create bookings" on bookings
  for insert to authenticated with check (
    auth_user_role() in ('producer', 'executive_producer', 'admin')
    and (producer_id = auth.uid() or auth_user_role() in ('executive_producer', 'admin'))
  );
create policy "producers update own draft bookings" on bookings
  for update to authenticated
  using (producer_id = auth.uid() and status in ('draft', 'ep_changes_requested'))
  with check (producer_id = auth.uid());
create policy "staff update bookings" on bookings
  for update to authenticated
  using (auth_user_role() in ('executive_producer', 'supervisor', 'booking_officer', 'manager', 'admin'))
  with check (auth_user_role() in ('executive_producer', 'supervisor', 'booking_officer', 'manager', 'admin'));
create policy "admin or owner delete booking" on bookings
  for delete to authenticated
  using (auth_user_role() = 'admin' or (producer_id = auth.uid() and status = 'draft'));

-- ---------------------------------------------------------------------------
-- booking_crew
-- ---------------------------------------------------------------------------
create policy "crew rows visible to stakeholders" on booking_crew
  for select to authenticated using (
    profile_id = auth.uid()
    or auth_user_role() in ('supervisor', 'booking_officer', 'manager', 'admin')
    or exists (
      select 1 from bookings b
      where b.id = booking_crew.booking_id
        and (b.producer_id = auth.uid() or b.executive_producer_id = auth.uid())
    )
  );
create policy "staff manage crew" on booking_crew
  for all to authenticated
  using (auth_user_role() in ('supervisor', 'booking_officer', 'admin'))
  with check (auth_user_role() in ('supervisor', 'booking_officer', 'admin'));
create policy "producer confirms attendance" on booking_crew
  for update to authenticated
  using (exists (
    select 1 from bookings b
    where b.id = booking_crew.booking_id and b.producer_id = auth.uid()
  ))
  with check (exists (
    select 1 from bookings b
    where b.id = booking_crew.booking_id and b.producer_id = auth.uid()
  ));

-- ---------------------------------------------------------------------------
-- booking_documents
-- ---------------------------------------------------------------------------
create policy "documents visible to stakeholders" on booking_documents
  for select to authenticated using (
    auth_user_role() in ('booking_officer', 'manager', 'admin')
    or exists (
      select 1 from bookings b
      where b.id = booking_documents.booking_id
        and (b.producer_id = auth.uid() or b.executive_producer_id = auth.uid())
    )
    or exists (
      select 1 from booking_crew bc
      where bc.booking_id = booking_documents.booking_id and bc.profile_id = auth.uid()
    )
  );
create policy "producers add documents" on booking_documents
  for insert to authenticated with check (
    auth_user_role() in ('producer', 'executive_producer', 'admin')
  );
create policy "uploader or admin delete document" on booking_documents
  for delete to authenticated
  using (uploaded_by = auth.uid() or auth_user_role() = 'admin');

-- ---------------------------------------------------------------------------
-- roster (supervisors own monthly; booking officer does NOT edit it)
-- ---------------------------------------------------------------------------
create policy "roster periods readable" on roster_periods
  for select to authenticated using (true);
create policy "supervisor manages own dept roster period" on roster_periods
  for all to authenticated
  using (auth_user_role() = 'admin' or (auth_user_role() = 'supervisor' and department_id = auth_user_department()))
  with check (auth_user_role() = 'admin' or (auth_user_role() = 'supervisor' and department_id = auth_user_department()));

create policy "roster assignments readable" on roster_assignments
  for select to authenticated using (true);
create policy "supervisor manages roster assignments" on roster_assignments
  for all to authenticated
  using (auth_user_role() in ('supervisor', 'admin'))
  with check (auth_user_role() in ('supervisor', 'admin'));

-- ---------------------------------------------------------------------------
-- production_logs (producer logs start/end; staff oversight)
-- ---------------------------------------------------------------------------
create policy "logs visible to stakeholders" on production_logs
  for select to authenticated using (
    auth_user_role() in ('supervisor', 'booking_officer', 'manager', 'admin')
    or exists (
      select 1 from bookings b
      where b.id = production_logs.booking_id
        and (b.producer_id = auth.uid() or b.executive_producer_id = auth.uid())
    )
    or exists (
      select 1 from booking_crew bc
      where bc.booking_id = production_logs.booking_id and bc.profile_id = auth.uid()
    )
  );
create policy "producer or staff write logs" on production_logs
  for all to authenticated
  using (
    auth_user_role() in ('supervisor', 'booking_officer', 'admin')
    or exists (select 1 from bookings b where b.id = production_logs.booking_id and b.producer_id = auth.uid())
  )
  with check (
    auth_user_role() in ('supervisor', 'booking_officer', 'admin')
    or exists (select 1 from bookings b where b.id = production_logs.booking_id and b.producer_id = auth.uid())
  );

-- ---------------------------------------------------------------------------
-- overtime_entries (operator sees own; EP/supervisor/managers verify)
-- ---------------------------------------------------------------------------
create policy "overtime visible to owner and oversight" on overtime_entries
  for select to authenticated using (
    profile_id = auth.uid()
    or auth_user_role() in ('supervisor', 'booking_officer', 'manager', 'admin', 'executive_producer')
  );
create policy "staff manage overtime" on overtime_entries
  for all to authenticated
  using (auth_user_role() in ('supervisor', 'booking_officer', 'manager', 'admin'))
  with check (auth_user_role() in ('supervisor', 'booking_officer', 'manager', 'admin'));

-- ---------------------------------------------------------------------------
-- production_reports (operator raises; supervisor/manager handle)
-- ---------------------------------------------------------------------------
create policy "reports visible to author and oversight" on production_reports
  for select to authenticated using (
    author_id = auth.uid()
    or auth_user_role() in ('supervisor', 'manager', 'admin')
  );
create policy "operators raise reports" on production_reports
  for insert to authenticated with check (author_id = auth.uid());
create policy "supervisors handle reports" on production_reports
  for update to authenticated
  using (auth_user_role() in ('supervisor', 'manager', 'admin'))
  with check (auth_user_role() in ('supervisor', 'manager', 'admin'));

-- ---------------------------------------------------------------------------
-- equipment / equipment_bookings
-- ---------------------------------------------------------------------------
create policy "equipment readable" on equipment
  for select to authenticated using (true);
create policy "equipment managed by supervisor" on equipment
  for all to authenticated
  using (auth_user_role() in ('supervisor', 'admin'))
  with check (auth_user_role() in ('supervisor', 'admin'));

create policy "equipment bookings visible" on equipment_bookings
  for select to authenticated using (
    requested_by = auth.uid()
    or auth_user_role() in ('supervisor', 'booking_officer', 'manager', 'admin')
  );
create policy "operators request equipment" on equipment_bookings
  for insert to authenticated with check (requested_by = auth.uid());
create policy "supervisors approve equipment" on equipment_bookings
  for update to authenticated
  using (auth_user_role() in ('supervisor', 'admin'))
  with check (auth_user_role() in ('supervisor', 'admin'));

-- ---------------------------------------------------------------------------
-- transport_requests / car_bookings
-- ---------------------------------------------------------------------------
create policy "transport visible to owner and booking office" on transport_requests
  for select to authenticated using (
    profile_id = auth.uid()
    or auth_user_role() in ('booking_officer', 'manager', 'admin')
  );
create policy "booking office manages transport" on transport_requests
  for all to authenticated
  using (auth_user_role() in ('booking_officer', 'admin'))
  with check (auth_user_role() in ('booking_officer', 'admin'));

create policy "car bookings visible to stakeholders" on car_bookings
  for select to authenticated using (
    profile_id = auth.uid()
    or auth_user_role() in ('supervisor', 'booking_officer', 'manager', 'admin')
    or exists (select 1 from bookings b where b.id = car_bookings.booking_id and b.producer_id = auth.uid())
  );
create policy "staff manage car bookings" on car_bookings
  for all to authenticated
  using (auth_user_role() in ('supervisor', 'booking_officer', 'admin'))
  with check (auth_user_role() in ('supervisor', 'booking_officer', 'admin'));

-- ---------------------------------------------------------------------------
-- notifications (recipient-scoped) / audit_log (oversight)
-- ---------------------------------------------------------------------------
create policy "notifications visible to recipient" on notifications
  for select to authenticated using (recipient_id = auth.uid());
create policy "recipient updates own notifications" on notifications
  for update to authenticated using (recipient_id = auth.uid()) with check (recipient_id = auth.uid());
create policy "authenticated create notifications" on notifications
  for insert to authenticated with check (true);

create policy "audit visible to oversight" on audit_log
  for select to authenticated
  using (auth_user_role() in ('supervisor', 'booking_officer', 'manager', 'admin'));
create policy "authenticated write audit" on audit_log
  for insert to authenticated with check (true);


-- ============================================================
-- supabase/migrations/0003_seed.sql
-- ============================================================
-- ===========================================================================
-- NBC PBRMS — 0003 seed (reference data)
-- Departments, shifts, skills, and the recurring production catalog.
-- Mirrors packages/shared/src/constants.ts. Re-runnable (guarded inserts).
-- ===========================================================================

-- ---- Departments: operations (crew) ----
insert into departments (name, kind)
select v.name, 'operations'::department_kind
from (values
  ('Camera'), ('Lighting'), ('Sound'), ('News Camera'),
  ('Production Officers'), ('Technicians'), ('Final Control Centre'), ('OB Operations')
) as v(name)
where not exists (select 1 from departments d where d.name = v.name and d.parent_id is null);

-- ---- Departments: content (editorial) ----
insert into departments (name, kind)
select v.name, 'content'::department_kind
from (values
  ('Content Hub'), ('News'), ('Current Affairs'), ('New Business'), ('Sports')
) as v(name)
where not exists (select 1 from departments d where d.name = v.name and d.parent_id is null);

-- ---- Content Hub sub-divisions ----
insert into departments (name, kind, parent_id)
select v.name, 'content'::department_kind, (select id from departments where name = 'Content Hub' and parent_id is null)
from (values
  ('Education'), ('Entertainment'), ('Documentaries'), ('AdHoc'),
  ('Drama'), ('Reality'), ('Social Media')
) as v(name)
where not exists (
  select 1 from departments d
  where d.name = v.name
    and d.parent_id = (select id from departments where name = 'Content Hub' and parent_id is null)
);

-- ---- Shifts ----
insert into shifts (code, name, start_time, end_time, as_per_booking, is_working) values
  ('FM',  'Floor Managing',         '05:00', '13:00', false, true),
  ('GM',  'Good Morning',           '05:00', '13:00', false, true),
  ('ST2', 'Studio 2',               '14:00', '23:00', false, true),
  ('ST4', 'Studio 4',               '08:00', '17:00', false, true),
  ('N',   'News',                   '13:00', '21:00', false, true),
  ('APP', 'Audio Post Production',   null,    null,    true,  true),
  ('TR',  'Trips',                  null,    null,    true,  true),
  ('A1',  'As Per Booking 1',       null,    null,    true,  true),
  ('A2',  'As Per Booking 2',       null,    null,    true,  true),
  ('A3',  'As Per Booking 3',       null,    null,    true,  true),
  ('A4',  'As Per Booking 4',       null,    null,    true,  true),
  ('A5',  'As Per Booking 5',       null,    null,    true,  true),
  ('A6',  'As Per Booking 6',       null,    null,    true,  true),
  ('O',   'Off',                    null,    null,    false, false),
  ('PH',  'Public Holiday',         null,    null,    false, false),
  ('L',   'Leave',                  null,    null,    false, false)
on conflict (code) do nothing;

-- ---- Skills / specialised gear capability ----
insert into skills (name, description)
select v.name, v.descr from (values
  ('Steadicam', 'Operate a Steadicam stabiliser rig'),
  ('Gimbal', 'Operate a motorised gimbal'),
  ('GoPro', 'Action camera setup and operation'),
  ('Drone', 'Licensed drone / aerial operator'),
  ('Pocket Cam', 'Compact / pocket camera operation'),
  ('Jib', 'Operate a camera jib / crane'),
  ('Wireless Mic Kit', 'Wireless microphone rigging')
) as v(name, descr)
where not exists (select 1 from skills s where s.name = v.name);

-- ---- Production catalog ----
insert into productions (name, kind, default_location, is_live, content_department_id)
select v.name, v.kind, v.loc::location_type, v.live,
       (select id from departments d where d.name = v.dept and d.parent_id is null)
from (values
  -- daily (Mon–Fri)
  ('Good Morning Namibia', 'daily', 'studio_2', true,  null),
  ('Namibia Connects',     'daily', 'studio_1', false, null),
  ('1 o''Clock News',      'daily', 'studio_1', true,  'News'),
  ('Eye on SADC',          'daily', 'studio_1', false, null),
  ('Indigenous News',      'daily', 'studio_1', false, 'News'),
  ('Daily Round-Up',       'daily', 'studio_2', false, null),
  ('8 o''Clock News',      'daily', 'studio_1', true,  'News'),
  ('Sport News',           'daily', 'studio_2', false, 'Sports'),
  -- weekend
  ('Sports Breakfast Show',     'weekend', 'studio_2', false, 'Sports'),
  ('Morning News Highlights',   'weekend', 'studio_1', false, 'News'),
  ('4 o''Clock News Highlights','weekend', 'studio_1', false, 'News'),
  ('6 o''Clock News Highlights','weekend', 'studio_1', false, 'News'),
  ('Wheels of Justice',         'weekend', 'studio_2', false, null),
  -- flagship live
  ('Talk of the Nation', 'flagship', 'studio_2', true, null),
  ('Business Today',     'flagship', 'studio_2', true, null),
  ('Tupopyeni',          'flagship', 'studio_2', true, null),
  ('Whatagwan',          'flagship', 'studio_4', true, null),
  ('Situation Kritical', 'flagship', 'studio_2', true, null),
  ('Soccer Pitch',       'flagship', 'studio_2', true, 'Sports'),
  -- recorded: Current Affairs
  ('Inside the Chambers',   'recorded', 'studio_2', false, 'Current Affairs'),
  ('One on One',            'recorded', 'studio_2', false, 'Current Affairs'),
  ('Public Service Corner', 'recorded', 'studio_2', false, 'Current Affairs'),
  -- recorded: Content Hub
  ('Toucy T Show',   'recorded', 'studio_2', false, 'Content Hub'),
  ('Man Unfiltered', 'recorded', 'studio_2', false, 'Content Hub'),
  ('New Season',     'recorded', null,       false, 'Content Hub'),
  ('Unrooted',       'recorded', null,       false, 'Content Hub'),
  ('@AM Saturday',   'recorded', 'studio_4', false, 'Content Hub'),
  ('@AM Sunday',     'recorded', 'studio_4', false, 'Content Hub'),
  ('On My Playlist', 'recorded', 'studio_2', false, 'Content Hub'),
  ('Sunshine Club',  'recorded', null,       false, 'Content Hub'),
  ('Just Teenz',     'recorded', null,       false, 'Content Hub'),
  -- recorded: Sports
  ('Sports Essentials', 'recorded', 'studio_2', false, 'Sports'),
  ('Absolute Rugby',    'recorded', 'studio_2', false, 'Sports'),
  -- location shoots
  ('Tutaleni',          'location', 'on_location', false, null),
  ('Legends of Change', 'location', 'on_location', false, null),
  ('Miss Namibia',      'location', 'on_location', false, null),
  ('In The Community',  'location', 'on_location', false, null),
  ('Sports Uncovered',  'location', 'on_location', false, 'Sports'),
  ('Twin Turbo',        'location', 'on_location', false, null),
  ('She Means Business','location', 'on_location', false, null),
  ('Whatalifestyle',    'location', 'on_location', false, null)
) as v(name, kind, loc, live, dept)
where not exists (select 1 from productions p where p.name = v.name);


-- ============================================================
-- supabase/migrations/0004_execution.sql
-- ============================================================
-- ===========================================================================
-- NBC PBRMS — 0004 execution
-- Lets a producer drive their own booking on the day: confirm attendance (via
-- the existing booking_crew policy) and move the booking through
-- confirmed -> in_progress -> completed while logging start/end times.
-- ===========================================================================

create policy "producer runs own booking" on bookings
  for update to authenticated
  using (producer_id = auth.uid() and status in ('confirmed', 'in_progress'))
  with check (producer_id = auth.uid());


-- ============================================================
-- supabase/migrations/0005_storeroom.sql
-- ============================================================
-- ===========================================================================
-- NBC PBRMS — 0005 storeroom
-- Make equipment bookings readable to all authenticated users (so operators can
-- see "who has what gear"), and seed a starter inventory.
-- ===========================================================================

create policy "equipment bookings readable by authenticated" on equipment_bookings
  for select to authenticated using (true);

insert into equipment (name, category, serial_number, home_location, status)
select v.name, v.category, v.serial, v.loc::location_type, 'available'::equipment_status
from (values
  ('Sony HDC Camera 1', 'Camera', 'CAM-001', 'studio_2'),
  ('Sony HDC Camera 2', 'Camera', 'CAM-002', 'studio_1'),
  ('Camera Tripod', 'Camera', 'TRP-001', null),
  ('Steadicam Rig', 'Specialised', 'STK-001', null),
  ('DJI Drone', 'Specialised', 'DRN-001', null),
  ('GoPro Hero 12', 'Specialised', 'GPR-001', null),
  ('DJI Gimbal', 'Specialised', 'GMB-001', null),
  ('Pocket Cam', 'Specialised', 'PKT-001', null),
  ('Sennheiser Wireless Mic Kit', 'Sound', 'MIC-001', 'studio_2'),
  ('Boom Mic', 'Sound', 'BOOM-001', 'studio_2'),
  ('Mic Stand', 'Sound', 'STND-001', 'studio_2'),
  ('LED Light Panel', 'Lighting', 'LGT-001', 'studio_4')
) as v(name, category, serial, loc)
on conflict (serial_number) do nothing;


-- ============================================================
-- supabase/migrations/0006_app.sql
-- ============================================================
-- ===========================================================================
-- NBC PBRMS — 0006 Audio Post Production (APP) fast-track
-- APP sessions bypass the EP/supervisor/booking-officer pipeline: the producer
-- creates a confirmed audio_post booking and assigns the two APP operators
-- directly. This policy lets a producer crew *only* their own audio_post
-- booking; everything else still goes through TV Operations.
-- ===========================================================================

create policy "producer crews own app session" on booking_crew
  for insert to authenticated
  with check (
    exists (
      select 1 from bookings b
      where b.id = booking_crew.booking_id
        and b.producer_id = auth.uid()
        and b.location_type = 'audio_post'
    )
  );


-- ============================================================
-- supabase/migrations/0007_realtime.sql
-- ============================================================
-- ===========================================================================
-- NBC PBRMS — 0007 realtime
-- Broadcast notification inserts so the in-app bell/inbox update live without a
-- page refresh. RLS still applies to what each client may read.
-- ===========================================================================

alter publication supabase_realtime add table notifications;


-- ============================================================
-- supabase/migrations/0008_notifications_delivery.sql
-- ============================================================
-- ===========================================================================
-- NBC PBRMS — 0008 notification delivery
-- Live schedule updates (realtime on bookings + crew), an SMS preference for
-- operators without smartphones, and a table of Expo push tokens. The actual
-- push/SMS fan-out is done by the `notify-dispatch` Edge Function.
-- ===========================================================================

-- Broadcast booking + crew changes so schedule views update without a refresh.
alter publication supabase_realtime add table bookings;
alter publication supabase_realtime add table booking_crew;

-- Operators without a smartphone receive updates via SMS.
alter table profiles add column if not exists prefers_sms boolean not null default false;

-- Expo push tokens (one device may register many times; token is unique).
create table if not exists device_tokens (
  id          uuid primary key default gen_random_uuid(),
  profile_id  uuid not null references profiles(id) on delete cascade,
  token       text not null unique,
  platform    text,
  created_at  timestamptz not null default now(),
  updated_at  timestamptz not null default now()
);
create index if not exists idx_device_tokens_profile on device_tokens(profile_id);

alter table device_tokens enable row level security;
create policy "manage own device tokens" on device_tokens
  for all to authenticated
  using (profile_id = auth.uid())
  with check (profile_id = auth.uid());


