import React, { useState } from 'react';
import {
  View,
  Text,
  StyleSheet,
  ScrollView,
  TextInput,
  TouchableOpacity,
  SafeAreaView,
  RefreshControl,
} from 'react-native';
import { Colors } from '../../theme/colors';
import { Header } from '../../components/common/Header';
import { Card } from '../../components/common/Card';
import { StatusBadge } from '../../components/common/StatusBadge';
import { useMillStore } from '../../store/millStore';
import { formatMass } from '../../utils/decimalMass';
import {
  Search,
  QrCode,
  ShieldCheck,
  CheckCircle2,
  Check,
  Sparkles,
} from 'lucide-react-native';

export default function TraceScreen() {
  const { traceLots, getLotByCode, isLoading, fetchMillData } = useMillStore();
  const [searchInput, setSearchInput] = useState('');
  const [selectedLotId, setSelectedLotId] = useState<string | null>(null);

  const currentLot = (selectedLotId ? traceLots.find((l) => l.id === selectedLotId) : undefined) ?? traceLots[0];

  const handleSearch = () => {
    const found = getLotByCode(searchInput);
    if (found) {
      setSelectedLotId(found.id);
    }
  };

  return (
    <SafeAreaView style={styles.safeArea}>
      <Header title="Truy xuất Nguồn gốc" subtitle="Tra cứu chuỗi hành trình và kiểm định MRL" />

      {/* Search and Scan Input */}
      <View style={styles.searchContainer}>
        <View style={styles.searchBar}>
          <Search size={18} color={Colors.textMuted} />
          <TextInput
            style={styles.input}
            placeholder="Nhập mã lô hàng (VD: RAW-...)"
            placeholderTextColor={Colors.textMuted}
            value={searchInput}
            onChangeText={setSearchInput}
            onSubmitEditing={handleSearch}
          />
          <TouchableOpacity style={styles.scanBtn} onPress={handleSearch}>
            <QrCode size={18} color={Colors.primary} />
          </TouchableOpacity>
        </View>

        {/* Suggestion Chips */}
        {traceLots.length > 0 ? (
          <View style={styles.suggestionRow}>
            <Text style={styles.suggestLabel}>Lô hàng thực tế:</Text>
            {traceLots.slice(0, 3).map((lot) => (
              <TouchableOpacity
                key={lot.id}
                style={[styles.chip, currentLot?.id === lot.id && { borderColor: Colors.primary }]}
                onPress={() => {
                  setSearchInput(lot.lotCode);
                  setSelectedLotId(lot.id);
                }}
              >
                <Text style={styles.chipText}>{lot.lotCode}</Text>
              </TouchableOpacity>
            ))}
          </View>
        ) : null}
      </View>

      <ScrollView
        style={styles.scroll}
        contentContainerStyle={styles.scrollContent}
        showsVerticalScrollIndicator={false}
        refreshControl={
          <RefreshControl
            refreshing={isLoading}
            onRefresh={fetchMillData}
            colors={[Colors.primary]}
          />
        }
      >
        {currentLot ? (
          <View>
            {/* Lot Summary Card */}
            <Card style={styles.lotHeroCard}>
              <View style={styles.heroTop}>
                <View style={{ flex: 1 }}>
                  <Text style={styles.heroCode}>{currentLot.lotCode}</Text>
                  <Text style={styles.heroProduct}>{currentLot.productType}</Text>
                </View>
                <StatusBadge status={currentLot.status} size="medium" />
              </View>

              <View style={styles.heroDivider} />

              <View style={styles.heroStatsRow}>
                <View style={styles.heroStatItem}>
                  <Text style={styles.heroStatLabel}>Khối lượng</Text>
                  <Text style={styles.heroStatVal}>
                    {formatMass(currentLot.currentMassKg, 0)} kg
                  </Text>
                </View>

                <View style={styles.heroStatItem}>
                  <Text style={styles.heroStatLabel}>Độ thuần chủng</Text>
                  <View style={{ flexDirection: 'row', alignItems: 'center', gap: 4 }}>
                    <Sparkles size={14} color="#D97706" />
                    <Text style={[styles.heroStatVal, { color: '#D97706' }]}>
                      {currentLot.purityScore}%
                    </Text>
                  </View>
                </View>

                <View style={styles.heroStatItem}>
                  <Text style={styles.heroStatLabel}>Ngày đóng gói</Text>
                  <Text style={styles.heroStatVal}>{currentLot.createdDate}</Text>
                </View>
              </View>
            </Card>

            {/* MRL Compliance Banner */}
            <Card style={styles.mrlCard}>
              <View style={styles.mrlHeader}>
                <View style={styles.mrlIconBox}>
                  <ShieldCheck size={24} color="#059669" />
                </View>
                <View style={{ flex: 1 }}>
                  <Text style={styles.mrlTitle}>Chứng nhận Dư lượng MRL Đạt chuẩn</Text>
                  <Text style={styles.mrlSub}>
                    Phù hợp quy chuẩn xuất khẩu thị trường EU, US và Nhật Bản.
                  </Text>
                </View>
              </View>

              <View style={styles.mrlDivider} />

              <View style={styles.mrlChecklist}>
                <View style={styles.mrlItem}>
                  <Check size={14} color="#059669" />
                  <Text style={styles.mrlItemText}>Azoxystrobin: 0.02 mg/kg (Ngưỡng 2.0)</Text>
                </View>
                <View style={styles.mrlItem}>
                  <Check size={14} color="#059669" />
                  <Text style={styles.mrlItemText}>Tricyclazole: Không phát hiện (LOD &lt; 0.01)</Text>
                </View>
                <View style={styles.mrlItem}>
                  <Check size={14} color="#059669" />
                  <Text style={styles.mrlItemText}>Kim loại nặng (Cadmium, Chì): Đạt an toàn</Text>
                </View>
              </View>
            </Card>

            {/* Farmer Origin Composition */}
            <View style={styles.sectionHeaderRow}>
              <Text style={styles.sectionTitle}>Nguồn gốc Nông hộ & Thửa ruộng</Text>
            </View>

            {currentLot.farmerOrigins.map((origin, index) => (
              <Card key={index} style={styles.originCard}>
                <View style={styles.originTop}>
                  <View>
                    <Text style={styles.originFarmer}>{origin.farmerName}</Text>
                    <Text style={styles.originPlot}>
                      Mã: {origin.farmerCode} • Thửa {origin.plotCode} ({origin.province})
                    </Text>
                  </View>
                  <View style={styles.originPercentBadge}>
                    <Text style={styles.originPercentText}>{origin.percentage}%</Text>
                  </View>
                </View>

                <View style={styles.originDivider} />

                <View style={styles.originFooter}>
                  <Text style={styles.originVariety}>Giống: {origin.variety}</Text>
                  <Text style={styles.originHarvest}>Thu hoạch: {origin.harvestDate}</Text>
                </View>
              </Card>
            ))}

            {/* Supply Chain Timeline */}
            <View style={styles.sectionHeaderRow}>
              <Text style={styles.sectionTitle}>Hành trình chuỗi cung ứng</Text>
            </View>

            <Card style={styles.timelineCard}>
              {currentLot.history.map((step, idx) => (
                <View key={idx} style={styles.timelineStep}>
                  <View style={styles.timelineLeft}>
                    <View style={styles.timelineDot}>
                      <CheckCircle2 size={12} color="#FFFFFF" />
                    </View>
                    {idx < currentLot.history.length - 1 && (
                      <View style={styles.timelineLine} />
                    )}
                  </View>

                  <View style={styles.timelineRight}>
                    <View style={styles.stepHeader}>
                      <Text style={styles.stepTitle}>{step.stage}</Text>
                      <Text style={styles.stepTime}>{step.timestamp}</Text>
                    </View>
                    <Text style={styles.stepOperator}>Thực hiện: {step.operator}</Text>
                    <Text style={styles.stepDetail}>{step.detail}</Text>
                  </View>
                </View>
              ))}
            </Card>
          </View>
        ) : (
          <View style={styles.emptyContainer}>
            <Text style={styles.emptyText}>Không tìm thấy mã lô tương ứng.</Text>
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
  searchContainer: {
    backgroundColor: '#FFFFFF',
    padding: 16,
    borderBottomWidth: 1,
    borderBottomColor: Colors.border,
  },
  searchBar: {
    flexDirection: 'row',
    alignItems: 'center',
    backgroundColor: Colors.cardSubtle,
    borderRadius: 12,
    paddingHorizontal: 12,
    height: 44,
    gap: 8,
  },
  input: {
    flex: 1,
    fontSize: 14,
    color: Colors.text,
  },
  scanBtn: {
    padding: 6,
    borderRadius: 8,
    backgroundColor: '#FFFFFF',
  },
  suggestionRow: {
    flexDirection: 'row',
    alignItems: 'center',
    marginTop: 10,
    gap: 8,
  },
  suggestLabel: {
    fontSize: 12,
    color: Colors.textMuted,
  },
  chip: {
    backgroundColor: Colors.primarySurface,
    paddingHorizontal: 10,
    paddingVertical: 4,
    borderRadius: 12,
  },
  chipText: {
    fontSize: 12,
    fontWeight: '600',
    color: Colors.primary,
  },
  scroll: {
    flex: 1,
  },
  scrollContent: {
    padding: 16,
    paddingBottom: 40,
  },
  lotHeroCard: {
    backgroundColor: '#FFFFFF',
    borderColor: Colors.border,
    marginBottom: 12,
  },
  heroTop: {
    flexDirection: 'row',
    justifyContent: 'space-between',
    alignItems: 'flex-start',
  },
  heroCode: {
    fontSize: 17,
    fontWeight: '800',
    color: Colors.text,
  },
  heroProduct: {
    fontSize: 13,
    color: Colors.textSecondary,
    marginTop: 2,
  },
  heroDivider: {
    height: 1,
    backgroundColor: Colors.divider,
    marginVertical: 12,
  },
  heroStatsRow: {
    flexDirection: 'row',
    justifyContent: 'space-between',
  },
  heroStatItem: {
    flex: 1,
  },
  heroStatLabel: {
    fontSize: 11,
    color: Colors.textMuted,
    marginBottom: 2,
  },
  heroStatVal: {
    fontSize: 14,
    fontWeight: '700',
    color: Colors.text,
  },
  mrlCard: {
    backgroundColor: '#F0FDF4',
    borderColor: '#BBF7D0',
    marginBottom: 16,
  },
  mrlHeader: {
    flexDirection: 'row',
    gap: 12,
    alignItems: 'center',
  },
  mrlIconBox: {
    width: 40,
    height: 40,
    borderRadius: 10,
    backgroundColor: '#DCFCE7',
    alignItems: 'center',
    justifyContent: 'center',
  },
  mrlTitle: {
    fontSize: 14,
    fontWeight: '800',
    color: '#166534',
  },
  mrlSub: {
    fontSize: 12,
    color: '#15803D',
    marginTop: 2,
  },
  mrlDivider: {
    height: 1,
    backgroundColor: '#BBF7D0',
    marginVertical: 10,
  },
  mrlChecklist: {
    gap: 6,
  },
  mrlItem: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: 6,
  },
  mrlItemText: {
    fontSize: 12,
    fontWeight: '600',
    color: '#166534',
  },
  sectionHeaderRow: {
    marginTop: 8,
    marginBottom: 10,
  },
  sectionTitle: {
    fontSize: 15,
    fontWeight: '800',
    color: Colors.text,
  },
  originCard: {
    marginBottom: 10,
  },
  originTop: {
    flexDirection: 'row',
    justifyContent: 'space-between',
    alignItems: 'center',
  },
  originFarmer: {
    fontSize: 14,
    fontWeight: '800',
    color: Colors.text,
  },
  originPlot: {
    fontSize: 12,
    color: Colors.textSecondary,
    marginTop: 2,
  },
  originPercentBadge: {
    backgroundColor: Colors.primarySurface,
    paddingHorizontal: 8,
    paddingVertical: 4,
    borderRadius: 8,
  },
  originPercentText: {
    fontSize: 13,
    fontWeight: '800',
    color: Colors.primary,
  },
  originDivider: {
    height: 1,
    backgroundColor: Colors.divider,
    marginVertical: 8,
  },
  originFooter: {
    flexDirection: 'row',
    justifyContent: 'space-between',
  },
  originVariety: {
    fontSize: 12,
    color: Colors.textSecondary,
  },
  originHarvest: {
    fontSize: 12,
    color: Colors.textMuted,
  },
  timelineCard: {
    padding: 16,
  },
  timelineStep: {
    flexDirection: 'row',
    gap: 12,
    marginBottom: 16,
  },
  timelineLeft: {
    alignItems: 'center',
    width: 20,
  },
  timelineDot: {
    width: 20,
    height: 20,
    borderRadius: 10,
    backgroundColor: Colors.primary,
    alignItems: 'center',
    justifyContent: 'center',
    zIndex: 2,
  },
  timelineLine: {
    width: 2,
    flex: 1,
    backgroundColor: '#CBD5E1',
    marginTop: 4,
    marginBottom: -16,
  },
  timelineRight: {
    flex: 1,
  },
  stepHeader: {
    flexDirection: 'row',
    justifyContent: 'space-between',
    alignItems: 'center',
  },
  stepTitle: {
    fontSize: 14,
    fontWeight: '700',
    color: Colors.text,
  },
  stepTime: {
    fontSize: 11,
    color: Colors.textMuted,
  },
  stepOperator: {
    fontSize: 12,
    color: Colors.primary,
    fontWeight: '600',
    marginTop: 2,
  },
  stepDetail: {
    fontSize: 12,
    color: Colors.textSecondary,
    marginTop: 2,
    lineHeight: 16,
  },
  emptyContainer: {
    padding: 30,
    alignItems: 'center',
  },
  emptyText: {
    fontSize: 14,
    color: Colors.textMuted,
  },
});
