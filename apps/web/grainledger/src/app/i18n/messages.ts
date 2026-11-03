/**
 * Application Message Codes (from SRS §5.3)
 */
export const MESSAGES = {
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

export type MessageCode = keyof typeof MESSAGES;

export function formatMessage(code: MessageCode, params: Record<string, string | number>): string {
  let template: string = MESSAGES[code];
  for (const [key, value] of Object.entries(params)) {
    template = template.replace(new RegExp(`\\{${key}\\}`, 'g'), String(value));
  }
  return template;
}
