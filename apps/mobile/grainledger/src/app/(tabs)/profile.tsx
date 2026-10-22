import React, { useState } from 'react';
import {
  View,
  Text,
  StyleSheet,
  ScrollView,
  TouchableOpacity,
  SafeAreaView,
  TextInput,
  Alert,
} from 'react-native';
import { useRouter } from 'expo-router';
import { Colors } from '../../theme/colors';
import { Header } from '../../components/common/Header';
import { Card } from '../../components/common/Card';
import { Button } from '../../components/common/Button';
import { useAuthStore } from '../../store/authStore';
import { useOfflineStore } from '../../store/offlineStore';
import { ROLE_DEFINITIONS, UserRole } from '../../types/roles';
import { api } from '../../services/api';
import {
  Shield,
  Cloud,
  RefreshCw,
  Server,
  LogOut,
  Building,
  CheckCircle,
} from 'lucide-react-native';

export default function ProfileScreen() {
  const router = useRouter();
  const { user, activeRole, switchRole, logout } = useAuthStore();
  const { queue, isSyncing, lastSyncedAt, syncAll, getPendingCount } = useOfflineStore();

  const [apiUrl, setApiUrl] = useState(api.getBaseUrl());
  const [testingConnection, setTestingConnection] = useState(false);
  const pendingCount = getPendingCount();

  const handleSaveApiUrl = async () => {
    await api.setBaseUrl(apiUrl);
    Alert.alert('Thành công', `Đã lưu địa chỉ máy chủ: ${apiUrl}`);
  };

  const handleTestConnection = async () => {
    setTestingConnection(true);
    const res = await api.request('/api/ledger/lookups');
    setTestingConnection(false);

    if (res.data && !res.isMock) {
      Alert.alert('Kết nối thành công', 'Máy chủ GrainLedger backend đang phản hồi tốt!');
    } else {
      Alert.alert(
        'Chế độ Ngoại tuyến / Demo',
        `Không thể kết nối trực tiếp đến ${apiUrl}. Hệ thống tự động chuyển sang lưu trữ cục bộ và dữ liệu kiểm thử (Mock mode).`
      );
    }
  };

  return (
    <SafeAreaView style={styles.safeArea}>
      <Header title="Tài khoản & Cài đặt" subtitle="Thông tin cá nhân, đồng bộ và chuyển vai trò" />

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
                {user?.displayName ? user.displayName.charAt(0) : 'U'}
              </Text>
            </View>
            <View style={{ flex: 1 }}>
              <Text style={styles.userName}>{user?.displayName}</Text>
              <Text style={styles.userEmail}>{user?.email}</Text>
              <View style={styles.roleRow}>
                <View style={styles.roleBadge}>
                  <Text style={styles.roleBadgeText}>
                    {ROLE_DEFINITIONS[activeRole]?.labelVi || activeRole}
                  </Text>
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
            title={isSyncing ? 'Đang truyền dữ liệu...' : 'Đồng bộ ngay'}
            icon={<RefreshCw size={16} color="#FFFFFF" />}
            variant="accent"
            loading={isSyncing}
            style={{ marginTop: 12 }}
            onPress={async () => {
              const res = await syncAll();
              Alert.alert('Đồng bộ hoàn tất', `Đã đồng bộ ${res.successCount} giao dịch.`);
            }}
          />
        </Card>

        {/* Demo Role Switcher */}
        <View style={styles.sectionHeaderRow}>
          <Text style={styles.sectionTitle}>Chuyển đổi vai trò Demo</Text>
          <Text style={styles.sectionSub}>7 vai trò kiểm thử</Text>
        </View>

        <Card style={styles.roleSwitcherCard}>
          {(Object.keys(ROLE_DEFINITIONS) as UserRole[]).map((r) => {
            const isCurrent = activeRole === r;
            const meta = ROLE_DEFINITIONS[r];

            return (
              <TouchableOpacity
                key={r}
                style={[styles.roleItem, isCurrent && styles.roleItemActive]}
                onPress={() => switchRole(r)}
                activeOpacity={0.7}
              >
                <View style={[styles.roleCodeBadge, isCurrent && styles.roleCodeActive]}>
                  <Text style={[styles.roleCodeText, isCurrent && styles.roleCodeTextActive]}>
                    {meta.code}
                  </Text>
                </View>

                <View style={{ flex: 1 }}>
                  <Text style={[styles.roleLabel, isCurrent && styles.roleLabelActive]}>
                    {meta.labelVi}
                  </Text>
                  <Text style={styles.roleDesc}>{meta.descriptionVi}</Text>
                </View>

                {isCurrent ? <CheckCircle size={18} color={Colors.primary} /> : null}
              </TouchableOpacity>
            );
          })}
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
  sectionSub: {
    fontSize: 12,
    color: Colors.textMuted,
  },
  roleSwitcherCard: {
    padding: 8,
    marginBottom: 16,
  },
  roleItem: {
    flexDirection: 'row',
    alignItems: 'center',
    padding: 10,
    borderRadius: 10,
    gap: 10,
    marginBottom: 4,
  },
  roleItemActive: {
    backgroundColor: Colors.primarySurface,
  },
  roleCodeBadge: {
    width: 32,
    height: 32,
    borderRadius: 8,
    backgroundColor: Colors.cardSubtle,
    alignItems: 'center',
    justifyContent: 'center',
  },
  roleCodeActive: {
    backgroundColor: Colors.primary,
  },
  roleCodeText: {
    fontSize: 12,
    fontWeight: '800',
    color: Colors.textSecondary,
  },
  roleCodeTextActive: {
    color: '#FFFFFF',
  },
  roleLabel: {
    fontSize: 14,
    fontWeight: '700',
    color: Colors.text,
  },
  roleLabelActive: {
    color: Colors.primary,
  },
  roleDesc: {
    fontSize: 11,
    color: Colors.textSecondary,
    marginTop: 2,
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
