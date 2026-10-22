import React from 'react';
import { View, Text, StyleSheet, TouchableOpacity } from 'react-native';
import { useRouter } from 'expo-router';
import { Colors } from '../../theme/colors';
import { useAuthStore } from '../../store/authStore';
import { useOfflineStore } from '../../store/offlineStore';
import { ROLE_DEFINITIONS } from '../../types/roles';
import { Cloud, CloudOff, RefreshCw } from 'lucide-react-native';

interface HeaderProps {
  title?: string;
  subtitle?: string;
  showBack?: boolean;
}

export const Header: React.FC<HeaderProps> = ({ title, subtitle }) => {
  const router = useRouter();
  const { activeRole } = useAuthStore();
  const { getPendingCount, isSyncing } = useOfflineStore();
  const pendingCount = getPendingCount();
  const roleMeta = ROLE_DEFINITIONS[activeRole];

  return (
    <View style={styles.container}>
      <View style={styles.topRow}>
        <View style={styles.brandContainer}>
          <View style={styles.logoBadge}>
            <Text style={styles.logoBadgeText}>GL</Text>
          </View>
          <View>
            <Text style={styles.brandTitle}>GrainLedger</Text>
            <Text style={styles.brandSubtitle}>Hệ thống Sổ cái Nông sản</Text>
          </View>
        </View>

        <View style={styles.actionsContainer}>
          <TouchableOpacity
            style={[
              styles.syncPill,
              pendingCount > 0 ? styles.syncPillPending : styles.syncPillSynced,
            ]}
            onPress={() => router.push('/offline-queue')}
            activeOpacity={0.7}
          >
            {isSyncing ? (
              <RefreshCw size={13} color={Colors.accent} />
            ) : pendingCount > 0 ? (
              <CloudOff size={13} color={Colors.amber} />
            ) : (
              <Cloud size={13} color={Colors.emerald} />
            )}
            <Text
              style={[
                styles.syncPillText,
                pendingCount > 0 ? styles.syncTextPending : styles.syncTextSynced,
              ]}
            >
              {isSyncing ? 'Đang đồng bộ...' : pendingCount > 0 ? `${pendingCount} chờ gửi` : 'Đã đồng bộ'}
            </Text>
          </TouchableOpacity>

          <TouchableOpacity
            style={styles.roleChip}
            onPress={() => router.push('/(tabs)/profile')}
            activeOpacity={0.7}
          >
            <View style={styles.roleDot} />
            <Text style={styles.roleChipText}>{roleMeta?.code || 'GL'}</Text>
          </TouchableOpacity>
        </View>
      </View>

      {title ? (
        <View style={styles.titleSection}>
          <Text style={styles.pageTitle}>{title}</Text>
          {subtitle ? <Text style={styles.pageSubtitle}>{subtitle}</Text> : null}
        </View>
      ) : null}
    </View>
  );
};

const styles = StyleSheet.create({
  container: {
    backgroundColor: Colors.primary,
    paddingHorizontal: 16,
    paddingTop: 12,
    paddingBottom: 16,
    borderBottomLeftRadius: 20,
    borderBottomRightRadius: 20,
  },
  topRow: {
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'space-between',
  },
  brandContainer: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: 10,
  },
  logoBadge: {
    width: 36,
    height: 36,
    borderRadius: 10,
    backgroundColor: Colors.accent,
    alignItems: 'center',
    justifyContent: 'center',
    shadowColor: '#000',
    shadowOffset: { width: 0, height: 2 },
    shadowOpacity: 0.15,
    shadowRadius: 3,
    elevation: 2,
  },
  logoBadgeText: {
    color: '#FFFFFF',
    fontWeight: '800',
    fontSize: 16,
    letterSpacing: 0.5,
  },
  brandTitle: {
    color: '#FFFFFF',
    fontSize: 16,
    fontWeight: '700',
    letterSpacing: -0.2,
  },
  brandSubtitle: {
    color: 'rgba(255, 255, 255, 0.7)',
    fontSize: 11,
  },
  actionsContainer: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: 8,
  },
  syncPill: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: 5,
    paddingHorizontal: 10,
    paddingVertical: 5,
    borderRadius: 20,
    borderWidth: 1,
  },
  syncPillSynced: {
    backgroundColor: 'rgba(16, 185, 129, 0.15)',
    borderColor: 'rgba(16, 185, 129, 0.35)',
  },
  syncPillPending: {
    backgroundColor: 'rgba(245, 158, 11, 0.2)',
    borderColor: 'rgba(245, 158, 11, 0.4)',
  },
  syncPillText: {
    fontSize: 11,
    fontWeight: '600',
  },
  syncTextSynced: {
    color: '#A7F3D0',
  },
  syncTextPending: {
    color: '#FDE68A',
  },
  roleChip: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: 4,
    backgroundColor: 'rgba(255, 255, 255, 0.12)',
    borderColor: 'rgba(255, 255, 255, 0.25)',
    borderWidth: 1,
    paddingHorizontal: 8,
    paddingVertical: 5,
    borderRadius: 8,
  },
  roleDot: {
    width: 6,
    height: 6,
    borderRadius: 3,
    backgroundColor: Colors.emerald,
  },
  roleChipText: {
    color: '#FFFFFF',
    fontSize: 12,
    fontWeight: '700',
  },
  titleSection: {
    marginTop: 14,
  },
  pageTitle: {
    color: '#FFFFFF',
    fontSize: 22,
    fontWeight: '800',
    letterSpacing: -0.4,
  },
  pageSubtitle: {
    color: 'rgba(255, 255, 255, 0.75)',
    fontSize: 13,
    marginTop: 2,
  },
});
