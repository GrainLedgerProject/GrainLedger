/** @type {import('tailwindcss').Config} */
export default {
  content: [
    "./index.html",
    "./src/**/*.{js,ts,jsx,tsx}",
  ],
  darkMode: 'class',
  theme: {
    extend: {
      colors: {
        surface: {
          base: 'var(--color-surface-base)',
          raised: 'var(--color-surface-raised)',
          overlay: 'var(--color-surface-overlay)',
        },
        ink: {
          primary: 'var(--color-ink-primary)',
          secondary: 'var(--color-ink-secondary)',
          tertiary: 'var(--color-ink-tertiary)',
          inverse: 'var(--color-ink-inverse)',
        },
        accent: {
          primary: 'var(--color-accent-primary)',
          'primary-dim': 'var(--color-accent-primary-dim)',
          warning: 'var(--color-accent-warning)',
          danger: 'var(--color-accent-danger)',
          info: 'var(--color-accent-info)',
          success: 'var(--color-accent-success)',
        },
        company: {
          a: 'var(--color-company-a)',
          b: 'var(--color-company-b)',
          c: 'var(--color-company-c)',
          d: 'var(--color-company-d)',
        },
        functional: {
          'hold-bg': 'var(--color-hold-bg)',
          'hold-border': 'var(--color-hold-border)',
          'stale-bg': 'var(--color-stale-bg)',
          'stale-border': 'var(--color-stale-border)',
          'balanced-bg': 'var(--color-balanced-bg)',
          'balanced-border': 'var(--color-balanced-border)',
          'rejected-bg': 'var(--color-rejected-bg)',
          'rejected-border': 'var(--color-rejected-border)',
        }
      },
      fontFamily: {
        sans: ['-apple-system', 'BlinkMacSystemFont', '"SF Pro Display"', '"Inter"', '"Segoe UI"', 'system-ui', 'sans-serif'],
        body: ['-apple-system', 'BlinkMacSystemFont', '"SF Pro Text"', '"Inter"', '"Segoe UI"', 'system-ui', 'sans-serif'],
        display: ['-apple-system', 'BlinkMacSystemFont', '"SF Pro Display"', '"Inter"', '"Segoe UI"', 'system-ui', 'sans-serif'],
        serif: ['"Georgia"', '"Times New Roman"', 'serif'],
        mono: ['"SF Mono"', '"JetBrains Mono"', '"Fira Mono"', 'ui-monospace', 'monospace'],
      },
      borderRadius: {
        sm: '8px',
        md: '12px',
        lg: '16px',
        xl: '24px',
        '2xl': '32px',
      },
      boxShadow: {
        xs: 'var(--shadow-xs)',
        sm: 'var(--shadow-sm)',
        md: 'var(--shadow-md)',
        lg: 'var(--shadow-lg)',
        xl: 'var(--shadow-xl)',
        glow: 'var(--shadow-glow)',
      },
      transitionTimingFunction: {
        spring: 'cubic-bezier(0.22, 1, 0.36, 1)',
        bounce: 'cubic-bezier(0.34, 1.56, 0.64, 1)',
        apple: 'cubic-bezier(0.25, 0.1, 0.25, 1)',
      },
      backdropBlur: {
        glass: '20px',
      },
      backdropSaturate: {
        glass: '1.8',
      },
    },
  },
  plugins: [],
}
