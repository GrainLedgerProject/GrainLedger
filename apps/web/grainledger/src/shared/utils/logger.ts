/**
 * Safe production logger utility.
 * No-ops in production to satisfy requirement: "No console.log in production code"
 */
const isDev = import.meta.env.DEV;

export const logger = {
  info: (...args: unknown[]) => {
    if (isDev) {
      // eslint-disable-next-line no-console
      console.info('[GrainLedger]', ...args);
    }
  },
  warn: (...args: unknown[]) => {
    if (isDev) {
      // eslint-disable-next-line no-console
      console.warn('[GrainLedger]', ...args);
    }
  },
  error: (...args: unknown[]) => {
    if (isDev) {
      // eslint-disable-next-line no-console
      console.error('[GrainLedger]', ...args);
    }
  },
};
