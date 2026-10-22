import React, { useState } from 'react';
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
import { InputField } from '../components/common/InputField';
import { Button } from '../components/common/Button';
import { useFieldStore } from '../store/fieldStore';
import { ArrowLeft, ShieldAlert } from 'lucide-react-native';

const PESTICIDE_CATALOG = [
  { name: 'Amistar Top 325SC', substance: 'Azoxystrobin + Difenoconazole', phi: 14, defaultRate: '0.5', unit: 'lít/ha' },
  { name: 'Beam 75WP', substance: 'Tricyclazole', phi: 21, defaultRate: '0.3', unit: 'kg/ha' },
  { name: 'Tilt Super 300EC', substance: 'Propiconazole + Difenoconazole', phi: 14, defaultRate: '0.4', unit: 'lít/ha' },
  { name: 'Filia 525SE', substance: 'Tricyclazole + Propiconazole', phi: 21, defaultRate: '0.5', unit: 'lít/ha' },
];

export default function SprayFormScreen() {
  const router = useRouter();
  const { plots, addSpray } = useFieldStore();

  const [selectedPlotId, setSelectedPlotId] = useState(plots[0]?.id || '');
  const [selectedPesticide, setSelectedPesticide] = useState(PESTICIDE_CATALOG[0]);
  const [appliedDate, setAppliedDate] = useState(new Date().toISOString().split('T')[0]);
  const [rate, setRate] = useState(PESTICIDE_CATALOG[0].defaultRate);
  const [rateUnit, setRateUnit] = useState(PESTICIDE_CATALOG[0].unit);
  const [phiDays, setPhiDays] = useState(String(PESTICIDE_CATALOG[0].phi));
  const [isSubmitting, setIsSubmitting] = useState(false);

  const selectedPlot = plots.find((p) => p.id === selectedPlotId);

  // Compute Safe Harvest Date
  const harvestAllowed = (() => {
    try {
      const d = new Date(appliedDate);
      d.setDate(d.getDate() + (parseInt(phiDays, 10) || 14));
      return d.toISOString().split('T')[0];
    } catch {
      return 'N/A';
    }
  })();

  const handleSubmit = async () => {
    if (!selectedPlot) {
      Alert.alert('Lỗi', 'Vui lòng chọn thửa ruộng.');
      return;
    }

    setIsSubmitting(true);
    await addSpray({
      plotSeasonId: selectedPlot.id,
      plotCode: selectedPlot.code,
      farmerName: selectedPlot.farmerName,
      appliedOn: appliedDate,
      productName: selectedPesticide.name,
      substanceId: selectedPesticide.substance,
      rate: parseFloat(rate) || 0.5,
      rateUnit,
      phiDays: parseInt(phiDays, 10) || 14,
    });
    setIsSubmitting(false);

    Alert.alert(
      'Khai báo thành công',
      `Đã ghi nhận phun ${selectedPesticide.name} cho thửa ${selectedPlot.code}. Ngày thu hoạch an toàn: ${harvestAllowed}.`,
      [{ text: 'Đóng', onPress: () => router.back() }]
    );
  };

  return (
    <SafeAreaView style={styles.safeArea}>
      <View style={styles.header}>
        <TouchableOpacity style={styles.backBtn} onPress={() => router.back()}>
          <ArrowLeft size={20} color={Colors.text} />
        </TouchableOpacity>
        <Text style={styles.headerTitle}>Khai báo Phun thuốc BVTV</Text>
        <View style={{ width: 36 }} />
      </View>

      <ScrollView
        style={styles.scroll}
        contentContainerStyle={styles.scrollContent}
        showsVerticalScrollIndicator={false}
      >
        {/* Choose Plot */}
        <Card style={styles.card}>
          <Text style={styles.cardHeader}>1. Chọn Thửa ruộng</Text>
          <ScrollView horizontal showsHorizontalScrollIndicator={false}>
            {plots.map((p) => {
              const isSelected = p.id === selectedPlotId;
              return (
                <TouchableOpacity
                  key={p.id}
                  style={[styles.plotChip, isSelected && styles.plotChipActive]}
                  onPress={() => setSelectedPlotId(p.id)}
                >
                  <Text style={[styles.plotChipTitle, isSelected && styles.plotChipTitleActive]}>
                    {p.code}
                  </Text>
                  <Text style={styles.plotChipSub}>
                    {p.variety} • {p.farmerName}
                  </Text>
                </TouchableOpacity>
              );
            })}
          </ScrollView>
        </Card>

        {/* Choose Pesticide from Approved Catalog */}
        <Card style={styles.card}>
          <Text style={styles.cardHeader}>2. Chọn Thuốc BVTV trong danh mục</Text>
          {PESTICIDE_CATALOG.map((item, idx) => {
            const isSelected = selectedPesticide.name === item.name;
            return (
              <TouchableOpacity
                key={idx}
                style={[styles.pestItem, isSelected && styles.pestItemActive]}
                onPress={() => {
                  setSelectedPesticide(item);
                  setRate(item.defaultRate);
                  setRateUnit(item.unit);
                  setPhiDays(String(item.phi));
                }}
              >
                <View style={{ flex: 1 }}>
                  <Text style={[styles.pestName, isSelected && styles.pestNameActive]}>
                    {item.name}
                  </Text>
                  <Text style={styles.pestSub}>Hoạt chất: {item.substance}</Text>
                </View>
                <View style={styles.phiTag}>
                  <Text style={styles.phiTagText}>PHI: {item.phi} ngày</Text>
                </View>
              </TouchableOpacity>
            );
          })}
        </Card>

        {/* Details & Calculations */}
        <Card style={styles.card}>
          <Text style={styles.cardHeader}>3. Thông số phun & Thời gian cách ly</Text>

          <InputField
            label="Ngày phun"
            value={appliedDate}
            onChangeText={setAppliedDate}
            placeholder="YYYY-MM-DD"
            required
          />

          <View style={styles.inputRow}>
            <InputField
              label="Liều lượng"
              value={rate}
              onChangeText={setRate}
              keyboardType="numeric"
              unitSuffix={rateUnit}
              required
              containerStyle={{ flex: 1 }}
            />
            <InputField
              label="Thời gian cách ly (PHI)"
              value={phiDays}
              onChangeText={setPhiDays}
              keyboardType="numeric"
              unitSuffix="ngày"
              required
              containerStyle={{ flex: 1 }}
            />
          </View>

          {/* Safety Warning & Calculation Banner */}
          <View style={styles.safetyBox}>
            <ShieldAlert size={20} color="#D97706" />
            <View style={{ flex: 1 }}>
              <Text style={styles.safetyTitle}>Khuyến cáo thu hoạch an toàn (MRL)</Text>
              <Text style={styles.safetyDesc}>
                Lúa thuộc thửa ruộng này chỉ được phép thu hoạch từ ngày{' '}
                <Text style={{ fontWeight: '800', color: Colors.primary }}>
                  {harvestAllowed}
                </Text>{' '}
                để đảm bảo không tồn dư hoạt chất {selectedPesticide.substance}.
              </Text>
            </View>
          </View>
        </Card>

        <Button
          title="Xác nhận Khai báo"
          size="large"
          loading={isSubmitting}
          onPress={handleSubmit}
          style={{ marginTop: 10, marginBottom: 30 }}
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
  },
  card: {
    marginBottom: 12,
  },
  cardHeader: {
    fontSize: 14,
    fontWeight: '700',
    color: Colors.text,
    marginBottom: 12,
  },
  plotChip: {
    backgroundColor: Colors.cardSubtle,
    borderWidth: 1.5,
    borderColor: Colors.border,
    borderRadius: 10,
    paddingHorizontal: 12,
    paddingVertical: 8,
    marginRight: 8,
  },
  plotChipActive: {
    backgroundColor: Colors.primarySurface,
    borderColor: Colors.primary,
  },
  plotChipTitle: {
    fontSize: 13,
    fontWeight: '700',
    color: Colors.text,
  },
  plotChipTitleActive: {
    color: Colors.primary,
  },
  plotChipSub: {
    fontSize: 11,
    color: Colors.textSecondary,
    marginTop: 2,
  },
  pestItem: {
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'space-between',
    padding: 10,
    borderRadius: 10,
    borderWidth: 1.5,
    borderColor: Colors.border,
    marginBottom: 8,
    backgroundColor: '#FFFFFF',
  },
  pestItemActive: {
    backgroundColor: Colors.primarySurface,
    borderColor: Colors.primary,
  },
  pestName: {
    fontSize: 14,
    fontWeight: '700',
    color: Colors.text,
  },
  pestNameActive: {
    color: Colors.primary,
  },
  pestSub: {
    fontSize: 12,
    color: Colors.textSecondary,
    marginTop: 2,
  },
  phiTag: {
    backgroundColor: '#FEF3C7',
    paddingHorizontal: 8,
    paddingVertical: 4,
    borderRadius: 6,
  },
  phiTagText: {
    fontSize: 11,
    fontWeight: '700',
    color: '#B45309',
  },
  inputRow: {
    flexDirection: 'row',
    gap: 12,
  },
  safetyBox: {
    flexDirection: 'row',
    alignItems: 'flex-start',
    gap: 10,
    backgroundColor: '#FFFBEB',
    borderWidth: 1,
    borderColor: '#FDE68A',
    borderRadius: 10,
    padding: 12,
    marginTop: 6,
  },
  safetyTitle: {
    fontSize: 13,
    fontWeight: '700',
    color: '#92400E',
  },
  safetyDesc: {
    fontSize: 12,
    color: '#78350F',
    marginTop: 2,
    lineHeight: 16,
  },
});
