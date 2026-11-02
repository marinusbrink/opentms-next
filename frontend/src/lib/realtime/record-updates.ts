import { HubConnectionBuilder, HubConnectionState, LogLevel, type HubConnection } from "@microsoft/signalr";

import { API_BASE_URL, currentAccessToken } from "@/lib/api/client";

/* Live record updates (docs/designs/49-derived-values-and-live-updates.md): one SignalR connection per browser tab,
 * started on the first watch. A screen watches the records it shows; the server pushes "recordUpdated" with the
 * record's type, id, version and calculation status, never data. Watches are reference-counted (two components on one
 * trip share one group) and re-sent after a reconnect, because a new connection has no groups.
 * Design 67 (docs/designs/67-platform-record-type-channel.md) adds the type channel: watchType(targetType) receives every
 * notice of that type in the user's tenant, with routing hints (dates, organisation unit, resources, removed). */

export const HUB_PATH = "/signalr-hubs/record-updates";
export const MAX_WATCH_PER_CALL = 200;
/** The pause before a new connection when automatic reconnect gave up and records are still watched. */
export const RESTART_DELAY_MS = 15_000;

/** Mirrors OpenTms.Platform.DerivedValues.DerivedValueStatus. */
export const DerivedValueStatus = { Pending: 1, UpToDate: 2, Failed: 3 } as const;
export type DerivedValueStatus = (typeof DerivedValueStatus)[keyof typeof DerivedValueStatus];

/** Routing hints of a notice (design 67): ids and dates only, never data. */
export interface RecordUpdateHints {
  /** ISO date (yyyy-MM-dd) or null. */
  from: string | null;
  /** ISO date (yyyy-MM-dd) or null. */
  till: string | null;
  organizationUnitId: string | null;
  resourceIds: string[] | null;
  /** The record left the type's set (deleted, or no longer matching). */
  removed: boolean;
}

export interface RecordUpdateMessage {
  targetType: string;
  targetId: string;
  version: number | null;
  /** Null for a plain "changed by someone else" notice. */
  status: DerivedValueStatus | null;
  /** Null (or absent) when the notice carries no hints. */
  hints?: RecordUpdateHints | null;
}

type Listener = (message: RecordUpdateMessage) => void;

/** The part of a HubConnection the client uses; tests pass a fake. */
export type RecordUpdatesConnection = Pick<HubConnection, "start" | "invoke" | "on" | "onreconnected" | "onclose" | "state">;

const key = (targetType: string, id: string) => `${targetType}|${id.toLowerCase()}`;

function chunks<T>(items: T[], size: number): T[][] {
  const result: T[][] = [];
  for (let i = 0; i < items.length; i += size) result.push(items.slice(i, i + size));
  return result;
}

export class RecordUpdatesClient {
  private connection: RecordUpdatesConnection | undefined;
  private starting: Promise<void> | undefined;
  /** Set when a connection closed for good: the next one has no groups, so it re-joins every watch and screens refetch. */
  private rejoinOnStart = false;
  private restartTimer: ReturnType<typeof setTimeout> | undefined;
  private readonly counts = new Map<string, { targetType: string; id: string; count: number }>();
  private readonly typeCounts = new Map<string, number>();
  private readonly listeners = new Set<Listener>();
  private readonly reconnectListeners = new Set<() => void>();

  private readonly createConnection: () => RecordUpdatesConnection;

  constructor(createConnection: () => RecordUpdatesConnection) {
    this.createConnection = createConnection;
  }

  /** Watches the records until the returned function is called. */
  watch(targetType: string, ids: readonly string[]): () => void {
    const added: string[] = [];
    for (const id of new Set(ids.filter(Boolean))) {
      const k = key(targetType, id);
      const entry = this.counts.get(k);
      if (entry) entry.count++;
      else {
        this.counts.set(k, { targetType, id, count: 1 });
        added.push(id);
      }
    }

    if (added.length > 0) void this.send("Watch", targetType, added);

    let released = false;
    return () => {
      if (released) return;
      released = true;
      const removed: string[] = [];
      for (const id of new Set(ids.filter(Boolean))) {
        const k = key(targetType, id);
        const entry = this.counts.get(k);
        if (!entry) continue;
        entry.count--;
        if (entry.count === 0) {
          this.counts.delete(k);
          removed.push(id);
        }
      }
      if (removed.length > 0 && this.connection?.state === HubConnectionState.Connected) void this.send("Unwatch", targetType, removed);
    };
  }

  /** Watches every record of the type (design 67) until the returned function is called. */
  watchType(targetType: string): () => void {
    const count = this.typeCounts.get(targetType) ?? 0;
    this.typeCounts.set(targetType, count + 1);
    if (count === 0) void this.sendType("WatchType", targetType);

    let released = false;
    return () => {
      if (released) return;
      released = true;
      const current = this.typeCounts.get(targetType) ?? 0;
      if (current <= 1) {
        this.typeCounts.delete(targetType);
        if (current === 1 && this.connection?.state === HubConnectionState.Connected) void this.sendType("UnwatchType", targetType);
      } else this.typeCounts.set(targetType, current - 1);
    };
  }

  subscribe(listener: Listener): () => void {
    this.listeners.add(listener);
    return () => this.listeners.delete(listener);
  }

  /** Called after a reconnect: anything may have changed while offline, so screens refetch. */
  onReconnected(listener: () => void): () => void {
    this.reconnectListeners.add(listener);
    return () => this.reconnectListeners.delete(listener);
  }

  /** The records currently watched, per type (for tests and diagnostics). */
  watched(): Map<string, string[]> {
    const result = new Map<string, string[]>();
    for (const { targetType, id } of this.counts.values()) result.set(targetType, [...(result.get(targetType) ?? []), id]);
    return result;
  }

  /** The record types currently watched as a whole (for tests and diagnostics). */
  watchedTypes(): string[] {
    return [...this.typeCounts.keys()];
  }

  private async ensureStarted(): Promise<RecordUpdatesConnection> {
    if (!this.connection) {
      const connection = this.createConnection();
      connection.on("recordUpdated", (message: RecordUpdateMessage) => this.listeners.forEach((l) => l(message)));
      connection.onreconnected(() => {
        void this.rewatchAll();
        this.reconnectListeners.forEach((l) => l());
      });
      /* Automatic reconnect gave up: start a new connection on the next watch, or after a pause while anything is still
       * watched, and re-join every group and refetch then (a new connection has no groups). */
      connection.onclose(() => {
        this.connection = undefined;
        this.starting = undefined;
        this.rejoinOnStart = true;
        this.scheduleRestart();
      });
      this.connection = connection;
    }

    const connection = this.connection;
    if (connection.state === HubConnectionState.Disconnected) {
      this.starting ??= connection.start().finally(() => {
        this.starting = undefined;
      });
    }
    if (this.starting) await this.starting;
    if (this.rejoinOnStart && connection.state === HubConnectionState.Connected) {
      this.rejoinOnStart = false;
      void this.rewatchAll();
      this.reconnectListeners.forEach((l) => l());
    }
    return connection;
  }

  private scheduleRestart(): void {
    if (this.restartTimer !== undefined) return;
    this.restartTimer = setTimeout(() => {
      this.restartTimer = undefined;
      if (this.connection || (this.counts.size === 0 && this.watchedTypes().length === 0)) return;
      void this.ensureStarted().catch(() => this.scheduleRestart());
    }, RESTART_DELAY_MS);
  }

  private async rewatchAll(): Promise<void> {
    for (const [targetType, ids] of this.watched()) await this.send("Watch", targetType, ids);
    for (const targetType of this.watchedTypes()) await this.sendType("WatchType", targetType);
  }

  private async send(method: "Watch" | "Unwatch", targetType: string, ids: string[]): Promise<void> {
    try {
      const connection = await this.ensureStarted();
      for (const part of chunks(ids, MAX_WATCH_PER_CALL)) await connection.invoke(method, targetType, part);
    } catch (error) {
      /* Best effort (design 49, assumption 4): without a live connection the screen still shows what it fetched. */
      console.warn(`Record updates: ${method} ${targetType} failed`, error);
    }
  }

  private async sendType(method: "WatchType" | "UnwatchType", targetType: string): Promise<void> {
    try {
      const connection = await this.ensureStarted();
      await connection.invoke(method, targetType);
    } catch (error) {
      console.warn(`Record updates: ${method} ${targetType} failed`, error);
    }
  }
}

function createHubConnection(): RecordUpdatesConnection {
  return new HubConnectionBuilder()
    .withUrl(`${API_BASE_URL}${HUB_PATH}`, { accessTokenFactory: () => currentAccessToken() ?? "" })
    .withAutomaticReconnect()
    .configureLogging(LogLevel.Warning)
    .build();
}

let shared: RecordUpdatesClient | undefined;

/** The tab's one client. */
export function recordUpdatesClient(): RecordUpdatesClient {
  shared ??= new RecordUpdatesClient(createHubConnection);
  return shared;
}
