import { AppHeader } from "@/components/AppHeader/AppHeader";
import { AppSidebar } from "@/components/AppSidebar/AppSidebar";
import { CompanyContextBar } from "@/components/CompanyContextBar/CompanyContextBar";
import { DynamicIslandNotification } from "@/components/DynamicIslandNotification/DynamicIslandNotification";
import { ErrorBoundary } from "@/components/ErrorBoundary/ErrorBoundary";
import { GlobalSearchModal } from "@/components/GlobalSearchModal/GlobalSearchModal";
import React from "react";
import { useTranslation } from "react-i18next";
import { Outlet } from "react-router-dom";

export const AppLayout: React.FC = () => {
  const { t } = useTranslation();

  return (
    <div className="min-h-screen bg-surface-base flex flex-row selection:bg-accent-primary/20 selection:text-accent-primary">
      {/* Dynamic Island Floating Notification */}
      {/* <DynamicIslandNotification /> */}

      {/* App Sidebar */}
      <AppSidebar />

      {/* Main Workspace Area */}
      <div className="flex-1 flex flex-col min-w-0 min-h-screen">
        <AppHeader />
        <CompanyContextBar />

        {/* Content Outlet with Error Boundary */}
        <main className="flex-1 p-4 sm:p-6 lg:p-8 max-w-7xl w-full mx-auto">
          <ErrorBoundary>
            <Outlet />
          </ErrorBoundary>
        </main>

        {/* Minimal Operational Footer — Apple-style clean */}
        <footer className="border-t border-ink-tertiary/10 py-4 px-6 text-xs text-ink-tertiary flex flex-col sm:flex-row items-center justify-between gap-2 bg-surface-base">
          <div className="flex items-center gap-3">
            <img
              src="/favicon.svg"
              alt="GrainLedger"
              className="h-5 w-5 shrink-0 opacity-60"
            />
            <span className="text-[11px]">
              GrainLedger © 2026 — Tiền Giang, Vietnam
            </span>
          </div>
          <div className="flex items-center gap-4 text-[11px] text-ink-tertiary/70">
            <span>
              {t("common.isoComplianceFooter", "ISO 22095:2020 Mass Balance")}
            </span>
            <span>{t("common.versionFooter", "v1.0.0-dev")}</span>
          </div>
        </footer>
      </div>

      {/* Global Cmd+K Search Modal */}
      <GlobalSearchModal />
    </div>
  );
};
