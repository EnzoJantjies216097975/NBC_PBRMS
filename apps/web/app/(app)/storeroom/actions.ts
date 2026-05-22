'use server';

import { redirect } from 'next/navigation';
import { revalidatePath } from 'next/cache';
import { createClient } from '@/lib/supabase/server';
import { notify } from '@/lib/notify';

const BACK = '/storeroom';

export async function requestEquipment(formData: FormData) {
  const supabase = await createClient();
  const {
    data: { user },
  } = await supabase.auth.getUser();
  if (!user) redirect('/login');

  const equipmentId = String(formData.get('equipment_id') ?? '');
  const bookingId = String(formData.get('booking_id') ?? '');
  const dueBack = String(formData.get('due_back_at') ?? '');
  const notes = String(formData.get('notes') ?? '').trim();
  if (!equipmentId) redirect(`${BACK}?error=${encodeURIComponent('Pick an item to request.')}`);

  const { error } = await supabase.from('equipment_bookings').insert({
    equipment_id: equipmentId,
    requested_by: user.id,
    booking_id: bookingId || null,
    due_back_at: dueBack ? new Date(dueBack).toISOString() : null,
    notes: notes || null,
    status: 'requested',
  });
  if (error) redirect(`${BACK}?error=${encodeURIComponent(error.message)}`);

  revalidatePath(BACK);
  redirect(BACK);
}

export async function approveCheckout(formData: FormData) {
  const supabase = await createClient();
  const {
    data: { user },
  } = await supabase.auth.getUser();
  if (!user) redirect('/login');

  const requestId = String(formData.get('request_id') ?? '');
  const equipmentId = String(formData.get('equipment_id') ?? '');
  const requesterId = String(formData.get('requester_id') ?? '');

  await supabase
    .from('equipment_bookings')
    .update({ status: 'checked_out', approved_by: user.id, checked_out_at: new Date().toISOString() })
    .eq('id', requestId);
  await supabase.from('equipment').update({ status: 'booked_out' }).eq('id', equipmentId);

  if (requesterId) {
    await notify(supabase, [
      {
        recipient_id: requesterId,
        type: 'equipment_approved',
        title: 'Equipment approved',
        body: 'Your equipment request was approved and checked out to you.',
      },
    ]);
  }

  revalidatePath(BACK);
  redirect(BACK);
}

export async function denyRequest(formData: FormData) {
  const supabase = await createClient();
  const {
    data: { user },
  } = await supabase.auth.getUser();
  if (!user) redirect('/login');

  const requestId = String(formData.get('request_id') ?? '');
  const requesterId = String(formData.get('requester_id') ?? '');

  await supabase
    .from('equipment_bookings')
    .update({ status: 'denied', approved_by: user.id })
    .eq('id', requestId);

  if (requesterId) {
    await notify(supabase, [
      {
        recipient_id: requesterId,
        type: 'equipment_denied',
        title: 'Equipment request denied',
        body: 'Your equipment request was not approved — check with your supervisor.',
      },
    ]);
  }

  revalidatePath(BACK);
  redirect(BACK);
}

export async function markReturned(formData: FormData) {
  const supabase = await createClient();
  const {
    data: { user },
  } = await supabase.auth.getUser();
  if (!user) redirect('/login');

  const requestId = String(formData.get('request_id') ?? '');
  const equipmentId = String(formData.get('equipment_id') ?? '');

  await supabase
    .from('equipment_bookings')
    .update({ status: 'returned', returned_at: new Date().toISOString() })
    .eq('id', requestId);
  await supabase.from('equipment').update({ status: 'available' }).eq('id', equipmentId);

  // Waitlist: tell the next person waiting on this item that it's free.
  const { data: equipment } = await supabase
    .from('equipment')
    .select('name')
    .eq('id', equipmentId)
    .single();
  const { data: nextWaiting } = await supabase
    .from('equipment_bookings')
    .select('requested_by')
    .eq('equipment_id', equipmentId)
    .eq('status', 'requested')
    .order('created_at', { ascending: true })
    .limit(1)
    .maybeSingle();

  if (nextWaiting?.requested_by) {
    await notify(supabase, [
      {
        recipient_id: nextWaiting.requested_by,
        type: 'equipment_available',
        title: 'Equipment now available',
        body: `${equipment?.name ?? 'The item'} you requested has been returned and is available.`,
      },
    ]);
  }

  revalidatePath(BACK);
  redirect(BACK);
}
