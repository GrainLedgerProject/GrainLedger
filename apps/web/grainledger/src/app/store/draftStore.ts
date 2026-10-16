import { create } from 'zustand';

interface DraftState {
  drafts: Record<string, unknown>;
  saveDraft: (formKey: string, data: unknown) => void;
  getDraft: <T>(formKey: string) => T | null;
  clearDraft: (formKey: string) => void;
  hasDraft: (formKey: string) => boolean;
}

const STORAGE_PREFIX = 'grainledger_draft_';

export const useDraftStore = create<DraftState>(() => ({
  drafts: {},

  saveDraft: (formKey, data) => {
    try {
      localStorage.setItem(`${STORAGE_PREFIX}${formKey}`, JSON.stringify(data));
    } catch {
      // ignore storage quota issues
    }
  },

  getDraft: <T>(formKey: string): T | null => {
    try {
      const item = localStorage.getItem(`${STORAGE_PREFIX}${formKey}`);
      return item ? (JSON.parse(item) as T) : null;
    } catch {
      return null;
    }
  },

  clearDraft: (formKey: string) => {
    localStorage.removeItem(`${STORAGE_PREFIX}${formKey}`);
  },

  hasDraft: (formKey: string) => {
    return !!localStorage.getItem(`${STORAGE_PREFIX}${formKey}`);
  },
}));
