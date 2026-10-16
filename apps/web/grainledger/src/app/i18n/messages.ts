import i18n from '@/app/i18n';

/**
 * Application Message Codes (from SRS §5.3)
 */
export const MESSAGES_VI = {
  MSG01: 'Bước chế biến bị từ chối. Độ lệch: {variance_kg} kg. Dung sai cho phép: {allowed_kg} kg.',
  MSG02: 'Thao tác bị từ chối. Lô {lot_id} còn {available_kg} kg khả dụng tại vị trí này.',
  MSG03: 'Đồng bộ hoàn tất: {accepted} đã nhận, {duplicate} trùng lặp, {rejected} bị từ chối, {conflict} xung đột.',
  MSG04: 'Kiểm tra lại khối lượng tịnh, đơn vị và giá trị độ ẩm của phiếu thu mua này.',
  MSG05: 'Bản ghi đang chờ. Nông hộ, thửa ruộng, vụ mùa hoặc lô liên kết chưa được xác nhận.',
  MSG06: 'Phương án {plan_id} đã được đề xuất cho container {container_id}. Cần Manager khách hàng duyệt.',
  MSG07: 'Không tìm được phương án với tham số hiện tại. Xem lại ràng buộc đang chặn và các lô liên quan.',
  MSG08: 'Phương án {plan_id} đã lỗi thời. Yêu cầu đánh giá mới trước khi tiếp tục thực thi hoặc xếp hàng.',
  MSG09: 'Đã đặt HOLD cho {target_type} {target_id}. Các thao tác liên quan đã bị chặn.',
  MSG10: 'Không thể xếp container {container_id}: {reason}.',
  MSG11: 'Đánh giá chưa đầy đủ. Kiểm tra bằng chứng hoặc quy tắc còn thiếu: {finding_summary}.',
  MSG12: 'Đăng nhập thất bại. Kiểm tra lại thông tin đăng nhập hoặc liên hệ Quản trị nhà máy.',
} as const;

export const MESSAGES_EN = {
  MSG01: 'Operation rejected. Variance: {variance_kg} kg. Allowed tolerance: {allowed_kg} kg.',
  MSG02: 'Operation rejected. Lot {lot_id} has only {available_kg} kg available at this location.',
  MSG03: 'Sync complete: {accepted} accepted, {duplicate} duplicate, {rejected} rejected, {conflict} conflict.',
  MSG04: 'Please check net weight, unit, and moisture value for this procurement receipt.',
  MSG05: 'Record pending. Farmer, plot, crop season, or linked lot has not been confirmed.',
  MSG06: 'Plan {plan_id} proposed for container {container_id}. Customer Manager review required.',
  MSG07: 'No feasible plan found with current parameters. Review blocking constraints and candidate lots.',
  MSG08: 'Plan {plan_id} is stale. Re-evaluation required before execution or loading.',
  MSG09: 'HOLD issued for {target_type} {target_id}. Associated operations have been blocked.',
  MSG10: 'Cannot load container {container_id}: {reason}.',
  MSG11: 'Evaluation incomplete. Check missing evidence or rules: {finding_summary}.',
  MSG12: 'Sign in failed. Check your credentials or contact Mill Administrator.',
} as const;

export const MESSAGES = MESSAGES_VI;

export type MessageCode = keyof typeof MESSAGES_VI;

export function formatMessage(
  code: MessageCode,
  params: Record<string, string | number>,
  lang?: string
): string {
  const currentLang = lang || i18n.language || 'vi';
  const dict = currentLang === 'en' ? MESSAGES_EN : MESSAGES_VI;
  let template: string = dict[code] || MESSAGES_VI[code] || '';
  for (const [key, value] of Object.entries(params)) {
    template = template.replace(new RegExp(`\\{${key}\\}`, 'g'), String(value));
  }
  return template;
}

