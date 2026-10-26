import React from 'react';
import { View, Text, StyleSheet } from 'react-native';

interface StatusBadgeProps {
  status: string;
  size?: 'small' | 'medium';
}

export const StatusBadge: React.FC<StatusBadgeProps> = ({ status, size = 'small' }) => {
  const getBadgeConfig = (s: string) => {
    const upper = s.toUpperCase();
    switch (upper) {
      case 'COMMITTED':
      case 'SYNCED':
      case 'AVAILABLE':
      case 'COMPLIANT':
      case 'CONFIRMED':
      case 'SUCCESS':
        return {
          bg: '#DCFCE7',
          color: '#15803D',
          border: '#86EFAC',
          label: upper === 'COMMITTED' ? 'Đã xác nhận' : upper === 'SYNCED' ? 'Đã đồng bộ' : upper === 'AVAILABLE' ? 'Khả dụng' : upper === 'COMPLIANT' ? 'Đạt chuẩn' : 'Hoàn thành',
        };
      case 'PENDING':
      case 'PENDING_WEIGH':
      case 'PENDING_APPROVAL':
        return {
          bg: '#FEF3C7',
          color: '#B45309',
          border: '#FCD34D',
          label: upper === 'PENDING_WEIGH' ? 'Chờ cân' : 'Chờ xử lý',
        };
      case 'WEIGHED':
      case 'IN_PROCESS':
      case 'SYNCING':
        return {
          bg: '#DBEAFE',
          color: '#1D4ED8',
          border: '#93C5FD',
          label: upper === 'WEIGHED' ? 'Đã cân' : 'Đang chế biến',
        };
      case 'HOLD':
      case 'QUARANTINED':
      case 'FAILED':
      case 'REJECTED':
      case 'BREACH':
        return {
          bg: '#FEE2E2',
          color: '#B91C1C',
          border: '#FCA5A5',
          label: upper === 'HOLD' ? 'Lệnh HOLD' : upper === 'BREACH' ? 'Vượt ngưỡng' : upper === 'REJECTED' ? 'Từ chối' : 'Cách ly',
        };
      case 'UNLOADED':
        return {
          bg: '#E0E7FF',
          color: '#4338CA',
          border: '#C7D2FE',
          label: 'Đã xả vào Silo',
        };
      default:
        return {
          bg: '#F1F5F9',
          color: '#475569',
          border: '#CBD5E1',
          label: s,
        };
    }
  };

  const config = getBadgeConfig(status);

  return (
    <View
      style={[
        styles.badge,
        { backgroundColor: config.bg, borderColor: config.border },
        size === 'medium' && styles.badgeMedium,
      ]}
    >
      <View style={[styles.dot, { backgroundColor: config.color }]} />
      <Text
        style={[
          styles.text,
          { color: config.color },
          size === 'medium' && styles.textMedium,
        ]}
      >
        {config.label}
      </Text>
    </View>
  );
};

const styles = StyleSheet.create({
  badge: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: 4,
    paddingHorizontal: 8,
    paddingVertical: 3,
    borderRadius: 12,
    borderWidth: 1,
    alignSelf: 'flex-start',
  },
  badgeMedium: {
    paddingHorizontal: 10,
    paddingVertical: 5,
    borderRadius: 14,
  },
  dot: {
    width: 5,
    height: 5,
    borderRadius: 2.5,
  },
  text: {
    fontSize: 11,
    fontWeight: '700',
  },
  textMedium: {
    fontSize: 12,
  },
});
