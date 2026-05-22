'use server';

import { redirect } from 'next/navigation';
import { revalidatePath } from 'next/cache';
import { createClient } from '@/lib/supabase/server';

export async function markRead(formData: FormData) {
  const supabase = await createClient();
  const {
    data: { user },
  } = await supabase.auth.getUser();
  if (!user) redirect('/login');

  const id = String(formData.get('id') ?? '');
  if (id) {
    await supabase
      .from('notifications')
      .update({ read: true })
      .eq('id', id)
      .eq('recipient_id', user.id);
  }

  revalidatePath('/notifications');
  revalidatePath('/', 'layout');
  redirect('/notifications');
}

export async function markAllRead() {
  const supabase = await createClient();
  const {
    data: { user },
  } = await supabase.auth.getUser();
  if (!user) redirect('/login');

  await supabase
    .from('notifications')
    .update({ read: true })
    .eq('recipient_id', user.id)
    .eq('read', false);

  revalidatePath('/notifications');
  revalidatePath('/', 'layout');
  redirect('/notifications');
}
