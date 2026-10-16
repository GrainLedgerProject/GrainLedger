import { AppHeader } from "@/components/AppHeader/AppHeader";
import { AppSidebar } from "@/components/AppSidebar/AppSidebar";
import { CompanyContextBar } from "@/components/CompanyContextBar/CompanyContextBar";
import { ErrorBoundary } from "@/components/ErrorBoundary/ErrorBoundary";
import { GlobalSearchModal } from "@/components/GlobalSearchModal/GlobalSearchModal";
import React from "react";
import { Outlet } from "react-router-dom";

export const AppLayout: React.FC = () => {
  return (
    <div className="min-h-screen bg-parchment-grain flex flex-row selection:bg-accent-primary/20 selection:text-accent-primary">
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

        {/* Minimal Operational Footer */}
        <footer className="border-t border-ink-tertiary/15 py-3 px-6 text-xs text-ink-tertiary flex flex-col sm:flex-row items-center justify-between gap-2 bg-surface-base/80">
          <footer className="h-16 flex items-center px-4">
            <div className="flex items-center gap-2 h-full">
              <img
                src="/favicon.svg"
                alt="GrainLedger"
                className="h-full w-auto shrink-0 object-contain"
              />
              <span>
                GrainLedger © 2026 GFA26SE137 (FA26SE272) — Tiền Giang, Vietnam
              </span>
            </div>
          </footer>
          <div className="flex items-center gap-4 text-[11px]">
            <span>Tuân thủ mô hình Mass Balance ISO 22095:2020</span>
            <span>Phiên bản v1.0.0-prod</span>
          </div>
        </footer>
      </div>

      {/* Global Cmd+K Search Modal */}
      <GlobalSearchModal />
    </div>
  );
};
