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
        sans: ['"Be Vietnam Pro"', 'Roboto', 'system-ui', '-apple-system', 'sans-serif'],
        body: ['"Be Vietnam Pro"', 'Roboto', 'system-ui', '-apple-system', 'sans-serif'],
        display: ['"Be Vietnam Pro"', 'Roboto', 'system-ui', '-apple-system', 'sans-serif'],
        serif: ['Lora', '"Times New Roman"', 'serif'],
        mono: ['"JetBrains Mono"', '"Fira Mono"', 'monospace'],
      },
      borderRadius: {
        sm: '4px',
        md: '6px',
        lg: '10px',
      },
      boxShadow: {
        xs: '0 1px 2px rgba(27,36,32,0.06)',
        sm: '0 2px 6px rgba(27,36,32,0.08)',
        md: '0 4px 16px rgba(27,36,32,0.10)',
        lg: '0 8px 32px rgba(27,36,32,0.14)',
      },
      transitionTimingFunction: {
        spring: 'cubic-bezier(0.22, 1, 0.36, 1)',
        bounce: 'cubic-bezier(0.34, 1.56, 0.64, 1)',
      }
    },
  },
  plugins: [],
}
