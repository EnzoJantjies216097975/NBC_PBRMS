import { redirect } from 'next/navigation';
import { ROLE_HOME } from '@nbc/shared';
import { createClient } from '@/lib/supabase/server';

/** Entry point — route the user to their role's home, or to login/onboarding. */
export default async function Home() {
  const supabase = await createClient();
  const {
    data: { user },
  } = await supabase.auth.getUser();

  if (!user) redirect('/login');

  const { data: profile } = await supabase
    .from('profiles')
    .select('role')
    .eq('id', user.id)
    .maybeSingle();

  if (!profile) redirect('/onboarding');
  redirect(ROLE_HOME[profile.role]);
}
