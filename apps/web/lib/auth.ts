import { redirect } from 'next/navigation';
import type { Profile } from '@nbc/shared';
import { createClient } from '@/lib/supabase/server';

/**
 * Returns the signed-in user's auth record + profile row. Redirects to /login
 * if not authenticated, or /onboarding if the profile hasn't been created yet.
 */
export async function requireProfile(): Promise<{ userId: string; profile: Profile }> {
  const supabase = await createClient();
  const {
    data: { user },
  } = await supabase.auth.getUser();

  if (!user) redirect('/login');

  const { data: profile } = await supabase
    .from('profiles')
    .select('*')
    .eq('id', user.id)
    .maybeSingle();

  if (!profile) redirect('/onboarding');

  return { userId: user.id, profile };
}

/** Like requireProfile, but enforces one of the allowed roles. */
export async function requireRole(allowed: Profile['role'][]) {
  const { userId, profile } = await requireProfile();
  if (!allowed.includes(profile.role)) redirect('/');
  return { userId, profile };
}
