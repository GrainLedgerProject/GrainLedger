import { useRouter } from 'expo-router';
import {
  Building,
  Cloud,
  LogOut,
  RefreshCw,
  Server,
  Shield,
  KeyRound,
} from 'lucide-react-native';
import { useState } from 'react';
import {
  Alert,
  SafeAreaView,
  ScrollView,
  StyleSheet,
  Text,
  TextInput,
  TouchableOpacity,
  View,
} from 'react-native';
import { Button } from '../../components/common/Button';
import { Card } from '../../components/common/Card';
import { Header } from '../../components/common/Header';
import { api } from '../../services/api';
import { useAuthStore } from '../../store/authStore';
import { useOfflineStore } from '../../store/offlineStore';
import { Colors } from '../../theme/colors';
import { ROLE_DEFINITIONS } from '../../types/roles';

export default function ProfileScreen() {
  const router = useRouter();
  const { user, activeRole, logout } = useAuthStore();
  const { queue, isSyncing, lastSyncedAt, syncAll, getPendingCount } = useOfflineStore();

  const [apiUrl, setApiUrl] = useState(api.getBaseUrl());
  const [testingConnection, setTestingConnection] = useState(false);
  const pendingCount = getPendingCount();

  const handleSaveApiUrl = async () => {
    await api.setBaseUrl(apiUrl);
    const normalized = api.getBaseUrl();
    setApiUrl(normalized);
    Alert.alert('Thành công', `Đã lưu địa chỉ máy chủ: ${normalized}`);
  };

  const handleTestConnection = async () => {
    setTestingConnection(true);
    // Ping /health endpoint (returns {"status":"ok"})
    const res = await api.request<{ status?: string }>('/health');
    setTestingConnection(false);

    if (res.data && !res.isMock) {
      Alert.alert(
        'Kết nối thành công',
        `Máy chủ GrainLedger backend đang phản hồi tốt!\nURL: ${api.getBaseUrl()}\nTrạng thái: ${
          typeof res.data === 'object' && res.data.status ? res.data.status : 'OK'
        }`
      );
    } else {
      Alert.alert(
        'Không thể kết nối máy chủ',
        `Không thể kết nối trực tiếp đến ${api.getBaseUrl()}.\n\nChi tiết lỗi: ${
          res.error || 'Máy chủ không phản hồi'
        }\n\nVui lòng kiểm tra lại URL máy chủ hoặc trạng thái backend.`
      );
    }
  };

  const roleLabel =
    (activeRole && ROLE_DEFINITIONS[activeRole]?.labelVi) ||
    user?.role ||
    'Chưa gán vai trò';

  return (
    <SafeAreaView style={styles.safeArea}>
      <Header title="Tài khoản & Cài đặt" subtitle="Thông tin tài khoản xác thực và kết nối máy chủ" />

      <ScrollView
        style={styles.scroll}
        contentContainerStyle={styles.scrollContent}
        showsVerticalScrollIndicator={false}
      >
        {/* Profile Info Card */}
        <Card style={styles.profileCard}>
          <View style={styles.profileTop}>
            <View style={styles.avatarCircle}>
              <Text style={styles.avatarInitial}>
                {user?.displayName ? user.displayName.charAt(0).toUpperCase() : 'U'}
              </Text>
            </View>
            <View style={{ flex: 1 }}>
              <Text style={styles.userName}>{user?.displayName || 'Chưa đăng nhập'}</Text>
              <Text style={styles.userEmail}>{user?.email || 'N/A'}</Text>
              <View style={styles.roleRow}>
                <View style={styles.roleBadge}>
                  <Text style={styles.roleBadgeText}>{roleLabel}</Text>
                </View>
              </View>
            </View>
          </View>

          <View style={styles.divider} />

          <View style={styles.infoRow}>
            <Building size={16} color={Colors.textSecondary} />
            <Text style={styles.infoText}>{user?.organizationName || 'Chưa gán đơn vị'}</Text>
          </View>

          {user?.assignedFacilities && user.assignedFacilities.length > 0 ? (
            <View style={[styles.infoRow, { marginTop: 6 }]}>
              <Shield size={16} color={Colors.textSecondary} />
              <Text style={styles.infoText}>
                Phạm vi: {user.assignedFacilities.join(', ')}
              </Text>
            </View>
          ) : null}

          {user?.permissions && user.permissions.length > 0 ? (
            <View style={{ marginTop: 10 }}>
              <View style={[styles.infoRow, { marginBottom: 6 }]}>
                <KeyRound size={16} color={Colors.textSecondary} />
                <Text style={styles.permissionTitle}>Quyền hạn được cấp ({user.permissions.length}):</Text>
              </View>
              <View style={styles.permissionList}>
                {user.permissions.map((p) => (
                  <View key={p} style={styles.permissionBadge}>
                    <Text style={styles.permissionBadgeText}>{p}</Text>
                  </View>
                ))}
              </View>
            </View>
          ) : null}
        </Card>

        {/* Offline Sync Manager Card */}
        <Card style={styles.syncCard}>
          <View style={styles.syncHeader}>
            <View style={styles.syncIconBox}>
              <Cloud size={20} color={pendingCount > 0 ? Colors.accent : Colors.emerald} />
            </View>
            <View style={{ flex: 1 }}>
              <Text style={styles.syncTitle}>Đồng bộ dữ liệu ngoại tuyến</Text>
              <Text style={styles.syncSub}>
                {pendingCount > 0
                  ? `Có ${pendingCount} thao tác đang lưu tạm trong máy`
                  : 'Tất cả dữ liệu đã được gửi lên hệ thống'}
              </Text>
            </View>
          </View>

          <View style={styles.divider} />

          <View style={styles.syncStatsRow}>
            <Text style={styles.syncLastTime}>
              Lần đồng bộ gần nhất: <Text style={{ fontWeight: '700' }}>{lastSyncedAt || 'Chưa có'}</Text>
            </Text>
            <TouchableOpacity onPress={() => router.push('/offline-queue')}>
              <Text style={styles.viewQueueText}>Xem hàng đợi ({queue.length})</Text>
            </TouchableOpacity>
          </View>

          <Button
            title={isSyncing ? 'Đang truyền dữ liệu lên máy chủ...' : 'Đồng bộ ngay'}
            icon={<RefreshCw size={16} color="#FFFFFF" />}
            variant="accent"
            loading={isSyncing}
            style={{ marginTop: 12 }}
            onPress={async () => {
              const res = await syncAll();
              Alert.alert('Đồng bộ hoàn tất', `Thành công: ${res.successCount}, Thất bại: ${res.failCount}`);
            }}
          />
        </Card>

        {/* Server API Endpoint Configuration */}
        <View style={styles.sectionHeaderRow}>
          <Text style={styles.sectionTitle}>Cấu hình Máy chủ API</Text>
        </View>

        <Card style={styles.apiCard}>
          <View style={styles.apiInputRow}>
            <Server size={18} color={Colors.textSecondary} />
            <TextInput
              style={styles.apiInput}
              value={apiUrl}
              onChangeText={setApiUrl}
              placeholder="http://10.0.2.2:5080 hoặc IP máy tính"
              placeholderTextColor={Colors.textMuted}
              autoCapitalize="none"
              autoCorrect={false}
            />
          </View>

          <View style={styles.apiButtonRow}>
            <Button
              title="Lưu URL"
              size="small"
              variant="outline"
              style={{ flex: 1 }}
              onPress={handleSaveApiUrl}
            />
            <Button
              title="Kiểm tra kết nối"
              size="small"
              variant="primary"
              loading={testingConnection}
              style={{ flex: 1 }}
              onPress={handleTestConnection}
            />
          </View>
        </Card>

        {/* Logout */}
        <Button
          title="Đăng xuất"
          icon={<LogOut size={16} color="#FFFFFF" />}
          variant="danger"
          style={{ marginTop: 20 }}
          onPress={async () => {
            await logout();
            router.replace('/login');
          }}
        />
      </ScrollView>
    </SafeAreaView>
  );
}

const styles = StyleSheet.create({
  safeArea: {
    flex: 1,
    backgroundColor: Colors.background,
  },
  scroll: {
    flex: 1,
  },
  scrollContent: {
    padding: 16,
    paddingBottom: 40,
  },
  profileCard: {
    marginBottom: 14,
  },
  profileTop: {
    flexDirection: 'row',
    gap: 14,
    alignItems: 'center',
  },
  avatarCircle: {
    width: 56,
    height: 56,
    borderRadius: 28,
    backgroundColor: Colors.primarySurface,
    borderWidth: 2,
    borderColor: Colors.primary,
    alignItems: 'center',
    justifyContent: 'center',
  },
  avatarInitial: {
    fontSize: 22,
    fontWeight: '800',
    color: Colors.primary,
  },
  userName: {
    fontSize: 18,
    fontWeight: '800',
    color: Colors.text,
  },
  userEmail: {
    fontSize: 13,
    color: Colors.textSecondary,
    marginTop: 2,
  },
  roleRow: {
    flexDirection: 'row',
    marginTop: 6,
  },
  roleBadge: {
    backgroundColor: Colors.primarySurface,
    paddingHorizontal: 8,
    paddingVertical: 3,
    borderRadius: 6,
  },
  roleBadgeText: {
    fontSize: 11,
    fontWeight: '700',
    color: Colors.primary,
  },
  divider: {
    height: 1,
    backgroundColor: Colors.divider,
    marginVertical: 12,
  },
  infoRow: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: 8,
  },
  infoText: {
    fontSize: 13,
    color: Colors.textSecondary,
    flex: 1,
  },
  permissionTitle: {
    fontSize: 12,
    fontWeight: '700',
    color: Colors.textSecondary,
  },
  permissionList: {
    flexDirection: 'row',
    flexWrap: 'wrap',
    gap: 6,
  },
  permissionBadge: {
    backgroundColor: Colors.cardSubtle,
    borderRadius: 6,
    paddingHorizontal: 8,
    paddingVertical: 3,
    borderWidth: 1,
    borderColor: Colors.border,
  },
  permissionBadgeText: {
    fontSize: 11,
    color: Colors.text,
    fontFamily: 'monospace',
  },
  syncCard: {
    backgroundColor: '#FFFBEB',
    borderColor: '#FDE68A',
    marginBottom: 16,
  },
  syncHeader: {
    flexDirection: 'row',
    gap: 12,
    alignItems: 'center',
  },
  syncIconBox: {
    width: 40,
    height: 40,
    borderRadius: 10,
    backgroundColor: '#FEF3C7',
    alignItems: 'center',
    justifyContent: 'center',
  },
  syncTitle: {
    fontSize: 15,
    fontWeight: '700',
    color: '#92400E',
  },
  syncSub: {
    fontSize: 12,
    color: '#B45309',
    marginTop: 2,
  },
  syncStatsRow: {
    flexDirection: 'row',
    justifyContent: 'space-between',
    alignItems: 'center',
  },
  syncLastTime: {
    fontSize: 12,
    color: '#92400E',
  },
  viewQueueText: {
    fontSize: 12,
    fontWeight: '700',
    color: Colors.primary,
  },
  sectionHeaderRow: {
    flexDirection: 'row',
    justifyContent: 'space-between',
    alignItems: 'baseline',
    marginTop: 8,
    marginBottom: 10,
  },
  sectionTitle: {
    fontSize: 15,
    fontWeight: '800',
    color: Colors.text,
  },
  apiCard: {
    marginBottom: 10,
  },
  apiInputRow: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: 8,
    backgroundColor: Colors.cardSubtle,
    borderRadius: 10,
    paddingHorizontal: 12,
    height: 44,
  },
  apiInput: {
    flex: 1,
    fontSize: 14,
    color: Colors.text,
  },
  apiButtonRow: {
    flexDirection: 'row',
    gap: 10,
    marginTop: 12,
  },
});
