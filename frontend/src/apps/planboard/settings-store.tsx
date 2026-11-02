import { createContext, useCallback, useContext, useEffect, useMemo, useRef, useSyncExternalStore, type ReactNode } from "react";
import { toast } from "sonner";

import { isValidRange, MAX_BOARD_RANGE_DAYS, type PlanboardRange } from "@/apps/planboard/range";
import { apiClient } from "@/lib/api/client";
import { getApiErrorMessage } from "@/lib/api/errors";
import { useL } from "@/lib/i18n/LocalizationProvider";

/* Per-user settings of the Planboard (design 70, "Persistence"): the dockview layout under `planboard.layout` (with the
 * board's date range and organisation units in the same record) and each pane instance's settings (mode, filters,
 * columns, zoom, bar fields) under `planboard.pane.<instanceId>`, written debounced (1 s). The default store keeps them in
 * Platform's GridUserSettings (`/api/platform/grid-settings/{gridId}`, the free-form `viewState`, at most 64 KB); the
 * memory store serves tests. */

export const PLANBOARD_LAYOUT_KEY = "planboard.layout";
export const paneSettingsKey = (instanceId: string) => `planboard.pane.${instanceId}`;
export const SETTINGS_SAVE_DEBOUNCE_MS = 1000;

/** The grid-settings endpoint's id rule: every settings key (and so every pane instance id) must match it. */
export const SETTINGS_KEY_PATTERN = /^[a-zA-Z0-9_.-]{1,100}$/;

export function isValidSettingsKey(key: string): boolean {
  return SETTINGS_KEY_PATTERN.test(key);
}

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

function assertKey(key: string) {
  if (!isValidSettingsKey(key)) throw new Error(`Invalid Planboard settings key: ${key}`);
}

/**
 * The server store: each key is a GridUserSettings row of the user (`gridId` = the key) holding the value as its
 * `viewState`. An absent row reads as null. A failed request rejects with the API error (the saver shows it).
 */
export function createServerSettingsStore(): PlanboardSettingsStore {
  return {
    async load(key) {
      assertKey(key);
      const { data, error, response } = await apiClient.GET("/api/platform/grid-settings/{gridId}", { params: { path: { gridId: key } } });
      if (error !== undefined || !response.ok) throw error ?? new Error(`HTTP ${response.status}`);
      return data?.viewState ?? null;
    },
    async save(key, value) {
      assertKey(key);
      const { error, response } = await apiClient.PUT("/api/platform/grid-settings/{gridId}", { params: { path: { gridId: key } }, body: { columnStates: [], sortModels: [], viewState: value } });
      if (error !== undefined || !response.ok) throw error ?? new Error(`HTTP ${response.status}`);
      return true;
    },
    async remove(key) {
      assertKey(key);
      const { error, response } = await apiClient.DELETE("/api/platform/grid-settings/{gridId}", { params: { path: { gridId: key } } });
      if (error !== undefined || !response.ok) throw error ?? new Error(`HTTP ${response.status}`);
      return true;
    },
  };
}

/** The default store of the app (the server; one per browser tab). */
const serverStore = createServerSettingsStore();

/** The board filters as the layout record keeps them (`board`). */
export interface StoredBoardFilters {
  range: PlanboardRange;
  organizationUnitIds: string[];
}

/** The board filters of a stored layout record, or null when absent or invalid (exported for tests). */
export function parseStoredBoardFilters(record: unknown): StoredBoardFilters | null {
  if (record == null || typeof record !== "object" || Array.isArray(record)) return null;
  const board = (record as { board?: unknown }).board;
  if (board == null || typeof board !== "object") return null;
  const { range, organizationUnitIds } = board as Partial<StoredBoardFilters>;
  if (!isValidRange(range, MAX_BOARD_RANGE_DAYS)) return null;
  const units = Array.isArray(organizationUnitIds) ? organizationUnitIds.filter((id): id is string => typeof id === "string" && id.length > 0) : [];
  return { range: { from: range.from, till: range.till }, organizationUnitIds: [...new Set(units)] };
}

/** The layout record without its board part. */
function withoutBoard(record: unknown): Record<string, unknown> | null {
  if (record == null || typeof record !== "object" || Array.isArray(record)) return null;
  const rest = { ...(record as Record<string, unknown>) };
  delete rest.board;
  return rest;
}

/**
 * The board filters inside the layout record: a store over `inner` that writes `board` into every save of
 * `planboard.layout` (the workspace saves the dockview part, the board filters add theirs), remembers the last layout so
 * a board change alone can save the record again, and shares the first read of the record between the board filters and
 * the workspace (one request at start). A board change before any layout is known waits for the first layout save: a
 * record with a board and no dockview part would reset the user's layout.
 */
export function createLayoutRecordStore(inner: PlanboardSettingsStore) {
  let board: StoredBoardFilters | null = null;
  let layout: Record<string, unknown> | null = null;
  let firstLoad: Promise<unknown> | null = null;

  const loadLayout = () => {
    firstLoad ??= inner.load(PLANBOARD_LAYOUT_KEY).then((record) => {
      layout ??= withoutBoard(record);
      return record;
    });
    return firstLoad;
  };

  const store: PlanboardSettingsStore = {
    load: (key) => (key === PLANBOARD_LAYOUT_KEY ? loadLayout() : inner.load(key)),
    save: (key, value) => {
      if (key !== PLANBOARD_LAYOUT_KEY) return inner.save(key, value);
      layout = withoutBoard(value);
      /* A later read must not return the record as it was at start. */
      firstLoad = Promise.resolve({ ...(layout ?? {}), ...(board ? { board } : {}) });
      return inner.save(key, { ...(layout ?? {}), ...(board ? { board } : {}) });
    },
    remove: (key) => {
      if (key === PLANBOARD_LAYOUT_KEY) {
        layout = null;
        firstLoad = null;
      }
      return inner.remove(key);
    },
  };

  return {
    store,
    /** The stored board filters (null when there are none). */
    loadBoard: async (): Promise<StoredBoardFilters | null> => parseStoredBoardFilters(await loadLayout()),
    /** Remembers the board filters; true when the record can be saved now (a layout is known). */
    setBoard(next: StoredBoardFilters): boolean {
      board = next;
      return layout != null;
    },
    /** The record to save after a board change. */
    currentLayout: () => layout,
  };
}

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
  /** The board filters in the layout record (see createLayoutRecordStore). */
  board: {
    load: () => Promise<StoredBoardFilters | null>;
    save: (next: StoredBoardFilters) => void;
  };
}

const PlanboardSettingsContext = createContext<PlanboardSettingsValue | null>(null);

export function PlanboardSettingsProvider({ store: innerStore = serverStore, children }: { store?: PlanboardSettingsStore; children: ReactNode }) {
  const { t } = useL();
  const tRef = useRef(t);
  tRef.current = t;
  const record = useMemo(() => createLayoutRecordStore(innerStore), [innerStore]);
  const store = record.store;
  const saver = useMemo(
    () =>
      createDebouncedSaver(store, (key, error) => {
        console.error({ event: "PlanboardSettingsSaveFailed", key });
        toast.error(tRef.current("Planboard:SettingsSaveFailed"), { id: "planboard-settings", description: error == null ? undefined : getApiErrorMessage(error, tRef.current) });
      }),
    [store],
  );
  const paneSettings = useMemo(() => createPaneSettingsCache(store, saver), [store, saver]);
  /* Leaving the app writes what is still pending instead of dropping it. */
  useEffect(() => () => void saver.flush(), [saver]);
  const board = useMemo(
    () => ({
      load: record.loadBoard,
      save: (next: StoredBoardFilters) => {
        if (record.setBoard(next)) saver.schedule(PLANBOARD_LAYOUT_KEY, record.currentLayout());
      },
    }),
    [record, saver],
  );
  const value = useMemo(() => ({ store, saver, paneSettings, board }), [store, saver, paneSettings, board]);
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

/**
 * Keeps the board filters in the layout record: once at start the stored range and units are handed to `restore` (unless
 * the user changed the filters meanwhile), and every later change is saved with the layout (1 s debounce). Without a
 * settings provider (a filters test) it does nothing.
 */
export function usePersistedBoardFilters(current: StoredBoardFilters, restore: (stored: StoredBoardFilters) => void): void {
  const settings = useContext(PlanboardSettingsContext);
  const restoreRef = useRef(restore);
  restoreRef.current = restore;
  const initial = useRef(JSON.stringify(current));
  const last = useRef<string | null>(null);
  const serialized = JSON.stringify(current);

  useEffect(() => {
    if (!settings) return;
    let cancelled = false;
    settings.board.load().then(
      (stored) => {
        if (cancelled || !stored) return;
        /* A change the user made before the record arrived wins. */
        if (last.current != null && last.current !== initial.current) return;
        last.current = JSON.stringify(stored);
        restoreRef.current(stored);
      },
      /* Unreadable: the defaults stay (the workspace falls back to its default layout the same way). */
      () => undefined,
    );
    return () => {
      cancelled = true;
    };
  }, [settings]);

  useEffect(() => {
    if (!settings) return;
    if (last.current == null) {
      last.current = serialized;
      if (serialized === initial.current) return;
    } else if (last.current === serialized) {
      return;
    }
    last.current = serialized;
    settings.board.save(JSON.parse(serialized) as StoredBoardFilters);
  }, [settings, serialized]);
}
