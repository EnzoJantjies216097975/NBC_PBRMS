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
