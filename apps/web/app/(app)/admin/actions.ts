'use server';

import { redirect } from 'next/navigation';
import { revalidatePath } from 'next/cache';
import { USER_ROLES, type UserRole } from '@nbc/shared';
import { createClient } from '@/lib/supabase/server';

export async function updateMember(formData: FormData) {
  const supabase = await createClient();
  const {
    data: { user },
  } = await supabase.auth.getUser();
  if (!user) redirect('/login');

  const profileId = String(formData.get('profile_id') ?? '');
  const role = String(formData.get('role') ?? '') as UserRole;
  const departmentId = String(formData.get('department_id') ?? '');
  const active = formData.get('active') === 'on';
  if (!profileId || !USER_ROLES.includes(role)) redirect('/admin');

  const { error } = await supabase
    .from('profiles')
    .update({ role, department_id: departmentId || null, active })
    .eq('id', profileId);
  if (error) redirect('/admin?error=' + encodeURIComponent(error.message));

  await supabase.from('audit_log').insert({
    actor_id: user.id,
    action: 'member_updated',
    detail: { profile_id: profileId, role, department_id: departmentId || null, active },
  });

  revalidatePath('/admin');
  redirect('/admin');
}
