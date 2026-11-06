import { act, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { beforeAll, beforeEach, describe, expect, it, vi } from "vitest";

/* Legs, orders and drivers dropped on the Scheduler (design 74 §§8–10): on a bar, on an empty cell, on a block; the type picker; a
 * drop that cannot proceed always says why. And the work after a command: the bar is patched from the answer, a refetch waits for
 * the calculation's notice, a notice during a command waits for its end. FullCalendar is replaced by a stand-in that hands out its
 * props; the commands run through the real hooks on the generated client. Risk class: High (planning). */

let grants: Record<string, boolean> = {};
vi.mock("@/lib/i18n/LocalizationProvider", () => {
  const t = (k: string) => k;
  return { useL: () => ({ t, culture: "en", isLoading: false }) };
});
vi.mock("@/lib/abp/queries", () => ({ useApplicationConfiguration: () => ({ data: { currentTenant: { id: "t-1" }, auth: { grantedPolicies: grants }, features: { values: {} } } }) }));
vi.mock("@/lib/api/client", () => ({ apiClient: { GET: vi.fn(), POST: vi.fn(), PUT: vi.fn(), DELETE: vi.fn() } }));
vi.mock("@/lib/realtime/record-updates", () => ({ DerivedValueStatus: { Pending: 1, UpToDate: 2, Failed: 3 } }));
vi.mock("@/lib/realtime/use-record-updates", () => ({ useDerivedValueStates: () => ({ data: states }), derivedValueStatesKey: (type: string) => ["states", type] }));
vi.mock("sonner", () => ({ toast: { success: vi.fn(), error: vi.fn(), warning: vi.fn(), info: vi.fn() } }));

const scheduler: { current: Record<string, never> | null; props: () => SchedulerProps } = { current: null, props: () => scheduler.current as unknown as SchedulerProps };
interface SchedulerProps {
  events: { id: string; data?: { bar: { concurrencyStamp?: string } } }[];
  onExternalDrop: (resourceId: string, start: Date, dataTransfer: DataTransfer, targetEventId: string | null) => void;
  onExternalDropMiss: () => void;
  acceptExternalDrag: (types: readonly string[]) => boolean;
  onEventMove: (eventId: string, start: Date, end: Date, fromResourceId: string, toResourceId: string) => void;
}
vi.mock("@/components/ui/resource-scheduler-lazy", () => ({
  LazyResourceScheduler: (props: Record<string, never>) => {
    scheduler.current = props;
    return <div data-testid="scheduler" />;
  },
}));

const live: Record<string, { onIds: (ids: string[], messages: unknown[]) => void; onResync?: () => void }> = {};
vi.mock("@/apps/planboard/live-updates", () => ({
  PLANBOARD_RECORD_TYPES: { trip: "trip", resourceAvailability: "availability" },
  usePlanboardLiveUpdates: (options: { targetType: string; onIds: (ids: string[], messages: unknown[]) => void }) => {
    live[options.targetType] = options;
  },
}));
vi.mock("@/apps/planboard/filters", () => ({ usePaneFilters: () => ({ filters: { range: { from: "2026-11-10", till: "2026-11-10" }, organizationUnitIds: [] }, isLoaded: true }) }));
vi.mock("@/apps/planboard/settings-store", () => ({ usePaneSettings: (_id: string, defaults: unknown) => ({ settings: defaults, update: vi.fn(), isLoaded: true }) }));
vi.mock("@/apps/planboard/panes/scheduler/SchedulerHeader", () => ({ SchedulerHeader: () => null }));
vi.mock("@/apps/planboard/trip-peek", () => ({ useOpenPlanboardTrip: () => vi.fn() }));
vi.mock("@/apps/planboard/panes/scheduler/CreateTripDialog", () => ({
  CreateTripDialog: (props: { resourceId: string; legIds?: string[]; orderId?: string; driverId?: string; onClose: () => void; onCreated: (change: unknown) => void }) => (
    <div data-testid="create-trip" data-resource={props.resourceId} data-legs={props.legIds?.join(",")} data-order={props.orderId} data-driver={props.driverId}>
      <button type="button" onClick={props.onClose}>
        picker-cancel
      </button>
      <button type="button" onClick={() => props.onCreated({ id: "new-trip", concurrencyStamp: "new-stamp", moved: [{ id: "l1" }], stuck: [] })}>
        picker-created
      </button>
    </div>
  ),
}));

import { toast } from "sonner";
import { DND_DRIVER, DND_LEGS, DND_ORDER, DND_TRIP } from "@/apps/planboard/dnd";
import { LEG_OUTSIDE_TRIP_WINDOW } from "@/apps/planboard/outside-window";
import SchedulerPane from "@/apps/planboard/panes/SchedulerPane";
import { PlanboardSelectionProvider, usePlanboardSelection } from "@/apps/planboard/selection";
import { fail, stubGlobals } from "@/apps/transport/__tests__/transport-test-mocks";
import { calls, GET, lastCall, POST, resetClient, routes } from "@/apps/transport/__tests__/test-setup-mocks";

const states = new Map<string, { status: number }>();

const PAGE = "/api/planning/planboard/scheduler";
const BARS = "/api/planning/planboard/scheduler-bars";
const ASSIGN_LEGS = "/api/planning/planboard/{id}/assign-legs";
const MOVE_LEGS = "/api/planning/transport-trip/{id}/move-legs";
const DRIVER = "/api/planning/planboard/{id}/driver";
const CREATE = "/api/planning/planboard/trip";
const RESCHEDULE = "/api/planning/planboard/{id}/reschedule";
const TRIP = "/api/planning/transport-trip/{id}";
const PREVIEW = "/api/planning/planboard/{id}/assign-order-preview";
const ASSIGN_ORDER = "/api/planning/planboard/{id}/assign-order";
const TYPES = "/api/planning/transport-trip-type/lookup";
const LEGS = "/api/planning/leg";

const VIEW = "PlanningExecution.TransportTrips";
const ALL = { [VIEW]: true, [`${VIEW}.Update`]: true, [`${VIEW}.Create`]: true, [`${VIEW}.AssignLegs`]: true };

const bar = (id: string, patch: Record<string, unknown> = {}) => ({ id, number: `TR-${id}`, status: 1, concurrencyStamp: `stamp-${id}`, plannedDeparture: "2026-11-10T08:00:00", plannedArrival: "2026-11-10T10:00:00", vehicleIds: ["r1"], driverIds: [], ...patch });
const SCHEDULE = {
  rows: [{ id: "r1", name: "Truck 1" }, { id: "r2", name: "Truck 2" }],
  bars: [bar("t1"), bar("t2", { status: 3, vehicleIds: ["r2"] })],
  blocks: [{ resourceId: "r2", from: "2026-11-10T12:00:00", till: "2026-11-10T14:00:00", unavailabilityId: "u1", reason: 1 }],
  names: {},
  totalRows: 2,
};

const CELL = new Date("2026-11-10T09:00:00");
const ON_BLOCK = new Date("2026-11-10T13:00:00");

function dataTransfer(mime: string, payload: unknown): DataTransfer {
  return { types: [mime], getData: (type: string) => (type === mime ? JSON.stringify(payload) : "") } as unknown as DataTransfer;
}
const legsDrag = (fromTripId?: string) => dataTransfer(DND_LEGS, { legIds: ["l1", "l2"], fromTripId });
const orderDrag = () => dataTransfer(DND_ORDER, { orderId: "o1" });
const driverDrag = () => dataTransfer(DND_DRIVER, { driverId: "d1" });

const selection: { current: ReturnType<typeof usePlanboardSelection> | null } = { current: null };
function SelectionProbe() {
  selection.current = usePlanboardSelection();
  return null;
}

async function show() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } });
  render(
    <QueryClientProvider client={client}>
      <PlanboardSelectionProvider>
        <SelectionProbe />
        <SchedulerPane instanceId="s1" type="scheduler" />
      </PlanboardSelectionProvider>
    </QueryClientProvider>,
  );
  await screen.findByTestId("scheduler");
  /* The trip types, which the pane's drop decides on. */
  if (grants[`${VIEW}.Create`]) await waitFor(() => expect(calls(GET, TYPES).length).toBeGreaterThanOrEqual(1));
  await act(async () => undefined);
}

const drop = async (resourceId: string, start: Date, transfer: DataTransfer, targetEventId: string | null = null) => {
  await act(async () => scheduler.props().onExternalDrop(resourceId, start, transfer, targetEventId));
};
const onBar = (tripId: string, rowId = "r1") => `${tripId}::${rowId}`;
const stampOf = (tripId: string) => scheduler.props().events.find((event) => event.id.startsWith(`${tripId}::`))?.data?.bar.concurrencyStamp;

const conflict = () => ({ data: undefined, error: { error: { message: "Stale" } }, response: new Response(null, { status: 409 }) });
const outsideWindow = () => ({ data: undefined, error: { error: { code: LEG_OUTSIDE_TRIP_WINDOW, message: "Outside the window" } }, response: new Response(null, { status: 403 }) });
const notice = (targetId: string, status: number | null, hints?: unknown) => ({ targetType: "trip", targetId, version: 1, status, hints });

const barReads = () => calls(GET, BARS);

beforeAll(stubGlobals);
beforeEach(() => {
  resetClient();
  sessionStorage.clear();
  states.clear();
  grants = { ...ALL };
  for (const fn of [toast.success, toast.error, toast.warning, toast.info]) vi.mocked(fn).mockReset();
  routes.GET[PAGE] = () => SCHEDULE;
  routes.GET[BARS] = (init) => ({ bars: ((init!.params!.query as { Ids: string[] }).Ids ?? []).map((id) => bar(id)), names: {} });
  routes.GET[TYPES] = () => [{ id: "tt-1", name: "Own fleet" }];
  routes.POST[ASSIGN_LEGS] = () => ({ id: "t1", concurrencyStamp: "after-assign" });
  routes.POST[MOVE_LEGS] = () => ({ id: "t1", concurrencyStamp: "after-move" });
  routes.POST[DRIVER] = () => ({ id: "t1", concurrencyStamp: "after-driver" });
  routes.POST[CREATE] = () => ({ id: "new-trip", concurrencyStamp: "new-stamp", moved: [{ id: "l1" }], stuck: [] });
  routes.POST[RESCHEDULE] = () => ({ id: "t1", concurrencyStamp: "after-move" });
  routes.GET[PREVIEW] = () => ({ movableCount: 1, legs: [{ id: "l1" }], stuck: [], pulledFrom: [] });
  routes.POST[ASSIGN_ORDER] = () => ({ id: "t1", concurrencyStamp: "after-order", moved: [{ id: "l1" }], stuck: [], pulledFrom: [] });
});

describe("SchedulerPane: what may be dragged", () => {
  it("every kind a drop can handle is let through; a trip's own grip and foreign drags are not", async () => {
    await show();
    const accept = scheduler.props().acceptExternalDrag;
    expect(accept([DND_LEGS])).toBe(true);
    expect(accept([DND_DRIVER])).toBe(true);
    expect(accept([DND_ORDER])).toBe(true);
    expect(accept([DND_TRIP])).toBe(false);
    expect(accept(["text/plain"])).toBe(false);
  });

  it("also without the permissions: the drop says why instead of a silent refusal", async () => {
    grants = { [VIEW]: true };
    await show();
    expect(scheduler.props().acceptExternalDrag([DND_LEGS])).toBe(true);
  });

  it("without the view permission there is no Scheduler", () => {
    grants = {};
    const client = new QueryClient();
    render(
      <QueryClientProvider client={client}>
        <PlanboardSelectionProvider>
          <SchedulerPane instanceId="s1" type="scheduler" />
        </PlanboardSelectionProvider>
      </QueryClientProvider>,
    );
    expect(screen.queryByTestId("scheduler")).toBeNull();
    expect(calls(GET, PAGE)).toHaveLength(0);
  });
});

describe("SchedulerPane: a drop that cannot proceed says why", () => {
  it("not over a row", async () => {
    await show();
    act(() => scheduler.props().onExternalDropMiss());
    expect(toast.error).toHaveBeenCalledWith("Planboard:Scheduler.DropNotOnRow");
  });

  it("a payload it does not understand", async () => {
    await show();
    await drop("r1", CELL, dataTransfer(DND_LEGS, { legIds: [] }));
    expect(toast.error).toHaveBeenCalledWith("Planboard:Scheduler.DropNotUnderstood");
    await drop("r1", CELL, dataTransfer(DND_TRIP, { tripId: "t1" }));
    expect(toast.error).toHaveBeenCalledTimes(2);
    expect(calls(POST, CREATE)).toHaveLength(0);
  });

  it("legs on a bar without the right to assign", async () => {
    grants = { ...ALL, [`${VIEW}.AssignLegs`]: false };
    await show();
    await drop("r1", CELL, legsDrag(), onBar("t1"));
    expect(toast.error).toHaveBeenCalledWith("Planboard:Scheduler.NoPermissionToAssign");
    expect(calls(POST, ASSIGN_LEGS)).toHaveLength(0);
  });

  it("an order on a bar without the right to assign", async () => {
    grants = { ...ALL, [`${VIEW}.AssignLegs`]: false };
    await show();
    await drop("r1", CELL, orderDrag(), onBar("t1"));
    expect(toast.error).toHaveBeenCalledWith("Planboard:Scheduler.NoPermissionToAssign");
    expect(calls(GET, PREVIEW)).toHaveLength(0);
  });

  it("a driver on a bar without the right to update", async () => {
    grants = { ...ALL, [`${VIEW}.Update`]: false };
    await show();
    await drop("r1", CELL, driverDrag(), onBar("t1"));
    expect(toast.error).toHaveBeenCalledWith("Planboard:Scheduler.NoPermissionToUpdate");
    expect(calls(POST, DRIVER)).toHaveLength(0);
  });

  it("on a completed trip's bar", async () => {
    await show();
    await drop("r2", CELL, legsDrag(), onBar("t2", "r2"));
    expect(toast.error).toHaveBeenCalledWith("Planboard:Scheduler.TripReadOnly");
    expect(calls(POST, ASSIGN_LEGS)).toHaveLength(0);
  });

  it("on an empty cell without the right to create: legs and drivers each with their reason", async () => {
    grants = { ...ALL, [`${VIEW}.Create`]: false };
    await show();
    await drop("r1", CELL, legsDrag());
    expect(toast.error).toHaveBeenLastCalledWith("Planboard:Scheduler.NoPermissionToCreate");
    await drop("r1", CELL, driverDrag());
    expect(toast.error).toHaveBeenLastCalledWith("Planboard:Scheduler.DropDriverOnBar");
    expect(calls(POST, CREATE)).toHaveLength(0);
  });

  it("legs on an empty cell without the right to assign", async () => {
    grants = { ...ALL, [`${VIEW}.AssignLegs`]: false };
    await show();
    await drop("r1", CELL, legsDrag());
    expect(toast.error).toHaveBeenCalledWith("Planboard:Scheduler.NoPermissionToAssign");
    expect(calls(POST, CREATE)).toHaveLength(0);
  });

  it("legs of a trip on an empty cell: a new trip takes unplanned legs only", async () => {
    await show();
    await drop("r1", CELL, legsDrag("t9"));
    expect(toast.error).toHaveBeenCalledWith("Planboard:Scheduler.PlannedLegsNeedBar");
    expect(calls(POST, CREATE)).toHaveLength(0);
  });

  it("legs on the trip they already are on", async () => {
    await show();
    await drop("r1", CELL, legsDrag("T1"), onBar("t1"));
    expect(toast.info).toHaveBeenCalledWith("Planboard:Scheduler.LegsAlreadyOnTrip");
    expect(calls(POST, MOVE_LEGS)).toHaveLength(0);
  });
});

describe("SchedulerPane: on a bar", () => {
  it("legs: assigned with the bar's stamp; the bar takes the answer's stamp and is not refetched", async () => {
    await show();
    await drop("r1", CELL, legsDrag(), onBar("t1"));
    expect(lastCall(POST, ASSIGN_LEGS)).toMatchObject({ params: { path: { id: "t1" } }, body: { legIds: ["l1", "l2"], concurrencyStamp: "stamp-t1", allowOutsideWindow: false } });
    expect(toast.success).toHaveBeenCalledWith("Planboard:Scheduler.LegsAssigned");
    await waitFor(() => expect(stampOf("t1")).toBe("after-assign"));
    expect(barReads()).toHaveLength(0);
    expect(calls(POST, CREATE)).toHaveLength(0);
  });

  it("a stale stamp is retried once with the fresh one, without a word", async () => {
    routes.GET[TRIP] = () => ({ id: "t1", concurrencyStamp: "fresh" });
    routes.POST[ASSIGN_LEGS] = (init) => ((init!.body as { concurrencyStamp: string }).concurrencyStamp === "fresh" ? { id: "t1", concurrencyStamp: "after-assign" } : conflict());
    await show();
    await drop("r1", CELL, legsDrag(), onBar("t1"));
    expect(calls(POST, ASSIGN_LEGS).map(([, init]) => (init!.body as { concurrencyStamp: string }).concurrencyStamp)).toEqual(["stamp-t1", "fresh"]);
    expect(toast.error).not.toHaveBeenCalled();
    expect(toast.success).toHaveBeenCalledTimes(1);
  });

  it("stale on the retry too: the message, and the bar is read again", async () => {
    routes.GET[TRIP] = () => ({ id: "t1", concurrencyStamp: "fresh" });
    routes.POST[ASSIGN_LEGS] = () => conflict();
    await show();
    await drop("r1", CELL, legsDrag(), onBar("t1"));
    expect(calls(POST, ASSIGN_LEGS)).toHaveLength(2);
    expect(toast.error).toHaveBeenCalledTimes(1);
    expect(barReads()).toHaveLength(1);
  });

  it("outside the trip's window: asks; confirming retries with allowOutsideWindow", async () => {
    routes.POST[ASSIGN_LEGS] = (init) => ((init!.body as { allowOutsideWindow: boolean }).allowOutsideWindow ? { id: "t1", concurrencyStamp: "after" } : outsideWindow());
    await show();
    await drop("r1", CELL, legsDrag(), onBar("t1"));
    const dialog = await screen.findByRole("alertdialog");
    expect(dialog.textContent).toContain("Outside the window");
    const confirm = Array.from(dialog.querySelectorAll("button")).find((button) => button.textContent !== "Cancel")!;
    await act(async () => void fireEvent.click(confirm));
    await waitFor(() => expect(toast.success).toHaveBeenCalledWith("Planboard:Scheduler.LegsAssigned"));
    expect(lastCall(POST, ASSIGN_LEGS)!.body).toMatchObject({ allowOutsideWindow: true });
  });

  it("outside the window, cancelled: nothing more and no toast", async () => {
    routes.POST[ASSIGN_LEGS] = () => outsideWindow();
    await show();
    await drop("r1", CELL, legsDrag(), onBar("t1"));
    await screen.findByRole("alertdialog");
    await act(async () => void fireEvent.click(screen.getByRole("button", { name: "Cancel" })));
    await waitFor(() => expect(screen.queryByRole("alertdialog")).toBeNull());
    expect(calls(POST, ASSIGN_LEGS)).toHaveLength(1);
    expect(toast.success).not.toHaveBeenCalled();
    expect(toast.error).not.toHaveBeenCalled();
  });

  it("legs from another trip move, they are not assigned", async () => {
    await show();
    await drop("r1", CELL, legsDrag("t9"), onBar("t1"));
    expect(lastCall(POST, MOVE_LEGS)).toMatchObject({ params: { path: { id: "t9" } }, body: { toTripId: "t1", legIds: ["l1", "l2"] } });
    expect(calls(POST, ASSIGN_LEGS)).toHaveLength(0);
    expect(toast.success).toHaveBeenCalledWith("Planboard:Scheduler.LegsAssigned");
  });

  it("a refused assign shows the reason and reads the bar again", async () => {
    routes.POST[ASSIGN_LEGS] = () => fail("Trip is locked");
    await show();
    await drop("r1", CELL, legsDrag(), onBar("t1"));
    expect(toast.error).toHaveBeenCalledWith("Trip is locked");
    expect(barReads()).toHaveLength(1);
  });

  it("a driver: added with the bar's stamp", async () => {
    await show();
    await drop("r1", CELL, driverDrag(), onBar("t1"));
    expect(lastCall(POST, DRIVER)).toMatchObject({ params: { path: { id: "t1" } }, body: { driverId: "d1", concurrencyStamp: "stamp-t1" } });
    expect(toast.success).toHaveBeenCalledWith("Planboard:Scheduler.DriverAdded");
    await waitFor(() => expect(stampOf("t1")).toBe("after-driver"));
  });

  it("an order: through its flow, with the bar's stamp", async () => {
    await show();
    await drop("r1", CELL, orderDrag(), onBar("t1"));
    await waitFor(() => expect(calls(POST, ASSIGN_ORDER)).toHaveLength(1));
    expect(lastCall(POST, ASSIGN_ORDER)!.body).toMatchObject({ orderId: "o1", concurrencyStamp: "stamp-t1" });
    await waitFor(() => expect(stampOf("t1")).toBe("after-order"));
    expect(barReads()).toHaveLength(0);
  });

  it("an order whose legs leave other trips: those bars are read, their stamps moved", async () => {
    routes.GET[PREVIEW] = () => ({ movableCount: 1, legs: [{ id: "l1" }], pulledFrom: [{ tripId: "t5", number: "TR-5", legCount: 1 }] });
    routes.POST[ASSIGN_ORDER] = () => ({ id: "t1", concurrencyStamp: "after", moved: [{ id: "l1" }], stuck: [], pulledFrom: [{ tripId: "t5", number: "TR-5", legCount: 1 }] });
    await show();
    await drop("r1", CELL, orderDrag(), onBar("t1"));
    await screen.findByRole("alertdialog");
    await act(async () => void fireEvent.click(screen.getByRole("button", { name: "Planboard:OrderDrop.ConfirmLabel" })));
    await waitFor(() => expect(barReads()).toHaveLength(1));
    expect(barReads()[0][1]!.params!.query).toMatchObject({ Ids: ["t5"] });
  });

  it("an order with nothing movable: the warning, and no assign", async () => {
    routes.GET[PREVIEW] = () => ({ movableCount: 0, legs: [], stuck: [] });
    await show();
    await drop("r1", CELL, orderDrag(), onBar("t1"));
    await waitFor(() => expect(toast.warning).toHaveBeenCalledTimes(1));
    expect(calls(POST, ASSIGN_ORDER)).toHaveLength(0);
  });
});

describe("SchedulerPane: a bar the pane does not have (design 74 §10)", () => {
  it("is fetched by id and assigned; it never falls through to creating a trip", async () => {
    routes.POST[ASSIGN_LEGS] = () => ({ id: "t7", concurrencyStamp: "after" });
    await show();
    await drop("r1", CELL, legsDrag(), onBar("t7"));
    expect(barReads()[0][1]!.params!.query).toMatchObject({ Ids: ["t7"] });
    expect(lastCall(POST, ASSIGN_LEGS)).toMatchObject({ params: { path: { id: "t7" } }, body: { concurrencyStamp: "stamp-t7" } });
    expect(calls(POST, CREATE)).toHaveLength(0);
  });

  it("a trip that is gone says so and refreshes the page", async () => {
    routes.GET[BARS] = () => ({ bars: [], names: {} });
    await show();
    const pageReads = calls(GET, PAGE).length;
    await drop("r1", CELL, legsDrag(), onBar("t7"));
    expect(toast.error).toHaveBeenCalledWith("Planboard:Scheduler.TripGone");
    await waitFor(() => expect(calls(GET, PAGE).length).toBe(pageReads + 1));
    expect(calls(POST, CREATE)).toHaveLength(0);
    expect(calls(POST, ASSIGN_LEGS)).toHaveLength(0);
  });

  it("a failing read says so, and creates nothing", async () => {
    routes.GET[BARS] = () => fail("Bars refused");
    await show();
    await drop("r1", CELL, legsDrag(), onBar("t7"));
    expect(toast.error).toHaveBeenCalledTimes(1);
    expect(calls(POST, CREATE)).toHaveLength(0);
    expect(calls(POST, ASSIGN_LEGS)).toHaveLength(0);
  });

  it("a completed trip fetched this way is read-only too", async () => {
    routes.GET[BARS] = () => ({ bars: [bar("t7", { status: 3 })], names: {} });
    await show();
    await drop("r1", CELL, legsDrag(), onBar("t7"));
    expect(toast.error).toHaveBeenCalledWith("Planboard:Scheduler.TripReadOnly");
  });
});

describe("SchedulerPane: on an empty cell", () => {
  it("legs with one trip type: a new trip there, no prompt; its bar is read and selected", async () => {
    await show();
    await drop("r1", CELL, legsDrag());
    expect(screen.queryByTestId("create-trip")).toBeNull();
    expect(lastCall(POST, CREATE)!.body).toMatchObject({ transportTripTypeId: "tt-1", mode: expect.anything(), resourceId: "r1", plannedDeparture: CELL.toISOString(), legIds: ["l1", "l2"] });
    expect(toast.success).toHaveBeenCalledWith("Planboard:Scheduler.TripCreatedWithLegs");
    await waitFor(() => expect(barReads()).toHaveLength(1));
    expect(barReads()[0][1]!.params!.query).toMatchObject({ Ids: ["new-trip"] });
    expect(selection.current!.selectedTripIds).toEqual(["new-trip"]);
  });

  it("no trip type: the server says what is missing", async () => {
    routes.GET[TYPES] = () => [];
    routes.POST[CREATE] = () => fail("No trip type");
    await show();
    await drop("r1", CELL, legsDrag());
    expect(lastCall(POST, CREATE)!.body).toMatchObject({ transportTripTypeId: null });
    expect(toast.error).toHaveBeenCalledWith("No trip type");
  });

  it("more than one type: the picker asks, nothing is created yet", async () => {
    routes.GET[TYPES] = () => [{ id: "tt-1", name: "A" }, { id: "tt-2", name: "B" }];
    await show();
    await waitFor(() => expect(calls(GET, TYPES).length).toBeGreaterThan(0));
    await act(async () => undefined);
    await drop("r1", CELL, legsDrag());
    const picker = screen.getByTestId("create-trip");
    expect(picker).toHaveAttribute("data-resource", "r1");
    expect(picker).toHaveAttribute("data-legs", "l1,l2");
    expect(calls(POST, CREATE)).toHaveLength(0);
  });

  it("the type picker's cancel undoes the drop: nothing created, nothing said", async () => {
    routes.GET[TYPES] = () => [{ id: "tt-1", name: "A" }, { id: "tt-2", name: "B" }];
    await show();
    await drop("r1", CELL, legsDrag());
    fireEvent.click(screen.getByRole("button", { name: "picker-cancel" }));
    expect(screen.queryByTestId("create-trip")).toBeNull();
    expect(calls(POST, CREATE)).toHaveLength(0);
    expect(toast.error).not.toHaveBeenCalled();
    expect(toast.success).not.toHaveBeenCalled();
  });

  it("the picker's created: the bar is read and selected", async () => {
    routes.GET[TYPES] = () => [{ id: "tt-1", name: "A" }, { id: "tt-2", name: "B" }];
    await show();
    await drop("r1", CELL, legsDrag());
    await act(async () => void fireEvent.click(screen.getByRole("button", { name: "picker-created" })));
    await waitFor(() => expect(barReads()).toHaveLength(1));
    await waitFor(() => expect(selection.current!.selectedTripIds).toEqual(["new-trip"]));
  });

  it("before the list of types is known the picker asks: it does not guess", async () => {
    routes.GET[TYPES] = () => new Promise(() => undefined);
    const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    render(
      <QueryClientProvider client={client}>
        <PlanboardSelectionProvider>
          <SchedulerPane instanceId="s1" type="scheduler" />
        </PlanboardSelectionProvider>
      </QueryClientProvider>,
    );
    await screen.findByTestId("scheduler");
    await drop("r1", CELL, legsDrag());
    expect(screen.getByTestId("create-trip")).toBeInTheDocument();
    expect(calls(POST, CREATE)).toHaveLength(0);
  });

  it("a driver: the create dialog with that driver, whatever the number of types", async () => {
    await show();
    await drop("r1", CELL, driverDrag());
    expect(screen.getByTestId("create-trip")).toHaveAttribute("data-driver", "d1");
    expect(calls(POST, CREATE)).toHaveLength(0);
  });

  it("an order: its legs checked first, then a new trip with the order", async () => {
    routes.GET[LEGS] = () => ({ rows: [{ id: "l1" }], totalCount: 1, filteredCount: 1 });
    await show();
    await drop("r1", CELL, orderDrag());
    await waitFor(() => expect(calls(POST, CREATE)).toHaveLength(1));
    expect(lastCall(POST, CREATE)!.body).toMatchObject({ orderId: "o1", resourceId: "r1" });
    await waitFor(() => expect(toast.success).toHaveBeenCalled());
  });

  it("an order with nothing movable: the warning, no trip", async () => {
    routes.GET[LEGS] = () => ({ rows: [], totalCount: 0, filteredCount: 0 });
    await show();
    await drop("r1", CELL, orderDrag());
    await waitFor(() => expect(toast.warning).toHaveBeenCalledTimes(1));
    expect(calls(POST, CREATE)).toHaveLength(0);
  });
});

describe("SchedulerPane: on an availability block", () => {
  it("warns first and creates nothing yet", async () => {
    await show();
    await drop("r2", ON_BLOCK, legsDrag());
    expect(await screen.findByRole("alertdialog")).toHaveTextContent("Planboard:Scheduler.ConfirmBlockTitle");
    expect(calls(POST, CREATE)).toHaveLength(0);
  });

  it("confirming goes on as on an empty cell", async () => {
    await show();
    await drop("r2", ON_BLOCK, legsDrag());
    await screen.findByRole("alertdialog");
    await act(async () => void fireEvent.click(screen.getByRole("button", { name: "Planboard:Scheduler.CreateAnyway" })));
    await waitFor(() => expect(calls(POST, CREATE)).toHaveLength(1));
    expect(lastCall(POST, CREATE)!.body).toMatchObject({ resourceId: "r2", plannedDeparture: ON_BLOCK.toISOString(), legIds: ["l1", "l2"] });
  });

  it("cancelling undoes the drop", async () => {
    await show();
    await drop("r2", ON_BLOCK, legsDrag());
    await screen.findByRole("alertdialog");
    await act(async () => void fireEvent.click(screen.getByRole("button", { name: "Cancel" })));
    await waitFor(() => expect(screen.queryByRole("alertdialog")).toBeNull());
    expect(calls(POST, CREATE)).toHaveLength(0);
  });

  it("the same time on another row is no block", async () => {
    await show();
    await drop("r1", ON_BLOCK, legsDrag());
    expect(screen.queryByRole("alertdialog")).toBeNull();
    expect(calls(POST, CREATE)).toHaveLength(1);
  });

  it("a bar over a block is still a bar: no warning", async () => {
    await show();
    await drop("r2", ON_BLOCK, legsDrag(), onBar("t1"));
    expect(screen.queryByRole("alertdialog")).toBeNull();
    expect(calls(POST, ASSIGN_LEGS)).toHaveLength(1);
  });
});

describe("SchedulerPane: the work after a command (design 74 §9)", () => {
  const notify = async (...messages: unknown[]) => {
    await act(async () => live.trip.onIds([], messages));
  };

  it("a Pending notice does not read the bar", async () => {
    await show();
    await notify(notice("t1", 1));
    expect(barReads()).toHaveLength(0);
  });

  it("the notice that ends the calculation does, once", async () => {
    await show();
    await notify(notice("T1", 2));
    expect(barReads()).toHaveLength(1);
    expect(barReads()[0][1]!.params!.query).toMatchObject({ Ids: ["t1"] });
  });

  it("a failed calculation does too", async () => {
    await show();
    await notify(notice("t1", 3));
    expect(barReads()).toHaveLength(1);
  });

  it("a plain notice does, unless that trip is calculating", async () => {
    await show();
    await notify(notice("t1", null));
    expect(barReads()).toHaveLength(1);
    states.set("t1", { status: 1 });
    await notify(notice("t1", null));
    expect(barReads()).toHaveLength(1);
  });

  it("a removed trip's notice always does", async () => {
    await show();
    await notify(notice("t1", 1, { removed: true }));
    expect(barReads()).toHaveLength(1);
  });

  it("a notice during a command waits for its end, and is then read once", async () => {
    let release: (value: unknown) => void = () => undefined;
    routes.POST[ASSIGN_LEGS] = () => new Promise((resolve) => (release = resolve));
    await show();
    await drop("r1", CELL, legsDrag(), onBar("t1"));
    await notify(notice("t1", 2));
    expect(barReads()).toHaveLength(0);
    expect(calls(POST, ASSIGN_LEGS)).toHaveLength(1);

    await act(async () => release({ id: "t1", concurrencyStamp: "after-assign" }));
    await waitFor(() => expect(barReads()).toHaveLength(1));
    expect(barReads()[0][1]!.params!.query).toMatchObject({ Ids: ["t1"] });
  });

  it("a notice for another trip during a command does not wait", async () => {
    let release: (value: unknown) => void = () => undefined;
    routes.POST[ASSIGN_LEGS] = () => new Promise((resolve) => (release = resolve));
    await show();
    await drop("r1", CELL, legsDrag(), onBar("t1"));
    await notify(notice("t2", 2));
    expect(barReads()).toHaveLength(1);
    await act(async () => release({ id: "t1", concurrencyStamp: "after-assign" }));
  });

  it("a notice during a move waits too, and a move that succeeds reads nothing itself", async () => {
    let release: (value: unknown) => void = () => undefined;
    routes.POST[RESCHEDULE] = () => new Promise((resolve) => (release = resolve));
    await show();
    await act(async () => scheduler.props().onEventMove(onBar("t1"), new Date("2026-11-10T20:00:00"), new Date("2026-11-10T22:00:00"), "r1", "r1"));
    await waitFor(() => expect(calls(POST, RESCHEDULE)).toHaveLength(1));
    await notify(notice("t1", 2));
    expect(barReads()).toHaveLength(0);
    await act(async () => release({ id: "t1", concurrencyStamp: "after-move" }));
    await waitFor(() => expect(barReads()).toHaveLength(1));
    await waitFor(() => expect(stampOf("t1")).not.toBeUndefined());
  });

  it("a move without a notice reads nothing: the bar shows the answer's stamp", async () => {
    await show();
    await act(async () => scheduler.props().onEventMove(onBar("t1"), new Date("2026-11-10T20:00:00"), new Date("2026-11-10T22:00:00"), "r1", "r1"));
    await waitFor(() => expect(stampOf("t1")).toBe("after-move"));
    expect(barReads()).toHaveLength(0);
  });

  it("a refused move goes back, says why and reads the bar", async () => {
    routes.POST[RESCHEDULE] = () => fail("Refused");
    await show();
    await act(async () => scheduler.props().onEventMove(onBar("t1"), new Date("2026-11-10T20:00:00"), new Date("2026-11-10T22:00:00"), "r1", "r1"));
    await waitFor(() => expect(toast.error).toHaveBeenCalledWith("Refused"));
    await waitFor(() => expect(barReads()).toHaveLength(1));
  });

  it("a notice about availability refreshes the page, not a bar", async () => {
    await show();
    const pageReads = calls(GET, PAGE).length;
    await act(async () => live.availability.onIds([], []));
    await waitFor(() => expect(calls(GET, PAGE).length).toBe(pageReads + 1));
    expect(barReads()).toHaveLength(0);
  });
});
