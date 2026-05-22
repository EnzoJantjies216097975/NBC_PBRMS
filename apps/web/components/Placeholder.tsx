/** Lightweight "coming in a later phase" panel so navigation stays coherent. */
export function Placeholder({ title, children }: { title: string; children?: React.ReactNode }) {
  return (
    <div className="mx-auto max-w-4xl">
      <h1 className="mb-2 text-2xl font-semibold">{title}</h1>
      <div className="card border-dashed">
        <p className="text-sm text-slate-600">{children ?? 'This module is scaffolded and arrives in a later phase.'}</p>
      </div>
    </div>
  );
}
