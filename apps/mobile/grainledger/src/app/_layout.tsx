import React, { useEffect } from 'react';
import { Stack } from 'expo-router';
import { StatusBar } from 'expo-status-bar';
import { SafeAreaProvider } from 'react-native-safe-area-context';
import { useAuthStore } from '../store/authStore';
import { useOfflineStore } from '../store/offlineStore';
import { useFieldStore } from '../store/fieldStore';
import { useMillStore } from '../store/millStore';

export default function RootLayout() {
  const initAuth = useAuthStore((s) => s.initAuth);
  const initQueue = useOfflineStore((s) => s.initQueue);
  const initFieldData = useFieldStore((s) => s.initFieldData);
  const initMillData = useMillStore((s) => s.initMillData);

  useEffect(() => {
    // Bootstrap data stores
    initAuth();
    initQueue();
    initFieldData();
    initMillData();
  }, [initAuth, initQueue, initFieldData, initMillData]);

  return (
    <SafeAreaProvider>
      <StatusBar style="light" />
      <Stack screenOptions={{ headerShown: false }}>
        <Stack.Screen name="(tabs)" options={{ headerShown: false }} />
        <Stack.Screen name="farmer-form" options={{ presentation: 'modal' }} />
        <Stack.Screen name="plot-form" options={{ presentation: 'modal' }} />
        <Stack.Screen name="spray-form" options={{ presentation: 'modal' }} />
        <Stack.Screen name="procurement-form" options={{ presentation: 'modal' }} />
        <Stack.Screen name="receiving-form" options={{ presentation: 'modal' }} />
        <Stack.Screen name="operation-form" options={{ presentation: 'modal' }} />
        <Stack.Screen name="lot-detail" options={{ presentation: 'card' }} />
        <Stack.Screen name="offline-queue" options={{ presentation: 'card' }} />
        <Stack.Screen name="login" options={{ presentation: 'card' }} />
      </Stack>
    </SafeAreaProvider>
  );
}
