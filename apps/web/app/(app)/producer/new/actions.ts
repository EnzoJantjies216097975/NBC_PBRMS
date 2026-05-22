'use server';

import { redirect } from 'next/navigation';
import { revalidatePath } from 'next/cache';
import type { LocationType } from '@nbc/shared';
import { createClient } from '@/lib/supabase/server';

export async function createBooking(formData: FormData) {
  const supabase = await createClient();
  const {
    data: { user },
  } = await supabase.auth.getUser();
  if (!user) redirect('/login');

  const { data: profile } = await supabase
    .from('profiles')
    .select('id, department_id')
    .eq('id', user.id)
    .single();

  const s = (k: string) => {
    const v = String(formData.get(k) ?? '').trim();
    return v === '' ? null : v;
  };
  const bool = (k: string) => formData.get(k) === 'on';
  const intent = String(formData.get('intent') ?? 'draft');

  const title = s('title');
  const locationType = s('location_type') as LocationType | null;
  const callDate = s('call_date');
  const callTime = s('call_time');

  if (!title || !locationType || !callDate || !callTime) {
    redirect('/producer/new?error=' + encodeURIComponent('Title, location, call date and time are required.'));
  }

  const durationRaw = s('duration_minutes');
  const submitting = intent === 'submit';

  const { error } = await supabase.from('bookings').insert({
    title: title!,
    producer_id: user.id,
    content_department_id: profile?.department_id ?? null,
    production_id: s('production_id'),
    location_type: locationType!,
    venue: s('venue'),
    air_date: s('air_date'),
    air_start: s('air_start'),
    air_end: s('air_end'),
    call_date: callDate!,
    call_time: callTime!,
    end_date: s('end_date'),
    end_time: s('end_time'),
    duration_minutes: durationRaw ? Number(durationRaw) : null,
    needs_ob_van: bool('needs_ob_van'),
    is_streaming: bool('is_streaming'),
    uses_kiloview: bool('uses_kiloview'),
    is_once_off: bool('is_once_off'),
    is_ad_hoc: bool('is_ad_hoc'),
    requires_car_booking: bool('requires_car_booking'),
    specialised_equipment: formData.getAll('specialised_equipment').map(String),
    notes: s('notes'),
    status: submitting ? 'submitted' : 'draft',
    submitted_at: submitting ? new Date().toISOString() : null,
  });

  if (error) {
    redirect('/producer/new?error=' + encodeURIComponent(error.message));
  }

  revalidatePath('/producer');
  redirect('/producer');
}
