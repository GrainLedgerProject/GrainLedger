/**
 * Complete Route Tree Scaffold for all 35 screens
 * Defined in SRS §3.1.1, §3.1.2, §3.1.3 and technical specs
 */
import { AppLayout } from "@/components/AppLayout/AppLayout";
import { PlaceholderPage } from "@/components/PlaceholderPage/PlaceholderPage";
import { BalancePolicyPage } from "@/features/admin/pages/BalancePolicyPage";
import { CustomerOrganizationsPage } from "@/features/admin/pages/CustomerOrganizationsPage";
import { FacilityConfigPage } from "@/features/admin/pages/FacilityConfigPage";
import { UserAccessManagementPage } from "@/features/admin/pages/UserAccessManagementPage";
import { EventHistoryPage } from "@/features/audit/pages/EventHistoryPage";
import { AcceptInvitationPage } from "@/features/auth/pages/AcceptInvitationPage";
import { ChangePasswordPage } from "@/features/auth/pages/ChangePasswordPage";
import { EditProfilePage } from "@/features/auth/pages/EditProfilePage";
import { ForgotPasswordPage } from "@/features/auth/pages/ForgotPasswordPage";
import { SignInPage } from "@/features/auth/pages/SignInPage";
import { MasterDataPage } from "@/features/catalogs/pages/MasterDataPage";
import { MrlVersionsPage } from "@/features/catalogs/pages/MrlVersionsPage";
import { MrlComplianceCheckPage } from "@/features/compliance/pages/MrlComplianceCheckPage";
import { RoleHomePage } from "@/features/dashboard/pages/RoleHomePage";
import { FarmerRegistrationPage } from "@/features/field/pages/FarmerRegistrationPage";
import { OfflineSyncQueuePage } from "@/features/field/pages/OfflineSyncQueuePage";
import { PesticideDeclarationPage } from "@/features/field/pages/PesticideDeclarationPage";
import { PlotRegistrationPage } from "@/features/field/pages/PlotRegistrationPage";
import { ProcurementReceiptPage } from "@/features/field/pages/ProcurementReceiptPage";
import { LandingPage } from "@/features/landing/pages/LandingPage";
import { CorrectionManagementPage } from "@/features/mill/pages/CorrectionManagementPage";
import { OperationEntryPage } from "@/features/mill/pages/OperationEntryPage";
import { ReceivingTasksPage } from "@/features/mill/pages/ReceivingTasksPage";
import { StockOverviewPage } from "@/features/mill/pages/StockOverviewPage";
import { HoldManagementPage } from "@/features/risk/pages/HoldManagementPage";
import React from "react";
import { createBrowserRouter, Navigate } from "react-router-dom";
import { ProtectedRoute } from "./ProtectedRoute";

export const router = createBrowserRouter([
  // Public Unauthenticated Routes
  {
    path: "/",
    element: <LandingPage />,
  },
  {
    path: "/sign-in",
    element: <SignInPage />,
  },
  {
    path: "/invite/:token",
    element: <AcceptInvitationPage />,
  },
  {
    path: "/forgot-password",
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
        path: "/home",
        element: <RoleHomePage />,
      },
      {
        path: "/settings/profile",
        element: <EditProfilePage />,
      },
      {
        path: "/settings/security",
        element: <ChangePasswordPage />,
      },

      // --- Internal Audit & Event History (All internal roles) ---
      {
        path: "/events",
        element: <EventHistoryPage />,
      },
      {
        path: "/events/:type/:id",
        element: <EventHistoryPage />,
      },

      // --- Mill Administration (Mill Administrator) ---
      {
        path: "/admin/customers",
        element: (
          <ProtectedRoute allowedRoles={["MILL_ADMIN"]}>
            <CustomerOrganizationsPage />
          </ProtectedRoute>
        ),
      },
      {
        path: "/admin/access",
        element: (
          <ProtectedRoute allowedRoles={["MILL_ADMIN"]}>
            <UserAccessManagementPage />
          </ProtectedRoute>
        ),
      },
      {
        path: "/admin/facilities",
        element: (
          <ProtectedRoute allowedRoles={["MILL_ADMIN"]}>
            <FacilityConfigPage />
          </ProtectedRoute>
        ),
      },
      {
        path: "/admin/balance-policy",
        element: (
          <ProtectedRoute allowedRoles={["MILL_ADMIN", "MILL_MANAGER"]}>
            <BalancePolicyPage />
          </ProtectedRoute>
        ),
      },
      {
        path: "/admin/master-data",
        element: (
          <ProtectedRoute allowedRoles={["MILL_ADMIN"]}>
            <MasterDataPage />
          </ProtectedRoute>
        ),
      },
      {
        path: "/compliance/mrl-versions",
        element: (
          <ProtectedRoute
            allowedRoles={[
              "MILL_ADMIN",
              "CUSTOMER_MANAGER",
              "CUSTOMER_EXPORT_OFFICER",
            ]}
          >
            <MrlVersionsPage />
          </ProtectedRoute>
        ),
      },

      // --- Field & Smallholder Procurement (Procurement Officer) ---
      {
        path: "/field/farmers/new",
        element: (
          <ProtectedRoute allowedRoles={["CUSTOMER_PROCUREMENT_OFFICER", "MILL_ADMIN"]}>
            <FarmerRegistrationPage />
          </ProtectedRoute>
        ),
      },
      {
        path: "/field/plots/new",
        element: (
          <ProtectedRoute allowedRoles={["CUSTOMER_PROCUREMENT_OFFICER", "MILL_ADMIN"]}>
            <PlotRegistrationPage />
          </ProtectedRoute>
        ),
      },
      {
        path: "/field/declarations/new",
        element: (
          <ProtectedRoute allowedRoles={["CUSTOMER_PROCUREMENT_OFFICER", "MILL_ADMIN"]}>
            <PesticideDeclarationPage />
          </ProtectedRoute>
        ),
      },
      {
        path: "/field/receipts/new",
        element: (
          <ProtectedRoute allowedRoles={["CUSTOMER_PROCUREMENT_OFFICER", "MILL_ADMIN"]}>
            <ProcurementReceiptPage />
          </ProtectedRoute>
        ),
      },
      {
        path: "/field/samples/new",
        element: (
          <ProtectedRoute allowedRoles={["CUSTOMER_PROCUREMENT_OFFICER", "MILL_ADMIN"]}>
            <PlaceholderPage
              screenCode="SC-13"
              featureCode="F06"
              titleVi="Lấy Mẫu Niêm Phong Thu Mua (Procurement Sample)"
              titleEn="Procurement Sample"
              roles={["CUSTOMER_PROCUREMENT_OFFICER"]}
              descriptionVi="Ghi nhận mã niêm chì và mã barcode của mẫu thóc đại diện được lấy tại điểm thu mua trước khi đưa về nhà máy."
              businessRuleCodes={["BR-05", "BR-19"]}
            />
          </ProtectedRoute>
        ),
      },
      {
        path: "/field/sync",
        element: (
          <ProtectedRoute allowedRoles={["CUSTOMER_PROCUREMENT_OFFICER", "MILL_ADMIN"]}>
            <OfflineSyncQueuePage />
          </ProtectedRoute>
        ),
      },

      // --- Mill Custody Ledger (Mill Operator, Mill Manager & Mill Admin) ---
      {
        path: "/mill/receiving",
        element: (
          <ProtectedRoute allowedRoles={["MILL_OPERATOR", "MILL_MANAGER", "MILL_ADMIN"]}>
            <ReceivingTasksPage />
          </ProtectedRoute>
        ),
      },
      {
        path: "/mill/operations/new",
        element: (
          <ProtectedRoute allowedRoles={["MILL_OPERATOR", "MILL_MANAGER", "MILL_ADMIN"]}>
            <OperationEntryPage />
          </ProtectedRoute>
        ),
      },
      {
        path: "/mill/corrections/new",
        element: (
          <ProtectedRoute allowedRoles={["MILL_OPERATOR", "MILL_ADMIN"]}>
            <CorrectionManagementPage />
          </ProtectedRoute>
        ),
      },
      {
        path: "/mill/corrections/pending",
        element: (
          <ProtectedRoute allowedRoles={["MILL_OPERATOR", "MILL_MANAGER", "MILL_ADMIN"]}>
            <CorrectionManagementPage />
          </ProtectedRoute>
        ),
      },
      {
        path: "/mill/reconciliations/:id",
        element: (
          <ProtectedRoute allowedRoles={["MILL_MANAGER", "MILL_ADMIN"]}>
            <CorrectionManagementPage />
          </ProtectedRoute>
        ),
      },
      {
        path: "/mill/plan-executions/:planId",
        element: (
          <ProtectedRoute allowedRoles={["MILL_OPERATOR", "MILL_ADMIN"]}>
            <PlaceholderPage
              screenCode="SC-29"
              featureCode="F16"
              titleVi="Thực thi Phương án Phối trộn đã Duyệt (Approved Plan Execution)"
              titleEn="Approved Plan Execution"
              roles={["MILL_OPERATOR"]}
              descriptionVi="Nhân viên vận hành lấy đúng số lượng gạo từ các silo đã được Customer Manager phê duyệt để thực hiện mẻ phối trộn xuất khẩu."
              businessRuleCodes={["BR-02", "BR-08", "BR-14"]}
            />
          </ProtectedRoute>
        ),
      },

      // --- Stock Positions (Stock Viewer, Mill Manager, Customer Manager, Mill Admin) ---
      {
        path: "/stock",
        element: <StockOverviewPage />,
      },
      {
        path: "/stock/lots/:lotId",
        element: <StockOverviewPage />,
      },

      // --- Compliance & Lab Evidences (Customer Manager & Mill Admin) ---
      {
        path: "/compliance/lab-samples/new",
        element: (
          <ProtectedRoute allowedRoles={["CUSTOMER_MANAGER", "MILL_ADMIN"]}>
            <MrlComplianceCheckPage />
          </ProtectedRoute>
        ),
      },
      {
        path: "/compliance/lab-results",
        element: (
          <ProtectedRoute allowedRoles={["CUSTOMER_MANAGER", "MILL_ADMIN"]}>
            <MrlComplianceCheckPage />
          </ProtectedRoute>
        ),
      },
      {
        path: "/compliance/safety-margin",
        element: (
          <ProtectedRoute allowedRoles={["CUSTOMER_MANAGER", "MILL_ADMIN"]}>
            <MrlComplianceCheckPage />
          </ProtectedRoute>
        ),
      },

      // --- Export Planning & Shipment (Export Officer & Customer Manager) ---
      {
        path: "/export/orders",
        element: (
          <ProtectedRoute allowedRoles={["CUSTOMER_EXPORT_OFFICER", "MILL_ADMIN"]}>
            <PlaceholderPage
              screenCode="SC-26"
              featureCode="F13"
              titleVi="Đơn hàng Xuất khẩu & Mục tiêu Container (Export Order)"
              titleEn="Export Order"
              roles={["CUSTOMER_EXPORT_OFFICER"]}
              descriptionVi="Ghi nhận hợp đồng xuất khẩu gạo với thị trường đích, khối lượng yêu cầu, chuẩn chất lượng và số lượng container 20ft mục tiêu."
              businessRuleCodes={["BR-05", "BR-14"]}
            />
          </ProtectedRoute>
        ),
      },
      {
        path: "/export/orders/:orderId/containers/:containerId/plan",
        element: (
          <ProtectedRoute allowedRoles={["CUSTOMER_EXPORT_OFFICER", "MILL_ADMIN"]}>
            <PlaceholderPage
              screenCode="SC-27"
              featureCode="F14"
              titleVi="Yêu cầu Phương án Phối trộn (Blend Plan Request)"
              titleEn="Blend Plan Request"
              roles={["CUSTOMER_EXPORT_OFFICER"]}
              descriptionVi="Thuật toán đề xuất công thức phối trộn từ các lô khả dụng của công ty để đạt quy cách % tấm và giới hạn MRL thị trường."
              businessRuleCodes={["BR-02", "BR-06", "BR-07", "BR-14"]}
            />
          </ProtectedRoute>
        ),
      },
      {
        path: "/export/plans",
        element: <Navigate to="/export/orders" replace />,
      },
      {
        path: "/export/plans/pending",
        element: (
          <ProtectedRoute
            allowedRoles={["CUSTOMER_MANAGER", "CUSTOMER_EXPORT_OFFICER", "MILL_ADMIN"]}
          >
            <PlaceholderPage
              screenCode="SC-28"
              featureCode="F15"
              titleVi="Duyệt hoặc Từ chối Phương án Phối trộn (Blend Plan Review)"
              titleEn="Blend Plan Review"
              roles={["CUSTOMER_MANAGER", "CUSTOMER_EXPORT_OFFICER"]}
              descriptionVi="Customer Manager kiểm tra đánh giá tuân thủ (PASS) và phê duyệt kế hoạch rút gạo từ silo trước khi cho phép công nhân thực thi."
              businessRuleCodes={["BR-07", "BR-08", "BR-14"]}
            />
          </ProtectedRoute>
        ),
      },
      {
        path: "/export/plans/:planId/review",
        element: (
          <ProtectedRoute
            allowedRoles={["CUSTOMER_MANAGER", "CUSTOMER_EXPORT_OFFICER", "MILL_ADMIN"]}
          >
            <PlaceholderPage
              screenCode="SC-28"
              featureCode="F15"
              titleVi="Chi tiết Thẩm định Phương án Phối trộn"
              titleEn="Blend Plan Review Detail"
              roles={["CUSTOMER_MANAGER", "CUSTOMER_EXPORT_OFFICER"]}
              descriptionVi="Kiểm tra chi tiết tỷ lệ thành phần các lô, cận dư lượng 95%, và biên an toàn được áp dụng trước khi ký duyệt."
              businessRuleCodes={["BR-07", "BR-08", "BR-14"]}
            />
          </ProtectedRoute>
        ),
      },
      {
        path: "/export/containers",
        element: (
          <ProtectedRoute allowedRoles={["CUSTOMER_EXPORT_OFFICER", "MILL_ADMIN"]}>
            <PlaceholderPage
              screenCode="SC-30"
              featureCode="F16"
              titleVi="Danh sách & Giám sát Xếp Container"
              titleEn="Containers Overview"
              roles={["CUSTOMER_EXPORT_OFFICER"]}
              descriptionVi="Theo dõi tiến độ xếp các container xuất khẩu thuộc các đơn hàng đang triển khai."
              businessRuleCodes={["BR-16"]}
            />
          </ProtectedRoute>
        ),
      },
      {
        path: "/export/containers/:id/loading",
        element: (
          <ProtectedRoute allowedRoles={["CUSTOMER_EXPORT_OFFICER", "MILL_ADMIN"]}>
            <PlaceholderPage
              screenCode="SC-30"
              featureCode="F16"
              titleVi="Ghi nhận Xếp hàng lên Container (Container Loading)"
              titleEn="Container Loading"
              roles={["CUSTOMER_EXPORT_OFFICER"]}
              descriptionVi="Ghi nhận số lượng bao gạo thực tế đưa vào container theo đúng phương án đã duyệt. Khấu trừ số dư bảo lưu."
              businessRuleCodes={["BR-02", "BR-08", "BR-16"]}
            />
          </ProtectedRoute>
        ),
      },
      {
        path: "/export/containers/:id/closure",
        element: (
          <ProtectedRoute allowedRoles={["CUSTOMER_EXPORT_OFFICER", "MILL_ADMIN"]}>
            <PlaceholderPage
              screenCode="SC-31"
              featureCode="F16"
              titleVi="Đóng Niêm Container (Container Closure)"
              titleEn="Container Closure"
              roles={["CUSTOMER_EXPORT_OFFICER"]}
              descriptionVi="Xác nhận đủ 25.000 kg, kiểm tra không bị HOLD, gắn số seal niêm chì và tự động phát hành bộ hồ sơ tuân thủ bất biến."
              businessRuleCodes={["BR-15", "BR-16"]}
            />
          </ProtectedRoute>
        ),
      },
      {
        path: "/export/dossiers",
        element: (
          <ProtectedRoute allowedRoles={["CUSTOMER_EXPORT_OFFICER", "MILL_ADMIN"]}>
            <PlaceholderPage
              screenCode="SC-32"
              featureCode="F17"
              titleVi="Quản lý Chứng từ & Hồ sơ Tuân thủ (Compliance Dossiers)"
              titleEn="Shipment Documents and Dossier"
              roles={["CUSTOMER_EXPORT_OFFICER"]}
              descriptionVi="Xuất chứng thư tuân thủ, bảng kê tỷ lệ nguồn gốc đóng góp và phụ lục kiểm định cho các container đã đóng niêm."
              businessRuleCodes={["BR-13", "BR-16"]}
            />
          </ProtectedRoute>
        ),
      },
      {
        path: "/export/containers/:id/dossier",
        element: (
          <ProtectedRoute allowedRoles={["CUSTOMER_EXPORT_OFFICER", "MILL_ADMIN"]}>
            <PlaceholderPage
              screenCode="SC-32"
              featureCode="F17"
              titleVi="Hồ sơ Tuân thủ Container Chi tiết"
              titleEn="Container Compliance Dossier"
              roles={["CUSTOMER_EXPORT_OFFICER"]}
              descriptionVi="Xem và xuất bản file hồ sơ tuân thủ chính thức của container kèm snapshot bằng chứng phòng lab."
              businessRuleCodes={["BR-13", "BR-16"]}
            />
          </ProtectedRoute>
        ),
      },

      // --- Risk, Traceback & HOLD (Customer Manager, Mill Manager, Mill Admin) ---
      {
        path: "/risk/traceback",
        element: (
          <ProtectedRoute
            allowedRoles={["CUSTOMER_MANAGER", "CUSTOMER_EXPORT_OFFICER", "MILL_ADMIN"]}
          >
            <PlaceholderPage
              screenCode="SC-33"
              featureCode="F18"
              titleVi="Truy vết Hai chiều Đồ thị (Bidirectional Traceback)"
              titleEn="Traceback"
              roles={["CUSTOMER_MANAGER", "CUSTOMER_EXPORT_OFFICER"]}
              descriptionVi="Truy vết xuôi từ kết quả xét nghiệm xấu đến đúng container bị ảnh hưởng; truy vết ngược từ container về danh sách các thửa ruộng và tỷ lệ đóng góp %."
              businessRuleCodes={["BR-08", "BR-10", "BR-17"]}
            />
          </ProtectedRoute>
        ),
      },
      {
        path: "/risk/holds/new",
        element: (
          <ProtectedRoute allowedRoles={["CUSTOMER_MANAGER", "MILL_ADMIN"]}>
            <HoldManagementPage />
          </ProtectedRoute>
        ),
      },
      {
        path: "/risk/holds",
        element: (
          <ProtectedRoute
            allowedRoles={[
              "MILL_MANAGER",
              "CUSTOMER_MANAGER",
              "CUSTOMER_EXPORT_OFFICER",
              "MILL_ADMIN",
            ]}
          >
            <HoldManagementPage />
          </ProtectedRoute>
        ),
      },
    ],
  },

  // Fallback 404
  {
    path: "*",
    element: <Navigate to="/home" replace />,
  },
]);
