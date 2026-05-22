'use server';

import { redirect } from 'next/navigation';
import { revalidatePath } from 'next/cache';
import { createClient } from '@/lib/supabase/server';
import { notify } from '@/lib/notify';

export async function confirmBooking(formData: FormData) {
  const supabase = await createClient();
  const {
    data: { user },
  } = await supabase.auth.getUser();
  if (!user) redirect('/login');

  const bookingId = String(formData.get('booking_id') ?? '');
  if (!bookingId) redirect('/booking-officer/bookings');
  const back = `/booking-officer/bookings/${bookingId}`;

  const { data: booking } = await supabase
    .from('bookings')
    .select('id, title, producer_id, call_date, call_time')
    .eq('id', bookingId)
    .single();
  if (!booking) redirect('/booking-officer/bookings');

  const { error } = await supabase
    .from('bookings')
    .update({ status: 'confirmed', confirmed_at: new Date().toISOString() })
    .eq('id', bookingId);
  if (error) redirect(`${back}?error=${encodeURIComponent(error.message)}`);

  await supabase.from('booking_crew').update({ status: 'confirmed' }).eq('booking_id', bookingId).eq('status', 'proposed');

  const { data: crew } = await supabase
    .from('booking_crew')
    .select('profile_id')
    .eq('booking_id', bookingId);

  await notify(supabase, [
    {
      recipient_id: booking.producer_id,
      type: 'booking_confirmed',
      title: `Confirmed: ${booking.title}`,
      body: `Crew is locked in for ${booking.call_date} at ${booking.call_time.slice(0, 5)}.`,
      booking_id: bookingId,
    },
    ...(crew ?? []).map((c) => ({
      recipient_id: c.profile_id,
      type: 'booking_confirmed',
      title: `Confirmed: ${booking.title}`,
      body: `Your booking is confirmed for ${booking.call_date} at ${booking.call_time.slice(0, 5)}.`,
      booking_id: bookingId,
    })),
  ]);

  await supabase.from('audit_log').insert({
    booking_id: bookingId,
    actor_id: user.id,
    action: 'confirmed',
  });

  revalidatePath(back);
  redirect(back);
}
