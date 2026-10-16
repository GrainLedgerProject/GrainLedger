/**
 * GrainLedger 7 Operational Roles and Access Control Definitions
 * Defined in SRS §2.1 and §3.1.3
 */
export type UserRole =
  | 'MILL_ADMIN'                    // MA — platform administrator
  | 'MILL_MANAGER'                  // MM — operations manager, correction authority
  | 'MILL_OPERATOR'                 // MO — records processing conversions
  | 'CUSTOMER_PROCUREMENT_OFFICER'  // PO — field, offline, smallholder intake
  | 'CUSTOMER_MANAGER'              // CM — lab evidence, plan approval, traceback, HOLD
  | 'CUSTOMER_EXPORT_OFFICER'       // EO — orders, loading, shipment
  | 'AUTHORIZED_STOCK_VIEWER';      // SV — read-only stock position

export type RoleSide = 'MILL' | 'CUSTOMER' | 'QUALIFIED_ACCESS';

export interface RoleMeta {
  code: string;
  labelVi: string;
  labelEn: string;
  side: RoleSide;
  descriptionVi: string;
  defaultPath: string;
}

export const ROLE_DEFINITIONS: Record<UserRole, RoleMeta> = {
  MILL_ADMIN: {
    code: 'MA',
    labelVi: 'Quản trị nhà máy',
    labelEn: 'Mill Administrator',
    side: 'MILL',
    descriptionVi: 'Quản lý khách hàng, tài khoản, silo, dây chuyền và bảng MRL',
    defaultPath: '/admin/customers',
  },
  MILL_MANAGER: {
    code: 'MM',
    labelVi: 'Quản lý nhà máy',
    labelEn: 'Mill Manager',
    side: 'MILL',
    descriptionVi: 'Xác minh đối soát, dung sai đo lường, cách ly và xử lý lệnh HOLD',
    defaultPath: '/mill/operations/new',
  },
  MILL_OPERATOR: {
    code: 'MO',
    labelVi: 'Nhân viên vận hành',
    labelEn: 'Mill Operator',
    side: 'MILL',
    descriptionVi: 'Ghi bước chế biến (sấy, xay, trộn, đóng bao, hao hụt)',
    defaultPath: '/mill/operations/new',
  },
  CUSTOMER_PROCUREMENT_OFFICER: {
    code: 'PO',
    labelVi: 'Nhân viên thu mua',
    labelEn: 'Procurement Officer',
    side: 'CUSTOMER',
    descriptionVi: 'Đăng ký nông hộ, thửa ruộng, khai báo thuốc và thu mua lúa',
    defaultPath: '/field/sync',
  },
  CUSTOMER_MANAGER: {
    code: 'CM',
    labelVi: 'Quản lý khách hàng',
    labelEn: 'Customer Manager',
    side: 'CUSTOMER',
    descriptionVi: 'Duyệt phương án phối trộn, đăng ký kết quả lab, phát lệnh HOLD và truy vết',
    defaultPath: '/risk/traceback',
  },
  CUSTOMER_EXPORT_OFFICER: {
    code: 'EO',
    labelVi: 'Nhân viên xuất khẩu',
    labelEn: 'Export Officer',
    side: 'CUSTOMER',
    descriptionVi: 'Tạo đơn xuất khẩu, yêu cầu phương án, giám sát xếp container và đóng niêm',
    defaultPath: '/export/orders',
  },
  AUTHORIZED_STOCK_VIEWER: {
    code: 'SV',
    labelVi: 'Người xem tồn kho',
    labelEn: 'Stock Viewer',
    side: 'QUALIFIED_ACCESS',
    descriptionVi: 'Quyền xem tồn kho định danh theo vị trí và thành phần nguồn gốc',
    defaultPath: '/stock',
  },
};

/** Normalize backend role strings to frontend UserRole */
export function normalizeUserRole(rawRole?: string | null): UserRole {
  if (!rawRole) return 'MILL_OPERATOR';
  const upper = rawRole.toUpperCase();
  if (upper === 'ADMINISTRATOR' || upper === 'MILL_ADMIN') return 'MILL_ADMIN';
  if (upper === 'MILLMANAGER' || upper === 'MILL_MANAGER') return 'MILL_MANAGER';
  if (upper === 'MILLOPERATOR' || upper === 'MILL_OPERATOR') return 'MILL_OPERATOR';
  if (upper === 'PURCHASINGSTAFF' || upper === 'CUSTOMER_PROCUREMENT_OFFICER' || upper === 'PROCUREMENT') return 'CUSTOMER_PROCUREMENT_OFFICER';
  if (upper === 'CUSTOMERMANAGER' || upper === 'CUSTOMER_MANAGER') return 'CUSTOMER_MANAGER';
  if (upper === 'EXPORTSTAFF' || upper === 'CUSTOMER_EXPORT_OFFICER' || upper === 'EXPORT') return 'CUSTOMER_EXPORT_OFFICER';
  if (upper === 'STOCKVIEWER' || upper === 'AUTHORIZED_STOCK_VIEWER') return 'AUTHORIZED_STOCK_VIEWER';
  return 'MILL_OPERATOR';
}

export function isMillSide(role: UserRole): boolean {
  return ROLE_DEFINITIONS[role].side === 'MILL';
}

export function isCustomerSide(role: UserRole): boolean {
  return ROLE_DEFINITIONS[role].side === 'CUSTOMER';
}
