import { createContext, useCallback, useContext, useEffect, useMemo, useRef, useSyncExternalStore, type ReactNode } from "react";
import { toast } from "sonner";

import { useL } from "@/lib/i18n/LocalizationProvider";

/* Per-user settings of the Planboard (design 70, "Persistence"): the dockview layout under `planboard.layout` and each
 * pane instance's settings (mode, filters, columns, zoom, bar fields) under `planboard.pane.<instanceId>`, written
 * debounced (1 s). The design puts both in Platform's GridUserSettings; the store sits behind this interface so the
 * workspace does not care where the values live.
 *
 * NOTE (contract gap, reported): the GridSettingsDto of the current API holds only column states and sort models, so
 * it cannot carry a layout. Until the contract gives the grid settings a free-form value, the default store keeps the
 * values in memory for the browser tab (they survive leaving and re-entering the app, not a reload). */

export const PLANBOARD_LAYOUT_KEY = "planboard.layout";
export const paneSettingsKey = (instanceId: string) => `planboard.pane.${instanceId}`;
export const SETTINGS_SAVE_DEBOUNCE_MS = 1000;

export interface PlanboardSettingsStore {
  /** The stored value, or null when there is none. Rejects when the store cannot be read. */
  load(key: string): Promise<unknown>;
  /** Resolves true when the value was stored. */
  save(key: string, value: unknown): Promise<boolean>;
  /** Resolves true when the value is gone. */
  remove(key: string): Promise<boolean>;
}

/** A store in memory (JSON round trip, so callers never share object references with it). */
export function createMemorySettingsStore(): PlanboardSettingsStore {
  const values = new Map<string, string>();
  return {
    load: async (key) => {
      const raw = values.get(key);
      return raw == null ? null : (JSON.parse(raw) as unknown);
    },
    save: async (key, value) => {
      values.set(key, JSON.stringify(value));
      return true;
    },
    remove: async (key) => {
      values.delete(key);
      return true;
    },
  };
}

/** The tab-wide default store (module state: one per browser tab, gone on reload). */
const tabStore = createMemorySettingsStore();

export interface DebouncedSaver {
  /** Schedules a save of the value; a newer value for the same key within the delay replaces it. */
  schedule(key: string, value: unknown): void;
  /** Saves every pending value now. */
  flush(): Promise<void>;
  /** Drops a pending save of the key. */
  cancel(key: string): void;
  /** Drops every pending save. */
  dispose(): void;
}

/** Debounces saves per key; `onFailed` is called once per failed save (UI-FB-005: never silent). */
export function createDebouncedSaver(
  store: PlanboardSettingsStore,
  onFailed: (key: string, error: unknown) => void,
  delayMs = SETTINGS_SAVE_DEBOUNCE_MS,
): DebouncedSaver {
  const pending = new Map<string, { value: unknown; timer: ReturnType<typeof setTimeout> }>();

  const write = async (key: string, value: unknown) => {
    try {
      if (!(await store.save(key, value))) onFailed(key, null);
    } catch (error) {
      onFailed(key, error);
    }
  };

  const fire = (key: string) => {
    const entry = pending.get(key);
    if (!entry) return Promise.resolve();
    pending.delete(key);
    return write(key, entry.value);
  };

  return {
    schedule(key, value) {
      const existing = pending.get(key);
      if (existing) clearTimeout(existing.timer);
      pending.set(key, { value, timer: setTimeout(() => void fire(key), delayMs) });
    },
    async flush() {
      const keys = [...pending.keys()];
      for (const key of keys) clearTimeout(pending.get(key)!.timer);
      await Promise.all(keys.map(fire));
    },
    cancel(key) {
      const existing = pending.get(key);
      if (existing) clearTimeout(existing.timer);
      pending.delete(key);
    },
    dispose() {
      for (const entry of pending.values()) clearTimeout(entry.timer);
      pending.clear();
    },
  };
}

/** One shared record per pane instance, so several hooks of one pane (filters, mode, columns) write one object. */
export interface PaneSettingsCache {
  get(key: string): { loaded: boolean; value: Record<string, unknown> };
  /** Loads the key once; later calls return the same promise. */
  ensureLoaded(key: string): Promise<void>;
  patch(key: string, patch: Record<string, unknown>): void;
  subscribe(key: string, listener: () => void): () => void;
}

export function createPaneSettingsCache(store: PlanboardSettingsStore, saver: DebouncedSaver): PaneSettingsCache {
  const entries = new Map<string, { loaded: boolean; value: Record<string, unknown> }>();
  const loading = new Map<string, Promise<void>>();
  const listeners = new Map<string, Set<() => void>>();
  const EMPTY_ENTRY = { loaded: false, value: {} };
  const notify = (key: string) => listeners.get(key)?.forEach((l) => l());
  return {
    get: (key) => entries.get(key) ?? EMPTY_ENTRY,
    ensureLoaded(key) {
      const existing = loading.get(key);
      if (existing) return existing;
      const promise = store.load(key).then(
        (stored) => {
          /* A patch made while loading wins over the stored value. */
          const current = entries.get(key)?.value ?? {};
          const base = stored != null && typeof stored === "object" && !Array.isArray(stored) ? (stored as Record<string, unknown>) : {};
          entries.set(key, { loaded: true, value: { ...base, ...current } });
          notify(key);
        },
        () => {
          entries.set(key, { loaded: true, value: entries.get(key)?.value ?? {} });
          notify(key);
        },
      );
      loading.set(key, promise);
      return promise;
    },
    patch(key, patch) {
      const current = entries.get(key) ?? { loaded: false, value: {} };
      const next = { loaded: current.loaded, value: { ...current.value, ...patch } };
      entries.set(key, next);
      saver.schedule(key, next.value);
      notify(key);
    },
    subscribe(key, listener) {
      const set = listeners.get(key) ?? new Set();
      set.add(listener);
      listeners.set(key, set);
      return () => set.delete(listener);
    },
  };
}

interface PlanboardSettingsValue {
  store: PlanboardSettingsStore;
  saver: DebouncedSaver;
  paneSettings: PaneSettingsCache;
}

const PlanboardSettingsContext = createContext<PlanboardSettingsValue | null>(null);

export function PlanboardSettingsProvider({ store = tabStore, children }: { store?: PlanboardSettingsStore; children: ReactNode }) {
  const { t } = useL();
  const tRef = useRef(t);
  tRef.current = t;
  const saver = useMemo(
    () =>
      createDebouncedSaver(store, (key, error) => {
        console.error({ event: "PlanboardSettingsSaveFailed", key });
        toast.error(tRef.current("Planboard:SettingsSaveFailed"), { id: "planboard-settings", description: error instanceof Error ? error.message : undefined });
      }),
    [store],
  );
  const paneSettings = useMemo(() => createPaneSettingsCache(store, saver), [store, saver]);
  /* Leaving the app writes what is still pending instead of dropping it. */
  useEffect(() => () => void saver.flush(), [saver]);
  const value = useMemo(() => ({ store, saver, paneSettings }), [store, saver, paneSettings]);
  return <PlanboardSettingsContext.Provider value={value}>{children}</PlanboardSettingsContext.Provider>;
}

export function usePlanboardSettings(): PlanboardSettingsValue {
  const value = useContext(PlanboardSettingsContext);
  if (!value) throw new Error("usePlanboardSettings needs a PlanboardSettingsProvider (the Planboard workspace).");
  return value;
}

export interface PaneSettingsState<T> {
  /** The pane's settings: the stored ones merged over the defaults, the defaults until loaded. */
  settings: T;
  /** False until the stored value has been read (or found absent or unreadable). */
  isLoaded: boolean;
  /** Merges a patch into the pane's record and schedules the save (1 s debounce). */
  update: (patch: Partial<T>) => void;
}

/**
 * The settings of one pane instance (`planboard.pane.<instanceId>`). Every hook on the same instance shares one record,
 * each reading the fields it knows: `defaults` fill what is absent and `sanitize` repairs or drops fields of an
 * outdated or corrupt shape (without it the stored fields are taken as they are).
 */
export function usePaneSettings<T extends object>(instanceId: string, defaults: T, sanitize?: (stored: Record<string, unknown>) => Partial<T>): PaneSettingsState<T> {
  const { paneSettings } = usePlanboardSettings();
  const key = paneSettingsKey(instanceId);
  const entry = useSyncExternalStore(
    useCallback((listener: () => void) => paneSettings.subscribe(key, listener), [paneSettings, key]),
    () => paneSettings.get(key),
  );

  useEffect(() => {
    void paneSettings.ensureLoaded(key);
  }, [paneSettings, key]);

  const defaultsRef = useRef(defaults);
  const sanitizeRef = useRef(sanitize);
  sanitizeRef.current = sanitize;
  const settings = useMemo(
    () => ({ ...defaultsRef.current, ...(sanitizeRef.current ? sanitizeRef.current(entry.value) : (entry.value as Partial<T>)) }) as T,
    [entry.value],
  );
  const update = useCallback((patch: Partial<T>) => paneSettings.patch(key, patch as Record<string, unknown>), [paneSettings, key]);
  return { settings, isLoaded: entry.loaded, update };
}
