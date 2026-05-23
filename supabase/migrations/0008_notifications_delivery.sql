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
