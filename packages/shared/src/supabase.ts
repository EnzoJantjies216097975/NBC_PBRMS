/**
 * Typed Supabase client factory.
 *
 * Platform-specific concerns (cookie storage on Next.js, AsyncStorage on Expo)
 * are passed in by each app. The web app generally uses `@supabase/ssr`
 * directly with the `Database` generic; this generic factory is convenient for
 * the mobile app and for any plain client usage.
 */

import { createClient, type SupabaseClient, type SupabaseClientOptions } from '@supabase/supabase-js';
import type { Database } from './database.types';

export type TypedSupabaseClient = SupabaseClient<Database>;

export function createSupabaseClient(
  url: string,
  anonKey: string,
  options?: SupabaseClientOptions<'public'>,
): TypedSupabaseClient {
  if (!url || !anonKey) {
    throw new Error(
      'Supabase URL and anon key are required. Check your environment variables.',
    );
  }
  return createClient<Database>(url, anonKey, options);
}
