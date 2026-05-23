import * as Device from 'expo-device';
import * as Notifications from 'expo-notifications';
import { supabase } from './supabase';

/**
 * Best-effort Expo push registration: asks permission, gets the device's Expo
 * push token, and stores it in `device_tokens` for the signed-in user. Safe to
 * call on every login — `device_tokens.token` is unique. Silently no-ops on
 * simulators or when a projectId isn't configured (dev).
 */
export async function registerForPush(userId: string): Promise<void> {
  try {
    if (!Device.isDevice) return;

    const existing = await Notifications.getPermissionsAsync();
    const status =
      existing.status === 'granted'
        ? existing.status
        : (await Notifications.requestPermissionsAsync()).status;
    if (status !== 'granted') return;

    const { data: token } = await Notifications.getExpoPushTokenAsync();
    if (!token) return;

    await supabase
      .from('device_tokens')
      .upsert({ profile_id: userId, token, platform: Device.osName ?? null }, { onConflict: 'token' });
  } catch {
    // Push is best-effort; in-app + SMS still deliver.
  }
}
