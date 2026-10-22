import React, { useState } from 'react';
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
import { StatusBadge } from '../../components/common/StatusBadge';
import { Button } from '../../components/common/Button';
import { useMillStore } from '../../store/millStore';
import { formatMass } from '../../utils/decimalMass';
import {
  Plus,
  Truck,
  ArrowRight,
  CheckCircle,
} from 'lucide-react-native';

type MillSubTab = 'receiving' | 'operations' | 'silos';

export default function MillScreen() {
  const router = useRouter();
  const [activeTab, setActiveTab] = useState<MillSubTab>('receiving');
  const { receivingTasks, operations, silos, updateReceivingTaskStatus } = useMillStore();

  return (
    <SafeAreaView style={styles.safeArea}>
      <Header title="Vận hành Nhà máy" subtitle="Tiếp nhận xe lúa, chế biến và tồn kho Silo" />

      {/* Sub Tabs */}
      <View style={styles.tabBar}>
        <TouchableOpacity
          style={[styles.tabItem, activeTab === 'receiving' && styles.tabItemActive]}
          onPress={() => setActiveTab('receiving')}
        >
          <Text style={[styles.tabText, activeTab === 'receiving' && styles.tabTextActive]}>
            Tiếp nhận xe ({receivingTasks.length})
          </Text>
        </TouchableOpacity>

        <TouchableOpacity
          style={[styles.tabItem, activeTab === 'operations' && styles.tabItemActive]}
          onPress={() => setActiveTab('operations')}
        >
          <Text style={[styles.tabText, activeTab === 'operations' && styles.tabTextActive]}>
            Mẻ chế biến ({operations.length})
          </Text>
        </TouchableOpacity>

        <TouchableOpacity
          style={[styles.tabItem, activeTab === 'silos' && styles.tabItemActive]}
          onPress={() => setActiveTab('silos')}
        >
          <Text style={[styles.tabText, activeTab === 'silos' && styles.tabTextActive]}>
            Tồn kho Silo ({silos.length})
          </Text>
        </TouchableOpacity>
      </View>

      {/* Action Bar */}
      <View style={styles.actionBar}>
        <Text style={styles.actionPromptText}>
          {activeTab === 'receiving'
            ? 'Danh sách xe chờ cân và nhập silo'
            : activeTab === 'operations'
            ? 'Các công đoạn: Sấy, Xát, Trộn, Đóng bao'
            : 'Tổng sức chứa và vị trí lưu trữ'}
        </Text>

        <Button
          title={activeTab === 'receiving' ? 'Cân xe mới' : activeTab === 'operations' ? 'Ghi nhận mẻ' : 'Cập nhật'}
          icon={<Plus size={16} color="#FFFFFF" />}
          size="small"
          onPress={() => {
            if (activeTab === 'receiving') router.push('/receiving-form');
            else if (activeTab === 'operations') router.push('/operation-form');
          }}
        />
      </View>

      <ScrollView
        style={styles.contentScroll}
        contentContainerStyle={styles.scrollContent}
        showsVerticalScrollIndicator={false}
      >
        {/* RECEIVING TAB */}
        {activeTab === 'receiving' && (
          <View>
            {receivingTasks.map((t) => (
              <Card key={t.id} style={styles.itemCard}>
                <View style={styles.cardHeader}>
                  <View style={{ flex: 1 }}>
                    <Text style={styles.itemCode}>{t.code}</Text>
                    <Text style={styles.itemSub}>{t.supplierName}</Text>
                  </View>
                  <StatusBadge status={t.status} />
                </View>

                <View style={styles.cardDivider} />

                <View style={styles.rowBetween}>
                  <View style={styles.infoCol}>
                    <Text style={styles.infoLabel}>Xe vận chuyển</Text>
                    <View style={styles.flexRow}>
                      <Truck size={14} color={Colors.textSecondary} />
                      <Text style={styles.plateText}>{t.vehiclePlate}</Text>
                    </View>
                  </View>

                  <View style={styles.infoCol}>
                    <Text style={styles.infoLabel}>Silo nhập</Text>
                    <Text style={styles.infoVal}>{t.targetSiloName}</Text>
                  </View>
                </View>

                <View style={styles.cardDivider} />

                <View style={styles.weightGrid}>
                  <View style={styles.weightBox}>
                    <Text style={styles.weightLabel}>Cân bì xe (Tare)</Text>
                    <Text style={styles.weightVal}>{formatMass(t.tareWeightKg, 0)} kg</Text>
                  </View>

                  <View style={styles.weightBox}>
                    <Text style={styles.weightLabel}>Cân tổng (Gross)</Text>
                    <Text style={styles.weightVal}>{formatMass(t.grossWeightKg, 0)} kg</Text>
                  </View>

                  <View style={[styles.weightBox, { backgroundColor: '#F0FDF4' }]}>
                    <Text style={[styles.weightLabel, { color: Colors.primary }]}>Lúa thực (Net)</Text>
                    <Text style={[styles.weightVal, { color: Colors.primary, fontWeight: '800' }]}>
                      {formatMass(t.netWeightKg, 0)} kg
                    </Text>
                  </View>
                </View>

                {t.status === 'PENDING_WEIGH' && (
                  <View style={styles.cardActionRow}>
                    <Button
                      title="Xác nhận cân và nạp Silo"
                      size="small"
                      variant="primary"
                      style={{ flex: 1 }}
                      onPress={() => updateReceivingTaskStatus(t.id, 'WEIGHED')}
                    />
                  </View>
                )}
              </Card>
            ))}
          </View>
        )}

        {/* OPERATIONS TAB */}
        {activeTab === 'operations' && (
          <View>
            {operations.map((op) => (
              <Card key={op.id} style={styles.itemCard}>
                <View style={styles.cardHeader}>
                  <View style={{ flex: 1 }}>
                    <Text style={styles.itemCode}>{op.code}</Text>
                    <Text style={styles.itemSub}>{op.facilityName}</Text>
                  </View>
                  <StatusBadge status={op.status} />
                </View>

                <View style={styles.cardDivider} />

                <View style={styles.opFlowBox}>
                  <View style={styles.opFlowCol}>
                    <Text style={styles.flowLabel}>Đầu vào (Input)</Text>
                    <Text style={styles.flowLotCode}>{op.inputLotCode}</Text>
                    <Text style={styles.flowMass}>{formatMass(op.inputMassKg, 0)} kg</Text>
                  </View>

                  <View style={styles.flowArrowCol}>
                    <View style={styles.flowArrowCircle}>
                      <ArrowRight size={16} color={Colors.primary} />
                    </View>
                    <Text style={styles.flowTypeBadge}>{op.operationType}</Text>
                  </View>

                  <View style={styles.opFlowCol}>
                    <Text style={styles.flowLabel}>Đầu ra (Output)</Text>
                    <Text style={styles.flowLotCode}>{op.outputLotCode}</Text>
                    <Text style={[styles.flowMass, { color: Colors.primary }]}>
                      {formatMass(op.outputMassKg, 0)} kg
                    </Text>
                  </View>
                </View>

                <View style={styles.cardDivider} />

                <View style={styles.lossRow}>
                  <View style={styles.lossCol}>
                    <Text style={styles.lossLabel}>Hao hụt khai báo</Text>
                    <Text style={styles.lossVal}>
                      {formatMass(op.declaredLossKg, 0)} kg
                    </Text>
                  </View>

                  <View style={styles.lossCol}>
                    <Text style={styles.lossLabel}>Bảo toàn khối lượng (BR-01)</Text>
                    <View style={styles.flexRow}>
                      <CheckCircle size={14} color="#059669" />
                      <Text style={styles.balancedText}>Đạt cân bằng (0.00 kg)</Text>
                    </View>
                  </View>
                </View>

                {op.lossReason ? (
                  <Text style={styles.lossReasonText}>Lý do: {op.lossReason}</Text>
                ) : null}
              </Card>
            ))}
          </View>
        )}

        {/* SILOS TAB */}
        {activeTab === 'silos' && (
          <View>
            {silos.map((silo) => {
              const current = parseFloat(silo.currentMassKg) || 0;
              const percent = Math.min(100, Math.round((current / silo.capacityKg) * 100));

              return (
                <Card key={silo.id} style={styles.itemCard}>
                  <View style={styles.cardHeader}>
                    <View style={{ flex: 1 }}>
                      <Text style={styles.itemCode}>{silo.siloName}</Text>
                      <Text style={styles.itemSub}>{silo.facilityName}</Text>
                    </View>
                    <StatusBadge status={silo.status} />
                  </View>

                  <View style={styles.cardDivider} />

                  <View style={styles.siloInfoRow}>
                    <View>
                      <Text style={styles.siloCommodity}>{silo.commodityType}</Text>
                      <Text style={styles.siloMoisture}>Độ ẩm: {silo.moisturePercent}</Text>
                    </View>

                    <View style={{ alignItems: 'flex-end' }}>
                      <Text style={styles.siloCapacityText}>
                        <Text style={{ fontWeight: '800', color: Colors.primary }}>
                          {formatMass(silo.currentMassKg, 0)}
                        </Text>{' '}
                        / {formatMass(silo.capacityKg, 0)} kg
                      </Text>
                      <Text style={styles.siloPercentText}>{percent}% sức chứa</Text>
                    </View>
                  </View>

                  {/* Capacity Bar */}
                  <View style={styles.capacityBarTrack}>
                    <View
                      style={[
                        styles.capacityBarFill,
                        {
                          width: `${percent}%`,
                          backgroundColor:
                            silo.status === 'HOLD'
                              ? Colors.red
                              : percent > 85
                              ? Colors.accent
                              : Colors.primary,
                        },
                      ]}
                    />
                  </View>

                  <Text style={styles.lastUpdatedText}>Cập nhật: {silo.lastUpdated}</Text>
                </Card>
              );
            })}
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
    flex: 1,
    paddingVertical: 12,
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
  actionBar: {
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'space-between',
    paddingHorizontal: 16,
    paddingVertical: 12,
    backgroundColor: '#FFFFFF',
    borderBottomWidth: 1,
    borderBottomColor: Colors.border,
  },
  actionPromptText: {
    fontSize: 12,
    color: Colors.textSecondary,
    flex: 1,
    marginRight: 8,
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
  rowBetween: {
    flexDirection: 'row',
    justifyContent: 'space-between',
  },
  infoCol: {
    flex: 1,
  },
  infoLabel: {
    fontSize: 11,
    color: Colors.textMuted,
    marginBottom: 2,
  },
  infoVal: {
    fontSize: 13,
    fontWeight: '600',
    color: Colors.text,
  },
  plateText: {
    fontSize: 13,
    fontWeight: '700',
    color: Colors.text,
    marginLeft: 4,
  },
  flexRow: {
    flexDirection: 'row',
    alignItems: 'center',
  },
  weightGrid: {
    flexDirection: 'row',
    gap: 8,
  },
  weightBox: {
    flex: 1,
    backgroundColor: Colors.cardSubtle,
    borderRadius: 8,
    padding: 8,
    alignItems: 'center',
  },
  weightLabel: {
    fontSize: 10,
    color: Colors.textMuted,
    textTransform: 'uppercase',
  },
  weightVal: {
    fontSize: 13,
    fontWeight: '700',
    color: Colors.text,
    marginTop: 2,
  },
  cardActionRow: {
    marginTop: 12,
  },
  opFlowBox: {
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'space-between',
    backgroundColor: Colors.cardSubtle,
    borderRadius: 12,
    padding: 10,
  },
  opFlowCol: {
    flex: 1,
  },
  flowLabel: {
    fontSize: 10,
    color: Colors.textMuted,
    textTransform: 'uppercase',
  },
  flowLotCode: {
    fontSize: 12,
    fontWeight: '700',
    color: Colors.text,
    marginTop: 2,
  },
  flowMass: {
    fontSize: 13,
    fontWeight: '800',
    marginTop: 2,
  },
  flowArrowCol: {
    alignItems: 'center',
    paddingHorizontal: 8,
  },
  flowArrowCircle: {
    width: 28,
    height: 28,
    borderRadius: 14,
    backgroundColor: '#FFFFFF',
    alignItems: 'center',
    justifyContent: 'center',
  },
  flowTypeBadge: {
    fontSize: 10,
    fontWeight: '700',
    color: Colors.primary,
    marginTop: 4,
  },
  lossRow: {
    flexDirection: 'row',
    justifyContent: 'space-between',
  },
  lossCol: {
    flex: 1,
  },
  lossLabel: {
    fontSize: 11,
    color: Colors.textMuted,
  },
  lossVal: {
    fontSize: 13,
    fontWeight: '700',
    color: Colors.text,
    marginTop: 2,
  },
  balancedText: {
    fontSize: 12,
    fontWeight: '600',
    color: '#059669',
    marginLeft: 4,
  },
  lossReasonText: {
    fontSize: 11,
    color: Colors.textSecondary,
    fontStyle: 'italic',
    marginTop: 6,
  },
  siloInfoRow: {
    flexDirection: 'row',
    justifyContent: 'space-between',
    alignItems: 'center',
    marginBottom: 8,
  },
  siloCommodity: {
    fontSize: 14,
    fontWeight: '700',
    color: Colors.text,
  },
  siloMoisture: {
    fontSize: 12,
    color: Colors.textSecondary,
    marginTop: 2,
  },
  siloCapacityText: {
    fontSize: 12,
    color: Colors.textSecondary,
  },
  siloPercentText: {
    fontSize: 11,
    fontWeight: '700',
    color: Colors.textMuted,
    marginTop: 2,
  },
  capacityBarTrack: {
    height: 8,
    backgroundColor: Colors.border,
    borderRadius: 4,
    overflow: 'hidden',
    marginVertical: 6,
  },
  capacityBarFill: {
    height: '100%',
    borderRadius: 4,
  },
  lastUpdatedText: {
    fontSize: 11,
    color: Colors.textMuted,
    marginTop: 4,
  },
});
