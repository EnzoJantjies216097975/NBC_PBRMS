/**
 * Safe Supabase type generation.
 *
 * Unlike `supabase gen types ... > file` (which truncates the file *before* the
 * command runs — so a failure leaves you with an empty Database type), this only
 * overwrites database.types.ts when generation actually succeeds.
 *
 * Usage:
 *   node scripts/gen-types.cjs <project-ref>     # hosted project
 *   SUPABASE_PROJECT_ID=<ref> node scripts/gen-types.cjs
 *   node scripts/gen-types.cjs --local           # local supabase (needs Docker)
 *
 * Requires the Supabase CLI (`npm i -g supabase`) and, for a hosted project,
 * `supabase login` first.
 */
const { execFileSync } = require('node:child_process');
const { writeFileSync } = require('node:fs');
const path = require('node:path');

const OUT = path.join('packages', 'shared', 'src', 'database.types.ts');
const arg = process.argv[2];
const ref = arg && arg !== '--local' ? arg : process.env.SUPABASE_PROJECT_ID;

const args = ['gen', 'types', 'typescript'];
if (arg === '--local') args.push('--local');
else if (ref) args.push(`--project-id=${ref}`);
else {
  console.error('No project ref. Pass one: `node scripts/gen-types.cjs <ref>`, set SUPABASE_PROJECT_ID, or use --local.');
  process.exit(1);
}

const cmd = process.platform === 'win32' ? 'supabase.cmd' : 'supabase';
try {
  const out = execFileSync(cmd, args, { encoding: 'utf8', maxBuffer: 64 * 1024 * 1024 });
  if (!out.includes('export type Database')) {
    console.error('Output did not look like generated types — leaving database.types.ts untouched.');
    process.exit(1);
  }
  writeFileSync(OUT, out);
  console.log(`Wrote ${OUT} (${out.length} bytes).`);
} catch (e) {
  console.error('gen-types failed:', e.message);
  console.error(`Left ${OUT} unchanged.`);
  process.exit(1);
}
