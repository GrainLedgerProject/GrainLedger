import React from 'react';
import {
  View,
  Text,
  StyleSheet,
  ScrollView,
  TouchableOpacity,
  SafeAreaView,
} from 'react-native';
import { useRouter } from 'expo-router';
import { Colors } from '../../theme/colors';
import { Header } from '../../components/common/Header';
import { Card } from '../../components/common/Card';
import { MetricCard } from '../../components/common/MetricCard';
import { StatusBadge } from '../../components/common/StatusBadge';
import { useAuthStore } from '../../store/authStore';
import { useFieldStore } from '../../store/fieldStore';
import { useMillStore } from '../../store/millStore';
import { useOfflineStore } from '../../store/offlineStore';
import { formatMass } from '../../utils/decimalMass';
import {
  FileText,
  UserPlus,
  Scale,
  Cpu,
  QrCode,
  ArrowRight,
  TrendingUp,
  Warehouse,
  CheckCircle2,
  AlertCircle,
  Truck,
} from 'lucide-react-native';

export default function HomeScreen() {
  const router = useRouter();
  const { user } = useAuthStore();
  const { farmers, receipts } = useFieldStore();
  const { receivingTasks, silos } = useMillStore();
  const { getPendingCount } = useOfflineStore();

  const totalProcuredToday = receipts.reduce(
    (acc, r) => acc + (parseFloat(r.measuredGrossKg) || 0),
    0
  );
  const pendingWeighCount = receivingTasks.filter(
    (t) => t.status === 'PENDING_WEIGH'
  ).length;

  return (
    <SafeAreaView style={styles.safeArea}>
      <Header />
      <ScrollView
        style={styles.scrollView}
        contentContainerStyle={styles.contentContainer}
        showsVerticalScrollIndicator={false}
      >
        {/* Welcome Section */}
        <View style={styles.welcomeSection}>
          <Text style={styles.greetingText}>Xin chào,</Text>
          <Text style={styles.userNameText}>{user?.displayName || 'Cán bộ vận hành'}</Text>
          <Text style={styles.orgText}>{user?.organizationName || 'GrainLedger Mekong'}</Text>
        </View>

        {/* Offline Banner if any */}
        {getPendingCount() > 0 ? (
          <TouchableOpacity
            style={styles.offlineBanner}
            onPress={() => router.push('/offline-queue')}
            activeOpacity={0.8}
          >
            <AlertCircle size={18} color="#B45309" />
            <View style={styles.offlineBannerTextCol}>
              <Text style={styles.offlineBannerTitle}>
                Có {getPendingCount()} giao dịch chờ gửi lên máy chủ
              </Text>
              <Text style={styles.offlineBannerSub}>
                Nhấn để xem và đồng bộ dữ liệu ngoại tuyến
              </Text>
            </View>
            <ArrowRight size={16} color="#B45309" />
          </TouchableOpacity>
        ) : null}

        {/* Key Metrics Grid */}
        <View style={styles.sectionHeaderRow}>
          <Text style={styles.sectionTitle}>Chỉ số vận hành</Text>
          <Text style={styles.sectionBadge}>Hôm nay</Text>
        </View>

        <View style={styles.metricsGrid}>
          <MetricCard
            label="Thu mua lúa tươi"
            value={formatMass(totalProcuredToday, 0)}
            unit="kg"
            subtext={`${receipts.length} chuyến hàng`}
            icon={<TrendingUp size={16} color={Colors.primary} />}
            variant="primary"
          />
          <MetricCard
            label="Xe chờ cân"
            value={pendingWeighCount}
            unit="xe"
            subtext="Tại cổng nhà máy"
            icon={<Truck size={16} color={Colors.accent} />}
            variant="accent"
          />
        </View>

        <View style={[styles.metricsGrid, { marginTop: 10 }]}>
          <MetricCard
            label="Nông hộ liên kết"
            value={farmers.length}
            unit="hộ"
            subtext="Vùng nguyên liệu Mekong"
            icon={<CheckCircle2 size={16} color={Colors.emerald} />}
            variant="emerald"
          />
          <MetricCard
            label="Silo đang hoạt động"
            value={silos.length}
            unit="silo"
            subtext="Sức chứa 340 tấn"
            icon={<Warehouse size={16} color="#EA580C" />}
            variant="amber"
          />
        </View>

        {/* Quick Action Shortcuts */}
        <View style={styles.sectionHeaderRow}>
          <Text style={styles.sectionTitle}>Thao tác nhanh</Text>
        </View>

        <View style={styles.quickActionsGrid}>
          <TouchableOpacity
            style={[styles.actionCard, { backgroundColor: '#F0FDF4', borderColor: '#BBF7D0' }]}
            onPress={() => router.push('/procurement-form')}
            activeOpacity={0.7}
          >
            <View style={[styles.actionIconBox, { backgroundColor: Colors.primarySurface }]}>
              <FileText size={20} color={Colors.primary} />
            </View>
            <Text style={styles.actionCardTitle}>Phiếu thu mua</Text>
            <Text style={styles.actionCardSub}>Lập phiếu lúa tươi</Text>
          </TouchableOpacity>

          <TouchableOpacity
            style={[styles.actionCard, { backgroundColor: '#FFFBEB', borderColor: '#FDE68A' }]}
            onPress={() => router.push('/receiving-form')}
            activeOpacity={0.7}
          >
            <View style={[styles.actionIconBox, { backgroundColor: '#FEF3C7' }]}>
              <Scale size={20} color={Colors.accent} />
            </View>
            <Text style={styles.actionCardTitle}>Cân xe lúa</Text>
            <Text style={styles.actionCardSub}>Tiếp nhận tại cổng</Text>
          </TouchableOpacity>

          <TouchableOpacity
            style={[styles.actionCard, { backgroundColor: '#EFF6FF', borderColor: '#BFDBFE' }]}
            onPress={() => router.push('/operation-form')}
            activeOpacity={0.7}
          >
            <View style={[styles.actionIconBox, { backgroundColor: '#DBEAFE' }]}>
              <Cpu size={20} color="#2563EB" />
            </View>
            <Text style={styles.actionCardTitle}>Mẻ chế biến</Text>
            <Text style={styles.actionCardSub}>Sấy / Xay xát / Trộn</Text>
          </TouchableOpacity>

          <TouchableOpacity
            style={[styles.actionCard, { backgroundColor: '#FAF5FF', borderColor: '#E9D5FF' }]}
            onPress={() => router.push('/farmer-form')}
            activeOpacity={0.7}
          >
            <View style={[styles.actionIconBox, { backgroundColor: '#F3E8FF' }]}>
              <UserPlus size={20} color="#9333EA" />
            </View>
            <Text style={styles.actionCardTitle}>Thêm nông hộ</Text>
            <Text style={styles.actionCardSub}>Đăng ký thực địa</Text>
          </TouchableOpacity>
        </View>

        {/* Recent Procurements & Mill Activity */}
        <View style={styles.sectionHeaderRow}>
          <Text style={styles.sectionTitle}>Phiếu thu mua gần đây</Text>
          <TouchableOpacity onPress={() => router.push('/(tabs)/field')}>
            <Text style={styles.seeAllText}>Xem tất cả</Text>
          </TouchableOpacity>
        </View>

        {receipts.slice(0, 2).map((receipt) => (
          <Card key={receipt.id} style={styles.receiptCard}>
            <View style={styles.receiptHeader}>
              <View>
                <Text style={styles.receiptNumber}>{receipt.receiptNumber}</Text>
                <Text style={styles.receiptFarmer}>{receipt.farmerName}</Text>
              </View>
              <StatusBadge status={receipt.status} />
            </View>

            <View style={styles.receiptDivider} />

            <View style={styles.receiptDetailsRow}>
              <View style={styles.detailCol}>
                <Text style={styles.detailLabel}>Loại hàng</Text>
                <Text style={styles.detailValue}>{receipt.commodityName}</Text>
              </View>
              <View style={styles.detailCol}>
                <Text style={styles.detailLabel}>Khối lượng</Text>
                <Text style={[styles.detailValue, { color: Colors.primary, fontWeight: '800' }]}>
                  {formatMass(receipt.measuredGrossKg, 0)} kg
                </Text>
              </View>
              <View style={styles.detailCol}>
                <Text style={styles.detailLabel}>Độ ẩm</Text>
                <Text style={styles.detailValue}>{receipt.moisturePercent}%</Text>
              </View>
            </View>
          </Card>
        ))}

        {/* Traceability Callout */}
        <Card style={styles.traceBannerCard}>
          <View style={styles.traceBannerRow}>
            <View style={styles.traceIconWrapper}>
              <QrCode size={28} color="#FFFFFF" />
            </View>
            <View style={styles.traceBannerTextCol}>
              <Text style={styles.traceBannerTitle}>Tra cứu nguồn gốc & MRL</Text>
              <Text style={styles.traceBannerDesc}>
                Kiểm tra chuỗi truy xuất hành trình hạt gạo từ đồng ruộng đến bàn ăn.
              </Text>
            </View>
          </View>
          <TouchableOpacity
            style={styles.traceBannerButton}
            onPress={() => router.push('/(tabs)/trace')}
            activeOpacity={0.8}
          >
            <Text style={styles.traceBannerButtonText}>Quét & Tra cứu ngay</Text>
            <ArrowRight size={16} color="#FFFFFF" />
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
  scrollView: {
    flex: 1,
  },
  contentContainer: {
    paddingHorizontal: 16,
    paddingTop: 16,
    paddingBottom: 40,
  },
  welcomeSection: {
    marginBottom: 16,
  },
  greetingText: {
    fontSize: 14,
    color: Colors.textSecondary,
    fontWeight: '500',
  },
  userNameText: {
    fontSize: 22,
    fontWeight: '800',
    color: Colors.text,
    letterSpacing: -0.3,
  },
  orgText: {
    fontSize: 13,
    color: Colors.primary,
    fontWeight: '600',
    marginTop: 2,
  },
  offlineBanner: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: 10,
    backgroundColor: '#FEF3C7',
    borderWidth: 1,
    borderColor: '#FCD34D',
    borderRadius: 14,
    padding: 12,
    marginBottom: 16,
  },
  offlineBannerTextCol: {
    flex: 1,
  },
  offlineBannerTitle: {
    fontSize: 13,
    fontWeight: '700',
    color: '#92400E',
  },
  offlineBannerSub: {
    fontSize: 11,
    color: '#B45309',
    marginTop: 1,
  },
  sectionHeaderRow: {
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'space-between',
    marginTop: 16,
    marginBottom: 12,
  },
  sectionTitle: {
    fontSize: 16,
    fontWeight: '800',
    color: Colors.text,
  },
  sectionBadge: {
    fontSize: 12,
    fontWeight: '600',
    color: Colors.textSecondary,
    backgroundColor: Colors.cardSubtle,
    paddingHorizontal: 8,
    paddingVertical: 3,
    borderRadius: 8,
  },
  seeAllText: {
    fontSize: 13,
    fontWeight: '700',
    color: Colors.primary,
  },
  metricsGrid: {
    flexDirection: 'row',
    gap: 10,
  },
  quickActionsGrid: {
    flexDirection: 'row',
    flexWrap: 'wrap',
    gap: 10,
  },
  actionCard: {
    width: '48%',
    borderRadius: 16,
    padding: 14,
    borderWidth: 1,
  },
  actionIconBox: {
    width: 38,
    height: 38,
    borderRadius: 10,
    alignItems: 'center',
    justifyContent: 'center',
    marginBottom: 10,
  },
  actionCardTitle: {
    fontSize: 14,
    fontWeight: '700',
    color: Colors.text,
  },
  actionCardSub: {
    fontSize: 11,
    color: Colors.textSecondary,
    marginTop: 2,
  },
  receiptCard: {
    marginBottom: 10,
  },
  receiptHeader: {
    flexDirection: 'row',
    justifyContent: 'space-between',
    alignItems: 'center',
  },
  receiptNumber: {
    fontSize: 14,
    fontWeight: '800',
    color: Colors.text,
  },
  receiptFarmer: {
    fontSize: 13,
    color: Colors.textSecondary,
    marginTop: 2,
  },
  receiptDivider: {
    height: 1,
    backgroundColor: Colors.divider,
    marginVertical: 10,
  },
  receiptDetailsRow: {
    flexDirection: 'row',
    justifyContent: 'space-between',
  },
  detailCol: {
    flex: 1,
  },
  detailLabel: {
    fontSize: 11,
    color: Colors.textMuted,
    marginBottom: 2,
  },
  detailValue: {
    fontSize: 13,
    fontWeight: '600',
    color: Colors.text,
  },
  traceBannerCard: {
    backgroundColor: Colors.primaryDark,
    borderColor: 'transparent',
    marginTop: 16,
    padding: 16,
  },
  traceBannerRow: {
    flexDirection: 'row',
    gap: 12,
    alignItems: 'center',
  },
  traceIconWrapper: {
    width: 48,
    height: 48,
    borderRadius: 14,
    backgroundColor: 'rgba(255, 255, 255, 0.15)',
    alignItems: 'center',
    justifyContent: 'center',
  },
  traceBannerTextCol: {
    flex: 1,
  },
  traceBannerTitle: {
    color: '#FFFFFF',
    fontSize: 16,
    fontWeight: '800',
  },
  traceBannerDesc: {
    color: 'rgba(255, 255, 255, 0.8)',
    fontSize: 12,
    marginTop: 3,
    lineHeight: 16,
  },
  traceBannerButton: {
    marginTop: 14,
    backgroundColor: Colors.accent,
    borderRadius: 10,
    paddingVertical: 10,
    paddingHorizontal: 16,
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'center',
    gap: 8,
  },
  traceBannerButtonText: {
    color: '#FFFFFF',
    fontWeight: '700',
    fontSize: 13,
  },
});
