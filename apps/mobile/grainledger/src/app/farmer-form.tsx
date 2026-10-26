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

export default function FarmerFormScreen() {
  const router = useRouter();
  const { addFarmer } = useFieldStore();

  const [name, setName] = useState('');
  const [contact, setContact] = useState('');
  const [identityNumber, setIdentityNumber] = useState('');
  const [province, setProvince] = useState('Tiền Giang');
  const [district, setDistrict] = useState('Huyện Cái Bè');
  const [commune, setCommune] = useState('Xã Hậu Mỹ Bắc B');
  const [areaHa, setAreaHa] = useState('3.0');
  const [isSubmitting, setIsSubmitting] = useState(false);

  const handleSubmit = async () => {
    if (!name.trim()) {
      Alert.alert('Thiếu thông tin', 'Vui lòng nhập họ tên nông hộ.');
      return;
    }
    if (!contact.trim()) {
      Alert.alert('Thiếu thông tin', 'Vui lòng nhập số điện thoại liên hệ.');
      return;
    }

    setIsSubmitting(true);
    const newFarmer = await addFarmer({
      name: name.trim(),
      contact: contact.trim(),
      identityNumber: identityNumber.trim() || undefined,
      province,
      district,
      commune,
      totalAreaHa: parseFloat(areaHa) || 0,
    });
    setIsSubmitting(false);

    Alert.alert(
      'Thành công',
      `Đã đăng ký nông hộ ${newFarmer.name} (Mã: ${newFarmer.code}) vào vùng nguyên liệu.`,
      [{ text: 'Đóng', onPress: () => router.back() }]
    );
  };

  return (
    <SafeAreaView style={styles.safeArea}>
      <View style={styles.header}>
        <TouchableOpacity style={styles.backBtn} onPress={() => router.back()}>
          <ArrowLeft size={20} color={Colors.text} />
        </TouchableOpacity>
        <Text style={styles.headerTitle}>Đăng ký Nông hộ Mới</Text>
        <View style={{ width: 36 }} />
      </View>

      <ScrollView
        style={styles.scroll}
        contentContainerStyle={styles.scrollContent}
        showsVerticalScrollIndicator={false}
      >
        <Card style={styles.formCard}>
          <Text style={styles.cardHeader}>Thông tin Nông hộ</Text>

          <InputField
            label="Họ và tên nông hộ"
            placeholder="Ví dụ: Trần Văn Nam"
            value={name}
            onChangeText={setName}
            required
          />

          <View style={styles.inputRow}>
            <InputField
              label="Số điện thoại"
              placeholder="0918..."
              value={contact}
              onChangeText={setContact}
              keyboardType="phone-pad"
              required
              containerStyle={{ flex: 1 }}
            />
            <InputField
              label="Số CCCD / CMND"
              placeholder="082..."
              value={identityNumber}
              onChangeText={setIdentityNumber}
              keyboardType="numeric"
              containerStyle={{ flex: 1 }}
            />
          </View>

          <InputField
            label="Tỉnh / Thành phố"
            value={province}
            onChangeText={setProvince}
            required
          />

          <View style={styles.inputRow}>
            <InputField
              label="Quận / Huyện"
              value={district}
              onChangeText={setDistrict}
              containerStyle={{ flex: 1 }}
            />
            <InputField
              label="Xã / Phường"
              value={commune}
              onChangeText={setCommune}
              containerStyle={{ flex: 1 }}
            />
          </View>

          <InputField
            label="Tổng diện tích canh tác"
            placeholder="3.5"
            value={areaHa}
            onChangeText={setAreaHa}
            keyboardType="numeric"
            unitSuffix="ha"
          />
        </Card>

        <Button
          title="Lưu Nông hộ"
          size="large"
          loading={isSubmitting}
          onPress={handleSubmit}
          style={{ marginTop: 12, marginBottom: 30 }}
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
  formCard: {
    marginBottom: 12,
  },
  cardHeader: {
    fontSize: 14,
    fontWeight: '700',
    color: Colors.text,
    marginBottom: 14,
  },
  inputRow: {
    flexDirection: 'row',
    gap: 12,
  },
});
