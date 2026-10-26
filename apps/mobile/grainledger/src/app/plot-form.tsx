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

const VARIETIES = ['ST25 Lúa Tôm', 'Jasmine 85', 'OM 5451', 'Đài Thơm 8', 'Nàng Hoa 9'];
const SEASONS = ['Đông Xuân 2026', 'Hè Thu 2026', 'Thu Đông 2026'];

export default function PlotFormScreen() {
  const router = useRouter();
  const { farmers, addPlot } = useFieldStore();

  const [selectedFarmerId, setSelectedFarmerId] = useState(farmers[0]?.id || '');
  const [variety, setVariety] = useState(VARIETIES[0]);
  const [season, setSeason] = useState(SEASONS[0]);
  const [areaHa, setAreaHa] = useState('2.5');
  const [latitude, setLatitude] = useState('10.3789');
  const [longitude, setLongitude] = useState('105.9542');
  const [isSubmitting, setIsSubmitting] = useState(false);

  const handleSubmit = async () => {
    if (!areaHa || isNaN(Number(areaHa)) || Number(areaHa) <= 0) {
      Alert.alert('Lỗi nhập liệu', 'Vui lòng nhập diện tích hợp lệ (> 0 ha).');
      return;
    }

    setIsSubmitting(true);
    const newPlot = await addPlot({
      farmerId: selectedFarmerId,
      variety,
      season,
      areaHa: parseFloat(areaHa),
      latitude: parseFloat(latitude) || undefined,
      longitude: parseFloat(longitude) || undefined,
    });
    setIsSubmitting(false);

    Alert.alert(
      'Thành công',
      `Đã tạo thửa ruộng ${newPlot.code} (${newPlot.areaHa} ha - ${newPlot.variety}).`,
      [{ text: 'Đóng', onPress: () => router.back() }]
    );
  };

  return (
    <SafeAreaView style={styles.safeArea}>
      <View style={styles.header}>
        <TouchableOpacity style={styles.backBtn} onPress={() => router.back()}>
          <ArrowLeft size={20} color={Colors.text} />
        </TouchableOpacity>
        <Text style={styles.headerTitle}>Đăng ký Thửa ruộng Mới</Text>
        <View style={{ width: 36 }} />
      </View>

      <ScrollView
        style={styles.scroll}
        contentContainerStyle={styles.scrollContent}
        showsVerticalScrollIndicator={false}
      >
        {/* Select Farmer */}
        <Card style={styles.card}>
          <Text style={styles.cardHeader}>1. Chọn Nông hộ</Text>
          <ScrollView horizontal showsHorizontalScrollIndicator={false}>
            {farmers.map((f) => {
              const isSelected = f.id === selectedFarmerId;
              return (
                <TouchableOpacity
                  key={f.id}
                  style={[styles.farmerChip, isSelected && styles.farmerChipActive]}
                  onPress={() => setSelectedFarmerId(f.id)}
                >
                  <Text style={[styles.farmerName, isSelected && styles.farmerNameActive]}>
                    {f.name}
                  </Text>
                  <Text style={styles.farmerSub}>{f.code}</Text>
                </TouchableOpacity>
              );
            })}
          </ScrollView>
        </Card>

        {/* Variety Selection */}
        <Card style={styles.card}>
          <Text style={styles.cardHeader}>2. Giống lúa canh tác</Text>
          <View style={styles.varietiesRow}>
            {VARIETIES.map((v) => {
              const isSelected = variety === v;
              return (
                <TouchableOpacity
                  key={v}
                  style={[styles.varietyChip, isSelected && styles.varietyChipActive]}
                  onPress={() => setVariety(v)}
                >
                  <Text style={[styles.varietyText, isSelected && styles.varietyTextActive]}>
                    {v}
                  </Text>
                </TouchableOpacity>
              );
            })}
          </View>
        </Card>

        {/* Season & Area */}
        <Card style={styles.card}>
          <Text style={styles.cardHeader}>3. Mùa vụ & Diện tích</Text>

          <View style={styles.varietiesRow}>
            {SEASONS.map((s) => {
              const isSelected = season === s;
              return (
                <TouchableOpacity
                  key={s}
                  style={[styles.seasonChip, isSelected && styles.seasonChipActive]}
                  onPress={() => setSeason(s)}
                >
                  <Text style={[styles.seasonText, isSelected && styles.seasonTextActive]}>
                    {s}
                  </Text>
                </TouchableOpacity>
              );
            })}
          </View>

          <InputField
            label="Diện tích thửa"
            value={areaHa}
            onChangeText={setAreaHa}
            keyboardType="numeric"
            unitSuffix="ha"
            required
            containerStyle={{ marginTop: 12 }}
          />

          <View style={styles.inputRow}>
            <InputField
              label="Vĩ độ GPS (Lat)"
              value={latitude}
              onChangeText={setLatitude}
              keyboardType="numeric"
              containerStyle={{ flex: 1 }}
            />
            <InputField
              label="Kinh độ GPS (Long)"
              value={longitude}
              onChangeText={setLongitude}
              keyboardType="numeric"
              containerStyle={{ flex: 1 }}
            />
          </View>
        </Card>

        <Button
          title="Lưu Thửa ruộng"
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
  farmerChip: {
    backgroundColor: Colors.cardSubtle,
    borderWidth: 1.5,
    borderColor: Colors.border,
    borderRadius: 10,
    paddingHorizontal: 12,
    paddingVertical: 8,
    marginRight: 8,
  },
  farmerChipActive: {
    backgroundColor: Colors.primarySurface,
    borderColor: Colors.primary,
  },
  farmerName: {
    fontSize: 13,
    fontWeight: '700',
    color: Colors.text,
  },
  farmerNameActive: {
    color: Colors.primary,
  },
  farmerSub: {
    fontSize: 11,
    color: Colors.textSecondary,
    marginTop: 2,
  },
  varietiesRow: {
    flexDirection: 'row',
    flexWrap: 'wrap',
    gap: 8,
  },
  varietyChip: {
    backgroundColor: Colors.cardSubtle,
    borderWidth: 1,
    borderColor: Colors.border,
    paddingHorizontal: 12,
    paddingVertical: 8,
    borderRadius: 8,
  },
  varietyChipActive: {
    backgroundColor: Colors.primarySurface,
    borderColor: Colors.primary,
  },
  varietyText: {
    fontSize: 13,
    fontWeight: '600',
    color: Colors.textSecondary,
  },
  varietyTextActive: {
    color: Colors.primary,
    fontWeight: '700',
  },
  seasonChip: {
    backgroundColor: Colors.cardSubtle,
    borderWidth: 1,
    borderColor: Colors.border,
    paddingHorizontal: 12,
    paddingVertical: 8,
    borderRadius: 8,
  },
  seasonChipActive: {
    backgroundColor: Colors.accentLight,
    borderColor: Colors.accent,
  },
  seasonText: {
    fontSize: 12,
    fontWeight: '600',
    color: Colors.textSecondary,
  },
  seasonTextActive: {
    color: Colors.accentDark,
    fontWeight: '700',
  },
  inputRow: {
    flexDirection: 'row',
    gap: 12,
    marginTop: 4,
  },
});
