import { HubConnectionBuilder, HubConnectionState, LogLevel, type HubConnection } from "@microsoft/signalr";

import { API_BASE_URL, currentAccessToken } from "@/lib/api/client";

/* Live record updates (docs/designs/49-derived-values-and-live-updates.md): one SignalR connection per browser tab,
 * started on the first watch. A screen watches the records it shows; the server pushes "recordUpdated" with the
 * record's type, id, version and calculation status, never data. Watches are reference-counted (two components on one
 * trip share one group) and re-sent after a reconnect, because a new connection has no groups. */

export const HUB_PATH = "/signalr-hubs/record-updates";
export const MAX_WATCH_PER_CALL = 200;

/** Mirrors OpenTms.Platform.DerivedValues.DerivedValueStatus. */
export const DerivedValueStatus = { Pending: 1, UpToDate: 2, Failed: 3 } as const;
export type DerivedValueStatus = (typeof DerivedValueStatus)[keyof typeof DerivedValueStatus];

export interface RecordUpdateMessage {
  targetType: string;
  targetId: string;
  version: number | null;
  /** Null for a plain "changed by someone else" notice. */
  status: DerivedValueStatus | null;
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
  private readonly counts = new Map<string, { targetType: string; id: string; count: number }>();
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

  private async ensureStarted(): Promise<RecordUpdatesConnection> {
    if (!this.connection) {
      const connection = this.createConnection();
      connection.on("recordUpdated", (message: RecordUpdateMessage) => this.listeners.forEach((l) => l(message)));
      connection.onreconnected(() => {
        void this.rewatchAll();
        this.reconnectListeners.forEach((l) => l());
      });
      /* Automatic reconnect gave up: start again on the next watch, and refetch then. */
      connection.onclose(() => {
        this.connection = undefined;
        this.starting = undefined;
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
    return connection;
  }

  private async rewatchAll(): Promise<void> {
    for (const [targetType, ids] of this.watched()) await this.send("Watch", targetType, ids);
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
