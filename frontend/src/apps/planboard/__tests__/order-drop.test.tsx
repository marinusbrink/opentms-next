import { act, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { useState } from "react";
import { beforeAll, beforeEach, describe, expect, it, vi } from "vitest";

/* An order dropped on a trip (design 74 §8), the one flow of every drop target: nothing movable says why without a call; legs that
 * would leave other trips ask once; the assign goes through the outside-window flow with a stale stamp retried once; the toasts
 * name what moved and what stayed. Risk class: High (planning). */

vi.mock("@/lib/i18n/LocalizationProvider", () => ({ useL: () => ({ t: (k: string) => (k.startsWith("Planboard:OrderDrop") ? `${k}[{0}|{1}]` : k), culture: "en", isLoading: false }) }));
vi.mock("@/lib/api/client", () => ({ apiClient: { GET: vi.fn(), POST: vi.fn(), PUT: vi.fn(), DELETE: vi.fn() } }));
vi.mock("sonner", () => ({ toast: { success: vi.fn(), error: vi.fn(), warning: vi.fn(), info: vi.fn() } }));

import { toast } from "sonner";
import { useOrderDrop, type OrderDropOnTrip } from "@/apps/planboard/order-drop";
import { LEG_OUTSIDE_TRIP_WINDOW } from "@/apps/planboard/outside-window";
import { fail, stubGlobals } from "@/apps/transport/__tests__/transport-test-mocks";
import { calls, GET, lastCall, POST, resetClient, routes } from "@/apps/transport/__tests__/test-setup-mocks";
import { PLANBOARD_STUCK_REASON } from "@/domains/planning-execution/planboard-lists";

const PREVIEW = "/api/planning/planboard/{id}/assign-order-preview";
const ASSIGN = "/api/planning/planboard/{id}/assign-order";
const TRIP = "/api/planning/transport-trip/{id}";
const LEGS = "/api/planning/leg";

const TARGET: OrderDropOnTrip = { orderId: "o1", tripId: "t1", tripNumber: "TR-1", stamp: "s1" };

const result = { current: undefined as unknown };

function Harness({ target = TARGET }: { target?: OrderDropOnTrip }) {
  const drop = useOrderDrop();
  const [, setTick] = useState(0);
  return (
    <>
      <button
        type="button"
        onClick={() => {
          void drop.dropOnTrip(target).then((value) => {
            result.current = value;
            setTick((n) => n + 1);
          });
        }}
      >
        drop
      </button>
      <button
        type="button"
        onClick={() => {
          void drop.prepareNewTrip("o1").then((value) => {
            result.current = value;
            setTick((n) => n + 1);
          });
        }}
      >
        prepare
      </button>
      {drop.dialog}
    </>
  );
}

function show() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } });
  render(
    <QueryClientProvider client={client}>
      <Harness />
    </QueryClientProvider>,
  );
}

const click = async (name: string) => {
  await act(async () => void fireEvent.click(screen.getByRole("button", { name })));
};
/** The whole Promise chain settled: `result.current` holds the flow's outcome. */
const settled = (value: unknown) => waitFor(() => expect(result.current).toEqual(value));
const CONFIRM = /Planboard:OrderDrop.ConfirmLabel/;

const conflict = () => ({ data: undefined, error: { error: { message: "Stale" } }, response: new Response(null, { status: 409 }) });
const outsideWindow = () => ({ data: undefined, error: { error: { code: LEG_OUTSIDE_TRIP_WINDOW, message: "Outside the window 08:00-12:00" } }, response: new Response(null, { status: 403 }) });

beforeAll(stubGlobals);
beforeEach(() => {
  resetClient();
  result.current = "pending";
  for (const fn of [toast.success, toast.error, toast.warning]) vi.mocked(fn).mockReset();
  routes.GET[PREVIEW] = () => ({ movableCount: 2, legs: [{ id: "l1" }, { id: "l2" }], stuck: [], pulledFrom: [] });
  routes.POST[ASSIGN] = () => ({ id: "t1", concurrencyStamp: "s2", moved: [{ id: "l1" }, { id: "l2" }], stuck: [], pulledFrom: [] });
});

describe("useOrderDrop: dropOnTrip", () => {
  it("nothing to pull: no dialog, the assign with the caller's stamp, the toast with the count", async () => {
    show();
    await click("drop");
    await settled({ id: "t1", concurrencyStamp: "s2", moved: [{ id: "l1" }, { id: "l2" }], stuck: [], pulledFrom: [] });
    expect(screen.queryByRole("alertdialog")).toBeNull();
    expect(lastCall(POST, ASSIGN)).toMatchObject({ params: { path: { id: "t1" } }, body: { orderId: "o1", allowOutsideWindow: false, concurrencyStamp: "s1" } });
    expect(lastCall(GET, PREVIEW)).toMatchObject({ params: { path: { id: "t1" }, query: { OrderId: "o1" } } });
    expect(toast.success).toHaveBeenCalledWith("Planboard:OrderDrop.LegsMoved[2|TR-1]");
    expect(toast.warning).not.toHaveBeenCalled();
  });

  it("legs that stayed are named in a warning after the toast", async () => {
    routes.POST[ASSIGN] = () => ({
      id: "t1",
      moved: [{ id: "l1" }],
      stuck: [
        { id: "l2", stuckReason: PLANBOARD_STUCK_REASON.TripFinal, originName: "A", destinationName: "B", tripNumber: "TR-9" },
        { id: "l3", stuckReason: PLANBOARD_STUCK_REASON.HasActuals, originName: "C", destinationName: "D" },
      ],
    });
    show();
    await click("drop");
    await waitFor(() => expect(toast.warning).toHaveBeenCalledTimes(1));
    const text = vi.mocked(toast.warning).mock.calls[0][0] as string;
    expect(text).toContain("A → B");
    expect(text).toContain("TR-9");
    expect(text).toContain("C → D");
    expect(toast.success).toHaveBeenCalledWith("Planboard:OrderDrop.LegsMoved[1|TR-1]");
  });

  it("nothing movable, all stuck: a warning that names them, and no call", async () => {
    routes.GET[PREVIEW] = () => ({ movableCount: 0, legs: [{ id: "l1" }], stuck: [{ id: "l1", stuckReason: PLANBOARD_STUCK_REASON.HasActuals, originName: "A", destinationName: "B" }] });
    show();
    await click("drop");
    await settled(null);
    expect(calls(POST, ASSIGN)).toHaveLength(0);
    const text = vi.mocked(toast.warning).mock.calls[0][0] as string;
    expect(text).toContain("Planboard:OrderDrop.AllLegsStuck");
    expect(text).toContain("A → B");
  });

  it("nothing movable because the legs already are on this trip: a warning, no call", async () => {
    routes.GET[PREVIEW] = () => ({ movableCount: 0, legs: [{ id: "l1" }], stuck: [] });
    show();
    await click("drop");
    await settled(null);
    expect(calls(POST, ASSIGN)).toHaveLength(0);
    expect(vi.mocked(toast.warning).mock.calls[0][0]).toContain("Planboard:OrderDrop.AlreadyOnTrip");
  });

  it("an order without legs: a warning, no call", async () => {
    routes.GET[PREVIEW] = () => ({ movableCount: 0, legs: [], stuck: [] });
    show();
    await click("drop");
    await settled(null);
    expect(calls(POST, ASSIGN)).toHaveLength(0);
    expect(vi.mocked(toast.warning).mock.calls[0][0]).toContain("Planboard:OrderDrop.NoLegs");
  });

  it("legs would leave other trips: one confirmation listing them; confirming assigns", async () => {
    routes.GET[PREVIEW] = () => ({ movableCount: 3, legs: [], pulledFrom: [{ tripId: "t2", number: "TR-2", legCount: 2 }, { tripId: "t3", number: "TR-3", legCount: 1 }] });
    show();
    await click("drop");
    const dialog = await screen.findByRole("alertdialog");
    expect(dialog.textContent).toContain("TR-1");
    expect(dialog.textContent).toContain("TR-2");
    expect(dialog.textContent).toContain("TR-3");
    expect(calls(POST, ASSIGN)).toHaveLength(0);

    await act(async () => void fireEvent.click(screen.getByRole("button", { name: CONFIRM })));
    await waitFor(() => expect(calls(POST, ASSIGN)).toHaveLength(1));
    await waitFor(() => expect(screen.queryByRole("alertdialog")).toBeNull());
    expect(toast.success).toHaveBeenCalledTimes(1);
  });

  it("cancelling the confirmation: no call, no toast", async () => {
    routes.GET[PREVIEW] = () => ({ movableCount: 1, legs: [], pulledFrom: [{ tripId: "t2", number: "TR-2", legCount: 1 }] });
    show();
    await click("drop");
    await screen.findByRole("alertdialog");
    await click("Cancel");
    await settled(null);
    expect(calls(POST, ASSIGN)).toHaveLength(0);
    expect(toast.success).not.toHaveBeenCalled();
    expect(toast.error).not.toHaveBeenCalled();
  });

  it("outside the window: asks with the server's text and retries with allowOutsideWindow", async () => {
    routes.POST[ASSIGN] = (init) => ((init!.body as { allowOutsideWindow: boolean }).allowOutsideWindow ? { id: "t1", moved: [{ id: "l1" }] } : outsideWindow());
    show();
    await click("drop");
    const dialog = await screen.findByRole("alertdialog");
    expect(dialog.textContent).toContain("Outside the window 08:00-12:00");
    const confirm = Array.from(dialog.querySelectorAll("button")).find((button) => button.textContent !== "Cancel")!;
    await act(async () => void fireEvent.click(confirm));
    await waitFor(() => expect(calls(POST, ASSIGN)).toHaveLength(2));
    expect(lastCall(POST, ASSIGN)!.body).toMatchObject({ allowOutsideWindow: true });
    await waitFor(() => expect(toast.success).toHaveBeenCalledWith("Planboard:OrderDrop.LegsMoved[1|TR-1]"));
  });

  it("outside the window, cancelled: nothing more, no toast", async () => {
    routes.POST[ASSIGN] = () => outsideWindow();
    show();
    await click("drop");
    await screen.findByRole("alertdialog");
    await click("Cancel");
    await settled(null);
    expect(calls(POST, ASSIGN)).toHaveLength(1);
    expect(toast.success).not.toHaveBeenCalled();
    expect(toast.error).not.toHaveBeenCalled();
  });

  it("a stale stamp: the trip's stamp is read afresh and the assign retried once, without a word", async () => {
    routes.GET[TRIP] = () => ({ id: "t1", concurrencyStamp: "fresh" });
    routes.POST[ASSIGN] = (init) => ((init!.body as { concurrencyStamp: string }).concurrencyStamp === "fresh" ? { id: "t1", moved: [{ id: "l1" }] } : conflict());
    show();
    await click("drop");
    await waitFor(() => expect(toast.success).toHaveBeenCalledTimes(1));
    expect(calls(POST, ASSIGN).map(([, init]) => (init!.body as { concurrencyStamp: string }).concurrencyStamp)).toEqual(["s1", "fresh"]);
    expect(toast.error).not.toHaveBeenCalled();
  });

  it("stale on the retry too: the 409 is shown", async () => {
    routes.GET[TRIP] = () => ({ id: "t1", concurrencyStamp: "fresh" });
    routes.POST[ASSIGN] = () => conflict();
    show();
    await click("drop");
    await settled(null);
    expect(calls(POST, ASSIGN)).toHaveLength(2);
    expect(toast.error).toHaveBeenCalledTimes(1);
    expect(toast.success).not.toHaveBeenCalled();
  });

  it("a failing preview or assign shows the error and returns nothing", async () => {
    routes.GET[PREVIEW] = () => fail("Preview refused");
    show();
    await click("drop");
    await settled(null);
    expect(toast.error).toHaveBeenCalledWith("Preview refused");
    expect(calls(POST, ASSIGN)).toHaveLength(0);
  });

  it("a refused assign shows the server's reason", async () => {
    routes.POST[ASSIGN] = () => fail("Order has no movable leg");
    show();
    await click("drop");
    await settled(null);
    expect(toast.error).toHaveBeenCalledWith("Order has no movable leg");
  });
});

describe("useOrderDrop: prepareNewTrip (an empty cell)", () => {
  const legsPage = (rows: unknown[]) => ({ rows, totalCount: rows.length, filteredCount: rows.length });

  it("asks the legs of that order", async () => {
    routes.GET[LEGS] = () => legsPage([{ id: "l1" }]);
    show();
    await click("prepare");
    await settled(true);
    expect((lastCall(GET, LEGS)!.params!.query as Record<string, unknown>).OrderId).toBe("o1");
    expect(screen.queryByRole("alertdialog")).toBeNull();
  });

  it("no legs: a warning and false", async () => {
    routes.GET[LEGS] = () => legsPage([]);
    show();
    await click("prepare");
    await settled(false);
    expect(vi.mocked(toast.warning).mock.calls[0][0]).toContain("Planboard:OrderDrop.NoLegs");
  });

  it("all on final trips: a warning naming them and false", async () => {
    routes.GET[LEGS] = () => legsPage([{ id: "l1", tripId: "t9", tripNumber: "TR-9", tripStatus: 3, origin: { name: "A" }, destination: { name: "B" } }]);
    show();
    await click("prepare");
    await settled(false);
    const text = vi.mocked(toast.warning).mock.calls[0][0] as string;
    expect(text).toContain("Planboard:OrderDrop.AllLegsStuck");
    expect(text).toContain("A → B");
    expect(text).toContain("TR-9");
  });

  it("legs on running trips: the confirmation for a new trip; true on confirm", async () => {
    routes.GET[LEGS] = () => legsPage([{ id: "l1", tripId: "t2", tripNumber: "TR-2", tripStatus: 1 }]);
    show();
    await click("prepare");
    const dialog = await screen.findByRole("alertdialog");
    expect(dialog.textContent).toContain("Planboard:OrderDrop.ConfirmTitleNewTrip");
    expect(dialog.textContent).toContain("TR-2");
    await act(async () => void fireEvent.click(screen.getByRole("button", { name: CONFIRM })));
    await settled(true);
  });

  it("false on cancel", async () => {
    routes.GET[LEGS] = () => legsPage([{ id: "l1", tripId: "t2", tripNumber: "TR-2", tripStatus: 1 }]);
    show();
    await click("prepare");
    await screen.findByRole("alertdialog");
    await click("Cancel");
    await settled(false);
  });

  it("a failing read shows the error and false", async () => {
    routes.GET[LEGS] = () => fail("Legs refused");
    show();
    await click("prepare");
    await settled(false);
    expect(toast.error).toHaveBeenCalledWith("Legs refused");
  });
});
