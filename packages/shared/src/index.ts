/**
 * @nbc/shared — framework-agnostic domain layer shared by the web and mobile
 * apps: enums, reference data, business rules (overtime + conflicts),
 * permissions, types, and the Supabase client factory.
 */

export * from './enums';
export * from './constants';
export * from './types';
export * from './permissions';
export * from './overtime';
export * from './conflicts';
export * from './recommend';
export * from './supabase';
export type { Database, Json, Tables, TablesInsert, TablesUpdate } from './database.types';
