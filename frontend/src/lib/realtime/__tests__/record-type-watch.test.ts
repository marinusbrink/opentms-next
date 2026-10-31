import { HubConnectionState } from "@microsoft/signalr";
import { describe, expect, it, vi } from "vitest";

import { RecordUpdatesClient, type RecordUpdateMessage, type RecordUpdatesConnection } from "@/lib/realtime/record-updates";

/* The record-type channel of design 67 on the client (RecordUpdatesClient.watchType) with a fake hub connection:
 * reference counting (one WatchType per type, UnwatchType only when the last watcher leaves, an idempotent release),
 * re-watching every type after a reconnect, and the messages of a type reaching subscribers with their hints.
 * Risk class: Critical (Platform change, design 67); the server side (group names per tenant) is covered by the
 * backend's RecordUpdatesHubTypeWatchTests / PlanboardRecordChannelPostgreSqlTests. */

function fakeConnection() {
  const handlers: Record<string, (message: RecordUpdateMessage) => void> = {};
  let reconnected: (() => void) | undefined;
  let closed: (() => void) | undefined;
  const connection = {
    state: HubConnectionState.Disconnected as HubConnectionState,
    start: vi.fn(async () => {
      connection.state = HubConnectionState.Connected;
    }),
    invoke: vi.fn(async (..._args: unknown[]) => undefined),
    on: vi.fn((name: string, handler: (message: RecordUpdateMessage) => void) => {
      handlers[name] = handler;
    }),
    onreconnected: vi.fn((cb: () => void) => {
      reconnected = cb;
    }),
    onclose: vi.fn((cb: () => void) => {
      closed = cb;
    }),
  };
  return {
    connection: connection as unknown as RecordUpdatesConnection,
    raw: connection,
    push: (message: RecordUpdateMessage) => handlers.recordUpdated?.(message),
    reconnect: () => reconnected?.(),
    close: () => {
      connection.state = HubConnectionState.Disconnected;
      closed?.();
    },
  };
}

const flush = () => new Promise((resolve) => setTimeout(resolve, 0));
const TRIP = "PlanningExecution.TransportTrip";
const LEG = "PlanningExecution.Leg";

describe("RecordUpdatesClient.watchType (design 67)", () => {
  it("sends WatchType once per type however many panes watch it, and UnwatchType when the last one leaves", async () => {
    const fake = fakeConnection();
    const client = new RecordUpdatesClient(() => fake.connection);

    const a = client.watchType(TRIP);
    const b = client.watchType(TRIP);
    const c = client.watchType(LEG);
    await flush();

    expect(fake.raw.start).toHaveBeenCalledTimes(1);
    expect(fake.raw.invoke.mock.calls).toEqual([
      ["WatchType", TRIP],
      ["WatchType", LEG],
    ]);
    expect(client.watchedTypes().sort()).toEqual([LEG, TRIP]);

    fake.raw.invoke.mockClear();
    a();
    await flush();
    expect(fake.raw.invoke).not.toHaveBeenCalled();
    expect(client.watchedTypes()).toContain(TRIP);

    b();
    await flush();
    expect(fake.raw.invoke.mock.calls).toEqual([["UnwatchType", TRIP]]);
    expect(client.watchedTypes()).toEqual([LEG]);

    c();
    await flush();
    expect(fake.raw.invoke).toHaveBeenLastCalledWith("UnwatchType", LEG);
    expect(client.watchedTypes()).toEqual([]);
  });

  it("a release called twice counts once (it does not release another pane's watch)", async () => {
    const fake = fakeConnection();
    const client = new RecordUpdatesClient(() => fake.connection);
    const a = client.watchType(TRIP);
    client.watchType(TRIP);
    await flush();
    fake.raw.invoke.mockClear();

    a();
    a();
    await flush();
    expect(fake.raw.invoke).not.toHaveBeenCalled();
    expect(client.watchedTypes()).toEqual([TRIP]);
  });

  it("watching a type again after it was released sends WatchType again", async () => {
    const fake = fakeConnection();
    const client = new RecordUpdatesClient(() => fake.connection);
    client.watchType(TRIP)();
    await flush();
    fake.raw.invoke.mockClear();
    client.watchType(TRIP);
    await flush();
    expect(fake.raw.invoke.mock.calls).toEqual([["WatchType", TRIP]]);
  });

  it("type watches and record watches are counted separately", async () => {
    const fake = fakeConnection();
    const client = new RecordUpdatesClient(() => fake.connection);
    const typeWatch = client.watchType(TRIP);
    client.watch(TRIP, ["t1"]);
    await flush();
    fake.raw.invoke.mockClear();

    typeWatch();
    await flush();
    expect(fake.raw.invoke.mock.calls).toEqual([["UnwatchType", TRIP]]);
    expect(client.watched().get(TRIP)).toEqual(["t1"]);
  });

  it("re-sends every active type watch (and record watch) after a reconnect, and tells the screens to refetch", async () => {
    const fake = fakeConnection();
    const client = new RecordUpdatesClient(() => fake.connection);
    const reconnects = vi.fn();
    client.onReconnected(reconnects);
    client.watchType(TRIP);
    client.watchType(TRIP);
    const leg = client.watchType(LEG);
    client.watch(TRIP, ["t1"]);
    leg();
    await flush();
    fake.raw.invoke.mockClear();

    fake.reconnect();
    await flush();

    expect(fake.raw.invoke.mock.calls).toEqual([
      ["Watch", TRIP, ["t1"]],
      ["WatchType", TRIP],
    ]);
    expect(reconnects).toHaveBeenCalledTimes(1);
  });

  it("does not send UnwatchType while disconnected (a new connection has no groups anyway)", async () => {
    const fake = fakeConnection();
    const client = new RecordUpdatesClient(() => fake.connection);
    const release = client.watchType(TRIP);
    await flush();
    fake.raw.state = HubConnectionState.Reconnecting;
    fake.raw.invoke.mockClear();
    release();
    await flush();
    expect(fake.raw.invoke).not.toHaveBeenCalled();
    expect(client.watchedTypes()).toEqual([]);
  });

  it("re-joins the type groups still watched when a new connection starts after the old one closed for good", async () => {
    /* onclose = automatic reconnect gave up. The client starts a new connection on the next watch; a new connection
     * has no groups, so every type still watched must be joined again (design 67: "re-sent after a reconnect like
     * watch"), otherwise the panes that are still open stay deaf until the page reloads. */
    const first = fakeConnection();
    const second = fakeConnection();
    const factory = vi.fn().mockReturnValueOnce(first.connection).mockReturnValueOnce(second.connection);
    const client = new RecordUpdatesClient(factory);
    client.watchType(TRIP);
    await flush();

    first.close();
    client.watchType(LEG);
    await flush();

    expect(factory).toHaveBeenCalledTimes(2);
    expect(second.raw.invoke.mock.calls).toContainEqual(["WatchType", LEG]);
    expect(second.raw.invoke.mock.calls).toContainEqual(["WatchType", TRIP]);
  });

  it("delivers a type's notices with their routing hints to subscribers", async () => {
    const fake = fakeConnection();
    const client = new RecordUpdatesClient(() => fake.connection);
    const received: RecordUpdateMessage[] = [];
    client.subscribe((m) => received.push(m));
    client.watchType(TRIP);
    await flush();

    const message: RecordUpdateMessage = {
      targetType: TRIP,
      targetId: "T9",
      version: null,
      status: null,
      hints: { from: "2026-11-02", till: "2026-11-03", organizationUnitId: "ou1", resourceIds: ["v1"], removed: false },
    };
    fake.push(message);
    expect(received).toEqual([message]);
  });

  it("a failing WatchType does not throw into the pane", async () => {
    const fake = fakeConnection();
    fake.raw.invoke.mockRejectedValueOnce(new Error("HubException: RecordUpdates:NotAllowed"));
    const warn = vi.spyOn(console, "warn").mockImplementation(() => undefined);
    const client = new RecordUpdatesClient(() => fake.connection);
    expect(() => client.watchType(TRIP)).not.toThrow();
    await flush();
    expect(warn).toHaveBeenCalled();
    warn.mockRestore();
  });
});
