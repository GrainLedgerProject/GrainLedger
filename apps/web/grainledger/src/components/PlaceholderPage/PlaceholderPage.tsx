import { RoleChip } from "@/components/RoleChip/RoleChip";
import type { UserRole } from "@/shared/types/roles";
import {
  ArrowLeft,
  BookOpen,
  Clock,
  FileCode,
  ShieldCheck,
} from "@phosphor-icons/react";
import React from "react";
import { useTranslation } from "react-i18next";
import { useNavigate } from "react-router-dom";

export interface PlaceholderPageProps {
  titleVi: string;
  titleEn: string;
  roles: UserRole[];
  descriptionVi: string;
  descriptionEn?: string;
  businessRuleCodes?: string[];
}

export const PlaceholderPage: React.FC<PlaceholderPageProps> = ({
  titleVi,
  titleEn,
  roles,
  descriptionVi,
  descriptionEn,
  businessRuleCodes = ["BR-01", "BR-05"],
}) => {
  const navigate = useNavigate();
  const { t, i18n } = useTranslation();
  const isEn = i18n.language === "en";

  const primaryTitle = isEn ? titleEn : titleVi;
  const secondaryTitle = isEn ? titleVi : titleEn;
  const description = isEn && descriptionEn ? descriptionEn : descriptionVi;

  return (
    <div className="max-w-4xl mx-auto py-8 px-4 space-y-6">
      {/* Back button */}
      <div>
        <button
          type="button"
          onClick={() => navigate(-1)}
          className="inline-flex items-center gap-1.5 text-xs text-ink-secondary hover:text-ink-primary transition-colors font-medium"
        >
          <ArrowLeft size={14} />
          {t("placeholder.back", "Quay lại màn hình trước")}
        </button>
      </div>

      {/* Main specification card */}
      <div className="bg-surface-raised dark:bg-surface-overlay border border-ink-tertiary/20 rounded-lg p-6 sm:p-8 space-y-6 shadow-sm">
        <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-4 border-b border-ink-tertiary/15 pb-6">
          <div className="space-y-1">
            <h1 className="text-2xl font-serif font-bold text-ink-primary">
              {primaryTitle}
            </h1>
            <p className="text-sm text-ink-secondary italic">{secondaryTitle}</p>
          </div>

          <div className="flex flex-col items-start sm:items-end gap-1.5">
            <span className="text-xs font-semibold text-ink-tertiary uppercase tracking-wider">
              {t("placeholder.authorizedRoles", "Vai trò được phép truy cập")}
            </span>
            <div className="flex flex-wrap gap-1.5">
              {roles.map((r) => (
                <RoleChip key={r} role={r} size="sm" showCode />
              ))}
            </div>
          </div>
        </div>

        {/* Detailed Description */}
        <div className="space-y-4 text-sm text-ink-primary leading-relaxed">
          <div className="p-4 rounded-md bg-surface-base border border-ink-tertiary/20">
            <h3 className="font-semibold text-xs uppercase tracking-wider text-ink-tertiary mb-2 flex items-center gap-1.5">
              <BookOpen size={16} className="text-accent-primary" />
              {t("placeholder.techSpec", "Mô tả nghiệp vụ kỹ thuật")}
            </h3>
            <p>{description}</p>
          </div>

          {/* Business Rules and Invariants */}
          <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
            <div className="p-4 rounded-md bg-surface-base border border-ink-tertiary/20 space-y-2">
              <h4 className="font-semibold text-xs uppercase tracking-wider text-ink-tertiary flex items-center gap-1.5">
                <ShieldCheck size={16} className="text-accent-success" />
                {t("placeholder.businessRules", "Ràng buộc nghiệp vụ (Business Rules)")}
              </h4>
              <div className="flex flex-wrap gap-2">
                {businessRuleCodes.map((code) => (
                  <span
                    key={code}
                    className="font-mono text-xs font-bold px-2 py-0.5 rounded bg-emerald-50 dark:bg-emerald-950/40 text-accent-primary border border-accent-primary/30"
                  >
                    {code}
                  </span>
                ))}
              </div>
              <p className="text-xs text-ink-secondary">
                {t(
                  "placeholder.appendOnlyNotice",
                  "Tuân thủ nghiêm ngặt mô hình sổ cái chỉ-ghi-thêm (Append-only) và cách ly Tenant theo công ty khách hàng."
                )}
              </p>
            </div>

            <div className="p-4 rounded-md bg-surface-base border border-ink-tertiary/20 space-y-2">
              <h4 className="font-semibold text-xs uppercase tracking-wider text-ink-tertiary flex items-center gap-1.5">
                <FileCode size={16} className="text-accent-info" />
                {t("placeholder.devStatus", "Trạng thái phát triển")}
              </h4>
              <p className="text-xs text-ink-secondary">
                {t(
                  "placeholder.devStatusDesc",
                  "Màn hình đã được khai báo đầy đủ trong cây định tuyến React Router v6 với route guard kiểm tra quyền hạn và phạm vi cơ sở."
                )}
              </p>
              <div className="flex items-center gap-1.5 text-xs text-amber-600 dark:text-amber-400 font-medium pt-1">
                <Clock size={14} />
                <span>
                  {t(
                    "placeholder.readyNextSprint",
                    "Sẵn sàng kết nối giao diện chi tiết trong Sprint kế tiếp"
                  )}
                </span>
              </div>
            </div>
          </div>
        </div>
      </div>
    </div>
  );
};

