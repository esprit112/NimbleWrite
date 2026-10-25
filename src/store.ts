// App-wide state: settings (persisted on the server) and toasts.
import { create } from 'zustand';
import type { Settings } from '../shared/types';
import { api } from './api';

interface SettingsState {
  settings: Settings | null;
  error: string | null;
  load: () => Promise<void>;
  update: (patch: Partial<Settings>) => Promise<void>;
}

export const useSettings = create<SettingsState>((set, get) => ({
  settings: null,
  error: null,
  load: async () => {
    try {
      set({ settings: await api.getSettings(), error: null });
    } catch (e) {
      set({ error: (e as Error).message });
    }
  },
  update: async (patch) => {
    const prev = get().settings;
    if (!prev) return;
    const next = { ...prev, ...patch };
    set({ settings: next });
    try {
      set({ settings: await api.saveSettings(next) });
    } catch (e) {
      set({ settings: prev });
      toast((e as Error).message, 'error');
    }
  },
}));

export interface Toast {
  id: number;
  message: string;
  kind: 'info' | 'success' | 'error';
}

interface ToastState {
  toasts: Toast[];
  push: (message: string, kind?: Toast['kind']) => void;
  dismiss: (id: number) => void;
}

let nextToast = 1;
export const useToasts = create<ToastState>((set) => ({
  toasts: [],
  push: (message, kind = 'info') => {
    const id = nextToast++;
    set((s) => ({ toasts: [...s.toasts.slice(-3), { id, message, kind }] }));
    setTimeout(() => set((s) => ({ toasts: s.toasts.filter((t) => t.id !== id) })), kind === 'error' ? 6500 : 3200);
  },
  dismiss: (id) => set((s) => ({ toasts: s.toasts.filter((t) => t.id !== id) })),
}));

export const toast = (message: string, kind: Toast['kind'] = 'info') => useToasts.getState().push(message, kind);
