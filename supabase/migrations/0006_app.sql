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
