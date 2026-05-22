'use server';

import { redirect } from 'next/navigation';
import { revalidatePath } from 'next/cache';
import type { Channel } from '@nbc/shared';
import { createClient } from '@/lib/supabase/server';

export async function approveBooking(formData: FormData) {
  const supabase = await createClient();
  const {
    data: { user },
  } = await supabase.auth.getUser();
  if (!user) redirect('/login');

  const id = String(formData.get('booking_id') ?? '');
  const channel = String(formData.get('channel') ?? '') as Channel;
  if (!id || !channel) redirect('/exec-producer?error=' + encodeURIComponent('Pick a channel to approve.'));

  const { error } = await supabase
    .from('bookings')
    .update({
      status: 'ep_approved',
      channel,
      executive_producer_id: user.id,
      ep_feedback: null,
    })
    .eq('id', id);
  if (error) redirect('/exec-producer?error=' + encodeURIComponent(error.message));

  await supabase.from('audit_log').insert({
    booking_id: id,
    actor_id: user.id,
    action: 'ep_approved',
    detail: { channel },
  });

  revalidatePath('/exec-producer');
  redirect('/exec-producer');
}

export async function requestChanges(formData: FormData) {
  const supabase = await createClient();
  const {
    data: { user },
  } = await supabase.auth.getUser();
  if (!user) redirect('/login');

  const id = String(formData.get('booking_id') ?? '');
  const feedback = String(formData.get('ep_feedback') ?? '').trim();
  if (!id || !feedback) {
    redirect('/exec-producer?error=' + encodeURIComponent('State what information is missing.'));
  }

  const { error } = await supabase
    .from('bookings')
    .update({ status: 'ep_changes_requested', ep_feedback: feedback, executive_producer_id: user.id })
    .eq('id', id);
  if (error) redirect('/exec-producer?error=' + encodeURIComponent(error.message));

  await supabase.from('audit_log').insert({
    booking_id: id,
    actor_id: user.id,
    action: 'ep_changes_requested',
    detail: { feedback },
  });

  revalidatePath('/exec-producer');
  redirect('/exec-producer');
}
