import { redirect } from 'next/navigation';

// The storeroom moved to a shared top-level route accessible to all roles.
export default function LegacyOperatorStoreroom() {
  redirect('/storeroom');
}
