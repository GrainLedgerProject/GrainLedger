import { useUiStore } from "@/app/store/uiStore";
import {
  FileText,
  MagnifyingGlass,
  MagnifyingGlassIcon,
  Package,
  Truck,
  User,
  X,
} from "@phosphor-icons/react";
import { AnimatePresence, motion } from "framer-motion";
import React, { useEffect, useMemo, useState } from "react";
import { useTranslation } from "react-i18next";
import { useNavigate } from "react-router-dom";

interface SearchResultItem {
  id: string;
  type: "LOT" | "CONTAINER" | "FARMER" | "EVENT";
  title: string;
  subtitle: string;
  targetPath: string;
}

const SEARCH_DATABASE: SearchResultItem[] = [
  {
    id: "LOT-2026-S1",
    type: "LOT",
    title: "Lô Thóc Khô S1 (88.000,000 kg)",
    subtitle: "Kho Silo Cụm 1 · Nguồn: L1 (50%), L2 (30%), L3 (20%)",
    targetPath: "/stock/lots/LOT-2026-S1",
  },
  {
    id: "LOT-2026-B1",
    type: "LOT",
    title: "Lô Gạo Trộn B1 (70.000,000 kg)",
    subtitle: "Gạo nguyên 5% tấm · Đạt chuẩn MRL EU",
    targetPath: "/stock/lots/LOT-2026-B1",
  },
  {
    id: "LOT-2026-P01",
    type: "LOT",
    title: "Lô Thóc Tươi L1 (50.000,000 kg)",
    subtitle: "Nông hộ: Nguyễn Văn An · Thửa L1 - Cái Bè, Tiền Giang",
    targetPath: "/stock/lots/LOT-2026-P01",
  },
  {
    id: "CONT-TG-2026-01",
    type: "CONTAINER",
    title: "Container C1 (25.000,000 kg)",
    subtitle: "Mã số: TGHU-202609-01 · Niêm chì: GL-SEAL-8891 · Thị trường: EU",
    targetPath: "/export/containers/CONT-TG-2026-01/dossier",
  },
  {
    id: "FARMER-01",
    type: "FARMER",
    title: "Nông hộ Nguyễn Văn An",
    subtitle: "Thửa L1 (1.8 ha) · Giống lúa OM5451 · Vụ Thu Đông 2026",
    targetPath: "/field/farmers/new",
  },
  {
    id: "FARMER-02",
    type: "FARMER",
    title: "Nông hộ Trần Thị Bích",
    subtitle: "Thửa L2 (2.2 ha) · Giống lúa Đài Thơm 8 · Vụ Thu Đông 2026",
    targetPath: "/field/farmers/new",
  },
  {
    id: "EVT-E00008-01",
    type: "EVENT",
    title: "Sự kiện Xay xát MillingConversion",
    subtitle: "Mã sự kiện: E_00008 · Phân tách gạo nguyên, tấm, cám, trấu",
    targetPath: "/events/conversion/E_00008",
  },
];

export const GlobalSearchModal: React.FC = () => {
  const { t } = useTranslation();
  const { isSearchOpen, setSearchOpen } = useUiStore();
  const [query, setQuery] = useState("");

  const navigate = useNavigate();

  // Listen to Cmd+K / Ctrl+K keyboard shortcut
  useEffect(() => {
    const handleKeyDown = (e: KeyboardEvent) => {
      if ((e.metaKey || e.ctrlKey) && e.key === "k") {
        e.preventDefault();
        setSearchOpen(!isSearchOpen);
      } else if (e.key === "Escape" && isSearchOpen) {
        setSearchOpen(false);
      }
    };
    window.addEventListener("keydown", handleKeyDown);
    return () => window.removeEventListener("keydown", handleKeyDown);
  }, [isSearchOpen, setSearchOpen]);

  const filteredResults = useMemo(() => {
    if (!query.trim()) return SEARCH_DATABASE.slice(0, 5);
    const q = query.toLowerCase();
    return SEARCH_DATABASE.filter(
      (item) =>
        item.title.toLowerCase().includes(q) ||
        item.subtitle.toLowerCase().includes(q) ||
        item.id.toLowerCase().includes(q),
    );
  }, [query]);

  const handleSelect = (item: SearchResultItem) => {
    setSearchOpen(false);
    navigate(item.targetPath);
  };

  const getIcon = (type: SearchResultItem["type"]) => {
    switch (type) {
      case "LOT":
        return <Package size={18} className="text-accent-primary" />;
      case "CONTAINER":
        return <Truck size={18} className="text-accent-info" />;
      case "FARMER":
        return <User size={18} className="text-amber-600" />;
      case "EVENT":
        return <FileText size={18} className="text-ink-secondary" />;
    }
  };

  return (
    <AnimatePresence>
      {isSearchOpen && (
        <div className="fixed inset-0 z-50 flex items-start justify-center pt-20 p-4">
          <motion.div
            initial={{ opacity: 0 }}
            animate={{ opacity: 1 }}
            exit={{ opacity: 0 }}
            className="fixed inset-0 bg-ink-primary/50 backdrop-blur-xs"
            onClick={() => setSearchOpen(false)}
          />

          <motion.div
            initial={{ opacity: 0, scale: 0.96, y: -10 }}
            animate={{ opacity: 1, scale: 1, y: 0 }}
            exit={{ opacity: 0, scale: 0.96, y: -10 }}
            transition={{ duration: 0.2, ease: [0.22, 1, 0.36, 1] }}
            className="relative z-10 w-full max-w-2xl bg-surface-base dark:bg-surface-raised rounded-lg border border-ink-tertiary/20 shadow-2xl overflow-hidden"
          >
            {/* Search Input Bar */}
            <div className="flex items-center px-7 py-3 border-b border-ink-tertiary/20 gap-3">
              <MagnifyingGlassIcon size={22} className="text-ink-tertiary" />
              <input
                type="text"
                autoFocus
                value={query}
                onChange={(e) => {
                  setQuery(e.target.value);
                }}
                placeholder={t(
                  "common.searchPlaceholderFull",
                  "Tìm kiếm mã lô, container, tên nông hộ, sự kiện (Cmd+K)...",
                )}
                className="w-full h-full px-2 py-2.5 bg-transparent text-sm text-ink-primary placeholder:text-ink-tertiary focus:outline-none"
              />
              <button
                type="button"
                onClick={() => setSearchOpen(false)}
                className="text-ink-tertiary hover:text-ink-primary p-1 rounded-sm"
              >
                <X size={18} />
              </button>
            </div>

            {/* Results List */}
            <div className="max-h-80 overflow-y-auto p-2 divide-y divide-ink-tertiary/10">
              {filteredResults.length === 0 ? (
                <div className="p-8 text-center text-xs text-ink-tertiary">
                  {t(
                    "common.noSearchResults",
                    "Không tìm thấy kết quả phù hợp với từ khóa",
                  )}{" "}
                  &ldquo;{query}&rdquo;
                </div>
              ) : (
                filteredResults.map((item, idx) => (
                  <div
                    key={item.id}
                    onClick={() => handleSelect(item)}
                    className={`flex items-center gap-3 px-3 py-2.5 rounded-md cursor-pointer transition-colors hover:bg-surface-raised dark:hover:bg-surface-overlay text-ink-primary hover:bg-accent-primary/10 hover:text-accent-primary hover:font-medium`}
                  >
                    <div className="p-2 rounded bg-surface-raised dark:bg-surface-overlay shrink-0">
                      {getIcon(item.type)}
                    </div>
                    <div className="flex-1 min-w-0">
                      <div className="flex items-center gap-2">
                        <span className="font-semibold text-sm truncate">
                          {item.title}
                        </span>
                        <span className="font-mono text-[10px] px-1.5 py-0.5 rounded bg-surface-overlay text-ink-tertiary">
                          {item.id}
                        </span>
                      </div>
                      <p className="text-xs text-ink-secondary truncate">
                        {item.subtitle}
                      </p>
                    </div>
                  </div>
                ))
              )}
            </div>

            {/* Footer with key hints */}
            <div className="px-4 py-2 bg-surface-raised dark:bg-surface-overlay border-t border-ink-tertiary/10 text-[11px] text-ink-tertiary flex items-center justify-between">
              <div className="flex items-center gap-3">
                <span>{t("common.escToClose", "Nhấn ESC để đóng")}</span>
                <span>
                  {t(
                    "common.scopePermission",
                    "Phạm vi: Trong công ty và cơ sở được cấp quyền",
                  )}
                </span>
              </div>
              <span className="font-mono">GrainLedger Global Search</span>
            </div>
          </motion.div>
        </div>
      )}
    </AnimatePresence>
  );
};
