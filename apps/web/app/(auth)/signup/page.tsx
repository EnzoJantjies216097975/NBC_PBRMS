import Link from 'next/link';
import { signup } from '../actions';

export default async function SignupPage({
  searchParams,
}: {
  searchParams: Promise<{ error?: string }>;
}) {
  const { error } = await searchParams;

  return (
    <div>
      <h2 className="mb-1 text-xl font-semibold">Create your profile</h2>
      <p className="mb-4 text-sm text-slate-600">
        A Supervisor / Executive Producer / Admin will assign your role and department after sign-up.
      </p>
      {error && (
        <p className="mb-4 rounded-md bg-red-50 px-3 py-2 text-sm text-red-700">{error}</p>
      )}
      <form action={signup} className="space-y-4">
        <div className="grid grid-cols-2 gap-3">
          <div>
            <label className="label" htmlFor="first_name">
              Name
            </label>
            <input className="input" id="first_name" name="first_name" required />
          </div>
          <div>
            <label className="label" htmlFor="last_name">
              Surname
            </label>
            <input className="input" id="last_name" name="last_name" required />
          </div>
        </div>
        <div>
          <label className="label" htmlFor="work_email">
            Work email
          </label>
          <input className="input" id="work_email" name="work_email" type="email" required />
        </div>
        <div>
          <label className="label" htmlFor="personal_email">
            Personal email
          </label>
          <input className="input" id="personal_email" name="personal_email" type="email" />
        </div>
        <div className="grid grid-cols-2 gap-3">
          <div>
            <label className="label" htmlFor="date_of_birth">
              Date of birth
            </label>
            <input className="input" id="date_of_birth" name="date_of_birth" type="date" />
          </div>
          <div>
            <label className="label" htmlFor="phone">
              Phone number
            </label>
            <input className="input" id="phone" name="phone" type="tel" />
          </div>
        </div>
        <div>
          <label className="label" htmlFor="physical_address">
            Physical address
          </label>
          <input className="input" id="physical_address" name="physical_address" />
        </div>
        <div>
          <label className="label" htmlFor="password">
            Password
          </label>
          <input
            className="input"
            id="password"
            name="password"
            type="password"
            required
            minLength={8}
            autoComplete="new-password"
          />
        </div>
        <button className="btn-primary w-full" type="submit">
          Create account
        </button>
      </form>
      <p className="mt-4 text-center text-sm text-slate-600">
        Already registered?{' '}
        <Link className="font-medium text-nbc hover:underline" href="/login">
          Sign in
        </Link>
      </p>
    </div>
  );
}
