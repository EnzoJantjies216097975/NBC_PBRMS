'use server';

import { redirect } from 'next/navigation';
import { revalidatePath } from 'next/cache';
import { createClient } from '@/lib/supabase/server';

export async function updateOperator(formData: FormData) {
  const supabase = await createClient();
  const {
    data: { user },
  } = await supabase.auth.getUser();
  if (!user) redirect('/login');

  const profileId = String(formData.get('profile_id') ?? '');
  if (!profileId) redirect('/supervisor/operators');

  const needsTransport = formData.get('needs_transport') === 'on';
  const prefersSms = formData.get('prefers_sms') === 'on';
  const canGoOnTrips = formData.get('can_go_on_trips') === 'on';
  const skillIds = formData.getAll('skill_ids').map(String).filter(Boolean);

  await supabase
    .from('profiles')
    .update({ needs_transport: needsTransport, prefers_sms: prefersSms, can_go_on_trips: canGoOnTrips })
    .eq('id', profileId);

  // Replace the operator's skill set.
  await supabase.from('profile_skills').delete().eq('profile_id', profileId);
  if (skillIds.length > 0) {
    await supabase
      .from('profile_skills')
      .insert(skillIds.map((skillId) => ({ profile_id: profileId, skill_id: skillId })));
  }

  revalidatePath('/supervisor/operators');
  redirect('/supervisor/operators');
}
