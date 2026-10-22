import React from 'react';
import { Tabs } from 'expo-router';
import { Platform } from 'react-native';
import { Colors } from '../../theme/colors';
import { Home, Sprout, Factory, QrCode, User } from 'lucide-react-native';

export default function TabLayout() {
  return (
    <Tabs
      screenOptions={{
        headerShown: false,
        tabBarActiveTintColor: Colors.primary,
        tabBarInactiveTintColor: Colors.textMuted,
        tabBarStyle: {
          backgroundColor: '#FFFFFF',
          borderTopWidth: 1,
          borderTopColor: Colors.border,
          height: Platform.OS === 'ios' ? 88 : 64,
          paddingBottom: Platform.OS === 'ios' ? 28 : 10,
          paddingTop: 8,
          elevation: 8,
          shadowColor: '#000',
          shadowOffset: { width: 0, height: -2 },
          shadowOpacity: 0.05,
          shadowRadius: 4,
        },
        tabBarLabelStyle: {
          fontSize: 11,
          fontWeight: '600',
        },
      }}
    >
      <Tabs.Screen
        name="index"
        options={{
          title: 'Tổng quan',
          tabBarIcon: ({ color, size }) => <Home size={size || 22} color={color} />,
        }}
      />
      <Tabs.Screen
        name="field"
        options={{
          title: 'Thực địa',
          tabBarIcon: ({ color, size }) => <Sprout size={size || 22} color={color} />,
        }}
      />
      <Tabs.Screen
        name="mill"
        options={{
          title: 'Nhà máy',
          tabBarIcon: ({ color, size }) => <Factory size={size || 22} color={color} />,
        }}
      />
      <Tabs.Screen
        name="trace"
        options={{
          title: 'Truy xuất',
          tabBarIcon: ({ color, size }) => <QrCode size={size || 22} color={color} />,
        }}
      />
      <Tabs.Screen
        name="profile"
        options={{
          title: 'Cá nhân',
          tabBarIcon: ({ color, size }) => <User size={size || 22} color={color} />,
        }}
      />
    </Tabs>
  );
}
