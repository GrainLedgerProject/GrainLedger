import React, { useState } from 'react';
import {
  View,
  Text,
  StyleSheet,
  SafeAreaView,
  ScrollView,
  TouchableOpacity,
  Alert,
} from 'react-native';
import { useRouter } from 'expo-router';
import { Colors } from '../theme/colors';
import { Card } from '../components/common/Card';
import { InputField } from '../components/common/InputField';
import { Button } from '../components/common/Button';
import { useAuthStore } from '../store/authStore';
import { UserRole } from '../types/roles';
import { UserCheck } from 'lucide-react-native';

export default function LoginScreen() {
  const router = useRouter();
  const { loginWithCredentials, loginAsDemo, isLoading } = useAuthStore();

  const [email, setEmail] = useState('purchasing@grainledger.local');
  const [password, setPassword] = useState('GrainLedger@2026');

  const handleLogin = async () => {
    if (!email.trim() || !password.trim()) {
      Alert.alert('Thiếu thông tin', 'Vui lòng nhập email và mật khẩu.');
      return;
    }

    const res = await loginWithCredentials(email, password);
    if (res.success) {
      router.replace('/(tabs)');
    } else {
      Alert.alert('Đăng nhập thất bại', res.error || 'Vui lòng kiểm tra lại tài khoản.');
    }
  };

  const handleQuickDemo = async (role: UserRole) => {
    await loginAsDemo(role);
    router.replace('/(tabs)');
  };

  return (
    <SafeAreaView style={styles.safeArea}>
      <ScrollView
        contentContainerStyle={styles.container}
        showsVerticalScrollIndicator={false}
      >
        {/* Brand Header */}
        <View style={styles.brandSection}>
          <View style={styles.logoBadge}>
            <Text style={styles.logoText}>GL</Text>
          </View>
          <Text style={styles.title}>GrainLedger</Text>
          <Text style={styles.subtitle}>Hệ thống Sổ cái & Truy xuất Nguồn gốc Lúa gạo</Text>
        </View>

        {/* Credentials Form */}
        <Card style={styles.card}>
          <Text style={styles.cardTitle}>Đăng nhập tài khoản</Text>

          <InputField
            label="Email công tác"
            value={email}
            onChangeText={setEmail}
            keyboardType="email-address"
            autoCapitalize="none"
            required
          />

          <InputField
            label="Mật khẩu"
            value={password}
            onChangeText={setPassword}
            secureTextEntry
            required
          />

          <Button
            title="Đăng nhập"
            size="large"
            loading={isLoading}
            onPress={handleLogin}
            style={{ marginTop: 8 }}
          />
        </Card>

        {/* Quick Demo Access */}
        <Card style={styles.demoCard}>
          <View style={styles.demoHeader}>
            <UserCheck size={18} color={Colors.primary} />
            <Text style={styles.demoTitle}>Đăng nhập nhanh (Chế độ Demo)</Text>
          </View>
          <Text style={styles.demoSub}>
            Chọn một vai trò để trải nghiệm luồng thao tác hoàn chỉnh:
          </Text>

          <TouchableOpacity
            style={styles.demoBtn}
            onPress={() => handleQuickDemo('CUSTOMER_PROCUREMENT_OFFICER')}
          >
            <View style={styles.demoRolePill}>
              <Text style={styles.demoRoleCode}>PO</Text>
            </View>
            <View style={{ flex: 1 }}>
              <Text style={styles.demoRoleName}>Nhân viên Thu mua Thực địa</Text>
              <Text style={styles.demoRoleDesc}>Đăng ký nông hộ, thửa ruộng, lập phiếu thu mua</Text>
            </View>
          </TouchableOpacity>

          <TouchableOpacity
            style={styles.demoBtn}
            onPress={() => handleQuickDemo('MILL_OPERATOR')}
          >
            <View style={[styles.demoRolePill, { backgroundColor: Colors.accent }]}>
              <Text style={styles.demoRoleCode}>MO</Text>
            </View>
            <View style={{ flex: 1 }}>
              <Text style={styles.demoRoleName}>Nhân viên Vận hành Nhà máy</Text>
              <Text style={styles.demoRoleDesc}>Cân xe lúa tươi, ghi nhận mẻ sấy & xay xát</Text>
            </View>
          </TouchableOpacity>

          <TouchableOpacity
            style={styles.demoBtn}
            onPress={() => handleQuickDemo('MILL_MANAGER')}
          >
            <View style={[styles.demoRolePill, { backgroundColor: '#3B82F6' }]}>
              <Text style={styles.demoRoleCode}>MM</Text>
            </View>
            <View style={{ flex: 1 }}>
              <Text style={styles.demoRoleName}>Quản lý Nhà máy & Silo</Text>
              <Text style={styles.demoRoleDesc}>Đối soát dung sai, kiểm soát lệnh HOLD</Text>
            </View>
          </TouchableOpacity>
        </Card>
      </ScrollView>
    </SafeAreaView>
  );
}

const styles = StyleSheet.create({
  safeArea: {
    flex: 1,
    backgroundColor: Colors.background,
  },
  container: {
    padding: 20,
    justifyContent: 'center',
    minHeight: '100%',
  },
  brandSection: {
    alignItems: 'center',
    marginBottom: 24,
  },
  logoBadge: {
    width: 64,
    height: 64,
    borderRadius: 18,
    backgroundColor: Colors.primary,
    alignItems: 'center',
    justifyContent: 'center',
    shadowColor: Colors.primary,
    shadowOffset: { width: 0, height: 4 },
    shadowOpacity: 0.25,
    shadowRadius: 8,
    elevation: 4,
    marginBottom: 12,
  },
  logoText: {
    color: '#FFFFFF',
    fontSize: 26,
    fontWeight: '800',
  },
  title: {
    fontSize: 24,
    fontWeight: '800',
    color: Colors.text,
    letterSpacing: -0.5,
  },
  subtitle: {
    fontSize: 13,
    color: Colors.textSecondary,
    textAlign: 'center',
    marginTop: 4,
    maxWidth: 280,
  },
  card: {
    marginBottom: 16,
  },
  cardTitle: {
    fontSize: 16,
    fontWeight: '800',
    color: Colors.text,
    marginBottom: 16,
  },
  demoCard: {
    backgroundColor: Colors.cardSubtle,
    borderColor: Colors.border,
  },
  demoHeader: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: 8,
    marginBottom: 4,
  },
  demoTitle: {
    fontSize: 14,
    fontWeight: '700',
    color: Colors.text,
  },
  demoSub: {
    fontSize: 12,
    color: Colors.textSecondary,
    marginBottom: 12,
  },
  demoBtn: {
    flexDirection: 'row',
    alignItems: 'center',
    backgroundColor: '#FFFFFF',
    borderWidth: 1,
    borderColor: Colors.border,
    padding: 10,
    borderRadius: 10,
    gap: 10,
    marginBottom: 8,
  },
  demoRolePill: {
    width: 32,
    height: 32,
    borderRadius: 8,
    backgroundColor: Colors.primary,
    alignItems: 'center',
    justifyContent: 'center',
  },
  demoRoleCode: {
    color: '#FFFFFF',
    fontSize: 12,
    fontWeight: '800',
  },
  demoRoleName: {
    fontSize: 13,
    fontWeight: '700',
    color: Colors.text,
  },
  demoRoleDesc: {
    fontSize: 11,
    color: Colors.textSecondary,
    marginTop: 1,
  },
});
