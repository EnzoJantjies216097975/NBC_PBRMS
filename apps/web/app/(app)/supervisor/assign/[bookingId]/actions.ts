'use server';

import { redirect } from 'next/navigation';
import { revalidatePath } from 'next/cache';
import { findCrewConflicts, fullName, type CrewBusyBlock } from '@nbc/shared';
import { createClient } from '@/lib/supabase/server';
import { notify } from '@/lib/notify';

interface Slot {
  profileId: string;
  roleLabel: string;
  deptId: string | null;
  needsCar: boolean;
  needsTransport: boolean;
}

function parseSlots(raw: string): Slot[] {
  try {
    const arr = JSON.parse(raw) as Slot[];
    return arr.filter((s) => s && s.profileId);
  } catch {
    return [];
  }
}

export async function assignCrew(formData: FormData) {
  const supabase = await createClient();
  const {
    data: { user },
  } = await supabase.auth.getUser();
  if (!user) redirect('/login');

  const bookingId = String(formData.get('booking_id') ?? '');
  const force = String(formData.get('force') ?? '') === '1';
  const slots = parseSlots(String(formData.get('payload') ?? '[]'));
  const back = `/supervisor/assign/${bookingId}`;

  if (!bookingId) redirect('/supervisor');
  if (slots.length === 0) redirect(`${back}?error=${encodeURIComponent('Select at least one crew member.')}`);

  // Reject duplicate operators across slots.
  const ids = slots.map((s) => s.profileId);
  if (new Set(ids).size !== ids.length) {
    redirect(`${back}?error=${encodeURIComponent('The same operator is selected in more than one slot.')}`);
  }

  const { data: booking } = await supabase
    .from('bookings')
    .select('id, title, producer_id, call_date, call_time, end_time, air_end')
    .eq('id', bookingId)
    .single();
  if (!booking) redirect('/supervisor');

  const windowEnd = booking!.end_time ?? booking!.air_end ?? booking!.call_time;

  // Authoritative conflict check (the client warns too, but never trust it).
  const { data: busyRows } = await supabase
    .from('booking_crew')
    .select('profile_id, bookings!inner(id, title, call_date, call_time, end_time, air_end, status)')
    .in('profile_id', ids)
    .in('status', ['proposed', 'confirmed', 'stand_in'])
    .eq('bookings.call_date', booking!.call_date)
    .neq('booking_id', bookingId)
    .not('bookings.status', 'in', '(cancelled,draft)');

  const busyByProfile = new Map<string, CrewBusyBlock[]>();
  for (const row of busyRows ?? []) {
    const bk = Array.isArray(row.bookings) ? row.bookings[0] : row.bookings;
    if (!bk) continue;
    const list = busyByProfile.get(row.profile_id) ?? [];
    list.push({
      bookingId: bk.id,
      title: bk.title,
      date: bk.call_date,
      start: bk.call_time,
      end: bk.end_time ?? bk.air_end ?? bk.call_time,
    });
    busyByProfile.set(row.profile_id, list);
  }

  if (!force) {
    const clashes: string[] = [];
    for (const slot of slots) {
      const conflicts = findCrewConflicts(
        { profileId: slot.profileId, date: booking!.call_date, start: booking!.call_time, end: windowEnd },
        busyByProfile.get(slot.profileId) ?? [],
      );
      if (conflicts.length > 0) clashes.push(`${slot.roleLabel}: already on "${conflicts[0]!.title}"`);
    }
    if (clashes.length > 0) {
      redirect(`${back}?error=${encodeURIComponent('Conflict — ' + clashes.join('; ') + '. Re-open and confirm to override.')}`);
    }
  }

  // Replace the crew set for this booking.
  await supabase.from('booking_crew').delete().eq('booking_id', bookingId);
  const { error: insertError } = await supabase.from('booking_crew').insert(
    slots.map((s) => ({
      booking_id: bookingId,
      profile_id: s.profileId,
      department_id: s.deptId,
      role_label: s.roleLabel,
      assigned_by: user.id,
      needs_car_booking: s.needsCar,
      needs_transport: s.needsTransport,
      status: 'proposed' as const,
    })),
  );
  if (insertError) redirect(`${back}?error=${encodeURIComponent(insertError.message)}`);

  await supabase.from('bookings').update({ status: 'crew_assigned' }).eq('id', bookingId);

  // Notify assigned operators + the producer.
  await notify(supabase, [
    ...slots.map((s) => ({
      recipient_id: s.profileId,
      type: 'crew_assigned',
      title: `You're booked: ${booking!.title}`,
      body: `${s.roleLabel} on ${booking!.call_date} at ${booking!.call_time.slice(0, 5)}.`,
      booking_id: bookingId,
    })),
    {
      recipient_id: booking!.producer_id,
      type: 'crew_assigned',
      title: `Crew assigned: ${booking!.title}`,
      body: `${slots.length} crew member(s) proposed by TV Operations.`,
      booking_id: bookingId,
    },
  ]);

  await supabase.from('audit_log').insert({
    booking_id: bookingId,
    actor_id: user.id,
    action: 'crew_assigned',
    detail: { count: slots.length, forced: force },
  });

  revalidatePath('/supervisor');
  redirect('/supervisor');
}

export async function declineBooking(formData: FormData) {
  const supabase = await createClient();
  const {
    data: { user },
  } = await supabase.auth.getUser();
  if (!user) redirect('/login');

  const bookingId = String(formData.get('booking_id') ?? '');
  const feedback = String(formData.get('supervisor_feedback') ?? '').trim();
  const back = `/supervisor/assign/${bookingId}`;
  if (!bookingId) redirect('/supervisor');
  if (!feedback) redirect(`${back}?error=${encodeURIComponent('State why you are sending it back.')}`);

  const { data: booking } = await supabase
    .from('bookings')
    .select('id, title, producer_id, executive_producer_id')
    .eq('id', bookingId)
    .single();
  if (!booking) redirect('/supervisor');

  await supabase
    .from('bookings')
    .update({ status: 'supervisor_changes_requested', supervisor_feedback: feedback })
    .eq('id', bookingId);

  await notify(supabase, [
    booking!.executive_producer_id
      ? {
          recipient_id: booking!.executive_producer_id,
          type: 'supervisor_declined',
          title: `Crewing issue: ${booking!.title}`,
          body: feedback,
          booking_id: bookingId,
        }
      : null,
    {
      recipient_id: booking!.producer_id,
      type: 'supervisor_declined',
      title: `Booking sent back: ${booking!.title}`,
      body: feedback,
      booking_id: bookingId,
    },
  ].filter((x): x is NonNullable<typeof x> => x !== null));

  await supabase.from('audit_log').insert({
    booking_id: bookingId,
    actor_id: user.id,
    action: 'supervisor_changes_requested',
    detail: { feedback },
  });

  revalidatePath('/supervisor');
  redirect('/supervisor');
}
