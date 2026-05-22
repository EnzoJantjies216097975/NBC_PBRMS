import { redirect } from 'next/navigation';
import { createClient } from '@/lib/supabase/server';
import { completeProfile } from './actions';

export default async function OnboardingPage({
  searchParams,
}: {
  searchParams: Promise<{ error?: string }>;
}) {
  const { error } = await searchParams;
  const supabase = await createClient();
  const {
    data: { user },
  } = await supabase.auth.getUser();
  if (!user) redirect('/login');

  return (
    <div className="mx-auto max-w-lg px-4 py-10">
      <div className="card">
        <h1 className="mb-1 text-xl font-semibold">Complete your profile</h1>
        <p className="mb-4 text-sm text-slate-600">We just need a few details to set you up.</p>
        {error && (
          <p className="mb-4 rounded-md bg-red-50 px-3 py-2 text-sm text-red-700">{error}</p>
        )}
        <form action={completeProfile} className="space-y-4">
          <div className="grid grid-cols-2 gap-3">
            <input className="input" name="first_name" placeholder="Name" required />
            <input className="input" name="last_name" placeholder="Surname" required />
          </div>
          <input
            className="input"
            name="work_email"
            type="email"
            placeholder="Work email"
            defaultValue={user.email ?? ''}
            required
          />
          <input className="input" name="personal_email" type="email" placeholder="Personal email" />
          <div className="grid grid-cols-2 gap-3">
            <input className="input" name="date_of_birth" type="date" />
            <input className="input" name="phone" type="tel" placeholder="Phone number" />
          </div>
          <input className="input" name="physical_address" placeholder="Physical address" />
          <button className="btn-primary w-full" type="submit">
            Save and continue
          </button>
        </form>
      </div>
    </div>
  );
}
