import Link from 'next/link';
import { login } from '../actions';

export default async function LoginPage({
  searchParams,
}: {
  searchParams: Promise<{ error?: string }>;
}) {
  const { error } = await searchParams;

  return (
    <div>
      <h2 className="mb-4 text-xl font-semibold">Sign in</h2>
      {error && (
        <p className="mb-4 rounded-md bg-red-50 px-3 py-2 text-sm text-red-700">{error}</p>
      )}
      <form action={login} className="space-y-4">
        <div>
          <label className="label" htmlFor="email">
            Work email
          </label>
          <input className="input" id="email" name="email" type="email" required autoComplete="email" />
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
            autoComplete="current-password"
          />
        </div>
        <button className="btn-primary w-full" type="submit">
          Sign in
        </button>
      </form>
      <p className="mt-4 text-center text-sm text-slate-600">
        No account?{' '}
        <Link className="font-medium text-nbc hover:underline" href="/signup">
          Create one
        </Link>
      </p>
    </div>
  );
}
