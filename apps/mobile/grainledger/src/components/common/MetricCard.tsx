import React from 'react';
import { View, Text, StyleSheet } from 'react-native';
import { Colors } from '../../theme/colors';

interface MetricCardProps {
  label: string;
  value: string | number;
  unit?: string;
  subtext?: string;
  icon?: React.ReactNode;
  variant?: 'primary' | 'accent' | 'emerald' | 'amber';
}

export const MetricCard: React.FC<MetricCardProps> = ({
  label,
  value,
  unit,
  subtext,
  icon,
  variant = 'primary',
}) => {
  const getColors = () => {
    switch (variant) {
      case 'accent':
        return {
          bg: '#FFFBEB',
          border: '#FDE68A',
          iconBg: '#FEF3C7',
          iconColor: Colors.accent,
        };
      case 'emerald':
        return {
          bg: '#ECFDF5',
          border: '#A7F3D0',
          iconBg: '#D1FAE5',
          iconColor: Colors.emerald,
        };
      case 'amber':
        return {
          bg: '#FFF7ED',
          border: '#FED7AA',
          iconBg: '#FFEDD5',
          iconColor: '#EA580C',
        };
      default:
        return {
          bg: '#F0FDF4',
          border: '#BBF7D0',
          iconBg: Colors.primarySurface,
          iconColor: Colors.primary,
        };
    }
  };

  const scheme = getColors();

  return (
    <View style={[styles.card, { backgroundColor: scheme.bg, borderColor: scheme.border }]}>
      <View style={styles.topRow}>
        <Text style={styles.label}>{label}</Text>
        {icon ? (
          <View style={[styles.iconWrapper, { backgroundColor: scheme.iconBg }]}>
            {icon}
          </View>
        ) : null}
      </View>

      <View style={styles.valueRow}>
        <Text style={styles.value}>{value}</Text>
        {unit ? <Text style={styles.unit}>{unit}</Text> : null}
      </View>

      {subtext ? <Text style={styles.subtext}>{subtext}</Text> : null}
    </View>
  );
};

const styles = StyleSheet.create({
  card: {
    flex: 1,
    minWidth: 150,
    borderRadius: 16,
    padding: 14,
    borderWidth: 1,
  },
  topRow: {
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'space-between',
    marginBottom: 6,
  },
  label: {
    fontSize: 12,
    fontWeight: '600',
    color: Colors.textSecondary,
    textTransform: 'uppercase',
    letterSpacing: 0.5,
  },
  iconWrapper: {
    width: 28,
    height: 28,
    borderRadius: 8,
    alignItems: 'center',
    justifyContent: 'center',
  },
  valueRow: {
    flexDirection: 'row',
    alignItems: 'baseline',
    gap: 4,
  },
  value: {
    fontSize: 22,
    fontWeight: '800',
    color: Colors.text,
    letterSpacing: -0.5,
  },
  unit: {
    fontSize: 13,
    fontWeight: '600',
    color: Colors.textSecondary,
  },
  subtext: {
    fontSize: 11,
    color: Colors.textMuted,
    marginTop: 4,
  },
});
