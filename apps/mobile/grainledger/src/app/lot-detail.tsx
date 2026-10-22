import React from 'react';
import {
  View,
  Text,
  StyleSheet,
  ScrollView,
  SafeAreaView,
  TouchableOpacity,
} from 'react-native';
import { useRouter, useLocalSearchParams } from 'expo-router';
import { Colors } from '../theme/colors';
import { Card } from '../components/common/Card';
import { StatusBadge } from '../components/common/StatusBadge';
import { useMillStore } from '../store/millStore';
import { formatMass } from '../utils/decimalMass';
import {
  ArrowLeft,
  ShieldCheck,
  CheckCircle2,
  Share2,
} from 'lucide-react-native';

export default function LotDetailScreen() {
  const router = useRouter();
  const params = useLocalSearchParams<{ code?: string }>();
  const { traceLots, getLotByCode } = useMillStore();

  const lot = (params.code ? getLotByCode(params.code) : null) || traceLots[0];

  return (
    <SafeAreaView style={styles.safeArea}>
      <View style={styles.header}>
        <TouchableOpacity style={styles.backBtn} onPress={() => router.back()}>
          <ArrowLeft size={20} color={Colors.text} />
        </TouchableOpacity>
        <Text style={styles.headerTitle}>Chi Tiết Lô Hàng & Truy Xuất</Text>
        <TouchableOpacity style={styles.backBtn} onPress={() => {}}>
          <Share2 size={18} color={Colors.text} />
        </TouchableOpacity>
      </View>

      <ScrollView
        style={styles.scroll}
        contentContainerStyle={styles.scrollContent}
        showsVerticalScrollIndicator={false}
      >
        {/* Hero Card */}
        <Card style={styles.heroCard}>
          <View style={styles.heroTop}>
            <View style={{ flex: 1 }}>
              <Text style={styles.heroCode}>{lot.lotCode}</Text>
              <Text style={styles.heroTitle}>{lot.productType}</Text>
            </View>
            <StatusBadge status={lot.status} size="medium" />
          </View>

          <View style={styles.divider} />

          <View style={styles.statsRow}>
            <View style={styles.statCol}>
              <Text style={styles.statLabel}>Khối lượng hiện tại</Text>
              <Text style={styles.statVal}>{formatMass(lot.currentMassKg, 0)} kg</Text>
            </View>
            <View style={styles.statCol}>
              <Text style={styles.statLabel}>Độ thuần hạt</Text>
              <Text style={[styles.statVal, { color: Colors.accent }]}>{lot.purityScore}%</Text>
            </View>
            <View style={styles.statCol}>
              <Text style={styles.statLabel}>Cơ sở chế biến</Text>
              <Text style={styles.statVal}>{lot.facilityName}</Text>
            </View>
          </View>
        </Card>

        {/* MRL Compliance Status */}
        <Card style={styles.mrlCard}>
          <View style={styles.mrlHeader}>
            <ShieldCheck size={22} color="#059669" />
            <Text style={styles.mrlTitle}>Đạt tiêu chuẩn dư lượng MRL Xuất Khẩu</Text>
          </View>
          <Text style={styles.mrlDesc}>
            Phân tích bởi Phòng Thí Nghiệm Quatest 3. Không phát hiện dư lượng hoạt chất
            Tricyclazole, Azoxystrobin nằm dưới ngưỡng an toàn theo quy chuẩn EU 396/2005.
          </Text>
        </Card>

        {/* Farmer Origins */}
        <View style={styles.sectionHeaderRow}>
          <Text style={styles.sectionTitle}>Tỷ lệ đóng góp nguồn gốc</Text>
        </View>

        {lot.farmerOrigins.map((origin, idx) => (
          <Card key={idx} style={styles.originCard}>
            <View style={styles.originHeader}>
              <View>
                <Text style={styles.originName}>{origin.farmerName}</Text>
                <Text style={styles.originCode}>
                  Mã nông hộ: {origin.farmerCode} • Thửa: {origin.plotCode}
                </Text>
              </View>
              <View style={styles.percentBadge}>
                <Text style={styles.percentText}>{origin.percentage}%</Text>
              </View>
            </View>
            <Text style={styles.originSub}>
              Giống: {origin.variety} • Thu hoạch: {origin.harvestDate}
            </Text>
          </Card>
        ))}

        {/* Full Timeline */}
        <View style={styles.sectionHeaderRow}>
          <Text style={styles.sectionTitle}>Nhật ký hành trình chế biến</Text>
        </View>

        <Card style={styles.timelineCard}>
          {lot.history.map((h, i) => (
            <View key={i} style={styles.timelineRow}>
              <View style={styles.dot}>
                <CheckCircle2 size={12} color="#FFFFFF" />
              </View>
              <View style={{ flex: 1 }}>
                <View style={styles.timelineHeader}>
                  <Text style={styles.timelineStage}>{h.stage}</Text>
                  <Text style={styles.timelineTime}>{h.timestamp}</Text>
                </View>
                <Text style={styles.timelineOperator}>{h.operator}</Text>
                <Text style={styles.timelineDetail}>{h.detail}</Text>
              </View>
            </View>
          ))}
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
  headerTitle: {
    fontSize: 16,
    fontWeight: '800',
    color: Colors.text,
  },
  scroll: {
    flex: 1,
  },
  scrollContent: {
    padding: 16,
    paddingBottom: 40,
  },
  heroCard: {
    marginBottom: 12,
  },
  heroTop: {
    flexDirection: 'row',
    justifyContent: 'space-between',
    alignItems: 'flex-start',
  },
  heroCode: {
    fontSize: 16,
    fontWeight: '800',
    color: Colors.text,
  },
  heroTitle: {
    fontSize: 13,
    color: Colors.textSecondary,
    marginTop: 2,
  },
  divider: {
    height: 1,
    backgroundColor: Colors.divider,
    marginVertical: 12,
  },
  statsRow: {
    flexDirection: 'row',
    justifyContent: 'space-between',
  },
  statCol: {
    flex: 1,
  },
  statLabel: {
    fontSize: 11,
    color: Colors.textMuted,
    marginBottom: 2,
  },
  statVal: {
    fontSize: 13,
    fontWeight: '700',
    color: Colors.text,
  },
  mrlCard: {
    backgroundColor: '#ECFDF5',
    borderColor: '#A7F3D0',
    marginBottom: 16,
  },
  mrlHeader: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: 8,
    marginBottom: 6,
  },
  mrlTitle: {
    fontSize: 14,
    fontWeight: '800',
    color: '#065F46',
  },
  mrlDesc: {
    fontSize: 12,
    color: '#047857',
    lineHeight: 16,
  },
  sectionHeaderRow: {
    marginBottom: 10,
    marginTop: 6,
  },
  sectionTitle: {
    fontSize: 15,
    fontWeight: '800',
    color: Colors.text,
  },
  originCard: {
    marginBottom: 10,
  },
  originHeader: {
    flexDirection: 'row',
    justifyContent: 'space-between',
    alignItems: 'center',
  },
  originName: {
    fontSize: 14,
    fontWeight: '700',
    color: Colors.text,
  },
  originCode: {
    fontSize: 11,
    color: Colors.textSecondary,
    marginTop: 2,
  },
  percentBadge: {
    backgroundColor: Colors.primarySurface,
    paddingHorizontal: 8,
    paddingVertical: 4,
    borderRadius: 8,
  },
  percentText: {
    fontSize: 13,
    fontWeight: '800',
    color: Colors.primary,
  },
  originSub: {
    fontSize: 12,
    color: Colors.textMuted,
    marginTop: 6,
  },
  timelineCard: {
    padding: 16,
  },
  timelineRow: {
    flexDirection: 'row',
    gap: 12,
    marginBottom: 14,
  },
  dot: {
    width: 18,
    height: 18,
    borderRadius: 9,
    backgroundColor: Colors.primary,
    alignItems: 'center',
    justifyContent: 'center',
    marginTop: 2,
  },
  timelineHeader: {
    flexDirection: 'row',
    justifyContent: 'space-between',
    alignItems: 'center',
  },
  timelineStage: {
    fontSize: 13,
    fontWeight: '700',
    color: Colors.text,
  },
  timelineTime: {
    fontSize: 11,
    color: Colors.textMuted,
  },
  timelineOperator: {
    fontSize: 12,
    color: Colors.primary,
    fontWeight: '600',
    marginTop: 1,
  },
  timelineDetail: {
    fontSize: 12,
    color: Colors.textSecondary,
    marginTop: 2,
    lineHeight: 16,
  },
});
