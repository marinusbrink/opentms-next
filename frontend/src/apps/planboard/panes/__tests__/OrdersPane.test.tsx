import { act, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { useEffect } from "react";
import { beforeAll, beforeEach, describe, expect, it, vi } from "vitest";

/* The Orders pane (design 74 §§6, 8): customer reference and internal remarks edited in the cell through Orders' narrow command, the
 * grip that drags the order onto a trip, the record opened with the way back. Risk class: Critical for the permission and feature
 * gates, High for the save. The grid is the transport tests' stand-in. */

const navigateMock = vi.fn();
const gridApi = { isDestroyed: () => false, applyServerSideTransaction: vi.fn() };
let grants: Record<string, boolean> = {};
let features: Record<string, string> = {};

vi.mock("@tanstack/react-router", () => ({ useNavigate: () => navigateMock, useLocation: () => ({ pathname: "/planboard", search: {} }) }));
vi.mock("@/lib/i18n/LocalizationProvider", () => {
  const t = (k: string) => k;
  return { useL: () => ({ t, culture: "en", isLoading: false }) };
});
vi.mock("@/lib/abp/queries", () => ({ useApplicationConfiguration: () => ({ data: { currentTenant: { id: "t-1" }, auth: { grantedPolicies: grants }, features: { values: features } } }) }));
vi.mock("@/lib/api/client", () => ({ apiClient: { GET: vi.fn(), POST: vi.fn(), PUT: vi.fn(), DELETE: vi.fn() } }));
vi.mock("@/components/ui/opentms-grid-lazy", async () => {
  const { MockOpenTmsGrid } = await import("@/apps/transport/__tests__/transport-test-mocks");
  return {
    LazyOpenTmsGrid: (props: Parameters<typeof MockOpenTmsGrid>[0]) => {
      const { onGridReady } = props as unknown as { onGridReady?: (api: unknown) => void };
      useEffect(() => onGridReady?.(gridApi), [onGridReady]);
      return <MockOpenTmsGrid {...props} />;
    },
  };
});
vi.mock("@/apps/planboard/filters", () => ({
  usePaneFilters: () => ({ filters: { range: { from: "2026-11-10", till: "2026-11-12" }, organizationUnitIds: ["ou-1"] }, isLoaded: true }),
}));
vi.mock("@/apps/planboard/panes/lists/PaneFilterBar", () => ({ PaneFilterBar: () => null }));
vi.mock("sonner", () => ({ toast: { success: vi.fn(), error: vi.fn(), warning: vi.fn() } }));

import { toast } from "sonner";
import OrdersPane from "@/apps/planboard/panes/OrdersPane";
import { fail, gridRows, stubGlobals } from "@/apps/transport/__tests__/transport-test-mocks";
import { calls, lastCall, GET, PUT, resetClient, routes } from "@/apps/transport/__tests__/test-setup-mocks";
import { DND_ORDER } from "@/apps/planboard/dnd";

const LIST = "/api/orders/planboard-order";
const FIELD = "/api/orders/planboard-order/{id}/field";
const ORDER = "/api/orders/planboard-order/{id}";
const TRANSPORT_ORDER = "/api/orders/transport-order/{id}";

const ORDER_ROW = { id: "o1", number: "O-1", concurrencyStamp: "s1", customerReference: "CR", internalRemarks: "IR" };

async function show() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } });
  render(
    <QueryClientProvider client={client}>
      <OrdersPane instanceId="orders-1" type="orders" />
    </QueryClientProvider>,
  );
  await screen.findByText("O-1");
}

const cell = (name: string) => document.querySelector<HTMLElement>(`[data-slot=mock-grid-row][data-id=o1] [data-cell=${name}]`)!;
const pencil = (name: string) => within(cell(name)).queryByRole("button", { name: "Edit" });
const edit = async (name: string, value: string) => {
  fireEvent.click(pencil(name)!);
  const input = within(cell(name)).getByRole("textbox", { name: "Edit" });
  fireEvent.change(input, { target: { value } });
  await act(async () => void fireEvent.keyDown(input, { key: "Enter" }));
};
const conflict = () => ({ data: undefined, error: { error: { message: "Stale" } }, response: new Response(null, { status: 409 }) });

beforeAll(stubGlobals);
beforeEach(() => {
  resetClient();
  navigateMock.mockReset();
  gridApi.applyServerSideTransaction.mockReset();
  vi.mocked(toast.error).mockReset();
  gridRows.current = [];
  grants = { "Orders.TransportOrders": true, "Orders.TransportOrders.Update": true, "PlanningExecution.TransportTrips.AssignLegs": true };
  features = { "Apps.Planboard": "true", "Apps.Planboard.Orders": "true", "Apps.Transport": "true", "Apps.Transport.Orders": "true" };
  routes.GET[LIST] = () => ({ rows: [ORDER_ROW], totalCount: 1, filteredCount: 1 });
  routes.PUT[FIELD] = (init) => {
    const body = init!.body as { field: number; value: string };
    return { concurrencyStamp: "s2", customerReference: body.field === 1 ? body.value : "CR", internalRemarks: body.field === 2 ? body.value : "IR" };
  };
});

describe("OrdersPane: the gates", () => {
  it("without the view permission: denied, and no list is read", () => {
    grants = {};
    const client = new QueryClient();
    render(
      <QueryClientProvider client={client}>
        <OrdersPane instanceId="orders-1" type="orders" />
      </QueryClientProvider>,
    );
    expect(screen.queryByTestId("mock-grid")).toBeNull();
    expect(calls(GET, LIST)).toHaveLength(0);
  });

  it("with everything: both cells editable", async () => {
    await show();
    expect(pencil("customerReference")).not.toBeNull();
    expect(pencil("internalRemarks")).not.toBeNull();
  });

  it("without TransportOrders.Update: read-only", async () => {
    grants = { "Orders.TransportOrders": true };
    await show();
    expect(pencil("customerReference")).toBeNull();
    expect(pencil("internalRemarks")).toBeNull();
    expect(cell("customerReference").textContent).toContain("CR");
  });

  it("with the Planboard's Orders feature off: read-only", async () => {
    features = { ...features, "Apps.Planboard.Orders": "false" };
    await show();
    expect(pencil("customerReference")).toBeNull();
  });

  it("with the Planboard feature off: read-only", async () => {
    features = { ...features, "Apps.Planboard": "false" };
    await show();
    expect(pencil("internalRemarks")).toBeNull();
  });

  it("the grip only with the right to assign, and it carries the order's id", async () => {
    await show();
    const grip = screen.getByRole("button", { name: "Planboard:Lists.DragOrderToTrip" });
    const dataTransfer = { setData: vi.fn(), effectAllowed: "none" };
    fireEvent.dragStart(grip, { dataTransfer });
    expect(dataTransfer.setData).toHaveBeenCalledWith(DND_ORDER, JSON.stringify({ orderId: "o1" }));
  });

  it("no grip without it", async () => {
    grants = { "Orders.TransportOrders": true };
    await show();
    expect(screen.queryByRole("button", { name: "Planboard:Lists.DragOrderToTrip" })).toBeNull();
  });
});

describe("OrdersPane: the list", () => {
  it("reads the board's range and units", async () => {
    await show();
    expect(lastCall(GET, LIST)!.params!.query).toMatchObject({ PlannedFrom: "2026-11-10", PlannedTo: "2026-11-12", OrganizationUnitIds: ["ou-1"] });
  });

  it("opening an order comes back to the Planboard", async () => {
    await show();
    fireEvent.click(screen.getByRole("button", { name: "open-row" }));
    const call = navigateMock.mock.calls[0][0] as { to: string; search: (previous: Record<string, unknown>) => Record<string, unknown> };
    expect(call.to).toBe("/transport/orders/o1");
    expect(call.search({})).toEqual({ returnTo: "/planboard" });
  });

  it("the row action too", async () => {
    await show();
    fireEvent.click(document.querySelector<HTMLElement>("[data-row-action=open]")!);
    const call = navigateMock.mock.calls[0][0] as { search: (previous: Record<string, unknown>) => Record<string, unknown> };
    expect(call.search({})).toEqual({ returnTo: "/planboard" });
  });
});

describe("OrdersPane: saving a cell", () => {
  it("customer reference: the narrow command with the row's stamp, patched from the answer", async () => {
    await show();
    await edit("customerReference", "NEW");
    expect(lastCall(PUT, FIELD)).toMatchObject({ params: { path: { id: "o1" } }, body: { field: 1, value: "NEW", concurrencyStamp: "s1" } });
    await waitFor(() => expect(gridApi.applyServerSideTransaction).toHaveBeenCalledTimes(1));
    expect(gridApi.applyServerSideTransaction).toHaveBeenCalledWith({ update: [{ ...ORDER_ROW, customerReference: "NEW", internalRemarks: "IR", concurrencyStamp: "s2" }] });
  });

  it("internal remarks: their own field", async () => {
    await show();
    await edit("internalRemarks", "NEW");
    expect(lastCall(PUT, FIELD)!.body).toMatchObject({ field: 2, value: "NEW" });
  });

  it("emptying sends null", async () => {
    await show();
    await edit("internalRemarks", "  ");
    expect(lastCall(PUT, FIELD)!.body).toMatchObject({ field: 2, value: null });
  });

  it("a 409 with the field unchanged retries once with the fresh stamp, without a word", async () => {
    routes.GET[ORDER] = () => ({ ...ORDER_ROW, concurrencyStamp: "fresh" });
    routes.PUT[FIELD] = (init) => ((init!.body as { concurrencyStamp: string }).concurrencyStamp === "fresh" ? { concurrencyStamp: "s3", customerReference: "NEW", internalRemarks: "IR" } : conflict());
    await show();
    await edit("customerReference", "NEW");
    await waitFor(() => expect(gridApi.applyServerSideTransaction).toHaveBeenCalledTimes(1));
    expect(calls(PUT, FIELD)).toHaveLength(2);
    expect(toast.error).not.toHaveBeenCalled();
  });

  it("on a tenant without Transport the retry works: it reads the Planboard's own endpoint, never the Transport order", async () => {
    features = { "Apps.Planboard": "true", "Apps.Planboard.Orders": "true", "Apps.Transport": "false", "Apps.Transport.Orders": "false" };
    routes.GET[TRANSPORT_ORDER] = () => fail("Feature disabled", 403);
    routes.GET[ORDER] = () => ({ ...ORDER_ROW, concurrencyStamp: "fresh" });
    routes.PUT[FIELD] = (init) => ((init!.body as { concurrencyStamp: string }).concurrencyStamp === "fresh" ? { concurrencyStamp: "s3", customerReference: "NEW", internalRemarks: "IR" } : conflict());
    await show();
    await edit("customerReference", "NEW");
    await waitFor(() => expect(gridApi.applyServerSideTransaction).toHaveBeenCalledTimes(1));
    expect(lastCall(GET, ORDER)).toMatchObject({ params: { path: { id: "o1" } } });
    expect(calls(GET, TRANSPORT_ORDER)).toHaveLength(0);
    expect(calls(PUT, FIELD)).toHaveLength(2);
    expect(toast.error).not.toHaveBeenCalled();
  });

  it("a stale retry whose read is refused goes to the toast: no patch, the old value back", async () => {
    routes.GET[ORDER] = () => fail("Refused read", 403);
    routes.PUT[FIELD] = () => conflict();
    await show();
    await edit("customerReference", "NEW");
    await waitFor(() => expect(toast.error).toHaveBeenCalledTimes(1));
    expect(calls(PUT, FIELD)).toHaveLength(1);
    expect(gridApi.applyServerSideTransaction).not.toHaveBeenCalled();
    await waitFor(() => expect(cell("customerReference").textContent).toContain("CR"));
  });

  it("a 409 with the field changed stands: the message, no patch, the old value back", async () => {
    routes.GET[ORDER] = () => ({ ...ORDER_ROW, concurrencyStamp: "fresh", customerReference: "SOMEONE ELSE" });
    routes.PUT[FIELD] = () => conflict();
    await show();
    await edit("customerReference", "NEW");
    await waitFor(() => expect(toast.error).toHaveBeenCalledTimes(1));
    expect(calls(PUT, FIELD)).toHaveLength(1);
    expect(gridApi.applyServerSideTransaction).not.toHaveBeenCalled();
    await waitFor(() => expect(cell("customerReference").textContent).toContain("CR"));
  });

  it("a change to the other field does not count: it retries", async () => {
    routes.GET[ORDER] = () => ({ ...ORDER_ROW, concurrencyStamp: "fresh", internalRemarks: "SOMEONE ELSE" });
    routes.PUT[FIELD] = (init) => ((init!.body as { concurrencyStamp: string }).concurrencyStamp === "fresh" ? { concurrencyStamp: "s3", customerReference: "NEW", internalRemarks: "SOMEONE ELSE" } : conflict());
    await show();
    await edit("customerReference", "NEW");
    await waitFor(() => expect(gridApi.applyServerSideTransaction).toHaveBeenCalledTimes(1));
    expect(gridApi.applyServerSideTransaction.mock.calls[0][0].update[0]).toMatchObject({ internalRemarks: "SOMEONE ELSE", concurrencyStamp: "s3" });
  });

  it("another refusal goes to the toast", async () => {
    routes.PUT[FIELD] = () => fail("Refused");
    await show();
    await edit("customerReference", "NEW");
    await waitFor(() => expect(toast.error).toHaveBeenCalledWith("Refused"));
  });
});
