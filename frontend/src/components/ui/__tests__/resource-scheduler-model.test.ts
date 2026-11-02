import { describe, expect, it } from "vitest";

import {
  BACKGROUND_ID_PREFIX,
  parseSlotDate,
  slotDurationMs,
  timeAtX,
  toTimelineEvents,
  toVisibleRange,
  type SchedulerBackgroundEvent,
  type SchedulerEvent,
} from "@/components/ui/resource-scheduler-model";

/* The pure part of the resource scheduler adapter (design 66 D13, design 70 "Scheduler"): bars and blocks mapped onto
 * the timeline's events (tone, conflict, selected and read-only classes; read-only bars not draggable; blocks as
 * background with a prefixed id), the visible range (exclusive end), slot sizes and the drop-position snapping.
 * Risk class: Medium. */

const bar = (id: string, extra: Partial<SchedulerEvent> = {}): SchedulerEvent => ({ id, resourceId: "R1", start: "2026-11-02T08:00:00", end: "2026-11-02T10:00:00", ...extra });

describe("toTimelineEvents", () => {
  it("maps an editable bar with its tone class and its source", () => {
    const source = bar("t1", { tone: "success", title: "R-1" });
    const [event] = toTimelineEvents([source]);
    expect(event).toMatchObject({
      id: "t1",
      resourceId: "R1",
      title: "R-1",
      editable: true,
      startEditable: true,
      durationEditable: true,
      resourceEditable: true,
      classNames: ["opentms-sched-event", "opentms-sched-event--tone-success"],
      extendedProps: { source, background: false },
    });
    expect(event.display).toBeUndefined();
  });

  it("defaults the tone to info and adds conflict, selected and read-only classes", () => {
    const [event] = toTimelineEvents([bar("t1", { conflict: true })], [], () => true, new Set(["t1"]));
    expect(event.classNames).toEqual([
      "opentms-sched-event",
      "opentms-sched-event--tone-info",
      "opentms-sched-event--conflict",
      "opentms-sched-event--selected",
      "opentms-sched-event--readonly",
    ]);
    expect([event.editable, event.startEditable, event.durationEditable, event.resourceEditable]).toEqual([false, false, false, false]);
  });

  it("decides read-only per bar", () => {
    const events = toTimelineEvents([bar("a", { data: { done: true } }), bar("b", { data: { done: false } })], [], (e) => (e.data as { done: boolean }).done);
    expect(events.map((e) => e.editable)).toEqual([false, true]);
  });

  it("maps blocks after the bars as background events with a prefixed id and their kind", () => {
    const block: SchedulerBackgroundEvent = { id: "t1", resourceId: "R1", start: "2026-11-02T00:00:00", end: "2026-11-02T06:00:00", kind: "outside-working-time", title: "Off" };
    const events = toTimelineEvents([bar("t1")], [block]);
    expect(events.map((e) => e.id)).toEqual(["t1", `${BACKGROUND_ID_PREFIX}t1`]);
    expect(events[1]).toMatchObject({ display: "background", classNames: ["opentms-sched-bg", "opentms-sched-bg--outside-working-time"], extendedProps: { source: block, background: true } });
    expect(events[1].editable).toBeUndefined();
  });
});

describe("ranges, slots and snapping", () => {
  it("shows from the first day to the day after the last (exclusive), across month and year ends", () => {
    expect(toVisibleRange({ from: "2026-11-02", till: "2026-11-08" })).toEqual({ start: "2026-11-02", end: "2026-11-09" });
    expect(toVisibleRange({ from: "2026-12-31", till: "2026-12-31" })).toEqual({ start: "2026-12-31", end: "2027-01-01" });
    expect(toVisibleRange({ from: "2026-10-25", till: "2026-10-25" }).end).toBe("2026-10-26");
  });

  it("sizes an hour and a day slot", () => {
    expect(slotDurationMs("hour")).toBe(3_600_000);
    expect(slotDurationMs("day")).toBe(86_400_000);
  });

  it("reads a bare slot date as local midnight", () => {
    expect(parseSlotDate("2026-11-02")!.getTime()).toBe(new Date(2026, 10, 2).getTime());
    expect(parseSlotDate("2026-11-02T08:00:00")!.getHours()).toBe(8);
    expect(parseSlotDate("")).toBeNull();
    expect(parseSlotDate("nope")).toBeNull();
  });

  it("snaps the time under x down to 15 minutes, null outside every slot", () => {
    const slots = [
      { start: new Date(2026, 10, 2, 8), left: 0, width: 100 },
      { start: new Date(2026, 10, 2, 9), left: 100, width: 100 },
    ];
    expect(timeAtX(slots, 0, 3_600_000)).toEqual(new Date(2026, 10, 2, 8, 0));
    expect(timeAtX(slots, 49, 3_600_000)).toEqual(new Date(2026, 10, 2, 8, 15));
    expect(timeAtX(slots, 50, 3_600_000)).toEqual(new Date(2026, 10, 2, 8, 30));
    expect(timeAtX(slots, 150, 3_600_000, 60)).toEqual(new Date(2026, 10, 2, 9, 0));
    expect(timeAtX(slots, 200, 3_600_000)).toBeNull();
    expect(timeAtX(slots, -1, 3_600_000)).toBeNull();
    expect(timeAtX([{ start: new Date(), left: 0, width: 0 }], 0, 3_600_000)).toBeNull();
  });
});
