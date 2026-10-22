/**
 * Complete Route Tree Scaffold for all 35 screens
 * Defined in SRS §3.1.1, §3.1.2, §3.1.3 and technical specs
 *
 * Phase 1: Code-split all page components via React.lazy to reduce
 * initial bundle from ~1.96 MB to <200 kB.
 */
/* eslint-disable react-refresh/only-export-components */
import { AppLayout } from "@/components/AppLayout/AppLayout";
import { AppleSuspenseLoader } from "@/components/AppleSuspenseLoader/AppleSuspenseLoader";
import React, { Suspense } from "react";
import { createBrowserRouter, Navigate } from "react-router-dom";
import { ProtectedRoute } from "./ProtectedRoute";

// ---------------------------------------------------------------------------
// Lazy-loaded page components — each becomes its own async chunk.
// PlaceholderPage stays static (tiny, used by many routes).
// ---------------------------------------------------------------------------
const PlaceholderPage = React.lazy(() =>
  import("@/components/PlaceholderPage/PlaceholderPage").then((m) => ({
    default: m.PlaceholderPage,
  }))
);

// Auth pages (public, loaded on-demand)
const SignInPage = React.lazy(() =>
  import("@/features/auth/pages/SignInPage").then((m) => ({ default: m.SignInPage }))
);
const ForgotPasswordPage = React.lazy(() =>
  import("@/features/auth/pages/ForgotPasswordPage").then((m) => ({
    default: m.ForgotPasswordPage,
  }))
);
const AcceptInvitationPage = React.lazy(() =>
  import("@/features/auth/pages/AcceptInvitationPage").then((m) => ({
    default: m.AcceptInvitationPage,
  }))
);
const EditProfilePage = React.lazy(() =>
  import("@/features/auth/pages/EditProfilePage").then((m) => ({
    default: m.EditProfilePage,
  }))
);
const ChangePasswordPage = React.lazy(() =>
  import("@/features/auth/pages/ChangePasswordPage").then((m) => ({
    default: m.ChangePasswordPage,
  }))
);

// Landing
const LandingPage = React.lazy(() =>
  import("@/features/landing/pages/LandingPage").then((m) => ({
    default: m.LandingPage,
  }))
);

// Dashboard
const RoleHomePage = React.lazy(() =>
  import("@/features/dashboard/pages/RoleHomePage").then((m) => ({
    default: m.RoleHomePage,
  }))
);

// Admin pages
const CustomerOrganizationsPage = React.lazy(() =>
  import("@/features/admin/pages/CustomerOrganizationsPage").then((m) => ({
    default: m.CustomerOrganizationsPage,
  }))
);
const UserAccessManagementPage = React.lazy(() =>
  import("@/features/admin/pages/UserAccessManagementPage").then((m) => ({
    default: m.UserAccessManagementPage,
  }))
);
const FacilityConfigPage = React.lazy(() =>
  import("@/features/admin/pages/FacilityConfigPage").then((m) => ({
    default: m.FacilityConfigPage,
  }))
);
const BalancePolicyPage = React.lazy(() =>
  import("@/features/admin/pages/BalancePolicyPage").then((m) => ({
    default: m.BalancePolicyPage,
  }))
);
const ReceivingThresholdsPage = React.lazy(() =>
  import("@/features/admin/pages/ReceivingThresholdsPage").then((m) => ({
    default: m.ReceivingThresholdsPage,
  }))
);

// Catalogs
const MasterDataPage = React.lazy(() =>
  import("@/features/catalogs/pages/MasterDataPage").then((m) => ({
    default: m.MasterDataPage,
  }))
);
const MrlVersionsPage = React.lazy(() =>
  import("@/features/catalogs/pages/MrlVersionsPage").then((m) => ({
    default: m.MrlVersionsPage,
  }))
);

// Field pages
const FarmerRegistrationPage = React.lazy(() =>
  import("@/features/field/pages/FarmerRegistrationPage").then((m) => ({
    default: m.FarmerRegistrationPage,
  }))
);
const PlotRegistrationPage = React.lazy(() =>
  import("@/features/field/pages/PlotRegistrationPage").then((m) => ({
    default: m.PlotRegistrationPage,
  }))
);
const PesticideDeclarationPage = React.lazy(() =>
  import("@/features/field/pages/PesticideDeclarationPage").then((m) => ({
    default: m.PesticideDeclarationPage,
  }))
);
const ProcurementReceiptPage = React.lazy(() =>
  import("@/features/field/pages/ProcurementReceiptPage").then((m) => ({
    default: m.ProcurementReceiptPage,
  }))
);
const FieldSampleRegistrationPage = React.lazy(() =>
  import("@/features/field/pages/FieldSampleRegistrationPage").then((m) => ({
    default: m.FieldSampleRegistrationPage,
  }))
);
const OfflineSyncQueuePage = React.lazy(() =>
  import("@/features/field/pages/OfflineSyncQueuePage").then((m) => ({
    default: m.OfflineSyncQueuePage,
  }))
);

// Mill pages
const ReceivingTasksPage = React.lazy(() =>
  import("@/features/mill/pages/ReceivingTasksPage").then((m) => ({
    default: m.ReceivingTasksPage,
  }))
);
const OperationEntryPage = React.lazy(() =>
  import("@/features/mill/pages/OperationEntryPage").then((m) => ({
    default: m.OperationEntryPage,
  }))
);
const CorrectionRequestPage = React.lazy(() =>
  import("@/features/mill/pages/CorrectionRequestPage").then((m) => ({
    default: m.CorrectionRequestPage,
  }))
);
const PendingCorrectionsPage = React.lazy(() =>
  import("@/features/mill/pages/PendingCorrectionsPage").then((m) => ({
    default: m.PendingCorrectionsPage,
  }))
);
const ReconciliationReviewPage = React.lazy(() =>
  import("@/features/mill/pages/ReconciliationReviewPage").then((m) => ({
    default: m.ReconciliationReviewPage,
  }))
);
const StockOverviewPage = React.lazy(() =>
  import("@/features/mill/pages/StockOverviewPage").then((m) => ({
    default: m.StockOverviewPage,
  }))
);

// Compliance pages
const LabSampleRegistrationPage = React.lazy(() =>
  import("@/features/compliance/pages/LabSampleRegistrationPage").then((m) => ({
    default: m.LabSampleRegistrationPage,
  }))
);
const MrlComplianceCheckPage = React.lazy(() =>
  import("@/features/compliance/pages/MrlComplianceCheckPage").then((m) => ({
    default: m.MrlComplianceCheckPage,
  }))
);
const SafetyMarginPolicyPage = React.lazy(() =>
  import("@/features/compliance/pages/SafetyMarginPolicyPage").then((m) => ({
    default: m.SafetyMarginPolicyPage,
  }))
);

// Audit
const EventHistoryPage = React.lazy(() =>
  import("@/features/audit/pages/EventHistoryPage").then((m) => ({
    default: m.EventHistoryPage,
  }))
);

// Risk
const HoldManagementPage = React.lazy(() =>
  import("@/features/risk/pages/HoldManagementPage").then((m) => ({
    default: m.HoldManagementPage,
  }))
);

// ---------------------------------------------------------------------------
// Suspense wrapper — wraps every lazy route element with shimmer fallback
// ---------------------------------------------------------------------------
function L(props: { children: React.ReactNode }) {
  return <Suspense fallback={<AppleSuspenseLoader />}>{props.children}</Suspense>;
}

// ---------------------------------------------------------------------------
// Router definition
// ---------------------------------------------------------------------------
export const router = createBrowserRouter([
  // Public Unauthenticated Routes
  {
    path: "/",
    element: (
      <L>
        <LandingPage />
      </L>
    ),
  },
  {
    path: "/sign-in",
    element: (
      <L>
        <SignInPage />
      </L>
    ),
  },
  {
    path: "/invite/:token",
    element: (
      <L>
        <AcceptInvitationPage />
      </L>
    ),
  },
  {
    path: "/forgot-password",
    element: (
      <L>
        <ForgotPasswordPage />
      </L>
    ),
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
        element: (
          <L>
            <RoleHomePage />
          </L>
        ),
      },
      {
        path: "/settings/profile",
        element: (
          <L>
            <EditProfilePage />
          </L>
        ),
      },
      {
        path: "/settings/security",
        element: (
          <L>
            <ChangePasswordPage />
          </L>
        ),
      },

      // --- Internal Audit & Event History (All internal roles) ---
      {
        path: "/events",
        element: (
          <L>
            <EventHistoryPage />
          </L>
        ),
      },
      {
        path: "/events/:type/:id",
        element: (
          <L>
            <EventHistoryPage />
          </L>
        ),
      },

      // --- Mill Administration (Mill Administrator) ---
      {
        path: "/admin/customers",
        element: (
          <ProtectedRoute allowedRoles={["MILL_ADMIN"]}>
            <L>
              <CustomerOrganizationsPage />
            </L>
          </ProtectedRoute>
        ),
      },
      {
        path: "/admin/access",
        element: (
          <ProtectedRoute allowedRoles={["MILL_ADMIN"]}>
            <L>
              <UserAccessManagementPage />
            </L>
          </ProtectedRoute>
        ),
      },
      {
        path: "/admin/facilities",
        element: (
          <ProtectedRoute allowedRoles={["MILL_ADMIN"]}>
            <L>
              <FacilityConfigPage />
            </L>
          </ProtectedRoute>
        ),
      },
      {
        path: "/admin/balance-policy",
        element: (
          <ProtectedRoute allowedRoles={["MILL_ADMIN", "MILL_MANAGER"]}>
            <L>
              <BalancePolicyPage />
            </L>
          </ProtectedRoute>
        ),
      },
      {
        path: "/admin/receiving-thresholds",
        element: (
          <ProtectedRoute allowedRoles={["MILL_ADMIN", "MILL_MANAGER"]}>
            <L>
              <ReceivingThresholdsPage />
            </L>
          </ProtectedRoute>
        ),
      },
      {
        path: "/admin/master-data",
        element: (
          <ProtectedRoute allowedRoles={["MILL_ADMIN"]}>
            <L>
              <MasterDataPage />
            </L>
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
            <L>
              <MrlVersionsPage />
            </L>
          </ProtectedRoute>
        ),
      },

      // --- Field & Smallholder Procurement (Procurement Officer) ---
      {
        path: "/field/farmers/new",
        element: (
          <ProtectedRoute
            allowedRoles={["CUSTOMER_PROCUREMENT_OFFICER", "MILL_ADMIN"]}
          >
            <L>
              <FarmerRegistrationPage />
            </L>
          </ProtectedRoute>
        ),
      },
      {
        path: "/field/plots/new",
        element: (
          <ProtectedRoute
            allowedRoles={["CUSTOMER_PROCUREMENT_OFFICER", "MILL_ADMIN"]}
          >
            <L>
              <PlotRegistrationPage />
            </L>
          </ProtectedRoute>
        ),
      },
      {
        path: "/field/declarations/new",
        element: (
          <ProtectedRoute
            allowedRoles={["CUSTOMER_PROCUREMENT_OFFICER", "MILL_ADMIN"]}
          >
            <L>
              <PesticideDeclarationPage />
            </L>
          </ProtectedRoute>
        ),
      },
      {
        path: "/field/receipts/new",
        element: (
          <ProtectedRoute
            allowedRoles={["CUSTOMER_PROCUREMENT_OFFICER", "MILL_ADMIN"]}
          >
            <L>
              <ProcurementReceiptPage />
            </L>
          </ProtectedRoute>
        ),
      },
      {
        path: "/field/samples/new",
        element: (
          <ProtectedRoute
            allowedRoles={["CUSTOMER_PROCUREMENT_OFFICER", "MILL_ADMIN"]}
          >
            <L>
              <FieldSampleRegistrationPage />
            </L>
          </ProtectedRoute>
        ),
      },
      {
        path: "/field/sync",
        element: (
          <ProtectedRoute
            allowedRoles={["CUSTOMER_PROCUREMENT_OFFICER", "MILL_ADMIN"]}
          >
            <L>
              <OfflineSyncQueuePage />
            </L>
          </ProtectedRoute>
        ),
      },

      // --- Mill Custody Ledger (Mill Operator, Mill Manager & Mill Admin) ---
      {
        path: "/mill/receiving",
        element: (
          <ProtectedRoute
            allowedRoles={["MILL_OPERATOR", "MILL_MANAGER", "MILL_ADMIN"]}
          >
            <L>
              <ReceivingTasksPage />
            </L>
          </ProtectedRoute>
        ),
      },
      {
        path: "/mill/operations/new",
        element: (
          <ProtectedRoute
            allowedRoles={["MILL_OPERATOR", "MILL_MANAGER", "MILL_ADMIN"]}
          >
            <L>
              <OperationEntryPage />
            </L>
          </ProtectedRoute>
        ),
      },
      {
        path: "/mill/corrections/new",
        element: (
          <ProtectedRoute allowedRoles={["MILL_OPERATOR", "MILL_ADMIN"]}>
            <L>
              <CorrectionRequestPage />
            </L>
          </ProtectedRoute>
        ),
      },
      {
        path: "/mill/corrections/pending",
        element: (
          <ProtectedRoute
            allowedRoles={["MILL_OPERATOR", "MILL_MANAGER", "MILL_ADMIN"]}
          >
            <L>
              <PendingCorrectionsPage />
            </L>
          </ProtectedRoute>
        ),
      },
      {
        path: "/mill/reconciliations",
        element: (
          <ProtectedRoute allowedRoles={["MILL_MANAGER", "MILL_ADMIN"]}>
            <L>
              <ReconciliationReviewPage />
            </L>
          </ProtectedRoute>
        ),
      },
      {
        path: "/mill/reconciliations/:id",
        element: (
          <ProtectedRoute allowedRoles={["MILL_MANAGER", "MILL_ADMIN"]}>
            <L>
              <ReconciliationReviewPage />
            </L>
          </ProtectedRoute>
        ),
      },
      {
        path: "/mill/plan-executions/:planId",
        element: (
          <ProtectedRoute allowedRoles={["MILL_OPERATOR", "MILL_ADMIN"]}>
            <L>
              <PlaceholderPage
                titleVi="Thực thi Phương án Phối trộn đã Duyệt (Approved Plan Execution)"
                titleEn="Approved Plan Execution"
                roles={["MILL_OPERATOR"]}
                descriptionVi="Nhân viên vận hành lấy đúng số lượng gạo từ các silo đã được Customer Manager phê duyệt để thực hiện mẻ phối trộn xuất khẩu."
                businessRuleCodes={["BR-02", "BR-08", "BR-14"]}
              />
            </L>
          </ProtectedRoute>
        ),
      },

      // --- Stock Positions (Stock Viewer, Mill Manager, Customer Manager, Mill Admin) ---
      {
        path: "/stock",
        element: (
          <L>
            <StockOverviewPage />
          </L>
        ),
      },
      {
        path: "/stock/lots/:lotId",
        element: (
          <L>
            <StockOverviewPage />
          </L>
        ),
      },

      // --- Compliance & Lab Evidences (Customer Manager & Mill Admin) ---
      {
        path: "/compliance/lab-samples",
        element: (
          <ProtectedRoute allowedRoles={["CUSTOMER_MANAGER", "MILL_ADMIN"]}>
            <L>
              <LabSampleRegistrationPage defaultTab="LIST" />
            </L>
          </ProtectedRoute>
        ),
      },
      {
        path: "/compliance/lab-samples/new",
        element: (
          <ProtectedRoute allowedRoles={["CUSTOMER_MANAGER", "MILL_ADMIN"]}>
            <L>
              <LabSampleRegistrationPage defaultTab="CREATE" />
            </L>
          </ProtectedRoute>
        ),
      },
      {
        path: "/compliance/lab-results",
        element: (
          <ProtectedRoute allowedRoles={["CUSTOMER_MANAGER", "MILL_ADMIN"]}>
            <L>
              <MrlComplianceCheckPage />
            </L>
          </ProtectedRoute>
        ),
      },
      {
        path: "/compliance/lab-results/:id",
        element: (
          <ProtectedRoute allowedRoles={["CUSTOMER_MANAGER", "MILL_ADMIN"]}>
            <L>
              <MrlComplianceCheckPage />
            </L>
          </ProtectedRoute>
        ),
      },
      {
        path: "/compliance/safety-margin",
        element: (
          <ProtectedRoute allowedRoles={["CUSTOMER_MANAGER", "MILL_ADMIN"]}>
            <L>
              <SafetyMarginPolicyPage />
            </L>
          </ProtectedRoute>
        ),
      },

      // --- Export Planning & Shipment (Export Officer & Customer Manager) ---
      {
        path: "/export/orders",
        element: (
          <ProtectedRoute
            allowedRoles={["CUSTOMER_EXPORT_OFFICER", "MILL_ADMIN"]}
          >
            <L>
              <PlaceholderPage
                titleVi="Đơn hàng Xuất khẩu & Mục tiêu Container (Export Order)"
                titleEn="Export Order"
                roles={["CUSTOMER_EXPORT_OFFICER"]}
                descriptionVi="Ghi nhận hợp đồng xuất khẩu gạo với thị trường đích, khối lượng yêu cầu, chuẩn chất lượng và số lượng container 20ft mục tiêu."
                businessRuleCodes={["BR-05", "BR-14"]}
              />
            </L>
          </ProtectedRoute>
        ),
      },
      {
        path: "/export/orders/:orderId/containers/:containerId/plan",
        element: (
          <ProtectedRoute
            allowedRoles={["CUSTOMER_EXPORT_OFFICER", "MILL_ADMIN"]}
          >
            <L>
              <PlaceholderPage
                titleVi="Yêu cầu Phương án Phối trộn (Blend Plan Request)"
                titleEn="Blend Plan Request"
                roles={["CUSTOMER_EXPORT_OFFICER"]}
                descriptionVi="Thuật toán đề xuất công thức phối trộn từ các lô khả dụng của công ty để đạt quy cách % tấm và giới hạn MRL thị trường."
                businessRuleCodes={["BR-02", "BR-06", "BR-07", "BR-14"]}
              />
            </L>
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
            allowedRoles={[
              "CUSTOMER_MANAGER",
              "CUSTOMER_EXPORT_OFFICER",
              "MILL_ADMIN",
            ]}
          >
            <L>
              <PlaceholderPage
                titleVi="Duyệt hoặc Từ chối Phương án Phối trộn (Blend Plan Review)"
                titleEn="Blend Plan Review"
                roles={["CUSTOMER_MANAGER", "CUSTOMER_EXPORT_OFFICER"]}
                descriptionVi="Customer Manager kiểm tra đánh giá tuân thủ (PASS) và phê duyệt kế hoạch rút gạo từ silo trước khi cho phép công nhân thực thi."
                businessRuleCodes={["BR-07", "BR-08", "BR-14"]}
              />
            </L>
          </ProtectedRoute>
        ),
      },
      {
        path: "/export/plans/:planId/review",
        element: (
          <ProtectedRoute
            allowedRoles={[
              "CUSTOMER_MANAGER",
              "CUSTOMER_EXPORT_OFFICER",
              "MILL_ADMIN",
            ]}
          >
            <L>
              <PlaceholderPage
                titleVi="Chi tiết Thẩm định Phương án Phối trộn"
                titleEn="Blend Plan Review Detail"
                roles={["CUSTOMER_MANAGER", "CUSTOMER_EXPORT_OFFICER"]}
                descriptionVi="Kiểm tra chi tiết tỷ lệ thành phần các lô, cận dư lượng 95%, và biên an toàn được áp dụng trước khi ký duyệt."
                businessRuleCodes={["BR-07", "BR-08", "BR-14"]}
              />
            </L>
          </ProtectedRoute>
        ),
      },
      {
        path: "/export/containers",
        element: (
          <ProtectedRoute
            allowedRoles={["CUSTOMER_EXPORT_OFFICER", "MILL_ADMIN"]}
          >
            <L>
              <PlaceholderPage
                titleVi="Danh sách & Giám sát Xếp Container"
                titleEn="Containers Overview"
                roles={["CUSTOMER_EXPORT_OFFICER"]}
                descriptionVi="Theo dõi tiến độ xếp các container xuất khẩu thuộc các đơn hàng đang triển khai."
                businessRuleCodes={["BR-16"]}
              />
            </L>
          </ProtectedRoute>
        ),
      },
      {
        path: "/export/containers/:id/loading",
        element: (
          <ProtectedRoute
            allowedRoles={["CUSTOMER_EXPORT_OFFICER", "MILL_ADMIN"]}
          >
            <L>
              <PlaceholderPage
                titleVi="Ghi nhận Xếp hàng lên Container (Container Loading)"
                titleEn="Container Loading"
                roles={["CUSTOMER_EXPORT_OFFICER"]}
                descriptionVi="Ghi nhận số lượng bao gạo thực tế đưa vào container theo đúng phương án đã duyệt. Khấu trừ số dư bảo lưu."
                businessRuleCodes={["BR-02", "BR-08", "BR-16"]}
              />
            </L>
          </ProtectedRoute>
        ),
      },
      {
        path: "/export/containers/:id/closure",
        element: (
          <ProtectedRoute
            allowedRoles={["CUSTOMER_EXPORT_OFFICER", "MILL_ADMIN"]}
          >
            <L>
              <PlaceholderPage
                titleVi="Đóng Niêm Container (Container Closure)"
                titleEn="Container Closure"
                roles={["CUSTOMER_EXPORT_OFFICER"]}
                descriptionVi="Xác nhận đủ 25.000 kg, kiểm tra không bị HOLD, gắn số seal niêm chì và tự động phát hành bộ hồ sơ tuân thủ bất biến."
                businessRuleCodes={["BR-15", "BR-16"]}
              />
            </L>
          </ProtectedRoute>
        ),
      },
      {
        path: "/export/dossiers",
        element: (
          <ProtectedRoute
            allowedRoles={["CUSTOMER_EXPORT_OFFICER", "MILL_ADMIN"]}
          >
            <L>
              <PlaceholderPage
                titleVi="Quản lý Chứng từ & Hồ sơ Tuân thủ (Compliance Dossiers)"
                titleEn="Shipment Documents and Dossier"
                roles={["CUSTOMER_EXPORT_OFFICER"]}
                descriptionVi="Xuất chứng thư tuân thủ, bảng kê tỷ lệ nguồn gốc đóng góp và phụ lục kiểm định cho các container đã đóng niêm."
                businessRuleCodes={["BR-13", "BR-16"]}
              />
            </L>
          </ProtectedRoute>
        ),
      },
      {
        path: "/export/containers/:id/dossier",
        element: (
          <ProtectedRoute
            allowedRoles={["CUSTOMER_EXPORT_OFFICER", "MILL_ADMIN"]}
          >
            <L>
              <PlaceholderPage
                titleVi="Hồ sơ Tuân thủ Container Chi tiết"
                titleEn="Container Compliance Dossier"
                roles={["CUSTOMER_EXPORT_OFFICER"]}
                descriptionVi="Xem và xuất bản file hồ sơ tuân thủ chính thức của container kèm snapshot bằng chứng phòng lab."
                businessRuleCodes={["BR-13", "BR-16"]}
              />
            </L>
          </ProtectedRoute>
        ),
      },

      // --- Risk, Traceback & HOLD (Customer Manager, Mill Manager, Mill Admin) ---
      {
        path: "/risk/traceback",
        element: (
          <ProtectedRoute
            allowedRoles={[
              "CUSTOMER_MANAGER",
              "CUSTOMER_EXPORT_OFFICER",
              "MILL_ADMIN",
            ]}
          >
            <L>
              <PlaceholderPage
                titleVi="Truy vết Hai chiều Đồ thị (Bidirectional Traceback)"
                titleEn="Traceback"
                roles={["CUSTOMER_MANAGER", "CUSTOMER_EXPORT_OFFICER"]}
                descriptionVi="Truy vết xuôi từ kết quả xét nghiệm xấu đến đúng container bị ảnh hưởng; truy vết ngược từ container về danh sách các thửa ruộng và tỷ lệ đóng góp %."
                businessRuleCodes={["BR-08", "BR-10", "BR-17"]}
              />
            </L>
          </ProtectedRoute>
        ),
      },
      {
        path: "/risk/holds/new",
        element: (
          <ProtectedRoute allowedRoles={["CUSTOMER_MANAGER", "MILL_ADMIN"]}>
            <L>
              <HoldManagementPage />
            </L>
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
            <L>
              <HoldManagementPage />
            </L>
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
