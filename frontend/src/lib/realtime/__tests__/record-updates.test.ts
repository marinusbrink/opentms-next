import { HubConnectionState } from "@microsoft/signalr";
import { describe, expect, it, vi } from "vitest";

import { RecordUpdatesClient, type RecordUpdateMessage, type RecordUpdatesConnection } from "@/lib/realtime/record-updates";

/* The live record-update client of design 49 with a fake hub connection: lazy start, reference-counted watches,
 * chunking at 200, Unwatch only when the last watcher leaves, messages to subscribers, and re-watching every active
 * record after a reconnect. Risk class: Medium. */

function fakeConnection() {
  const handlers: Record<string, (message: RecordUpdateMessage) => void> = {};
  let reconnected: (() => void) | undefined;
  const connection = {
    state: HubConnectionState.Disconnected as HubConnectionState,
    start: vi.fn(async () => {
      connection.state = HubConnectionState.Connected;
    }),
    invoke: vi.fn(async () => undefined),
    on: vi.fn((name: string, handler: (message: RecordUpdateMessage) => void) => {
      handlers[name] = handler;
    }),
    onreconnected: vi.fn((cb: () => void) => {
      reconnected = cb;
    }),
    onclose: vi.fn(),
  };
  return {
    connection: connection as unknown as RecordUpdatesConnection,
    raw: connection,
    push: (message: RecordUpdateMessage) => handlers.recordUpdated?.(message),
    reconnect: () => reconnected?.(),
  };
}

const flush = () => new Promise((resolve) => setTimeout(resolve, 0));

describe("RecordUpdatesClient", () => {
  it("starts the connection on the first watch and joins each record once", async () => {
    const fake = fakeConnection();
    const factory = vi.fn(() => fake.connection);
    const client = new RecordUpdatesClient(factory);
    expect(factory).not.toHaveBeenCalled();

    const a = client.watch("PlanningExecution.TransportTrip", ["t1", "t2"]);
    const b = client.watch("PlanningExecution.TransportTrip", ["t2", "t3"]);
    await flush();

    expect(factory).toHaveBeenCalledTimes(1);
    expect(fake.raw.start).toHaveBeenCalledTimes(1);
    expect(fake.raw.invoke.mock.calls).toEqual([
      ["Watch", "PlanningExecution.TransportTrip", ["t1", "t2"]],
      ["Watch", "PlanningExecution.TransportTrip", ["t3"]],
    ]);

    a();
    await flush();
    expect(fake.raw.invoke).toHaveBeenLastCalledWith("Unwatch", "PlanningExecution.TransportTrip", ["t1"]);

    b();
    await flush();
    expect(fake.raw.invoke).toHaveBeenLastCalledWith("Unwatch", "PlanningExecution.TransportTrip", ["t2", "t3"]);
    expect(client.watched().size).toBe(0);
  });

  it("sends at most 200 ids per call", async () => {
    const fake = fakeConnection();
    const client = new RecordUpdatesClient(() => fake.connection);
    client.watch("Orders.TransportOrder", Array.from({ length: 450 }, (_, i) => `o${i}`));
    await flush();
    expect(fake.raw.invoke.mock.calls.map((c) => ((c as unknown[])[2] as string[]).length)).toEqual([200, 200, 50]);
  });

  it("delivers pushes to subscribers and re-watches everything after a reconnect", async () => {
    const fake = fakeConnection();
    const client = new RecordUpdatesClient(() => fake.connection);
    const received: RecordUpdateMessage[] = [];
    const reconnects = vi.fn();
    client.subscribe((m) => received.push(m));
    client.onReconnected(reconnects);
    client.watch("Orders.TransportOrder", ["o1"]);
    await flush();

    fake.push({ targetType: "Orders.TransportOrder", targetId: "o1", version: 3, status: 2 });
    expect(received).toEqual([{ targetType: "Orders.TransportOrder", targetId: "o1", version: 3, status: 2 }]);

    fake.raw.invoke.mockClear();
    fake.reconnect();
    await flush();
    expect(fake.raw.invoke).toHaveBeenCalledWith("Watch", "Orders.TransportOrder", ["o1"]);
    expect(reconnects).toHaveBeenCalledTimes(1);
  });

  it("a failing watch does not throw into the screen", async () => {
    const fake = fakeConnection();
    fake.raw.start.mockRejectedValueOnce(new Error("offline"));
    const warn = vi.spyOn(console, "warn").mockImplementation(() => {});
    const client = new RecordUpdatesClient(() => fake.connection);
    expect(() => client.watch("Orders.TransportOrder", ["o1"])).not.toThrow();
    await flush();
    expect(warn).toHaveBeenCalled();
    warn.mockRestore();
  });
});
