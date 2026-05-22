'use server';

import { redirect } from 'next/navigation';
import { revalidatePath } from 'next/cache';
import { createClient } from '@/lib/supabase/server';
import { notify } from '@/lib/notify';

const BACK = '/producer/app';

export async function bookApp(formData: FormData) {
  const supabase = await createClient();
  const {
    data: { user },
  } = await supabase.auth.getUser();
  if (!user) redirect('/login');

  const f = (k: string) => String(formData.get(k) ?? '').trim();
  const title = f('title');
  const callDate = f('call_date');
  const callTime = f('call_time');
  const endTime = f('end_time');
  const notes = f('notes');
  const opIds = [f('op1'), f('op2')].filter(Boolean);

  if (!title || !callDate || !callTime) {
    redirect(`${BACK}?error=${encodeURIComponent('Production name, date and start time are required.')}`);
  }

  const { data: profile } = await supabase
    .from('profiles')
    .select('department_id')
    .eq('id', user.id)
    .single();

  const { data: soundDept } = await supabase
    .from('departments')
    .select('id')
    .eq('name', 'Sound')
    .is('parent_id', null)
    .maybeSingle();

  // Fast-track: create the APP session already confirmed (bypasses the pipeline).
  const { data: booking, error } = await supabase
    .from('bookings')
    .insert({
      title,
      producer_id: user.id,
      content_department_id: profile?.department_id ?? null,
      location_type: 'audio_post',
      call_date: callDate,
      call_time: callTime,
      end_time: endTime || null,
      notes: notes || null,
      status: 'confirmed',
      confirmed_at: new Date().toISOString(),
    })
    .select('id')
    .single();
  if (error || !booking) {
    redirect(`${BACK}?error=${encodeURIComponent(error?.message ?? 'Could not create the APP session.')}`);
  }

  // Assign the (up to two) APP operators.
  const uniqueOps = [...new Set(opIds)];
  if (uniqueOps.length > 0) {
    await supabase.from('booking_crew').insert(
      uniqueOps.map((pid, i) => ({
        booking_id: booking!.id,
        profile_id: pid,
        department_id: soundDept?.id ?? null,
        role_label: `APP ${i + 1}`,
        assigned_by: user.id,
        status: 'confirmed' as const,
      })),
    );
  }

  // Keep the Sound Supervisor + Booking Officer informed; notify the operators.
  const { data: supervisors } = soundDept
    ? await supabase
        .from('profiles')
        .select('id')
        .eq('role', 'supervisor')
        .eq('department_id', soundDept.id)
    : { data: [] };
  const { data: bookingOfficers } = await supabase
    .from('profiles')
    .select('id')
    .eq('role', 'booking_officer');

  const informed = new Set<string>();
  for (const s of supervisors ?? []) informed.add(s.id);
  for (const b of bookingOfficers ?? []) informed.add(b.id);

  await notify(supabase, [
    ...[...informed].map((rid) => ({
      recipient_id: rid,
      type: 'app_booked',
      title: `APP session booked: ${title}`,
      body: `Audio Post Production on ${callDate} at ${callTime.slice(0, 5)} (fast-tracked by the producer).`,
      booking_id: booking!.id,
    })),
    ...uniqueOps.map((pid) => ({
      recipient_id: pid,
      type: 'crew_assigned',
      title: `APP session: ${title}`,
      body: `You're on Audio Post Production on ${callDate} at ${callTime.slice(0, 5)}.`,
      booking_id: booking!.id,
    })),
  ]);

  await supabase.from('audit_log').insert({
    booking_id: booking!.id,
    actor_id: user.id,
    action: 'app_booked',
    detail: { operators: uniqueOps.length },
  });

  revalidatePath(BACK);
  redirect(BACK);
}
