import { useEffect, useState } from 'react';
import {
  ActivityIndicator,
  Alert,
  Pressable,
  SafeAreaView,
  ScrollView,
  StyleSheet,
  Text,
  TextInput,
  View,
} from 'react-native';
import { StatusBar } from 'expo-status-bar';
import type { Session } from '@supabase/supabase-js';
import { USER_ROLE_LABELS, fullName, type Profile } from '@nbc/shared';
import { supabase } from './lib/supabase';

export default function App() {
  const [loading, setLoading] = useState(true);
  const [session, setSession] = useState<Session | null>(null);

  useEffect(() => {
    supabase.auth.getSession().then(({ data }) => {
      setSession(data.session);
      setLoading(false);
    });
    const { data: sub } = supabase.auth.onAuthStateChange((_event, s) => setSession(s));
    return () => sub.subscription.unsubscribe();
  }, []);

  if (loading) {
    return (
      <SafeAreaView style={styles.center}>
        <ActivityIndicator color="#0a4d8c" />
      </SafeAreaView>
    );
  }

  return (
    <SafeAreaView style={styles.flex}>
      <StatusBar style="light" />
      {session ? <Home /> : <SignIn />}
    </SafeAreaView>
  );
}

function SignIn() {
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [busy, setBusy] = useState(false);

  async function onSignIn() {
    setBusy(true);
    const { error } = await supabase.auth.signInWithPassword({ email: email.trim(), password });
    setBusy(false);
    if (error) Alert.alert('Sign in failed', error.message);
  }

  return (
    <View style={styles.authWrap}>
      <Text style={[styles.brand, styles.brandLight]}>NBC PBRMS</Text>
      <Text style={styles.subtitle}>Production Booking &amp; Roster</Text>
      <TextInput
        style={styles.input}
        placeholder="Work email"
        autoCapitalize="none"
        keyboardType="email-address"
        value={email}
        onChangeText={setEmail}
      />
      <TextInput
        style={styles.input}
        placeholder="Password"
        secureTextEntry
        value={password}
        onChangeText={setPassword}
      />
      <Pressable style={styles.button} onPress={onSignIn} disabled={busy}>
        <Text style={styles.buttonText}>{busy ? 'Signing in…' : 'Sign in'}</Text>
      </Pressable>
      <Text style={styles.hint}>Use the same credentials as the web app.</Text>
    </View>
  );
}

function Home() {
  const [profile, setProfile] = useState<Profile | null>(null);

  useEffect(() => {
    (async () => {
      const { data: auth } = await supabase.auth.getUser();
      if (!auth.user) return;
      const { data } = await supabase.from('profiles').select('*').eq('id', auth.user.id).maybeSingle();
      setProfile(data ?? null);
    })();
  }, []);

  return (
    <ScrollView contentContainerStyle={styles.homeWrap}>
      <Text style={styles.brand}>Welcome{profile ? `, ${fullName(profile)}` : ''}</Text>
      {profile && <Text style={styles.role}>{USER_ROLE_LABELS[profile.role]}</Text>}
      <Text style={styles.body}>
        Your schedule, notifications, and overtime will appear here. This is the mobile auth
        skeleton wired to the same Supabase backend as the web app.
      </Text>
      <Pressable style={[styles.button, styles.signout]} onPress={() => supabase.auth.signOut()}>
        <Text style={styles.buttonText}>Sign out</Text>
      </Pressable>
    </ScrollView>
  );
}

const styles = StyleSheet.create({
  flex: { flex: 1, backgroundColor: '#f8fafc' },
  center: { flex: 1, alignItems: 'center', justifyContent: 'center' },
  authWrap: { flex: 1, padding: 24, justifyContent: 'center', backgroundColor: '#063662' },
  brand: { fontSize: 28, fontWeight: '700', color: '#0a4d8c' },
  brandLight: { color: '#ffffff' },
  subtitle: { color: '#cbd5e1', marginBottom: 24 },
  input: {
    backgroundColor: '#fff',
    borderRadius: 8,
    paddingHorizontal: 12,
    paddingVertical: 12,
    marginBottom: 12,
  },
  button: { backgroundColor: '#0a4d8c', borderRadius: 8, paddingVertical: 14, alignItems: 'center' },
  signout: { backgroundColor: '#e2231a', marginTop: 24 },
  buttonText: { color: '#fff', fontWeight: '600' },
  hint: { color: '#cbd5e1', marginTop: 12, fontSize: 12, textAlign: 'center' },
  homeWrap: { padding: 24, gap: 8 },
  role: { color: '#475569', marginBottom: 12 },
  body: { color: '#334155', lineHeight: 20 },
});
