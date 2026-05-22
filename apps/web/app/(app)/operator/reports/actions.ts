'use server';

import { redirect } from 'next/navigation';
import { revalidatePath } from 'next/cache';
import type { Severity } from '@nbc/shared';
import { createClient } from '@/lib/supabase/server';

export async function submitReport(formData: FormData) {
  const supabase = await createClient();
  const {
    data: { user },
  } = await supabase.auth.getUser();
  if (!user) redirect('/login');

  const title = String(formData.get('title') ?? '').trim();
  const severity = String(formData.get('severity') ?? 'low') as Severity;
  const body = String(formData.get('body') ?? '').trim();
  const bookingId = String(formData.get('booking_id') ?? '');

  if (!title) redirect('/operator/reports?error=' + encodeURIComponent('A short title is required.'));

  const { data: profile } = await supabase
    .from('profiles')
    .select('department_id')
    .eq('id', user.id)
    .single();

  const { error } = await supabase.from('production_reports').insert({
    author_id: user.id,
    department_id: profile?.department_id ?? null,
    severity,
    title,
    body: body || null,
    booking_id: bookingId || null,
  });
  if (error) redirect('/operator/reports?error=' + encodeURIComponent(error.message));

  revalidatePath('/operator/reports');
  redirect('/operator/reports');
}
