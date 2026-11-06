import { act, fireEvent, render, screen, within } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

/* The Stops timeline (design 74 §2): click selects, double click or Enter opens, the arrow keys move, the right click opens the
 * menu; a stop's grip drags it onto another (no grip once it has actuals); "now" moves once a minute. Risk class: Medium (UI
 * flow); the reorder's server side is in the backend's tests. */

vi.mock("@/lib/i18n/LocalizationProvider", () => ({ useL: () => ({ t: (k: string) => k, culture: "en", isLoading: false }) }));

import { StopTimeline, type StopTimelineProps } from "@/apps/planboard/panes/stops/StopTimeline";
import type { TransportTripDto, TripStopDto } from "@/domains/planning-execution/trips";

const iso = (hour: number, minute = 0) => new Date(2026, 10, 10, hour, minute).toISOString();

const stop = (id: string, position: number, patch: Partial<TripStopDto> = {}): TripStopDto => ({ id, position, relationName: `Stop ${id}`, plannedArrival: iso(7 + position * 2), plannedDeparture: iso(8 + position * 2), ...patch }) as TripStopDto;

const STOPS = [stop("a", 1), stop("b", 2), stop("c", 3)];
const RUNNING = { id: "t1", status: 2 } as TransportTripDto;

const handlers = () => ({
  onSelect: vi.fn(),
  onOpen: vi.fn(),
  onMove: vi.fn(),
  onRecordActuals: vi.fn(),
  onRemove: vi.fn(),
  onAddStop: vi.fn(),
  onCommitRemarks: vi.fn(async () => undefined),
});
let on: ReturnType<typeof handlers>;

function show(props: Partial<StopTimelineProps> = {}) {
  return render(<StopTimeline trip={RUNNING} stops={STOPS} editable activityName={() => "Load"} selectedStopId={null} {...on} {...props} />);
}

const row = (id: string) => document.querySelector<HTMLElement>(`[data-stop-id="${id}"]`)!;
const grip = (id: string) => within(row(id)).queryByRole("button", { name: "Planboard:Lists.DragToReorder" });
const nowMarkers = () => Array.from(document.querySelectorAll<HTMLElement>("[data-slot=planboard-stop-now]"));

function transfer() {
  const data = new Map<string, string>();
  return {
    get types() {
      return [...data.keys()];
    },
    setData: (type: string, value: string) => void data.set(type, value),
    getData: (type: string) => data.get(type) ?? "",
    effectAllowed: "none",
    dropEffect: "none",
  };
}

beforeEach(() => {
  on = handlers();
  Element.prototype.scrollIntoView = vi.fn();
  vi.useFakeTimers({ toFake: ["Date", "setTimeout", "clearTimeout", "setInterval", "clearInterval"] });
  vi.setSystemTime(new Date(2026, 10, 10, 6, 0, 0));
});

afterEach(() => {
  vi.useRealTimers();
});

describe("StopTimeline: mouse", () => {
  it("a click selects, a double click opens", () => {
    show();
    fireEvent.click(row("b"));
    expect(on.onSelect).toHaveBeenCalledWith(STOPS[1]);
    expect(on.onOpen).not.toHaveBeenCalled();
    fireEvent.doubleClick(row("b"));
    expect(on.onOpen).toHaveBeenCalledWith(STOPS[1]);
  });

  it("a right click selects too", () => {
    show();
    fireEvent.contextMenu(row("c"));
    expect(on.onSelect).toHaveBeenCalledWith(STOPS[2]);
  });

  it("marks the selected stop, whatever the case of its id", () => {
    show({ selectedStopId: "B" });
    expect(row("b")).toHaveAttribute("aria-current", "true");
    expect(row("a")).not.toHaveAttribute("aria-current");
  });

  it("brings the selected stop into view", () => {
    show({ selectedStopId: "b" });
    expect(Element.prototype.scrollIntoView).toHaveBeenCalled();
  });
});

describe("StopTimeline: keyboard", () => {
  it("Enter opens the row", () => {
    show();
    fireEvent.keyDown(row("a"), { key: "Enter" });
    expect(on.onOpen).toHaveBeenCalledWith(STOPS[0]);
  });

  it("the arrow keys select and focus the next and previous row", () => {
    show();
    row("a").focus();
    fireEvent.keyDown(row("a"), { key: "ArrowDown" });
    expect(on.onSelect).toHaveBeenLastCalledWith(STOPS[1]);
    expect(document.activeElement).toBe(row("b"));
    fireEvent.keyDown(row("b"), { key: "ArrowUp" });
    expect(on.onSelect).toHaveBeenLastCalledWith(STOPS[0]);
    expect(document.activeElement).toBe(row("a"));
  });

  it("stops at the ends", () => {
    show();
    fireEvent.keyDown(row("a"), { key: "ArrowUp" });
    fireEvent.keyDown(row("c"), { key: "ArrowDown" });
    expect(on.onSelect).not.toHaveBeenCalled();
  });

  it("an element inside the row keeps its own keys", () => {
    show();
    fireEvent.keyDown(grip("a")!, { key: "Enter" });
    expect(on.onOpen).not.toHaveBeenCalled();
  });

  it("has one row in the tab order: the selected stop, else the first", () => {
    const { rerender } = show();
    expect(row("a").tabIndex).toBe(0);
    expect(row("b").tabIndex).toBe(-1);
    rerender(<StopTimeline trip={RUNNING} stops={STOPS} editable activityName={() => "Load"} selectedStopId="b" {...on} />);
    expect(row("a").tabIndex).toBe(-1);
    expect(row("b").tabIndex).toBe(0);
  });
});

describe("StopTimeline: the menu", () => {
  const openMenu = (id: string) => {
    fireEvent.contextMenu(row(id));
    act(() => void vi.advanceTimersByTime(0));
  };
  const item = (name: string) => screen.getByRole("menuitem", { name });
  const isDisabled = (name: string) => item(name).hasAttribute("data-disabled");

  it("offers everything on an editable trip", () => {
    show();
    openMenu("b");
    expect(screen.getAllByRole("menuitem").map((i) => i.textContent)).toEqual(["PlanningExecution:OpenStop", "Orders:MoveUp", "Orders:MoveDown", "PlanningExecution:RecordActuals", "PlanningExecution:AddStop", "PlanningExecution:RemoveStop"]);
  });

  it("offers only Open on a read-only trip", () => {
    show({ editable: false });
    openMenu("b");
    expect(screen.getAllByRole("menuitem").map((i) => i.textContent)).toEqual(["PlanningExecution:OpenStop"]);
  });

  it("the items do what they say", () => {
    show();
    openMenu("b");
    fireEvent.click(item("Orders:MoveUp"));
    expect(on.onMove).toHaveBeenLastCalledWith(STOPS[1], 1);
    openMenu("b");
    fireEvent.click(item("Orders:MoveDown"));
    expect(on.onMove).toHaveBeenLastCalledWith(STOPS[1], 3);
    openMenu("b");
    fireEvent.click(item("PlanningExecution:OpenStop"));
    expect(on.onOpen).toHaveBeenLastCalledWith(STOPS[1]);
    openMenu("b");
    fireEvent.click(item("PlanningExecution:RecordActuals"));
    expect(on.onRecordActuals).toHaveBeenLastCalledWith(STOPS[1]);
    openMenu("b");
    fireEvent.click(item("PlanningExecution:AddStop"));
    expect(on.onAddStop).toHaveBeenCalledTimes(1);
  });

  it("disables moving past the ends", () => {
    show();
    openMenu("a");
    expect(isDisabled("Orders:MoveUp")).toBe(true);
    expect(isDisabled("Orders:MoveDown")).toBe(false);
  });

  it("disables moving down on the last stop", () => {
    show();
    openMenu("c");
    expect(isDisabled("Orders:MoveDown")).toBe(true);
    expect(isDisabled("Orders:MoveUp")).toBe(false);
  });

  it("disables moving a stop with actuals, naming why", () => {
    show({ stops: [STOPS[0], stop("b", 2, { actualArrival: iso(9) }), STOPS[2]] });
    openMenu("b");
    expect(isDisabled("Orders:MoveUp")).toBe(true);
    expect(isDisabled("Orders:MoveDown")).toBe(true);
    expect(item("Orders:MoveUp")).toHaveAttribute("title", "OpenTms.PlanningExecution:StopHasActuals");
  });

  it("Remove only for a manual stop, naming why not", () => {
    show({ stops: [stop("a", 1, { isManual: true }), stop("b", 2)] });
    openMenu("a");
    expect(isDisabled("PlanningExecution:RemoveStop")).toBe(false);
    fireEvent.click(item("PlanningExecution:RemoveStop"));
    expect(on.onRemove).toHaveBeenCalledTimes(1);
    openMenu("b");
    expect(isDisabled("PlanningExecution:RemoveStop")).toBe(true);
    expect(item("PlanningExecution:RemoveStop")).toHaveAttribute("title", "OpenTms.PlanningExecution:NotManualStop");
  });
});

describe("StopTimeline: drag to reorder", () => {
  const drag = (from: string, to: string, dataTransfer = transfer()) => {
    fireEvent.dragStart(grip(from)!, { dataTransfer });
    fireEvent.dragOver(row(to), { dataTransfer });
    fireEvent.drop(row(to), { dataTransfer });
  };

  it("a drop moves the stop to the target's position", () => {
    show();
    drag("a", "c");
    expect(on.onMove).toHaveBeenCalledTimes(1);
    expect(on.onMove).toHaveBeenCalledWith(STOPS[0], 3);
  });

  it("marks the row under the drag, until the drop", () => {
    show();
    const dataTransfer = transfer();
    fireEvent.dragStart(grip("a")!, { dataTransfer });
    fireEvent.dragOver(row("b"), { dataTransfer });
    expect(row("b").className).toContain("ring-brand");
    fireEvent.drop(row("b"), { dataTransfer });
    expect(row("b").className).not.toContain("ring-brand");
  });

  it("a drop on itself moves nothing", () => {
    show();
    drag("b", "b");
    expect(on.onMove).not.toHaveBeenCalled();
  });

  it("a foreign drag is not accepted and moves nothing", () => {
    show();
    const dataTransfer = transfer();
    dataTransfer.setData("application/x-opentms-legs", "{}");
    const prevented = !fireEvent.dragOver(row("b"), { dataTransfer });
    expect(prevented).toBe(false);
    fireEvent.drop(row("b"), { dataTransfer });
    expect(on.onMove).not.toHaveBeenCalled();
  });

  it("a stop with actuals has no grip, the others keep theirs", () => {
    show({ stops: [stop("a", 1, { actualArrival: iso(8) }), stop("b", 2, { actualDeparture: iso(9) }), STOPS[2]] });
    expect(grip("a")).toBeNull();
    expect(grip("b")).toBeNull();
    expect(grip("c")).not.toBeNull();
  });

  it("no grip and no move on a read-only trip", () => {
    show({ editable: false });
    expect(grip("a")).toBeNull();
    const dataTransfer = transfer();
    dataTransfer.setData("application/x-opentms-stop", "a");
    expect(fireEvent.dragOver(row("b"), { dataTransfer })).toBe(true);
    fireEvent.drop(row("b"), { dataTransfer });
    expect(on.onMove).not.toHaveBeenCalled();
  });
});

describe("StopTimeline: what a stop shows", () => {
  it("one tier per stop: the actual with its check, the estimate with its truck, the plan without an icon", () => {
    show({ stops: [stop("a", 1, { actualArrival: iso(8) }), stop("b", 2, { estimatedArrival: iso(10) }), stop("c", 3)] });
    expect(within(row("a")).getByRole("img", { name: "Planboard:Stops.Tier.actual" })).toBeInTheDocument();
    expect(within(row("b")).getByRole("img", { name: "Planboard:Stops.Tier.estimated" })).toBeInTheDocument();
    expect(within(row("c")).queryByRole("img", { name: /Planboard:Stops.Tier/ })).toBeNull();
  });

  it("the card: name, activity and city, orders once, the window", () => {
    const legs = [{ legId: "l1", orderNumber: "O-1", windowFrom: iso(8), windowTill: iso(10) }, { legId: "l2", orderNumber: "O-1" }, { legId: "l3", orderNumber: "O-2" }];
    show({ stops: [stop("a", 1, { city: "Tilburg", legs } as Partial<TripStopDto>)] });
    const card = within(row("a"));
    expect(card.getByText("Stop a")).toBeInTheDocument();
    expect(card.getByText("Load · Tilburg")).toBeInTheDocument();
    expect(card.getByText("O-1, O-2")).toBeInTheDocument();
    expect(row("a").textContent).toContain("PlanningExecution:Window:");
  });

  it("the state badge: late with its minutes", () => {
    show({ stops: [stop("a", 1, { estimatedArrival: iso(10, 30), legs: [{ legId: "l1", windowTill: iso(10) }] } as Partial<TripStopDto>)] });
    expect(within(row("a")).getByText("Planboard:Lists.LateMinutes")).toBeInTheDocument();
  });

  it("remarks: shown, and editable in place only on an editable trip", () => {
    show({ stops: [stop("a", 1, { remarks: "Ring first" })] });
    expect(within(row("a")).getByText("Ring first")).toBeInTheDocument();
    expect(within(row("a")).getByRole("button", { name: "Edit" })).toBeInTheDocument();
  });

  it("remarks are read-only on a read-only trip, and absent without any", () => {
    show({ editable: false, stops: [stop("a", 1, { remarks: "Ring first" }), stop("b", 2)] });
    expect(within(row("a")).getByText("Ring first")).toBeInTheDocument();
    expect(within(row("a")).queryByRole("button", { name: "Edit" })).toBeNull();
    expect(within(row("b")).queryByLabelText("PlanningExecution:Remarks")).toBeNull();
  });

  it("an edited remark commits", async () => {
    show({ stops: [stop("a", 1, { remarks: "Old" })] });
    fireEvent.click(within(row("a")).getByRole("button", { name: "Edit" }));
    const input = within(row("a")).getByRole("textbox", { name: "Edit" });
    fireEvent.change(input, { target: { value: "New" } });
    await act(async () => void fireEvent.keyDown(input, { key: "Enter" }));
    expect(on.onCommitRemarks).toHaveBeenCalledTimes(1);
    expect((on.onCommitRemarks.mock.calls as unknown[][])[0][1]).toBe("New");
  });
});

describe("StopTimeline: now", () => {
  it("before the first stop: above it, 'not started'", () => {
    show();
    const [marker] = nowMarkers();
    expect(nowMarkers()).toHaveLength(1);
    expect(marker.textContent).toContain("Planboard:Stops.NotStarted");
    expect(marker.compareDocumentPosition(row("a")) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
  });

  it("at a stop: inside its row", () => {
    vi.setSystemTime(new Date(2026, 10, 10, 11, 30));
    show();
    expect(nowMarkers()).toHaveLength(1);
    expect(row("b")).toContainElement(nowMarkers()[0]);
    expect(nowMarkers()[0].textContent).toContain("Planboard:Stops.Now");
  });

  it("between two stops: between their rows", () => {
    vi.setSystemTime(new Date(2026, 10, 10, 10, 30));
    show();
    const [marker] = nowMarkers();
    expect(row("a").compareDocumentPosition(marker) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
    expect(marker.compareDocumentPosition(row("b")) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
    expect(row("a")).not.toContainElement(marker);
    expect(row("b")).not.toContainElement(marker);
  });

  it("every expected time passed without actuals: on the last stop", () => {
    vi.setSystemTime(new Date(2026, 10, 10, 23, 0));
    show();
    expect(row("c")).toContainElement(nowMarkers()[0]);
  });

  it("none on a completed or a cancelled trip", () => {
    vi.setSystemTime(new Date(2026, 10, 10, 11, 30));
    const { unmount } = show({ trip: { id: "t1", status: 3 } as TransportTripDto });
    expect(nowMarkers()).toHaveLength(0);
    unmount();
    show({ trip: { id: "t1", status: 4 } as TransportTripDto });
    expect(nowMarkers()).toHaveLength(0);
  });

  it("none after an actual departure from the last stop", () => {
    show({ stops: [stop("a", 1, { actualDeparture: iso(5) })] });
    expect(nowMarkers()).toHaveLength(0);
  });

  it("shows the delay at the next stop, not when early", () => {
    vi.setSystemTime(new Date(2026, 10, 10, 9, 30));
    const late = show({ stops: [STOPS[0], stop("b", 2, { estimatedArrival: iso(11, 20) })] });
    expect(nowMarkers()[0].textContent).toContain("Planboard:Lists.LateMinutes");
    late.unmount();
    show({ stops: [STOPS[0], stop("b", 2, { estimatedArrival: iso(10, 40) })] });
    expect(nowMarkers()[0].textContent).toContain("Planboard:Lists.OnTime");
  });

  it("moves on at the full minute and every minute after, not before", () => {
    /* Stop a's stay ends at 10:00; the marker leaves its row after 10:00. */
    vi.setSystemTime(new Date(2026, 10, 10, 9, 59, 30));
    show({ stops: [stop("a", 1, { plannedArrival: iso(9), plannedDeparture: iso(10) }), stop("b", 2, { plannedArrival: iso(12), plannedDeparture: iso(13) })] });
    expect(row("a")).toContainElement(nowMarkers()[0]);
    const text = () => nowMarkers()[0].textContent ?? "";
    const before = text();

    act(() => void vi.advanceTimersByTime(29_000));
    expect(text()).toBe(before);

    /* At 10:00:00 sharp: the time changes, the stay still includes it. */
    act(() => void vi.advanceTimersByTime(1_000));
    expect(text()).not.toBe(before);
    expect(row("a")).toContainElement(nowMarkers()[0]);

    /* At 10:01:00: between the stops. */
    act(() => void vi.advanceTimersByTime(60_000));
    expect(row("a")).not.toContainElement(nowMarkers()[0]);
    expect(row("b")).not.toContainElement(nowMarkers()[0]);
  });

  it("stops its clock when it goes", () => {
    const { unmount } = show();
    expect(vi.getTimerCount()).toBeGreaterThan(0);
    act(() => void vi.advanceTimersByTime(60_000));
    unmount();
    expect(vi.getTimerCount()).toBe(0);
  });
});
