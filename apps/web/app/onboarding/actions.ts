'use server';

import { redirect } from 'next/navigation';
import { revalidatePath } from 'next/cache';
import { createClient } from '@/lib/supabase/server';

export async function completeProfile(formData: FormData) {
  const supabase = await createClient();
  const {
    data: { user },
  } = await supabase.auth.getUser();
  if (!user) redirect('/login');

  const f = (k: string) => String(formData.get(k) ?? '').trim();

  const { error } = await supabase.from('profiles').upsert({
    id: user.id,
    first_name: f('first_name'),
    last_name: f('last_name'),
    work_email: f('work_email') || user.email || '',
    personal_email: f('personal_email') || null,
    date_of_birth: f('date_of_birth') || null,
    phone: f('phone') || null,
    physical_address: f('physical_address') || null,
  });
  if (error) redirect(`/onboarding?error=${encodeURIComponent(error.message)}`);

  revalidatePath('/', 'layout');
  redirect('/');
}
