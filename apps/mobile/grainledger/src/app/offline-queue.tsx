import React from 'react';
import {
  View,
  Text,
  StyleSheet,
  ScrollView,
  SafeAreaView,
  TouchableOpacity,
  Alert,
} from 'react-native';
import { useRouter } from 'expo-router';
import { Colors } from '../theme/colors';
import { Card } from '../components/common/Card';
import { StatusBadge } from '../components/common/StatusBadge';
import { Button } from '../components/common/Button';
import { useOfflineStore } from '../store/offlineStore';
import { ArrowLeft, RefreshCw, Trash2, Cloud, CheckCircle } from 'lucide-react-native';

export default function OfflineQueueScreen() {
  const router = useRouter();
  const { queue, isSyncing, syncAll, retryItem, clearCompleted, getPendingCount } =
    useOfflineStore();

  const pendingCount = getPendingCount();

  const handleSyncAll = async () => {
    const res = await syncAll();
    Alert.alert('Hoàn tất đồng bộ', `Đã đồng bộ thành công ${res.successCount} mục.`);
  };

  return (
    <SafeAreaView style={styles.safeArea}>
      <View style={styles.header}>
        <TouchableOpacity style={styles.backBtn} onPress={() => router.back()}>
          <ArrowLeft size={20} color={Colors.text} />
        </TouchableOpacity>
        <Text style={styles.headerTitle}>Hàng Đợi Ngoại Tuyến</Text>
        <TouchableOpacity
          style={styles.clearBtn}
          onPress={() => {
            clearCompleted();
            Alert.alert('Đã dọn dẹp', 'Đã xóa các mục đã đồng bộ thành công.');
          }}
        >
          <Trash2 size={18} color={Colors.textSecondary} />
        </TouchableOpacity>
      </View>

      {/* Top Summary Banner */}
      <View style={styles.summaryBanner}>
        <View style={styles.summaryIconBox}>
          <Cloud size={24} color={pendingCount > 0 ? Colors.accent : Colors.emerald} />
        </View>
        <View style={{ flex: 1 }}>
          <Text style={styles.summaryTitle}>
            {pendingCount > 0
              ? `${pendingCount} thao tác đang chờ kết nối`
              : 'Dữ liệu đã được đồng bộ đầy đủ'}
          </Text>
          <Text style={styles.summarySub}>
            Tất cả thao tác thực địa đều được lưu trữ an toàn trong bộ nhớ máy.
          </Text>
        </View>
      </View>

      <View style={styles.actionRow}>
        <Button
          title={isSyncing ? 'Đang truyền...' : 'Đồng bộ tất cả'}
          icon={<RefreshCw size={16} color="#FFFFFF" />}
          loading={isSyncing}
          variant="primary"
          style={{ flex: 1 }}
          onPress={handleSyncAll}
        />
      </View>

      <ScrollView
        style={styles.scroll}
        contentContainerStyle={styles.scrollContent}
        showsVerticalScrollIndicator={false}
      >
        {queue.length === 0 ? (
          <View style={styles.emptyBox}>
            <CheckCircle size={40} color={Colors.emerald} />
            <Text style={styles.emptyTitle}>Hàng đợi trống</Text>
            <Text style={styles.emptySub}>Không có giao dịch nào đang chờ gửi.</Text>
          </View>
        ) : (
          queue.map((item) => (
            <Card key={item.id} style={styles.itemCard}>
              <View style={styles.cardTop}>
                <View style={{ flex: 1 }}>
                  <Text style={styles.itemTitle}>{item.title}</Text>
                  <Text style={styles.itemSummary}>{item.summary}</Text>
                </View>
                <StatusBadge status={item.status} />
              </View>

              <View style={styles.divider} />

              <View style={styles.cardFooter}>
                <Text style={styles.timeText}>
                  Tạo lúc: {new Date(item.createdAt).toLocaleTimeString('vi-VN')}
                </Text>

                {item.status === 'PENDING' || item.status === 'FAILED' ? (
                  <TouchableOpacity
                    style={styles.retryBtn}
                    onPress={async () => {
                      await retryItem(item.id);
                      Alert.alert('Thành công', 'Đã đồng bộ mục này.');
                    }}
                  >
                    <RefreshCw size={13} color={Colors.primary} />
                    <Text style={styles.retryText}>Thử lại</Text>
                  </TouchableOpacity>
                ) : (
                  <Text style={styles.syncedText}>Đã truyền thành công</Text>
                )}
              </View>
            </Card>
          ))
        )}
      </ScrollView>
    </SafeAreaView>
  );
}

const styles = StyleSheet.create({
  safeArea: {
    flex: 1,
    backgroundColor: Colors.background,
  },
  header: {
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'space-between',
    paddingHorizontal: 16,
    paddingVertical: 14,
    backgroundColor: '#FFFFFF',
    borderBottomWidth: 1,
    borderBottomColor: Colors.border,
  },
  backBtn: {
    width: 36,
    height: 36,
    borderRadius: 18,
    backgroundColor: Colors.cardSubtle,
    alignItems: 'center',
    justifyContent: 'center',
  },
  clearBtn: {
    width: 36,
    height: 36,
    borderRadius: 18,
    backgroundColor: Colors.cardSubtle,
    alignItems: 'center',
    justifyContent: 'center',
  },
  headerTitle: {
    fontSize: 16,
    fontWeight: '800',
    color: Colors.text,
  },
  summaryBanner: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: 12,
    padding: 16,
    backgroundColor: '#FFFFFF',
    borderBottomWidth: 1,
    borderBottomColor: Colors.border,
  },
  summaryIconBox: {
    width: 44,
    height: 44,
    borderRadius: 12,
    backgroundColor: Colors.primarySurface,
    alignItems: 'center',
    justifyContent: 'center',
  },
  summaryTitle: {
    fontSize: 14,
    fontWeight: '800',
    color: Colors.text,
  },
  summarySub: {
    fontSize: 12,
    color: Colors.textSecondary,
    marginTop: 2,
    lineHeight: 16,
  },
  actionRow: {
    paddingHorizontal: 16,
    paddingVertical: 12,
    backgroundColor: '#FFFFFF',
  },
  scroll: {
    flex: 1,
  },
  scrollContent: {
    padding: 16,
    paddingBottom: 40,
  },
  itemCard: {
    marginBottom: 10,
  },
  cardTop: {
    flexDirection: 'row',
    justifyContent: 'space-between',
    alignItems: 'flex-start',
  },
  itemTitle: {
    fontSize: 14,
    fontWeight: '700',
    color: Colors.text,
  },
  itemSummary: {
    fontSize: 12,
    color: Colors.textSecondary,
    marginTop: 2,
  },
  divider: {
    height: 1,
    backgroundColor: Colors.divider,
    marginVertical: 10,
  },
  cardFooter: {
    flexDirection: 'row',
    justifyContent: 'space-between',
    alignItems: 'center',
  },
  timeText: {
    fontSize: 11,
    color: Colors.textMuted,
  },
  retryBtn: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: 4,
    backgroundColor: Colors.primarySurface,
    paddingHorizontal: 10,
    paddingVertical: 5,
    borderRadius: 8,
  },
  retryText: {
    fontSize: 12,
    fontWeight: '700',
    color: Colors.primary,
  },
  syncedText: {
    fontSize: 11,
    fontWeight: '600',
    color: Colors.emerald,
  },
  emptyBox: {
    padding: 40,
    alignItems: 'center',
    justifyContent: 'center',
  },
  emptyTitle: {
    fontSize: 16,
    fontWeight: '700',
    color: Colors.text,
    marginTop: 12,
  },
  emptySub: {
    fontSize: 13,
    color: Colors.textMuted,
    marginTop: 4,
  },
});
