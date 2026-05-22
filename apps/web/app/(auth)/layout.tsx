export default function AuthLayout({ children }: { children: React.ReactNode }) {
  return (
    <div className="flex min-h-screen items-center justify-center bg-gradient-to-br from-nbc-dark to-nbc px-4 py-10">
      <div className="w-full max-w-md">
        <div className="mb-6 text-center text-white">
          <h1 className="text-3xl font-bold tracking-tight">NBC PBRMS</h1>
          <p className="text-sm text-white/80">Production Booking &amp; Roster Management</p>
        </div>
        <div className="card">{children}</div>
      </div>
    </div>
  );
}
