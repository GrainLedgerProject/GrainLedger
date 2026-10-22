import React, { useState } from 'react';
import {
  View,
  Text,
  StyleSheet,
  SafeAreaView,
  ScrollView,
  Alert,
  TouchableOpacity,
  TextInput,
} from 'react-native';
import { useRouter } from 'expo-router';
import { Colors } from '../theme/colors';
import { Card } from '../components/common/Card';
import { InputField } from '../components/common/InputField';
import { Button } from '../components/common/Button';
import { useAuthStore } from '../store/authStore';
import { api } from '../services/api';
import {
  Server,
  Sliders,
  ChevronDown,
  ChevronUp,
  CheckCircle2,
  AlertTriangle,
  Zap,
} from 'lucide-react-native';

const DEV_ACCOUNTS = [
  { label: 'Thu mua (PO)', email: 'purchasing@grainledger.local' },
  { label: 'Vận hành (MO)', email: 'mill.operator@grainledger.local' },
  { label: 'Quản lý (MM)', email: 'mill.manager@grainledger.local' },
  { label: 'Quản trị (Admin)', email: 'admin@grainledger.local' },
];

export default function LoginScreen() {
  const router = useRouter();
  const { loginWithCredentials, isLoading } = useAuthStore();

  const [email, setEmail] = useState('purchasing@grainledger.local');
  const [password, setPassword] = useState('Grainledger@1');

  // Dev Server Configuration State
  const [showDevSettings, setShowDevSettings] = useState(false);
  const [serverUrl, setServerUrl] = useState(api.getBaseUrl());
  const [testingServer, setTestingServer] = useState(false);
  const [serverStatus, setServerStatus] = useState<'idle' | 'ok' | 'error'>('idle');

  const handleLogin = async () => {
    if (!email.trim() || !password.trim()) {
      Alert.alert('Thiếu thông tin', 'Vui lòng nhập đầy đủ email và mật khẩu.');
      return;
    }

    const res = await loginWithCredentials(email, password);
    if (res.success) {
      router.replace('/(tabs)');
    } else {
      Alert.alert(
        'Đăng nhập thất bại',
        res.error || 'Vui lòng kiểm tra lại tài khoản hoặc kết nối máy chủ.'
      );
    }
  };

  const handleSaveServerUrl = async () => {
    await api.setBaseUrl(serverUrl);
    const normalized = api.getBaseUrl();
    setServerUrl(normalized);
    Alert.alert('Thành công', `Đã lưu địa chỉ máy chủ: ${normalized}`);
  };

  const handleTestConnection = async () => {
    setTestingServer(true);
    setServerStatus('idle');

    // Ping health endpoint on the entered or saved server
    await api.setBaseUrl(serverUrl);
    const normalized = api.getBaseUrl();
    setServerUrl(normalized);

    const res = await api.request<{ status?: string }>('/health');
    setTestingServer(false);

    if (res.data && !res.isMock) {
      setServerStatus('ok');
      Alert.alert(
        'Kết nối thành công',
        `Máy chủ đang phản hồi tốt!\nURL: ${normalized}\nTrạng thái: ${
          typeof res.data === 'object' && res.data.status ? res.data.status : 'OK'
        }`
      );
    } else {
      setServerStatus('error');
      Alert.alert(
        'Không thể kết nối',
        `Không thể kết nối tới ${normalized}.\n\nLỗi: ${
          res.error || 'Timeout hoặc máy chủ không phản hồi'
        }\n\nGợi ý: Kiểm tra lại backend .NET đang chạy cổng 5080 và dùng 10.0.2.2 cho máy ảo Android.`
      );
    }
  };

  const handleSelectPreset = (url: string) => {
    setServerUrl(url);
    api.setBaseUrl(url);
    setServerStatus('idle');
  };

  const handleFillAccount = (devEmail: string) => {
    setEmail(devEmail);
    setPassword('Grainledger@1');
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

        {/* Developer Server Configuration Card */}
        <Card style={styles.devCard}>
          <TouchableOpacity
            style={styles.devHeaderToggle}
            onPress={() => setShowDevSettings(!showDevSettings)}
            activeOpacity={0.7}
          >
            <View style={styles.devHeaderLeft}>
              <View style={styles.devIconWrap}>
                <Sliders size={16} color={Colors.accent} />
              </View>
              <View>
                <View style={styles.devTitleRow}>
                  <Text style={styles.devTitle}>Cấu hình Server (Dev)</Text>
                  {serverStatus === 'ok' ? (
                    <View style={styles.statusPillOk}>
                      <CheckCircle2 size={11} color="#047857" />
                      <Text style={styles.statusPillOkText}>Online</Text>
                    </View>
                  ) : serverStatus === 'error' ? (
                    <View style={styles.statusPillError}>
                      <AlertTriangle size={11} color="#B91C1C" />
                      <Text style={styles.statusPillErrorText}>Lỗi</Text>
                    </View>
                  ) : null}
                </View>
                <Text style={styles.devCurrentUrl} numberOfLines={1}>
                  {api.getBaseUrl()}
                </Text>
              </View>
            </View>

            {showDevSettings ? (
              <ChevronUp size={18} color={Colors.textSecondary} />
            ) : (
              <ChevronDown size={18} color={Colors.textSecondary} />
            )}
          </TouchableOpacity>

          {showDevSettings && (
            <View style={styles.devBody}>
              <View style={styles.divider} />

              <Text style={styles.fieldLabel}>Địa chỉ API Backend:</Text>
              <View style={styles.urlInputRow}>
                <Server size={16} color={Colors.textSecondary} />
                <TextInput
                  style={styles.urlInput}
                  value={serverUrl}
                  onChangeText={(val) => {
                    setServerUrl(val);
                    setServerStatus('idle');
                  }}
                  placeholder="http://10.0.2.2:5080 hoặc IP máy"
                  placeholderTextColor={Colors.textMuted}
                  autoCapitalize="none"
                  autoCorrect={false}
                />
              </View>

              {/* Quick Presets */}
              <Text style={styles.presetsLabel}>Chọn nhanh địa chỉ thường dùng:</Text>
              <View style={styles.presetsRow}>
                <TouchableOpacity
                  style={[
                    styles.presetBtn,
                    serverUrl.includes('10.0.2.2') && styles.presetBtnActive,
                  ]}
                  onPress={() => handleSelectPreset('http://10.0.2.2:5080')}
                >
                  <Text
                    style={[
                      styles.presetText,
                      serverUrl.includes('10.0.2.2') && styles.presetTextActive,
                    ]}
                  >
                    10.0.2.2 (Máy ảo)
                  </Text>
                </TouchableOpacity>

                <TouchableOpacity
                  style={[
                    styles.presetBtn,
                    serverUrl.includes('localhost') && styles.presetBtnActive,
                  ]}
                  onPress={() => handleSelectPreset('http://localhost:5080')}
                >
                  <Text
                    style={[
                      styles.presetText,
                      serverUrl.includes('localhost') && styles.presetTextActive,
                    ]}
                  >
                    localhost:5080
                  </Text>
                </TouchableOpacity>
              </View>

              {/* Action Buttons */}
              <View style={styles.actionBtnRow}>
                <Button
                  title="Lưu URL"
                  size="small"
                  variant="outline"
                  style={{ flex: 1 }}
                  onPress={handleSaveServerUrl}
                />
                <Button
                  title="Kiểm tra kết nối"
                  size="small"
                  variant="primary"
                  loading={testingServer}
                  style={{ flex: 1 }}
                  onPress={handleTestConnection}
                />
              </View>

              {/* Quick Account Fill for dev convenience */}
              <View style={styles.accountQuickSection}>
                <View style={styles.accountQuickHeader}>
                  <Zap size={14} color={Colors.accent} />
                  <Text style={styles.accountQuickTitle}>Điền nhanh tài khoản mẫu:</Text>
                </View>
                <View style={styles.accountChipGrid}>
                  {DEV_ACCOUNTS.map((acc) => (
                    <TouchableOpacity
                      key={acc.email}
                      style={[
                        styles.accountChip,
                        email === acc.email && styles.accountChipActive,
                      ]}
                      onPress={() => handleFillAccount(acc.email)}
                    >
                      <Text
                        style={[
                          styles.accountChipText,
                          email === acc.email && styles.accountChipTextActive,
                        ]}
                      >
                        {acc.label}
                      </Text>
                    </TouchableOpacity>
                  ))}
                </View>
              </View>
            </View>
          )}
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
    marginBottom: 14,
  },
  cardTitle: {
    fontSize: 16,
    fontWeight: '800',
    color: Colors.text,
    marginBottom: 16,
  },
  devCard: {
    backgroundColor: '#FFFBEB',
    borderColor: '#FDE68A',
    padding: 12,
  },
  devHeaderToggle: {
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'space-between',
  },
  devHeaderLeft: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: 10,
    flex: 1,
  },
  devIconWrap: {
    width: 34,
    height: 34,
    borderRadius: 8,
    backgroundColor: '#FEF3C7',
    alignItems: 'center',
    justifyContent: 'center',
  },
  devTitleRow: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: 6,
  },
  devTitle: {
    fontSize: 14,
    fontWeight: '700',
    color: '#92400E',
  },
  devCurrentUrl: {
    fontSize: 11,
    color: '#B45309',
    marginTop: 2,
    maxWidth: 210,
  },
  statusPillOk: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: 3,
    backgroundColor: '#D1FAE5',
    paddingHorizontal: 6,
    paddingVertical: 1,
    borderRadius: 10,
  },
  statusPillOkText: {
    fontSize: 10,
    fontWeight: '700',
    color: '#047857',
  },
  statusPillError: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: 3,
    backgroundColor: '#FEE2E2',
    paddingHorizontal: 6,
    paddingVertical: 1,
    borderRadius: 10,
  },
  statusPillErrorText: {
    fontSize: 10,
    fontWeight: '700',
    color: '#B91C1C',
  },
  devBody: {
    marginTop: 10,
  },
  divider: {
    height: 1,
    backgroundColor: '#FDE68A',
    marginBottom: 12,
  },
  fieldLabel: {
    fontSize: 12,
    fontWeight: '700',
    color: '#92400E',
    marginBottom: 6,
  },
  urlInputRow: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: 8,
    backgroundColor: '#FFFFFF',
    borderWidth: 1,
    borderColor: '#FCD34D',
    borderRadius: 8,
    paddingHorizontal: 10,
    height: 42,
  },
  urlInput: {
    flex: 1,
    fontSize: 13,
    color: Colors.text,
  },
  presetsLabel: {
    fontSize: 11,
    fontWeight: '600',
    color: '#B45309',
    marginTop: 10,
    marginBottom: 6,
  },
  presetsRow: {
    flexDirection: 'row',
    gap: 8,
  },
  presetBtn: {
    paddingHorizontal: 10,
    paddingVertical: 6,
    borderRadius: 6,
    backgroundColor: '#FFFFFF',
    borderWidth: 1,
    borderColor: '#FCD34D',
  },
  presetBtnActive: {
    backgroundColor: Colors.accent,
    borderColor: Colors.accent,
  },
  presetText: {
    fontSize: 11,
    fontWeight: '600',
    color: '#92400E',
  },
  presetTextActive: {
    color: '#FFFFFF',
  },
  actionBtnRow: {
    flexDirection: 'row',
    gap: 10,
    marginTop: 14,
  },
  accountQuickSection: {
    marginTop: 14,
    paddingTop: 12,
    borderTopWidth: 1,
    borderTopColor: '#FDE68A',
  },
  accountQuickHeader: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: 5,
    marginBottom: 8,
  },
  accountQuickTitle: {
    fontSize: 11,
    fontWeight: '700',
    color: '#92400E',
  },
  accountChipGrid: {
    flexDirection: 'row',
    flexWrap: 'wrap',
    gap: 6,
  },
  accountChip: {
    paddingHorizontal: 8,
    paddingVertical: 5,
    borderRadius: 6,
    backgroundColor: '#FFFFFF',
    borderWidth: 1,
    borderColor: '#FCD34D',
  },
  accountChipActive: {
    backgroundColor: Colors.primary,
    borderColor: Colors.primary,
  },
  accountChipText: {
    fontSize: 11,
    fontWeight: '600',
    color: Colors.textSecondary,
  },
  accountChipTextActive: {
    color: '#FFFFFF',
  },
});
