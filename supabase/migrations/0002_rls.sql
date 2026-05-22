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
