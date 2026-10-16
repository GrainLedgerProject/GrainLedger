/**
 * Complete Route Tree Scaffold for all 35 screens
 * Defined in SRS §3.1.1, §3.1.2, §3.1.3 and technical specs
 */
import { AppLayout } from '@/components/AppLayout/AppLayout';
import { PlaceholderPage } from '@/components/PlaceholderPage/PlaceholderPage';
import { AcceptInvitationPage } from '@/features/auth/pages/AcceptInvitationPage';
import { ChangePasswordPage } from '@/features/auth/pages/ChangePasswordPage';
import { EditProfilePage } from '@/features/auth/pages/EditProfilePage';
import { ForgotPasswordPage } from '@/features/auth/pages/ForgotPasswordPage';
import { SignInPage } from '@/features/auth/pages/SignInPage';
import { RoleHomePage } from '@/features/dashboard/pages/RoleHomePage';
import { OfflineSyncQueuePage } from '@/features/field/pages/OfflineSyncQueuePage';
import { LandingPage } from '@/features/landing/pages/LandingPage';
import { OperationEntryPage } from '@/features/mill/pages/OperationEntryPage';
import React from 'react';
import { createBrowserRouter, Navigate } from 'react-router-dom';
import { ProtectedRoute } from './ProtectedRoute';

export const router = createBrowserRouter([
  // Public Unauthenticated Routes
  {
    path: '/',
    element: <LandingPage />,
  },
  {
    path: '/sign-in',
    element: <SignInPage />,
  },
  {
    path: '/invite/:token',
    element: <AcceptInvitationPage />,
  },
  {
    path: '/forgot-password',
    element: <ForgotPasswordPage />,
  },

  // Authenticated App Shell Routes
  {
    element: (
      <ProtectedRoute>
        <AppLayout />
      </ProtectedRoute>
    ),
    children: [
      {
        path: '/home',
        element: <RoleHomePage />,
      },
      {
        path: '/settings/profile',
        element: <EditProfilePage />,
      },
      {
        path: '/settings/security',
        element: <ChangePasswordPage />,
      },

      // --- Internal Audit & Event History (All internal roles) ---
      {
        path: '/events',
        element: (
          <PlaceholderPage
            screenCode="SC-21"
            featureCode="F20"
            titleVi="Tìm kiếm sự kiện nghiệp vụ (Event Search)"
            titleEn="Event Search"
            roles={[
              'MILL_ADMIN',
              'MILL_MANAGER',
              'MILL_OPERATOR',
              'CUSTOMER_PROCUREMENT_OFFICER',
              'CUSTOMER_MANAGER',
              'CUSTOMER_EXPORT_OFFICER',
            ]}
            descriptionVi="Cho phép người dùng nội bộ tra cứu các sự kiện nghiệp vụ bất biến trong phạm vi được cấp quyền (thu mua, chế biến, đối soát, kiểm nghiệm, đóng container)."
            businessRuleCodes={['BR-05', 'BR-17']}
          />
        ),
      },
      {
        path: '/events/:type/:id',
        element: (
          <PlaceholderPage
            screenCode="SC-22"
            featureCode="F20"
            titleVi="Lịch sử sự kiện của đối tượng (Event History)"
            titleEn="Event History"
            roles={[
              'MILL_ADMIN',
              'MILL_MANAGER',
              'MILL_OPERATOR',
              'CUSTOMER_PROCUREMENT_OFFICER',
              'CUSTOMER_MANAGER',
              'CUSTOMER_EXPORT_OFFICER',
            ]}
            descriptionVi="Xem toàn bộ lịch sử biến đổi, các bút toán bù và chuỗi dòng thời gian bất biến gắn với một lô hàng hoặc container cụ thể."
            businessRuleCodes={['BR-03', 'BR-17']}
          />
        ),
      },

      // --- Mill Administration (Mill Administrator) ---
      {
        path: '/admin/customers',
        element: (
          <ProtectedRoute allowedRoles={['MILL_ADMIN']}>
            <PlaceholderPage
              screenCode="SC-03"
              featureCode="F01"
              titleVi="Quản lý Công ty Khách hàng"
              titleEn="Customer Company Management"
              roles={['MILL_ADMIN']}
              descriptionVi="Quản lý danh sách các công ty thu mua và xuất khẩu lúa gạo gửi hàng vào nhà máy, thiết lập ranh giới cách ly dữ liệu Tenant."
              businessRuleCodes={['BR-05']}
            />
          </ProtectedRoute>
        ),
      },
      {
        path: '/admin/access',
        element: (
          <ProtectedRoute allowedRoles={['MILL_ADMIN']}>
            <PlaceholderPage
              screenCode="SC-04"
              featureCode="F01"
              titleVi="Quản lý Tài khoản, Vai trò & Phân quyền Cơ sở"
              titleEn="User, Role and Facility Grant Management"
              roles={['MILL_ADMIN']}
              descriptionVi="Tạo tài khoản người dùng, gắn vai trò nghiệp vụ (6 vai trò nội bộ + 1 người xem kho), gán công ty khách hàng và cấp quyền cơ sở vận hành."
              businessRuleCodes={['BR-05', 'BR-18']}
            />
          </ProtectedRoute>
        ),
      },
      {
        path: '/admin/facilities',
        element: (
          <ProtectedRoute allowedRoles={['MILL_ADMIN']}>
            <PlaceholderPage
              screenCode="SC-05"
              featureCode="F02"
              titleVi="Cấu hình Cơ sở & Thiết bị Nhà máy"
              titleEn="Facility and Equipment Configuration"
              roles={['MILL_ADMIN']}
              descriptionVi="Cấu hình nhà kho, silo lưu trữ, máy sấy lúa, dây chuyền xay xát và dung tích thiết kế với đơn vị đo lường kg chuẩn."
              businessRuleCodes={['BR-10']}
            />
          </ProtectedRoute>
        ),
      },
      {
        path: '/admin/balance-policy',
        element: (
          <ProtectedRoute allowedRoles={['MILL_ADMIN', 'MILL_MANAGER']}>
            <PlaceholderPage
              screenCode="SC-06"
              featureCode="F02"
              titleVi="Cấu hình Chính sách Dung sai Cân bằng (Balance Policy)"
              titleEn="Balance Policy Configuration"
              roles={['MILL_ADMIN', 'MILL_MANAGER']}
              descriptionVi="Cấu hình tỷ lệ phần trăm dung sai tối đa cho từng loại công đoạn (sấy, xay, tách tấm, phối trộn, đóng bao). Quản lý nhà máy có quyền xem."
              businessRuleCodes={['BR-01']}
            />
          </ProtectedRoute>
        ),
      },
      {
        path: '/admin/master-data',
        element: (
          <ProtectedRoute allowedRoles={['MILL_ADMIN']}>
            <PlaceholderPage
              screenCode="SC-07"
              featureCode="F03"
              titleVi="Danh mục Vận hành & Hệ số Chế biến (Master Data)"
              titleEn="Operational Master Data"
              roles={['MILL_ADMIN']}
              descriptionVi="Duy trì danh mục 14 hoạt chất thuốc BVTV, giống lúa Mekong, đơn vị đo lường và bảng hệ số chế biến theo phân đoạn (gạo nguyên, tấm, cám, trấu)."
              businessRuleCodes={['BR-04', 'BR-13']}
            />
          </ProtectedRoute>
        ),
      },
      {
        path: '/compliance/mrl-versions',
        element: (
          <ProtectedRoute allowedRoles={['MILL_ADMIN', 'CUSTOMER_MANAGER', 'CUSTOMER_EXPORT_OFFICER']}>
            <PlaceholderPage
              screenCode="SC-08"
              featureCode="F03"
              titleVi="Bảng giới hạn MRL theo Thị trường có Phiên bản"
              titleEn="MRL Table Versions"
              roles={['MILL_ADMIN', 'CUSTOMER_MANAGER', 'CUSTOMER_EXPORT_OFFICER']}
              descriptionVi="Quản lý ngưỡng dư lượng MRL theo ngày hiệu lực cho 6 thị trường xuất khẩu đích: EU (EC 396/2005), Mỹ, Nhật Bản, Hàn Quốc, Codex, Việt Nam."
              businessRuleCodes={['BR-07', 'BR-13']}
            />
          </ProtectedRoute>
        ),
      },

      // --- Field & Smallholder Procurement (Procurement Officer) ---
      {
        path: '/field/farmers/new',
        element: (
          <ProtectedRoute allowedRoles={['CUSTOMER_PROCUREMENT_OFFICER']}>
            <PlaceholderPage
              screenCode="SC-09"
              featureCode="F04"
              titleVi="Đăng ký Nông hộ (Farmer Registration)"
              titleEn="Farmer Registration"
              roles={['CUSTOMER_PROCUREMENT_OFFICER']}
              descriptionVi="Ghi nhận hồ sơ nông hộ nhỏ tại địa phương, gắn với công ty thu mua, hỗ trợ nhập liệu ngoại tuyến tại ruộng lúa."
              businessRuleCodes={['BR-05', 'BR-09']}
            />
          </ProtectedRoute>
        ),
      },
      {
        path: '/field/plots/new',
        element: (
          <ProtectedRoute allowedRoles={['CUSTOMER_PROCUREMENT_OFFICER']}>
            <PlaceholderPage
              screenCode="SC-10"
              featureCode="F04"
              titleVi="Đăng ký Thửa ruộng & Toạ độ GPS (Plot Registration)"
              titleEn="Plot Registration"
              roles={['CUSTOMER_PROCUREMENT_OFFICER']}
              descriptionVi="Ghi nhận thửa ruộng, diện tích hecta, giống lúa canh tác và toạ độ ranh giới thửa polygon."
              businessRuleCodes={['BR-05', 'BR-09']}
            />
          </ProtectedRoute>
        ),
      },
      {
        path: '/field/declarations/new',
        element: (
          <ProtectedRoute allowedRoles={['CUSTOMER_PROCUREMENT_OFFICER']}>
            <PlaceholderPage
              screenCode="SC-11"
              featureCode="F05"
              titleVi="Khai báo Phun thuốc BVTV theo Thửa (Pesticide Declaration)"
              titleEn="Pesticide Declaration"
              roles={['CUSTOMER_PROCUREMENT_OFFICER']}
              descriptionVi="Thu thập khai báo nông dân về loại thuốc, hoạt chất, ngày phun và liều lượng. Tạo bằng chứng rủi ro ban đầu cho truy vết."
              businessRuleCodes={['BR-05', 'BR-06']}
            />
          </ProtectedRoute>
        ),
      },
      {
        path: '/field/receipts/new',
        element: (
          <ProtectedRoute allowedRoles={['CUSTOMER_PROCUREMENT_OFFICER']}>
            <PlaceholderPage
              screenCode="SC-12"
              featureCode="F06"
              titleVi="Lập Phiếu Thu Mua & Tạo Lô Lúa (Procurement Receipt)"
              titleEn="Procurement Receipt"
              roles={['CUSTOMER_PROCUREMENT_OFFICER']}
              descriptionVi="Cân lúa tươi, đo độ ẩm, sinh lô lúa nguồn kèm số dư ban đầu theo khối lượng thực tế kg."
              businessRuleCodes={['BR-04', 'BR-09']}
            />
          </ProtectedRoute>
        ),
      },
      {
        path: '/field/samples/new',
        element: (
          <ProtectedRoute allowedRoles={['CUSTOMER_PROCUREMENT_OFFICER']}>
            <PlaceholderPage
              screenCode="SC-13"
              featureCode="F06"
              titleVi="Lấy Mẫu Niêm Phong Thu Mua (Procurement Sample)"
              titleEn="Procurement Sample"
              roles={['CUSTOMER_PROCUREMENT_OFFICER']}
              descriptionVi="Ghi nhận mã niêm chì và mã barcode của mẫu thóc đại diện được lấy tại điểm thu mua trước khi đưa về nhà máy."
              businessRuleCodes={['BR-05', 'BR-19']}
            />
          </ProtectedRoute>
        ),
      },
      {
        path: '/field/sync',
        element: (
          <ProtectedRoute allowedRoles={['CUSTOMER_PROCUREMENT_OFFICER']}>
            <OfflineSyncQueuePage />
          </ProtectedRoute>
        ),
      },

      // --- Mill Custody Ledger (Mill Operator & Mill Manager) ---
      {
        path: '/mill/operations/new',
        element: (
          <ProtectedRoute allowedRoles={['MILL_OPERATOR', 'MILL_MANAGER']}>
            <OperationEntryPage />
          </ProtectedRoute>
        ),
      },
      {
        path: '/mill/corrections/new',
        element: (
          <ProtectedRoute allowedRoles={['MILL_OPERATOR']}>
            <PlaceholderPage
              screenCode="SC-16"
              featureCode="F08"
              titleVi="Yêu cầu sửa bước chế biến (Correction Request)"
              titleEn="Correction Request"
              roles={['MILL_OPERATOR']}
              descriptionVi="Tạo yêu cầu sửa đổi khi phát hiện nhầm lẫn trong bước chế biến đã commit. Giữ nguyên lịch sử gốc và khóa tạm thời các lô đầu ra."
              businessRuleCodes={['BR-03', 'BR-12']}
            />
          </ProtectedRoute>
        ),
      },
      {
        path: '/mill/corrections/pending',
        element: (
          <ProtectedRoute allowedRoles={['MILL_OPERATOR', 'MILL_MANAGER']}>
            <PlaceholderPage
              screenCode="SC-17"
              featureCode="F08"
              titleVi="Ghi bút toán bù & Thay thế (Correction Commit)"
              titleEn="Correction Commit"
              roles={['MILL_OPERATOR', 'MILL_MANAGER']}
              descriptionVi="Ghi nhận bút toán bù đảo ngược và tạo bút toán thay thế chuẩn xác trong cùng một giao dịch cơ sở dữ liệu."
              businessRuleCodes={['BR-01', 'BR-03', 'BR-12']}
            />
          </ProtectedRoute>
        ),
      },
      {
        path: '/mill/reconciliations/:id',
        element: (
          <ProtectedRoute allowedRoles={['MILL_MANAGER']}>
            <PlaceholderPage
              screenCode="SC-18"
              featureCode="F08"
              titleVi="Đối soát Bằng chứng & Xác nhận Lệch cân (Reconciliation Review)"
              titleEn="Reconciliation and Variance Review"
              roles={['MILL_MANAGER']}
              descriptionVi="Kiểm tra bằng chứng cân đo, phiếu cân xe hoặc silo, và phê duyệt các sai lệch đo lường nằm trong dung sai."
              businessRuleCodes={['BR-01', 'BR-12']}
            />
          </ProtectedRoute>
        ),
      },
      {
        path: '/mill/plan-executions/:planId',
        element: (
          <ProtectedRoute allowedRoles={['MILL_OPERATOR']}>
            <PlaceholderPage
              screenCode="SC-29"
              featureCode="F16"
              titleVi="Thực thi Phương án Phối trộn đã Duyệt (Approved Plan Execution)"
              titleEn="Approved Plan Execution"
              roles={['MILL_OPERATOR']}
              descriptionVi="Nhân viên vận hành lấy đúng số lượng gạo từ các silo đã được Customer Manager phê duyệt để thực hiện mẻ phối trộn xuất khẩu."
              businessRuleCodes={['BR-02', 'BR-08', 'BR-14']}
            />
          </ProtectedRoute>
        ),
      },

      // --- Stock Positions (Stock Viewer, Mill Manager, Customer Manager) ---
      {
        path: '/stock',
        element: (
          <PlaceholderPage
            screenCode="SC-19"
            featureCode="F10"
            titleVi="Tồn kho theo Vị trí & Silo (Owner Stock by Location)"
            titleEn="Owner Stock by Location"
            roles={['AUTHORIZED_STOCK_VIEWER', 'MILL_MANAGER', 'CUSTOMER_MANAGER']}
            descriptionVi="Xem số lượng tồn kho thực tế, số dư khả dụng, số dư đang bảo lưu và số dư đang phong toả HOLD theo từng silo và ngăn chứa."
            businessRuleCodes={['BR-02', 'BR-05', 'BR-10']}
          />
        ),
      },
      {
        path: '/stock/lots/:lotId',
        element: (
          <PlaceholderPage
            screenCode="SC-20"
            featureCode="F10"
            titleVi="Chi tiết Lô & Tỷ lệ Nguồn gốc (Lot Detail & Mass Composition)"
            titleEn="Lot Detail"
            roles={['AUTHORIZED_STOCK_VIEWER', 'MILL_MANAGER', 'CUSTOMER_MANAGER']}
            descriptionVi="Xem vị trí hiện tại, khối lượng chính xác đến 6 chữ số thập phân, cận dư lượng MRL, và danh sách các thửa ruộng đóng góp kèm tỷ lệ %."
            businessRuleCodes={['BR-04', 'BR-05', 'BR-10']}
          />
        ),
      },

      // --- Compliance & Lab Evidences (Customer Manager) ---
      {
        path: '/compliance/lab-samples/new',
        element: (
          <ProtectedRoute allowedRoles={['CUSTOMER_MANAGER']}>
            <PlaceholderPage
              screenCode="SC-23"
              featureCode="F11"
              titleVi="Đăng ký Mẫu Xét nghiệm Phòng Lab (Lab Sample Registration)"
              titleEn="Laboratory Sample Registration"
              roles={['CUSTOMER_MANAGER']}
              descriptionVi="Đăng ký mẫu gửi viện kiểm nghiệm ISO/IEC 17025 gắn với lô gạo hoặc lô thóc cần kiểm tra dư lượng."
              businessRuleCodes={['BR-13', 'BR-19']}
            />
          </ProtectedRoute>
        ),
      },
      {
        path: '/compliance/lab-results',
        element: (
          <ProtectedRoute allowedRoles={['CUSTOMER_MANAGER']}>
            <PlaceholderPage
              screenCode="SC-24"
              featureCode="F11"
              titleVi="Kết quả Xét nghiệm & Báo cáo Thay thế (Lab Results & Superseding)"
              titleEn="Laboratory Result and Superseding Report"
              roles={['CUSTOMER_MANAGER']}
              descriptionVi="Nhập chỉ số nồng độ mg/kg của từng hoạt chất từ phiếu kết quả lab. Hỗ trợ ghi nhận báo cáo đính chính mà không xóa báo cáo cũ."
              businessRuleCodes={['BR-13', 'BR-19']}
            />
          </ProtectedRoute>
        ),
      },
      {
        path: '/compliance/safety-margin',
        element: (
          <ProtectedRoute allowedRoles={['CUSTOMER_MANAGER']}>
            <PlaceholderPage
              screenCode="SC-25"
              featureCode="F11"
              titleVi="Chính sách Biên An Toàn Dư Lượng (Safety Margin Policy)"
              titleEn="Safety Margin Policy"
              roles={['CUSTOMER_MANAGER']}
              descriptionVi="Cấu hình phần trăm biên đệm an toàn trừ vào mức MRL tối đa của thị trường (ví dụ 20%) để phòng ngừa sai số lấy mẫu."
              businessRuleCodes={['BR-07']}
            />
          </ProtectedRoute>
        ),
      },

      // --- Export Planning & Shipment (Export Officer & Customer Manager) ---
      {
        path: '/export/orders',
        element: (
          <ProtectedRoute allowedRoles={['CUSTOMER_EXPORT_OFFICER']}>
            <PlaceholderPage
              screenCode="SC-26"
              featureCode="F13"
              titleVi="Đơn hàng Xuất khẩu & Mục tiêu Container (Export Order)"
              titleEn="Export Order"
              roles={['CUSTOMER_EXPORT_OFFICER']}
              descriptionVi="Ghi nhận hợp đồng xuất khẩu gạo với thị trường đích, khối lượng yêu cầu, chuẩn chất lượng và số lượng container 20ft mục tiêu."
              businessRuleCodes={['BR-05', 'BR-14']}
            />
          </ProtectedRoute>
        ),
      },
      {
        path: '/export/orders/:orderId/containers/:containerId/plan',
        element: (
          <ProtectedRoute allowedRoles={['CUSTOMER_EXPORT_OFFICER']}>
            <PlaceholderPage
              screenCode="SC-27"
              featureCode="F14"
              titleVi="Yêu cầu Phương án Phối trộn (Blend Plan Request)"
              titleEn="Blend Plan Request"
              roles={['CUSTOMER_EXPORT_OFFICER']}
              descriptionVi="Thuật toán đề xuất công thức phối trộn từ các lô khả dụng của công ty để đạt quy cách % tấm và giới hạn MRL thị trường."
              businessRuleCodes={['BR-02', 'BR-06', 'BR-07', 'BR-14']}
            />
          </ProtectedRoute>
        ),
      },
      {
        path: '/export/plans',
        element: <Navigate to="/export/orders" replace />,
      },
      {
        path: '/export/plans/pending',
        element: (
          <ProtectedRoute allowedRoles={['CUSTOMER_MANAGER', 'CUSTOMER_EXPORT_OFFICER']}>
            <PlaceholderPage
              screenCode="SC-28"
              featureCode="F15"
              titleVi="Duyệt hoặc Từ chối Phương án Phối trộn (Blend Plan Review)"
              titleEn="Blend Plan Review"
              roles={['CUSTOMER_MANAGER', 'CUSTOMER_EXPORT_OFFICER']}
              descriptionVi="Customer Manager kiểm tra đánh giá tuân thủ (PASS) và phê duyệt kế hoạch rút gạo từ silo trước khi cho phép công nhân thực thi."
              businessRuleCodes={['BR-07', 'BR-08', 'BR-14']}
            />
          </ProtectedRoute>
        ),
      },
      {
        path: '/export/plans/:planId/review',
        element: (
          <ProtectedRoute allowedRoles={['CUSTOMER_MANAGER', 'CUSTOMER_EXPORT_OFFICER']}>
            <PlaceholderPage
              screenCode="SC-28"
              featureCode="F15"
              titleVi="Chi tiết Thẩm định Phương án Phối trộn"
              titleEn="Blend Plan Review Detail"
              roles={['CUSTOMER_MANAGER', 'CUSTOMER_EXPORT_OFFICER']}
              descriptionVi="Kiểm tra chi tiết tỷ lệ thành phần các lô, cận dư lượng 95%, và biên an toàn được áp dụng trước khi ký duyệt."
              businessRuleCodes={['BR-07', 'BR-08', 'BR-14']}
            />
          </ProtectedRoute>
        ),
      },
      {
        path: '/export/containers',
        element: (
          <ProtectedRoute allowedRoles={['CUSTOMER_EXPORT_OFFICER']}>
            <PlaceholderPage
              screenCode="SC-30"
              featureCode="F16"
              titleVi="Danh sách & Giám sát Xếp Container"
              titleEn="Containers Overview"
              roles={['CUSTOMER_EXPORT_OFFICER']}
              descriptionVi="Theo dõi tiến độ xếp các container xuất khẩu thuộc các đơn hàng đang triển khai."
              businessRuleCodes={['BR-16']}
            />
          </ProtectedRoute>
        ),
      },
      {
        path: '/export/containers/:id/loading',
        element: (
          <ProtectedRoute allowedRoles={['CUSTOMER_EXPORT_OFFICER']}>
            <PlaceholderPage
              screenCode="SC-30"
              featureCode="F16"
              titleVi="Ghi nhận Xếp hàng lên Container (Container Loading)"
              titleEn="Container Loading"
              roles={['CUSTOMER_EXPORT_OFFICER']}
              descriptionVi="Ghi nhận số lượng bao gạo thực tế đưa vào container theo đúng phương án đã duyệt. Khấu trừ số dư bảo lưu."
              businessRuleCodes={['BR-02', 'BR-08', 'BR-16']}
            />
          </ProtectedRoute>
        ),
      },
      {
        path: '/export/containers/:id/closure',
        element: (
          <ProtectedRoute allowedRoles={['CUSTOMER_EXPORT_OFFICER']}>
            <PlaceholderPage
              screenCode="SC-31"
              featureCode="F16"
              titleVi="Đóng Niêm Container (Container Closure)"
              titleEn="Container Closure"
              roles={['CUSTOMER_EXPORT_OFFICER']}
              descriptionVi="Xác nhận đủ 25.000 kg, kiểm tra không bị HOLD, gắn số seal niêm chì và tự động phát hành bộ hồ sơ tuân thủ bất biến."
              businessRuleCodes={['BR-15', 'BR-16']}
            />
          </ProtectedRoute>
        ),
      },
      {
        path: '/export/dossiers',
        element: (
          <ProtectedRoute allowedRoles={['CUSTOMER_EXPORT_OFFICER']}>
            <PlaceholderPage
              screenCode="SC-32"
              featureCode="F17"
              titleVi="Quản lý Chứng từ & Hồ sơ Tuân thủ (Compliance Dossiers)"
              titleEn="Shipment Documents and Dossier"
              roles={['CUSTOMER_EXPORT_OFFICER']}
              descriptionVi="Xuất chứng thư tuân thủ, bảng kê tỷ lệ nguồn gốc đóng góp và phụ lục kiểm định cho các container đã đóng niêm."
              businessRuleCodes={['BR-13', 'BR-16']}
            />
          </ProtectedRoute>
        ),
      },
      {
        path: '/export/containers/:id/dossier',
        element: (
          <ProtectedRoute allowedRoles={['CUSTOMER_EXPORT_OFFICER']}>
            <PlaceholderPage
              screenCode="SC-32"
              featureCode="F17"
              titleVi="Hồ sơ Tuân thủ Container Chi tiết"
              titleEn="Container Compliance Dossier"
              roles={['CUSTOMER_EXPORT_OFFICER']}
              descriptionVi="Xem và xuất bản file hồ sơ tuân thủ chính thức của container kèm snapshot bằng chứng phòng lab."
              businessRuleCodes={['BR-13', 'BR-16']}
            />
          </ProtectedRoute>
        ),
      },

      // --- Risk, Traceback & HOLD (Customer Manager & Mill Manager) ---
      {
        path: '/risk/traceback',
        element: (
          <ProtectedRoute allowedRoles={['CUSTOMER_MANAGER', 'CUSTOMER_EXPORT_OFFICER']}>
            <PlaceholderPage
              screenCode="SC-33"
              featureCode="F18"
              titleVi="Truy vết Hai chiều Đồ thị (Bidirectional Traceback)"
              titleEn="Traceback"
              roles={['CUSTOMER_MANAGER', 'CUSTOMER_EXPORT_OFFICER']}
              descriptionVi="Truy vết xuôi từ kết quả xét nghiệm xấu đến đúng container bị ảnh hưởng; truy vết ngược từ container về danh sách các thửa ruộng và tỷ lệ đóng góp %."
              businessRuleCodes={['BR-08', 'BR-10', 'BR-17']}
            />
          </ProtectedRoute>
        ),
      },
      {
        path: '/risk/holds/new',
        element: (
          <ProtectedRoute allowedRoles={['CUSTOMER_MANAGER']}>
            <PlaceholderPage
              screenCode="SC-34"
              featureCode="F19"
              titleVi="Ban hành Lệnh Giữ Phong toả (Issue HOLD)"
              titleEn="HOLD Issue"
              roles={['CUSTOMER_MANAGER']}
              descriptionVi="Customer Manager phát lệnh HOLD đối với một lô gạo hoặc một container khi phát hiện dấu hiệu vi phạm an toàn thực phẩm."
              businessRuleCodes={['BR-05', 'BR-15', 'BR-17']}
            />
          </ProtectedRoute>
        ),
      },
      {
        path: '/risk/holds',
        element: (
          <ProtectedRoute allowedRoles={['MILL_MANAGER', 'CUSTOMER_MANAGER', 'CUSTOMER_EXPORT_OFFICER']}>
            <PlaceholderPage
              screenCode="SC-35"
              featureCode="F19"
              titleVi="Cảnh báo & Xử lý Lệnh HOLD (HOLD Alerts and Response)"
              titleEn="HOLD Alerts and Response"
              roles={['MILL_MANAGER', 'CUSTOMER_MANAGER', 'CUSTOMER_EXPORT_OFFICER']}
              descriptionVi="Quản lý nhà máy xác nhận lệnh HOLD, ghi nhận chuyển kho cách ly vật lý. Hệ thống tuyệt đối không có thao tác RELEASE tự do."
              businessRuleCodes={['BR-15', 'BR-17']}
            />
          </ProtectedRoute>
        ),
      },
    ],
  },

  // Fallback 404
  {
    path: '*',
    element: <Navigate to="/home" replace />,
  },
]);
