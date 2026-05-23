'use client';

import { useEffect } from 'react';
import { useRouter } from 'next/navigation';
import { createClient } from '@/lib/supabase/client';

/**
 * Refreshes the current route whenever any booking or crew assignment changes,
 * so schedule/calendar views stay live. Renders nothing. Mount on pages that
 * show bookings (daily calendar, EP calendar, supervisor/operator schedules).
 */
export function ScheduleRealtime() {
  const router = useRouter();

  useEffect(() => {
    const supabase = createClient();
    const channel = supabase
      .channel('schedule-refresh')
      .on('postgres_changes', { event: '*', schema: 'public', table: 'bookings' }, () => router.refresh())
      .on('postgres_changes', { event: '*', schema: 'public', table: 'booking_crew' }, () => router.refresh())
      .subscribe();

    return () => {
      void supabase.removeChannel(channel);
    };
  }, [router]);

  return null;
}
