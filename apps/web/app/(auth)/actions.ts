'use server';

import { redirect } from 'next/navigation';
import { revalidatePath } from 'next/cache';
import { createClient } from '@/lib/supabase/server';

function fail(path: string, message: string): never {
  redirect(`${path}?error=${encodeURIComponent(message)}`);
}

export async function login(formData: FormData) {
  const email = String(formData.get('email') ?? '').trim();
  const password = String(formData.get('password') ?? '');
  if (!email || !password) fail('/login', 'Email and password are required.');

  const supabase = await createClient();
  const { error } = await supabase.auth.signInWithPassword({ email, password });
  if (error) fail('/login', error.message);

  revalidatePath('/', 'layout');
  redirect('/');
}

export async function signup(formData: FormData) {
  const f = (k: string) => String(formData.get(k) ?? '').trim();
  const workEmail = f('work_email');
  const password = String(formData.get('password') ?? '');
  const firstName = f('first_name');
  const lastName = f('last_name');

  if (!workEmail || !password || !firstName || !lastName) {
    fail('/signup', 'Name, surname, work email and password are required.');
  }
  if (password.length < 8) fail('/signup', 'Password must be at least 8 characters.');

  const supabase = await createClient();
  const { data, error } = await supabase.auth.signUp({
    email: workEmail,
    password,
    options: { data: { first_name: firstName, last_name: lastName } },
  });
  if (error) fail('/signup', error.message);
  if (!data.user) fail('/signup', 'Sign-up did not return a user. Check email confirmation settings.');

  // Create the profile row (role defaults to "operator"; a Supervisor / Exec
  // Producer / Admin assigns the real role + department afterwards).
  const { error: profileError } = await supabase.from('profiles').insert({
    id: data.user.id,
    first_name: firstName,
    last_name: lastName,
    work_email: workEmail,
    personal_email: f('personal_email') || null,
    date_of_birth: f('date_of_birth') || null,
    phone: f('phone') || null,
    physical_address: f('physical_address') || null,
  });
  // If there's no active session yet (email confirmation on), the insert is
  // blocked by RLS — onboarding will finish it after first login.
  if (profileError && !profileError.message.toLowerCase().includes('row-level security')) {
    fail('/signup', profileError.message);
  }

  revalidatePath('/', 'layout');
  redirect('/');
}

export async function signOut() {
  const supabase = await createClient();
  await supabase.auth.signOut();
  revalidatePath('/', 'layout');
  redirect('/login');
}
