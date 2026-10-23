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
import { ArrowLeft } from 'lucide-react-native';
import { toDecimal, formatMass } from '../utils/decimalMass';

export default function ProcurementFormScreen() {
  const router = useRouter();
  const { farmers, plots, addReceipt } = useFieldStore();

  const [selectedFarmerId, setSelectedFarmerId] = useState(farmers[0]?.id || '');
  const [selectedPlotId, setSelectedPlotId] = useState(plots[0]?.id || '');
  const [grossKg, setGrossKg] = useState('12500');
  const [moisture, setMoisture] = useState('24.0');
  const [pricePerKg, setPricePerKg] = useState('9800');
  const [vehiclePlate, setVehiclePlate] = useState('63C-188.22');
  const [driverName, setDriverName] = useState('Nguyễn Văn Tuấn');
  const [collectionSite, setCollectionSite] = useState('Kênh 28 Cái Bè - Tiền Giang');
  const [commodity, setCommodity] = useState('Lúa tươi ST25');
  const [isSubmitting, setIsSubmitting] = useState(false);

  const selectedFarmer = farmers.find((f) => f.id === selectedFarmerId);
  const selectedPlot = plots.find((p) => p.id === selectedPlotId);

  // Dynamic Total Calculation
  const totalAmount = toDecimal(grossKg).times(toDecimal(pricePerKg));

  const handleSubmit = async () => {
    if (!grossKg || isNaN(Number(grossKg)) || Number(grossKg) <= 0) {
      Alert.alert('Lỗi nhập liệu', 'Vui lòng nhập khối lượng cân hợp lệ (> 0 kg).');
      return;
    }

    setIsSubmitting(true);
    await addReceipt({
      farmerId: selectedFarmerId,
      farmerName: selectedFarmer?.name || 'Nông hộ',
      farmerPhone: selectedFarmer?.contact || '',
      plotSeasonId: selectedPlotId,
      plotCode: selectedPlot?.code || 'TR-01',
      commodityName: commodity,
      measuredGrossKg: grossKg,
      moisturePercent: moisture,
      pricePerKg,
      totalAmount: totalAmount.toFixed(0),
      vehiclePlate,
      driverName,
      receivingLocationId: 'loc-silo-01',
      collectionSite,
    });
    setIsSubmitting(false);

    Alert.alert(
      'Thành công',
      `Đã lập phiếu thu mua ${grossKg} kg ${commodity} từ nông hộ ${selectedFarmer?.name}. Dữ liệu đã được lưu trữ an toàn.`,
      [{ text: 'Đóng', onPress: () => router.back() }]
    );
  };

  return (
    <SafeAreaView style={styles.safeArea}>
      <View style={styles.header}>
        <TouchableOpacity style={styles.backBtn} onPress={() => router.back()}>
          <ArrowLeft size={20} color={Colors.text} />
        </TouchableOpacity>
        <Text style={styles.headerTitle}>Lập Phiếu Thu Mua Lúa Tươi</Text>
        <View style={{ width: 36 }} />
      </View>

      <ScrollView
        style={styles.scroll}
        contentContainerStyle={styles.scrollContent}
        showsVerticalScrollIndicator={false}
      >
        {/* Farmer Selection */}
        <Card style={styles.sectionCard}>
          <Text style={styles.cardTitle}>1. Chọn Nông hộ</Text>
          <ScrollView horizontal showsHorizontalScrollIndicator={false} style={styles.chipsScroll}>
            {farmers.map((f) => {
              const isSelected = f.id === selectedFarmerId;
              return (
                <TouchableOpacity
                  key={f.id}
                  style={[styles.farmerChip, isSelected && styles.farmerChipActive]}
                  onPress={() => setSelectedFarmerId(f.id)}
                >
                  <Text style={[styles.chipName, isSelected && styles.chipNameActive]}>
                    {f.name}
                  </Text>
                  <Text style={[styles.chipSub, isSelected && styles.chipSubActive]}>
                    {f.code} • {f.commune}
                  </Text>
                </TouchableOpacity>
              );
            })}
          </ScrollView>
        </Card>

        {/* Plot Selection */}
        <Card style={styles.sectionCard}>
          <Text style={styles.cardTitle}>2. Chọn Thửa ruộng & Giống lúa</Text>
          <ScrollView horizontal showsHorizontalScrollIndicator={false} style={styles.chipsScroll}>
            {plots.map((p) => {
              const isSelected = p.id === selectedPlotId;
              return (
                <TouchableOpacity
                  key={p.id}
                  style={[styles.plotChip, isSelected && styles.plotChipActive]}
                  onPress={() => {
                    setSelectedPlotId(p.id);
                    setCommodity(`Lúa tươi ${p.variety.split(' ')[0]}`);
                  }}
                >
                  <Text style={[styles.chipName, isSelected && styles.chipNameActive]}>
                    {p.code}
                  </Text>
                  <Text style={[styles.chipSub, isSelected && styles.chipSubActive]}>
                    {p.variety} ({p.areaHa} ha)
                  </Text>
                </TouchableOpacity>
              );
            })}
          </ScrollView>
        </Card>

        {/* Measurement Details */}
        <Card style={styles.sectionCard}>
          <Text style={styles.cardTitle}>3. Thông tin cân & đo độ ẩm</Text>

          <InputField
            label="Loại lúa thu mua"
            value={commodity}
            onChangeText={setCommodity}
            required
          />

          <View style={styles.inputRow}>
            <InputField
              label="Khối lượng cân (Gross)"
              value={grossKg}
              onChangeText={setGrossKg}
              keyboardType="numeric"
              unitSuffix="kg"
              required
              containerStyle={{ flex: 1 }}
            />
            <InputField
              label="Độ ẩm tại ruộng"
              value={moisture}
              onChangeText={setMoisture}
              keyboardType="numeric"
              unitSuffix="%"
              required
              containerStyle={{ flex: 1 }}
            />
          </View>

          <View style={styles.inputRow}>
            <InputField
              label="Đơn giá thu mua"
              value={pricePerKg}
              onChangeText={setPricePerKg}
              keyboardType="numeric"
              unitSuffix="đ/kg"
              containerStyle={{ flex: 1 }}
            />
          </View>

          {/* Real-time Total Callout */}
          <View style={styles.totalBox}>
            <Text style={styles.totalLabel}>Thành tiền dự kiến:</Text>
            <Text style={styles.totalValue}>{formatMass(totalAmount.toString(), 0)} VNĐ</Text>
          </View>
        </Card>

        {/* Transport & Location */}
        <Card style={styles.sectionCard}>
          <Text style={styles.cardTitle}>4. Vận chuyển & Giao nhận</Text>

          <View style={styles.inputRow}>
            <InputField
              label="Biển số xe"
              value={vehiclePlate}
              onChangeText={setVehiclePlate}
              containerStyle={{ flex: 1 }}
            />
            <InputField
              label="Tài xế vận chuyển"
              value={driverName}
              onChangeText={setDriverName}
              containerStyle={{ flex: 1 }}
            />
          </View>

          <InputField
            label="Điểm tập kết thu mua"
            value={collectionSite}
            onChangeText={setCollectionSite}
          />
        </Card>

        {/* Submit Button */}
        <Button
          title="Xác nhận & Lưu phiếu thu mua"
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
  sectionCard: {
    marginBottom: 14,
  },
  cardTitle: {
    fontSize: 14,
    fontWeight: '700',
    color: Colors.text,
    marginBottom: 12,
  },
  chipsScroll: {
    flexDirection: 'row',
    gap: 8,
  },
  farmerChip: {
    backgroundColor: Colors.cardSubtle,
    borderWidth: 1.5,
    borderColor: Colors.border,
    borderRadius: 12,
    paddingHorizontal: 14,
    paddingVertical: 10,
    marginRight: 8,
  },
  farmerChipActive: {
    backgroundColor: Colors.primarySurface,
    borderColor: Colors.primary,
  },
  plotChip: {
    backgroundColor: Colors.cardSubtle,
    borderWidth: 1.5,
    borderColor: Colors.border,
    borderRadius: 12,
    paddingHorizontal: 14,
    paddingVertical: 10,
    marginRight: 8,
  },
  plotChipActive: {
    backgroundColor: Colors.primarySurface,
    borderColor: Colors.primary,
  },
  chipName: {
    fontSize: 14,
    fontWeight: '700',
    color: Colors.text,
  },
  chipNameActive: {
    color: Colors.primary,
  },
  chipSub: {
    fontSize: 11,
    color: Colors.textSecondary,
    marginTop: 2,
  },
  chipSubActive: {
    color: Colors.primaryDark,
  },
  inputRow: {
    flexDirection: 'row',
    gap: 12,
  },
  totalBox: {
    backgroundColor: '#F0FDF4',
    borderWidth: 1,
    borderColor: '#BBF7D0',
    borderRadius: 10,
    padding: 12,
    flexDirection: 'row',
    justifyContent: 'space-between',
    alignItems: 'center',
    marginTop: 4,
  },
  totalLabel: {
    fontSize: 13,
    color: '#166534',
    fontWeight: '600',
  },
  totalValue: {
    fontSize: 16,
    fontWeight: '800',
    color: '#15803D',
  },
});
