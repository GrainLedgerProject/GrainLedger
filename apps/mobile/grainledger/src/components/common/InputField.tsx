import React from 'react';
import {
  View,
  Text,
  TextInput,
  StyleSheet,
  TextInputProps,
  ViewStyle,
} from 'react-native';
import { Colors } from '../../theme/colors';

interface InputFieldProps extends TextInputProps {
  label: string;
  error?: string;
  unitSuffix?: string;
  containerStyle?: ViewStyle;
  required?: boolean;
}

export const InputField: React.FC<InputFieldProps> = ({
  label,
  error,
  unitSuffix,
  containerStyle,
  required,
  style,
  ...rest
}) => {
  return (
    <View style={[styles.container, containerStyle]}>
      <View style={styles.labelRow}>
        <Text style={styles.label}>{label}</Text>
        {required ? <Text style={styles.requiredMark}>*</Text> : null}
      </View>

      <View style={[styles.inputWrapper, error ? styles.inputError : null]}>
        <TextInput
          style={[styles.input, style]}
          placeholderTextColor={Colors.textMuted}
          {...rest}
        />
        {unitSuffix ? (
          <View style={styles.suffixContainer}>
            <Text style={styles.suffixText}>{unitSuffix}</Text>
          </View>
        ) : null}
      </View>

      {error ? <Text style={styles.errorText}>{error}</Text> : null}
    </View>
  );
};

const styles = StyleSheet.create({
  container: {
    marginBottom: 14,
  },
  labelRow: {
    flexDirection: 'row',
    alignItems: 'center',
    marginBottom: 6,
    gap: 4,
  },
  label: {
    fontSize: 14,
    fontWeight: '600',
    color: Colors.text,
  },
  requiredMark: {
    color: Colors.red,
    fontSize: 14,
    fontWeight: '700',
  },
  inputWrapper: {
    flexDirection: 'row',
    alignItems: 'center',
    backgroundColor: '#FFFFFF',
    borderWidth: 1.5,
    borderColor: Colors.border,
    borderRadius: 12,
    paddingHorizontal: 12,
  },
  inputError: {
    borderColor: Colors.red,
  },
  input: {
    flex: 1,
    height: 48,
    fontSize: 15,
    color: Colors.text,
  },
  suffixContainer: {
    paddingLeft: 8,
    borderLeftWidth: 1,
    borderLeftColor: Colors.border,
    paddingVertical: 4,
  },
  suffixText: {
    fontSize: 14,
    fontWeight: '600',
    color: Colors.textSecondary,
  },
  errorText: {
    fontSize: 12,
    color: Colors.red,
    marginTop: 4,
    fontWeight: '500',
  },
});
