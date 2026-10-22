import React from 'react';
import {
  TouchableOpacity,
  Text,
  StyleSheet,
  ActivityIndicator,
  ViewStyle,
  TextStyle,
  View,
} from 'react-native';
import { Colors } from '../../theme/colors';

interface ButtonProps {
  title: string;
  onPress: () => void;
  variant?: 'primary' | 'secondary' | 'accent' | 'outline' | 'danger';
  size?: 'small' | 'medium' | 'large';
  loading?: boolean;
  disabled?: boolean;
  icon?: React.ReactNode;
  style?: ViewStyle;
  textStyle?: TextStyle;
}

export const Button: React.FC<ButtonProps> = ({
  title,
  onPress,
  variant = 'primary',
  size = 'medium',
  loading = false,
  disabled = false,
  icon,
  style,
  textStyle,
}) => {
  const getVariantStyles = () => {
    switch (variant) {
      case 'secondary':
        return {
          button: styles.btnSecondary,
          text: styles.textSecondary,
        };
      case 'accent':
        return {
          button: styles.btnAccent,
          text: styles.textAccent,
        };
      case 'outline':
        return {
          button: styles.btnOutline,
          text: styles.textOutline,
        };
      case 'danger':
        return {
          button: styles.btnDanger,
          text: styles.textDanger,
        };
      default:
        return {
          button: styles.btnPrimary,
          text: styles.textPrimary,
        };
    }
  };

  const getSizeStyles = () => {
    switch (size) {
      case 'small':
        return {
          button: styles.sizeSmall,
          text: styles.textSizeSmall,
        };
      case 'large':
        return {
          button: styles.sizeLarge,
          text: styles.textSizeLarge,
        };
      default:
        return {
          button: styles.sizeMedium,
          text: styles.textSizeMedium,
        };
    }
  };

  const vStyles = getVariantStyles();
  const sStyles = getSizeStyles();

  return (
    <TouchableOpacity
      style={[
        styles.base,
        vStyles.button,
        sStyles.button,
        (disabled || loading) && styles.disabled,
        style,
      ]}
      onPress={onPress}
      disabled={disabled || loading}
      activeOpacity={0.8}
    >
      {loading ? (
        <ActivityIndicator
          size="small"
          color={variant === 'outline' ? Colors.primary : '#FFFFFF'}
        />
      ) : (
        <View style={styles.contentRow}>
          {icon ? <View style={styles.iconContainer}>{icon}</View> : null}
          <Text style={[styles.baseText, vStyles.text, sStyles.text, textStyle]}>
            {title}
          </Text>
        </View>
      )}
    </TouchableOpacity>
  );
};

const styles = StyleSheet.create({
  base: {
    borderRadius: 12,
    alignItems: 'center',
    justifyContent: 'center',
  },
  contentRow: {
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'center',
    gap: 8,
  },
  iconContainer: {
    marginRight: 2,
  },
  baseText: {
    fontWeight: '700',
    textAlign: 'center',
  },
  // Variants
  btnPrimary: {
    backgroundColor: Colors.primary,
  },
  textPrimary: {
    color: '#FFFFFF',
  },
  btnSecondary: {
    backgroundColor: Colors.primarySurface,
  },
  textSecondary: {
    color: Colors.primary,
  },
  btnAccent: {
    backgroundColor: Colors.accent,
  },
  textAccent: {
    color: '#FFFFFF',
  },
  btnOutline: {
    backgroundColor: 'transparent',
    borderWidth: 1.5,
    borderColor: Colors.border,
  },
  textOutline: {
    color: Colors.text,
  },
  btnDanger: {
    backgroundColor: Colors.red,
  },
  textDanger: {
    color: '#FFFFFF',
  },
  // Sizes
  sizeSmall: {
    paddingVertical: 8,
    paddingHorizontal: 12,
  },
  textSizeSmall: {
    fontSize: 13,
  },
  sizeMedium: {
    paddingVertical: 12,
    paddingHorizontal: 18,
  },
  textSizeMedium: {
    fontSize: 15,
  },
  sizeLarge: {
    paddingVertical: 15,
    paddingHorizontal: 24,
  },
  textSizeLarge: {
    fontSize: 16,
  },
  disabled: {
    opacity: 0.5,
  },
});
