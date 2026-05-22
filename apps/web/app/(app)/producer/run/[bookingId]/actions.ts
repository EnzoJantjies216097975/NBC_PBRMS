'use server';

import { redirect } from 'next/navigation';
import { revalidatePath } from 'next/cache';
import { LOCATION_TYPE_LABELS, computeProductionOverrun, type LocationType } from '@nbc/shared';
import { createClient } from '@/lib/supabase/server';
import { notify } from '@/lib/notify';

const back = (id: string) => `/producer/run/${id}`;

export async function confirmPresent(formData: FormData) {
  const supabase = await createClient();
  const {
    data: { user },
  } = await supabase.auth.getUser();
  if (!user) redirect('/login');

  const crewId = String(formData.get('crew_id') ?? '');
  const bookingId = String(formData.get('booking_id') ?? '');
  if (crewId) {
    await supabase
      .from('booking_crew')
      .update({ attended: true, attended_confirmed_by: user.id, attended_at: new Date().toISOString() })
      .eq('id', crewId);
  }
  revalidatePath(back(bookingId));
  redirect(back(bookingId));
}

export async function signalOperator(formData: FormData) {
  const supabase = await createClient();
  const {
    data: { user },
  } = await supabase.auth.getUser();
  if (!user) redirect('/login');

  const crewId = String(formData.get('crew_id') ?? '');
  const bookingId = String(formData.get('booking_id') ?? '');

  const { data: crew } = await supabase
    .from('booking_crew')
    .select('profile_id, booking:bookings(title, location_type)')
    .eq('id', crewId)
    .single();

  if (crew) {
    const bk = Array.isArray(crew.booking) ? crew.booking[0] : crew.booking;
    const where = bk ? LOCATION_TYPE_LABELS[bk.location_type as LocationType] : 'the studio';
    await notify(supabase, [
      {
        recipient_id: crew.profile_id,
        type: 'attendance_signal',
        title: `Please head to ${where}`,
        body: `The producer needs you at ${where} for "${bk?.title ?? 'your production'}" now.`,
        booking_id: bookingId,
      },
    ]);
  }
  revalidatePath(back(bookingId));
  redirect(back(bookingId));
}

export async function startProduction(formData: FormData) {
  const supabase = await createClient();
  const {
    data: { user },
  } = await supabase.auth.getUser();
  if (!user) redirect('/login');

  const bookingId = String(formData.get('booking_id') ?? '');
  if (!bookingId) redirect('/producer');

  await supabase
    .from('production_logs')
    .insert({ booking_id: bookingId, actual_start: new Date().toISOString(), started_by: user.id });
  await supabase.from('bookings').update({ status: 'in_progress' }).eq('id', bookingId);

  await supabase.from('audit_log').insert({ booking_id: bookingId, actor_id: user.id, action: 'production_started' });

  revalidatePath(back(bookingId));
  redirect(back(bookingId));
}

export async function endProduction(formData: FormData) {
  const supabase = await createClient();
  const {
    data: { user },
  } = await supabase.auth.getUser();
  if (!user) redirect('/login');

  const bookingId = String(formData.get('booking_id') ?? '');
  if (!bookingId) redirect('/producer');

  const { data: booking } = await supabase
    .from('bookings')
    .select('id, title, call_date, end_time, air_end')
    .eq('id', bookingId)
    .single();

  const { data: openLog } = await supabase
    .from('production_logs')
    .select('id, actual_start')
    .eq('booking_id', bookingId)
    .is('actual_end', null)
    .order('actual_start', { ascending: false })
    .limit(1)
    .maybeSingle();

  const now = new Date();

  // Overrun vs the booked end time → flagged as overtime awareness.
  let overrunMinutes = 0;
  const bookedEndTime = booking?.end_time ?? booking?.air_end;
  if (booking?.call_date && bookedEndTime) {
    const bookedEnd = new Date(`${booking.call_date}T${bookedEndTime}`);
    overrunMinutes = computeProductionOverrun(bookedEnd, now).minutesOver;
  }
  const isOvertime = overrunMinutes >= 30;

  if (openLog) {
    await supabase
      .from('production_logs')
      .update({
        actual_end: now.toISOString(),
        ended_by: user.id,
        is_overtime: isOvertime,
        overtime_minutes: overrunMinutes,
      })
      .eq('id', openLog.id);
  }
  await supabase.from('bookings').update({ status: 'completed' }).eq('id', bookingId);

  // If it ran long, make the crew's supervisors aware (overtime).
  if (isOvertime) {
    const { data: crew } = await supabase
      .from('booking_crew')
      .select('profile:profiles(supervisor_id)')
      .eq('booking_id', bookingId);
    const supervisorIds = new Set<string>();
    for (const c of crew ?? []) {
      const p = Array.isArray(c.profile) ? c.profile[0] : c.profile;
      if (p?.supervisor_id) supervisorIds.add(p.supervisor_id);
    }
    await notify(
      supabase,
      [...supervisorIds].map((sid) => ({
        recipient_id: sid,
        type: 'overtime_flag',
        title: `Overtime: ${booking?.title ?? 'production'}`,
        body: `Ran ${overrunMinutes} min past its booked end — crew overtime applies.`,
        booking_id: bookingId,
      })),
    );
  }

  await supabase.from('audit_log').insert({
    booking_id: bookingId,
    actor_id: user.id,
    action: 'production_ended',
    detail: { overrunMinutes, isOvertime },
  });

  revalidatePath(back(bookingId));
  redirect(back(bookingId));
}
