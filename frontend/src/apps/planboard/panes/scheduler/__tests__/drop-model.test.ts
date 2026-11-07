import { describe, expect, it } from "vitest";

import { blocksAt, idsToRefetch, tripTypeChoice } from "@/apps/planboard/panes/scheduler/model";
import type { SchedulerBackgroundEvent } from "@/components/ui/resource-scheduler-model";
import { DerivedValueStatus, type RecordUpdateMessage } from "@/lib/realtime/record-updates";

/* What design 74 §§9, 10 added to the Scheduler's pure model: the blocks under a drop, the type picker's question and which
 * notices refetch a bar. Risk class: High (planning). */

const block = (id: string, resourceId: string, start: string, end: string): SchedulerBackgroundEvent => ({ id, resourceId, start, end, kind: "unavailable" });

describe("blocksAt", () => {
  const blocks = [block("b1", "r1", "2026-11-10T08:00:00", "2026-11-10T12:00:00"), block("b2", "r1", "2026-11-10T11:00:00", "2026-11-10T13:00:00"), block("b3", "r2", "2026-11-10T08:00:00", "2026-11-10T12:00:00")];
  const at = (time: string) => new Date(`2026-11-10T${time}`);

  it("finds the blocks of that row at that moment", () => {
    expect(blocksAt("r1", at("09:00:00"), blocks).map((b) => b.id)).toEqual(["b1"]);
    expect(blocksAt("r1", at("11:30:00"), blocks).map((b) => b.id)).toEqual(["b1", "b2"]);
  });

  it("includes the start, excludes the end", () => {
    expect(blocksAt("r1", at("08:00:00"), blocks).map((b) => b.id)).toEqual(["b1"]);
    expect(blocksAt("r1", at("13:00:00"), blocks)).toEqual([]);
    expect(blocksAt("r1", at("07:59:59"), blocks)).toEqual([]);
  });

  it("ignores other rows", () => {
    expect(blocksAt("r3", at("09:00:00"), blocks)).toEqual([]);
    expect(blocksAt("r2", at("12:30:00"), blocks)).toEqual([]);
  });

  it("takes Date bounds too", () => {
    const dated: SchedulerBackgroundEvent = { id: "d", resourceId: "r1", start: at("08:00:00"), end: at("09:00:00"), kind: "outside-working-time" };
    expect(blocksAt("r1", at("08:30:00"), [dated])).toEqual([dated]);
  });
});

describe("tripTypeChoice", () => {
  it("asks before the list is known and with more than one type", () => {
    expect(tripTypeChoice(undefined)).toEqual({ ask: true });
    expect(tripTypeChoice([{ id: "a" }, { id: "b" }])).toEqual({ ask: true });
  });

  it("does not ask with one type", () => {
    expect(tripTypeChoice([{ id: "a" }])).toEqual({ ask: false, typeId: "a" });
  });

  it("with no type the server says what is missing", () => {
    expect(tripTypeChoice([])).toEqual({ ask: false, typeId: null });
  });
});

describe("idsToRefetch", () => {
  const message = (targetId: string, status: RecordUpdateMessage["status"], hints?: RecordUpdateMessage["hints"]): RecordUpdateMessage => ({ targetType: "t", targetId, version: 1, status, hints });
  const never = () => false;

  it("a Pending notice does not refetch: its values are about to change", () => {
    expect(idsToRefetch([message("T1", DerivedValueStatus.Pending)], never)).toEqual([]);
  });

  it("the notice that ends a calculation refetches, up to date or failed", () => {
    expect(idsToRefetch([message("T1", DerivedValueStatus.UpToDate), message("T2", DerivedValueStatus.Failed)], () => true)).toEqual(["t1", "t2"]);
  });

  it("a plain notice refetches, unless the trip is calculating", () => {
    const calculating = (id: string) => id === "t1";
    expect(idsToRefetch([message("T1", null), message("T2", null)], calculating)).toEqual(["t2"]);
  });

  it("a plain notice with no status field at all counts as plain", () => {
    const plain = { targetType: "t", targetId: "T1", version: 1 } as unknown as RecordUpdateMessage;
    expect(idsToRefetch([plain], () => true)).toEqual([]);
    expect(idsToRefetch([plain], never)).toEqual(["t1"]);
  });

  it("a removed record always does: its bar must go", () => {
    const removed = { removed: true } as RecordUpdateMessage["hints"];
    expect(idsToRefetch([message("T1", DerivedValueStatus.Pending, removed), message("T2", null, removed)], () => true)).toEqual(["t1", "t2"]);
  });

  it("asks about the lower-cased id", () => {
    const asked: string[] = [];
    idsToRefetch([message("ABC", null)], (id) => {
      asked.push(id);
      return false;
    });
    expect(asked).toEqual(["abc"]);
  });
});
