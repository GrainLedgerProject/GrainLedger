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
import { useMillStore } from '../store/millStore';
import { OperationType } from '../types/mill';
import { checkMassBalance, formatMass } from '../utils/decimalMass';
import { ArrowLeft, CheckCircle2, AlertOctagon } from 'lucide-react-native';

const OPERATION_TYPES: { type: OperationType; label: string; defaultOut: string }[] = [
  { type: 'DRYING', label: '1. Sấy lúa khô (Hạ ẩm)', defaultOut: 'Lúa khô ST25 (14% ẩm)' },
  { type: 'MILLING', label: '2. Xay xát & Đánh bóng', defaultOut: 'Gạo lứt / Gạo trắng ST25' },
  { type: 'BLENDING', label: '3. Phối trộn (Blending)', defaultOut: 'Gạo phối trộn ST25 đặc sản' },
  { type: 'BAGGING', label: '4. Đóng bao thành phẩm', defaultOut: 'Gạo ST25 đóng bao 5kg' },
];

export default function OperationFormScreen() {
  const router = useRouter();
  const { addOperation } = useMillStore();

  const [opType, setOpType] = useState<OperationType>('DRYING');
  const [facility, setFacility] = useState('Nhà máy Tiền Giang - Lò sấy vỉ ngang 01');
  const [inputLotCode, setInputLotCode] = useState('LOT-RAW-ST25-02');
  const [inputMass, setInputMass] = useState('10000');
  const [outputLotCode, setOutputLotCode] = useState('LOT-DRY-ST25-02');
  const [outputProduct, setOutputProduct] = useState(OPERATION_TYPES[0].defaultOut);
  const [outputMass, setOutputMass] = useState('8850');
  const [declaredLoss, setDeclaredLoss] = useState('1150');
  const [lossReason, setLossReason] = useState('Bay hơi ẩm tự nhiên từ 24.5% xuống 14.0%');
  const [isSubmitting, setIsSubmitting] = useState(false);

  // Live Mass Balance Calculation (BR-01)
  const balance = checkMassBalance(inputMass, outputMass, declaredLoss, 0.5);

  const handleSubmit = async () => {
    setIsSubmitting(true);
    const result = await addOperation({
      facilityId: 'fac-01',
      facilityName: facility,
      operationType: opType,
      inputLotId: 'lot-' + Date.now(),
      inputLotCode,
      inputMassKg: inputMass,
      outputLotCode,
      outputProductType: outputProduct,
      outputMassKg: outputMass,
      declaredLossKg: declaredLoss,
      lossReason,
    });
    setIsSubmitting(false);

    if (result.success && result.operation) {
      Alert.alert(
        'Ghi nhận thành công',
        `Mẻ ${result.operation.code} (${opType}) đã được ghi nhận vào sổ cái vận hành.`,
        [{ text: 'Đóng', onPress: () => router.back() }]
      );
    } else {
      Alert.alert('Không thể lưu mẻ', result.error || 'Kiểm tra lại số liệu.');
    }
  };

  return (
    <SafeAreaView style={styles.safeArea}>
      <View style={styles.header}>
        <TouchableOpacity style={styles.backBtn} onPress={() => router.back()}>
          <ArrowLeft size={20} color={Colors.text} />
        </TouchableOpacity>
        <Text style={styles.headerTitle}>Ghi Nhận Mẻ Chế Biến</Text>
        <View style={{ width: 36 }} />
      </View>

      <ScrollView
        style={styles.scroll}
        contentContainerStyle={styles.scrollContent}
        showsVerticalScrollIndicator={false}
      >
        {/* Choose Operation Step */}
        <Card style={styles.card}>
          <Text style={styles.cardHeader}>1. Chọn Công đoạn chế biến</Text>
          <View style={styles.opTypesCol}>
            {OPERATION_TYPES.map((item) => {
              const isSelected = opType === item.type;
              return (
                <TouchableOpacity
                  key={item.type}
                  style={[styles.opTypeOption, isSelected && styles.opTypeOptionActive]}
                  onPress={() => {
                    setOpType(item.type);
                    setOutputProduct(item.defaultOut);
                  }}
                >
                  <Text style={[styles.opTypeLabel, isSelected && styles.opTypeLabelActive]}>
                    {item.label}
                  </Text>
                </TouchableOpacity>
              );
            })}
          </View>
        </Card>

        {/* Facility */}
        <Card style={styles.card}>
          <Text style={styles.cardHeader}>2. Dây chuyền & Cơ sở thực hiện</Text>
          <InputField
            label="Tên dây chuyền / Thiết bị"
            value={facility}
            onChangeText={setFacility}
            required
          />
        </Card>

        {/* Input Lot */}
        <Card style={styles.card}>
          <Text style={styles.cardHeader}>3. Lô nguyên liệu Đầu vào (Input)</Text>
          <View style={styles.inputRow}>
            <InputField
              label="Mã lô đầu vào"
              value={inputLotCode}
              onChangeText={setInputLotCode}
              required
              containerStyle={{ flex: 1 }}
            />
            <InputField
              label="Khối lượng nạp"
              value={inputMass}
              onChangeText={setInputMass}
              keyboardType="numeric"
              unitSuffix="kg"
              required
              containerStyle={{ flex: 1 }}
            />
          </View>
        </Card>

        {/* Output Lot & Loss */}
        <Card style={styles.card}>
          <Text style={styles.cardHeader}>4. Lô thành phẩm Đầu ra & Hao hụt</Text>
          <InputField
            label="Mô tả sản phẩm đầu ra"
            value={outputProduct}
            onChangeText={setOutputProduct}
            required
          />

          <View style={styles.inputRow}>
            <InputField
              label="Mã lô đầu ra"
              value={outputLotCode}
              onChangeText={setOutputLotCode}
              required
              containerStyle={{ flex: 1 }}
            />
            <InputField
              label="Khối lượng ra"
              value={outputMass}
              onChangeText={setOutputMass}
              keyboardType="numeric"
              unitSuffix="kg"
              required
              containerStyle={{ flex: 1 }}
            />
          </View>

          <View style={styles.inputRow}>
            <InputField
              label="Hao hụt khai báo"
              value={declaredLoss}
              onChangeText={setDeclaredLoss}
              keyboardType="numeric"
              unitSuffix="kg"
              required
              containerStyle={{ flex: 1 }}
            />
          </View>

          <InputField
            label="Lý do hao hụt"
            value={lossReason}
            onChangeText={setLossReason}
          />

          {/* Mass Balance Verification Banner (BR-01) */}
          <View
            style={[
              styles.balanceBox,
              balance.isBalanced ? styles.balanceSuccess : styles.balanceError,
            ]}
          >
            <View style={styles.balanceTop}>
              {balance.isBalanced ? (
                <CheckCircle2 size={18} color="#059669" />
              ) : (
                <AlertOctagon size={18} color="#DC2626" />
              )}
              <Text
                style={[
                  styles.balanceTitle,
                  { color: balance.isBalanced ? '#065F46' : '#991B1B' },
                ]}
              >
                {balance.isBalanced
                  ? 'Thỏa quy tắc Bảo toàn Khối lượng (BR-01)'
                  : 'Cảnh báo: Vi phạm Bảo toàn Khối lượng (BR-01)'}
              </Text>
            </View>

            <Text
              style={[
                styles.balanceDesc,
                { color: balance.isBalanced ? '#047857' : '#B91C1C' },
              ]}
            >
              Tổng Vào ({formatMass(inputMass, 0)} kg) vs [Ra ({formatMass(outputMass, 0)} kg) + Hao hụt (
              {formatMass(declaredLoss, 0)} kg)] • Chênh lệch:{' '}
              {formatMass(balance.varianceKg.toString(), 2)} kg (Cho phép:{' '}
              {formatMass(balance.allowedKg.toString(), 2)} kg)
            </Text>
          </View>
        </Card>

        <Button
          title="Xác nhận & Lưu Mẻ Chế Biến"
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
  opTypesCol: {
    gap: 8,
  },
  opTypeOption: {
    padding: 12,
    borderRadius: 10,
    borderWidth: 1.5,
    borderColor: Colors.border,
    backgroundColor: '#FFFFFF',
  },
  opTypeOptionActive: {
    backgroundColor: Colors.primarySurface,
    borderColor: Colors.primary,
  },
  opTypeLabel: {
    fontSize: 14,
    fontWeight: '700',
    color: Colors.text,
  },
  opTypeLabelActive: {
    color: Colors.primary,
  },
  inputRow: {
    flexDirection: 'row',
    gap: 12,
  },
  balanceBox: {
    padding: 12,
    borderRadius: 10,
    borderWidth: 1.5,
    marginTop: 8,
  },
  balanceSuccess: {
    backgroundColor: '#ECFDF5',
    borderColor: '#A7F3D0',
  },
  balanceError: {
    backgroundColor: '#FEF2F2',
    borderColor: '#FCA5A5',
  },
  balanceTop: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: 8,
  },
  balanceTitle: {
    fontSize: 13,
    fontWeight: '700',
  },
  balanceDesc: {
    fontSize: 11,
    marginTop: 4,
    lineHeight: 16,
  },
});
