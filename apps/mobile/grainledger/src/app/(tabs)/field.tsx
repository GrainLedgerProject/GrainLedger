import React, { useState } from 'react';
import {
  View,
  Text,
  StyleSheet,
  ScrollView,
  TouchableOpacity,
  SafeAreaView,
  TextInput,
} from 'react-native';
import { useRouter } from 'expo-router';
import { Colors } from '../../theme/colors';
import { Header } from '../../components/common/Header';
import { Card } from '../../components/common/Card';
import { StatusBadge } from '../../components/common/StatusBadge';
import { Button } from '../../components/common/Button';
import { useFieldStore } from '../../store/fieldStore';
import { formatMass } from '../../utils/decimalMass';
import {
  Search,
  Plus,
  Phone,
  MapPin,
  Calendar,
  ShieldCheck,
  User,
  Trees,
} from 'lucide-react-native';

type SubTab = 'receipts' | 'farmers' | 'plots' | 'sprays';

export default function FieldScreen() {
  const router = useRouter();
  const [activeTab, setActiveTab] = useState<SubTab>('receipts');
  const [searchQuery, setSearchQuery] = useState('');

  const { farmers, plots, sprays, receipts } = useFieldStore();

  const filteredReceipts = receipts.filter(
    (r) =>
      r.receiptNumber.toLowerCase().includes(searchQuery.toLowerCase()) ||
      r.farmerName.toLowerCase().includes(searchQuery.toLowerCase()) ||
      r.commodityName.toLowerCase().includes(searchQuery.toLowerCase())
  );

  const filteredFarmers = farmers.filter(
    (f) =>
      f.name.toLowerCase().includes(searchQuery.toLowerCase()) ||
      f.code.toLowerCase().includes(searchQuery.toLowerCase()) ||
      f.contact.includes(searchQuery)
  );

  const filteredPlots = plots.filter(
    (p) =>
      p.code.toLowerCase().includes(searchQuery.toLowerCase()) ||
      p.variety.toLowerCase().includes(searchQuery.toLowerCase()) ||
      (p.farmerName && p.farmerName.toLowerCase().includes(searchQuery.toLowerCase()))
  );

  const filteredSprays = sprays.filter(
    (s) =>
      s.productName.toLowerCase().includes(searchQuery.toLowerCase()) ||
      s.plotCode.toLowerCase().includes(searchQuery.toLowerCase()) ||
      s.substanceId.toLowerCase().includes(searchQuery.toLowerCase())
  );

  return (
    <SafeAreaView style={styles.safeArea}>
      <Header title="Thực địa & Thu mua" subtitle="Quản lý nông hộ, thửa ruộng và phiếu cân" />

      {/* Sub Tabs Selector */}
      <View style={styles.tabBar}>
        <TouchableOpacity
          style={[styles.tabItem, activeTab === 'receipts' && styles.tabItemActive]}
          onPress={() => setActiveTab('receipts')}
        >
          <Text style={[styles.tabText, activeTab === 'receipts' && styles.tabTextActive]}>
            Phiếu thu mua ({receipts.length})
          </Text>
        </TouchableOpacity>

        <TouchableOpacity
          style={[styles.tabItem, activeTab === 'farmers' && styles.tabItemActive]}
          onPress={() => setActiveTab('farmers')}
        >
          <Text style={[styles.tabText, activeTab === 'farmers' && styles.tabTextActive]}>
            Nông hộ ({farmers.length})
          </Text>
        </TouchableOpacity>

        <TouchableOpacity
          style={[styles.tabItem, activeTab === 'plots' && styles.tabItemActive]}
          onPress={() => setActiveTab('plots')}
        >
          <Text style={[styles.tabText, activeTab === 'plots' && styles.tabTextActive]}>
            Thửa ruộng ({plots.length})
          </Text>
        </TouchableOpacity>

        <TouchableOpacity
          style={[styles.tabItem, activeTab === 'sprays' && styles.tabItemActive]}
          onPress={() => setActiveTab('sprays')}
        >
          <Text style={[styles.tabText, activeTab === 'sprays' && styles.tabTextActive]}>
            Khai báo thuốc ({sprays.length})
          </Text>
        </TouchableOpacity>
      </View>

      {/* Search and Action Bar */}
      <View style={styles.searchRow}>
        <View style={styles.searchWrapper}>
          <Search size={18} color={Colors.textMuted} />
          <TextInput
            style={styles.searchInput}
            placeholder="Tìm kiếm mã, tên, số ĐT..."
            placeholderTextColor={Colors.textMuted}
            value={searchQuery}
            onChangeText={setSearchQuery}
          />
        </View>

        <Button
          title="Tạo mới"
          icon={<Plus size={16} color="#FFFFFF" />}
          variant="primary"
          size="small"
          onPress={() => {
            if (activeTab === 'receipts') router.push('/procurement-form');
            else if (activeTab === 'farmers') router.push('/farmer-form');
            else if (activeTab === 'plots') router.push('/plot-form');
            else router.push('/spray-form');
          }}
        />
      </View>

      <ScrollView
        style={styles.contentScroll}
        contentContainerStyle={styles.scrollContent}
        showsVerticalScrollIndicator={false}
      >
        {/* RECEIPTS TAB */}
        {activeTab === 'receipts' && (
          <View>
            {filteredReceipts.map((r) => (
              <Card key={r.id} style={styles.itemCard}>
                <View style={styles.cardHeader}>
                  <View style={styles.headerTitleCol}>
                    <Text style={styles.itemCode}>{r.receiptNumber}</Text>
                    <Text style={styles.itemSub}>{r.commodityName}</Text>
                  </View>
                  <StatusBadge status={r.status} />
                </View>

                <View style={styles.cardDivider} />

                <View style={styles.infoRow}>
                  <User size={14} color={Colors.textSecondary} />
                  <Text style={styles.infoText}>
                    Nông hộ: <Text style={styles.infoTextBold}>{r.farmerName}</Text>
                  </Text>
                </View>

                <View style={styles.infoRow}>
                  <MapPin size={14} color={Colors.textSecondary} />
                  <Text style={styles.infoText}>
                    Thửa ruộng: {r.plotCode} • {r.collectionSite}
                  </Text>
                </View>

                <View style={styles.cardDivider} />

                <View style={styles.metricsRow}>
                  <View style={styles.metricBlock}>
                    <Text style={styles.metricLabel}>Khối lượng cân</Text>
                    <Text style={[styles.metricValue, { color: Colors.primary }]}>
                      {formatMass(r.measuredGrossKg, 0)} kg
                    </Text>
                  </View>

                  <View style={styles.metricBlock}>
                    <Text style={styles.metricLabel}>Độ ẩm</Text>
                    <Text style={styles.metricValue}>{r.moisturePercent}%</Text>
                  </View>

                  <View style={styles.metricBlock}>
                    <Text style={styles.metricLabel}>Biển số xe</Text>
                    <Text style={styles.metricValue}>{r.vehiclePlate || 'Chưa gán'}</Text>
                  </View>
                </View>
              </Card>
            ))}
          </View>
        )}

        {/* FARMERS TAB */}
        {activeTab === 'farmers' && (
          <View>
            {filteredFarmers.map((f) => (
              <Card key={f.id} style={styles.itemCard}>
                <View style={styles.cardHeader}>
                  <View>
                    <Text style={styles.itemCode}>{f.name}</Text>
                    <Text style={styles.itemSub}>Mã: {f.code}</Text>
                  </View>
                  <StatusBadge status={f.syncStatus} />
                </View>

                <View style={styles.cardDivider} />

                <View style={styles.infoRow}>
                  <Phone size={14} color={Colors.primary} />
                  <Text style={[styles.infoText, { color: Colors.primary, fontWeight: '600' }]}>
                    {f.contact}
                  </Text>
                  {f.identityNumber ? (
                    <Text style={styles.identityText}>• CCCD: {f.identityNumber}</Text>
                  ) : null}
                </View>

                <View style={styles.infoRow}>
                  <MapPin size={14} color={Colors.textSecondary} />
                  <Text style={styles.infoText}>
                    {f.commune}, {f.district}, {f.province}
                  </Text>
                </View>

                <View style={styles.cardDivider} />

                <View style={styles.footerRow}>
                  <Text style={styles.areaText}>
                    Tổng diện tích: <Text style={styles.areaBold}>{f.totalAreaHa || 0} ha</Text>
                  </Text>
                  <Text style={styles.dateText}>Đăng ký: {f.registeredAt}</Text>
                </View>
              </Card>
            ))}
          </View>
        )}

        {/* PLOTS TAB */}
        {activeTab === 'plots' && (
          <View>
            {filteredPlots.map((p) => (
              <Card key={p.id} style={styles.itemCard}>
                <View style={styles.cardHeader}>
                  <View>
                    <Text style={styles.itemCode}>{p.code}</Text>
                    <Text style={styles.itemSub}>{p.variety}</Text>
                  </View>
                  <StatusBadge status={p.syncStatus} />
                </View>

                <View style={styles.cardDivider} />

                <View style={styles.infoRow}>
                  <User size={14} color={Colors.textSecondary} />
                  <Text style={styles.infoText}>Nông hộ sở hữu: {p.farmerName}</Text>
                </View>

                <View style={styles.infoRow}>
                  <Calendar size={14} color={Colors.textSecondary} />
                  <Text style={styles.infoText}>Mùa vụ: {p.season}</Text>
                </View>

                <View style={styles.cardDivider} />

                <View style={styles.footerRow}>
                  <Text style={styles.areaText}>
                    Diện tích: <Text style={styles.areaBold}>{p.areaHa} ha</Text>
                  </Text>
                  {p.latitude && p.longitude ? (
                    <Text style={styles.gpsText}>
                      GPS: {p.latitude.toFixed(4)}, {p.longitude.toFixed(4)}
                    </Text>
                  ) : null}
                </View>
              </Card>
            ))}
          </View>
        )}

        {/* SPRAYS TAB */}
        {activeTab === 'sprays' && (
          <View>
            {filteredSprays.map((s) => (
              <Card key={s.id} style={styles.itemCard}>
                <View style={styles.cardHeader}>
                  <View style={{ flex: 1 }}>
                    <Text style={styles.itemCode}>{s.productName}</Text>
                    <Text style={styles.itemSub}>Hoạt chất: {s.substanceId}</Text>
                  </View>
                  <StatusBadge status={s.syncStatus} />
                </View>

                <View style={styles.cardDivider} />

                <View style={styles.infoRow}>
                  <Trees size={14} color={Colors.textSecondary} />
                  <Text style={styles.infoText}>Thửa ruộng: {s.plotCode}</Text>
                </View>

                <View style={styles.infoRow}>
                  <Calendar size={14} color={Colors.textSecondary} />
                  <Text style={styles.infoText}>Ngày phun: {s.appliedOn}</Text>
                  <Text style={styles.rateText}>
                    • Liều lượng: {s.rate} {s.rateUnit}
                  </Text>
                </View>

                <View style={styles.cardDivider} />

                <View style={styles.phiBanner}>
                  <ShieldCheck size={16} color="#059669" />
                  <View style={{ flex: 1 }}>
                    <Text style={styles.phiTitle}>
                      Thời gian cách ly (PHI): {s.phiDays} ngày
                    </Text>
                    <Text style={styles.phiDesc}>
                      Ngày thu hoạch an toàn dự kiến:{' '}
                      <Text style={{ fontWeight: '800' }}>{s.harvestAllowedDate}</Text>
                    </Text>
                  </View>
                </View>
              </Card>
            ))}
          </View>
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
  tabBar: {
    flexDirection: 'row',
    backgroundColor: '#FFFFFF',
    borderBottomWidth: 1,
    borderBottomColor: Colors.border,
  },
  tabItem: {
    paddingVertical: 12,
    paddingHorizontal: 12,
    alignItems: 'center',
    borderBottomWidth: 2,
    borderBottomColor: 'transparent',
  },
  tabItemActive: {
    borderBottomColor: Colors.primary,
  },
  tabText: {
    fontSize: 13,
    fontWeight: '600',
    color: Colors.textSecondary,
  },
  tabTextActive: {
    color: Colors.primary,
    fontWeight: '700',
  },
  searchRow: {
    flexDirection: 'row',
    alignItems: 'center',
    paddingHorizontal: 16,
    paddingVertical: 12,
    gap: 10,
    backgroundColor: '#FFFFFF',
  },
  searchWrapper: {
    flex: 1,
    flexDirection: 'row',
    alignItems: 'center',
    backgroundColor: Colors.cardSubtle,
    borderRadius: 10,
    paddingHorizontal: 10,
    gap: 8,
    height: 40,
  },
  searchInput: {
    flex: 1,
    fontSize: 14,
    color: Colors.text,
  },
  contentScroll: {
    flex: 1,
  },
  scrollContent: {
    padding: 16,
    paddingBottom: 40,
  },
  itemCard: {
    marginBottom: 12,
  },
  cardHeader: {
    flexDirection: 'row',
    justifyContent: 'space-between',
    alignItems: 'flex-start',
  },
  headerTitleCol: {
    flex: 1,
  },
  itemCode: {
    fontSize: 15,
    fontWeight: '800',
    color: Colors.text,
  },
  itemSub: {
    fontSize: 12,
    color: Colors.textSecondary,
    marginTop: 2,
  },
  cardDivider: {
    height: 1,
    backgroundColor: Colors.divider,
    marginVertical: 10,
  },
  infoRow: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: 6,
    marginBottom: 6,
  },
  infoText: {
    fontSize: 13,
    color: Colors.textSecondary,
  },
  infoTextBold: {
    color: Colors.text,
    fontWeight: '700',
  },
  identityText: {
    fontSize: 12,
    color: Colors.textMuted,
  },
  metricsRow: {
    flexDirection: 'row',
    justifyContent: 'space-between',
  },
  metricBlock: {
    flex: 1,
  },
  metricLabel: {
    fontSize: 11,
    color: Colors.textMuted,
    marginBottom: 2,
  },
  metricValue: {
    fontSize: 14,
    fontWeight: '700',
    color: Colors.text,
  },
  footerRow: {
    flexDirection: 'row',
    justifyContent: 'space-between',
    alignItems: 'center',
  },
  areaText: {
    fontSize: 13,
    color: Colors.textSecondary,
  },
  areaBold: {
    fontWeight: '700',
    color: Colors.primary,
  },
  dateText: {
    fontSize: 12,
    color: Colors.textMuted,
  },
  gpsText: {
    fontSize: 12,
    color: Colors.textSecondary,
    fontFamily: 'monospace',
  },
  rateText: {
    fontSize: 12,
    color: Colors.textSecondary,
  },
  phiBanner: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: 8,
    backgroundColor: '#ECFDF5',
    padding: 10,
    borderRadius: 8,
    borderWidth: 1,
    borderColor: '#A7F3D0',
  },
  phiTitle: {
    fontSize: 12,
    fontWeight: '700',
    color: '#065F46',
  },
  phiDesc: {
    fontSize: 11,
    color: '#047857',
    marginTop: 2,
  },
});
