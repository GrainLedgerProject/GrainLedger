import React from 'react';
import { Link } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import { useUiStore } from '@/app/store/uiStore';
import { Sun, Moon, Globe, ArrowRight } from '@phosphor-icons/react';
import { motion } from 'framer-motion';

export const AppleNavbar: React.FC = () => {
  const { t } = useTranslation();
  const { theme, toggleTheme, language, setLanguage } = useUiStore();

  return (
    <header className="sticky top-0 z-50 transition-all duration-300 backdrop-blur-xl bg-white/75 dark:bg-black/75 border-b border-black/[0.06] dark:border-white/[0.08] shadow-[0_1px_3px_0_rgba(0,0,0,0.02)]">
      <div className="max-w-7xl mx-auto px-4 sm:px-6 lg:px-8 h-16 sm:h-20 flex items-center justify-between">
        {/* Logo & Platform Tag */}
        <Link to="/" className="flex items-center h-full gap-3 group focus:outline-none">
          <div className="flex items-center justify-center h-10 w-auto transition-transform duration-200 group-hover:scale-105 active:scale-95">
            <img
              src="/icons.svg"
              alt="GrainLedger"
              className="h-10 w-auto object-contain"
            />
          </div>
        </Link>

        {/* Desktop Nav Links */}
        <nav className="hidden lg:flex items-center gap-1 p-1 rounded-full bg-black/[0.03] dark:bg-white/[0.05] border border-black/[0.04] dark:border-white/[0.06] text-xs font-medium text-ink-secondary dark:text-ink-tertiary">
          <a
            href="#problem"
            className="px-4 py-2 rounded-full hover:text-ink-primary dark:hover:text-white hover:bg-white dark:hover:bg-white/10 transition-all duration-200"
          >
            Thách thức
          </a>
          <a
            href="#tech"
            className="px-4 py-2 rounded-full hover:text-ink-primary dark:hover:text-white hover:bg-white dark:hover:bg-white/10 transition-all duration-200"
          >
            Hệ thống lõi
          </a>
          <a
            href="#comparison"
            className="px-4 py-2 rounded-full hover:text-ink-primary dark:hover:text-white hover:bg-white dark:hover:bg-white/10 transition-all duration-200"
          >
            Đối chiếu
          </a>
          <a
            href="#simulators"
            className="px-4 py-2 rounded-full hover:text-ink-primary dark:hover:text-white hover:bg-white dark:hover:bg-white/10 transition-all duration-200"
          >
            Mô phỏng
          </a>
          <a
            href="#ecosystem"
            className="px-4 py-2 rounded-full hover:text-ink-primary dark:hover:text-white hover:bg-white dark:hover:bg-white/10 transition-all duration-200"
          >
            Hệ sinh thái
          </a>
        </nav>

        {/* Right Action Controls */}
        <div className="flex items-center gap-2.5">
          {/* Dark Mode Toggle */}
          <button
            type="button"
            onClick={toggleTheme}
            className="w-10 h-10 rounded-full flex items-center justify-center text-ink-secondary dark:text-ink-tertiary hover:text-ink-primary dark:hover:text-white hover:bg-black/[0.04] dark:hover:bg-white/[0.08] active:scale-95 transition-all duration-200 focus:outline-none"
            aria-label="Toggle theme"
          >
            {theme === 'dark' ? <Sun size={18} weight="bold" /> : <Moon size={18} weight="bold" />}
          </button>

          {/* Language Switcher */}
          <button
            type="button"
            onClick={() => setLanguage(language === 'vi' ? 'en' : 'vi')}
            className="hidden sm:flex items-center gap-1.5 px-3 py-1.5 rounded-full text-xs font-semibold bg-black/[0.04] dark:bg-white/[0.06] hover:bg-black/[0.08] dark:hover:bg-white/[0.1] text-ink-primary dark:text-white active:scale-95 transition-all duration-200 focus:outline-none"
          >
            <Globe size={14} weight="bold" />
            <span className="font-mono uppercase">{language}</span>
          </button>

          {/* Apple-grade CTA Button */}
          <motion.div whileHover={{ scale: 1.02 }} whileTap={{ scale: 0.98 }}>
            <Link
              to="/sign-in"
              className="inline-flex items-center gap-2 px-5 py-2.5 rounded-full text-xs font-semibold bg-[#1B2420] dark:bg-white text-white dark:text-black hover:bg-black dark:hover:bg-white/90 shadow-sm hover:shadow-md transition-all duration-200"
            >
              <span>{t('landing.ctaSignIn', 'Đăng nhập')}</span>
              <ArrowRight size={13} weight="bold" />
            </Link>
          </motion.div>
        </div>
      </div>
    </header>
  );
};
