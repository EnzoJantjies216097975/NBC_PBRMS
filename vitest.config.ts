import { defineConfig } from 'vitest/config';
import { fileURLToPath } from 'node:url';

const r = (p: string) => fileURLToPath(new URL(p, import.meta.url));

export default defineConfig({
  test: {
    environment: 'node',
    include: ['packages/shared/src/**/*.test.ts', 'apps/web/lib/**/*.test.ts'],
  },
  resolve: {
    alias: {
      '@nbc/shared': r('./packages/shared/src/index.ts'),
      '@': r('./apps/web'),
    },
  },
});
