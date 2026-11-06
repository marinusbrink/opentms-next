import { act, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import type { ColDef } from "ag-grid-community";
import { useMemo, useState } from "react";
import { beforeAll, beforeEach, describe, expect, it, vi } from "vitest";

/* The columns Legs and Legs to schedule edit in the cell (design 74 §6): read-only per row on a completed or cancelled trip, per
 * column without the permission or the Planboard's Orders feature. Orders fields carry the order's stamp, which all rows of the
 * order share: every one of them is patched. A 409 retries once when the edited field is unchanged. Risk class: Critical for the
 * permission and feature gates (authorization, here in the UI; the server's are in the backend's tests), High for the rest. */

let grants: Record<string, boolean> = {};
let features: Record<string, string> = {};
/* A stable `t`, as the real provider's: the columns are memoized on it, and a new cell renderer would remount every cell. */
vi.mock("@/lib/i18n/LocalizationProvider", () => {
  const t = (k: string) => k;
  return { useL: () => ({ t, culture: "en", isLoading: false }) };
});
vi.mock("@/lib/abp/queries", () => ({ useApplicationConfiguration: () => ({ data: { currentTenant: { id: "t-1" }, auth: { grantedPolicies: grants }, features: { values: features } } }) }));
vi.mock("@/lib/api/client", () => ({ apiClient: { GET: vi.fn(), POST: vi.fn(), PUT: vi.fn(), DELETE: vi.fn() } }));
vi.mock("@/components/ui/date-picker", async (importOriginal) => ({ ...(await importOriginal<typeof import("@/components/ui/date-picker")>()), DatePicker: (await import("@/apps/transport/__tests__/transport-test-mocks")).MockDatePicker }));
vi.mock("@/components/ui/reference-combobox", async () => ({ ReferenceCombobox: (await import("@/apps/transport/__tests__/transport-test-mocks")).MockReferenceCombobox }));
vi.mock("sonner", () => ({ toast: { success: vi.fn(), error: vi.fn(), warning: vi.fn() } }));

import { toast } from "sonner";
import { legWindowText, useLegInlineColumns } from "@/apps/planboard/panes/lists/leg-columns";
import { fail, stubGlobals } from "@/apps/transport/__tests__/transport-test-mocks";
import { calls, GET, lastCall, PUT, resetClient, routes } from "@/apps/transport/__tests__/test-setup-mocks";
import type { LegDto } from "@/domains/planning-execution/legs";

const LEG_PUT = "/api/planning/leg/{id}";
const ORDER_FIELD = "/api/orders/planboard-order/{id}/field";
const LINE_FIELD = "/api/orders/planboard-order/{id}/line/{lineId}/field";

const leg = (id: string, patch: Partial<LegDto> = {}): LegDto =>
  ({
    id,
    concurrencyStamp: `leg-stamp-${id}`,
    orderId: "o1",
    orderLineId: "line-1",
    orderConcurrencyStamp: "order-stamp",
    customerReference: "CR",
    remarks: "Remark",
    transportUnitId: "u1",
    transportUnitName: "Pallet",
    productId: "p1",
    productName: "Apples",
    temperatureId: null,
    temperatureName: null,
    origin: { reference: "OR", plannedDate: "2026-11-10", windowFrom: null, windowTill: null },
    destination: { reference: "DR", plannedDate: "2026-11-11" },
    ...patch,
  }) as LegDto;

/* l1 and l2 on line-1 and l3 on line-2 of order o1; l4 on another order. */
const ROWS = () => [leg("l1"), leg("l2"), leg("l3", { orderLineId: "line-2", transportUnitName: "Box", transportUnitId: "u2" }), leg("l4", { orderId: "o2", orderLineId: "line-9", orderConcurrencyStamp: "other-stamp", customerReference: "Other" })];

let latest: LegDto[] = [];

function Harness({ initial }: { initial: LegDto[] }) {
  const [rows, setRows] = useState(initial);
  latest = rows;
  const columns = useLegInlineColumns<LegDto>({ patchRows: (match, patch) => setRows((current) => current.map((row) => (match(row) ? patch(row) : row))) });
  const defs = useMemo<ColDef<LegDto>[]>(() => [columns.originReference(), columns.destinationReference(), columns.originDate(), columns.destinationDate(), columns.originWindow(), columns.destinationWindow(), columns.remarks(), columns.customerReference(), columns.transportUnit(), columns.product(), columns.temperature()], [columns]);
  return (
    <>
      {rows.map((row) => (
        <div key={row.id} data-testid={`row-${row.id}`}>
          {defs.map((def) => {
            const Cell = def.cellRenderer as (props: { value: unknown; data: LegDto }) => React.ReactNode;
            const value = typeof def.valueGetter === "function" ? def.valueGetter({ data: row } as never) : undefined;
            return (
              <div key={def.colId} data-cell={def.colId}>
                <Cell value={value} data={row} />
              </div>
            );
          })}
        </div>
      ))}
    </>
  );
}

function show(rows: LegDto[] = ROWS()) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } });
  return render(
    <QueryClientProvider client={client}>
      <Harness initial={rows} />
    </QueryClientProvider>,
  );
}

const cell = (rowId: string, colId: string) => within(screen.getByTestId(`row-${rowId}`)).getByText((_, el) => el?.getAttribute("data-cell") === colId);
const pencil = (rowId: string, colId: string) => within(cell(rowId, colId)).queryByRole("button", { name: "Edit" });
const editable = (rowId: string) => ["originReference", "destinationReference", "originDate", "destinationDate", "originWindow", "destinationWindow", "remarks", "customerReference", "transportUnit", "product", "temperature"].filter((colId) => pencil(rowId, colId) != null);

const LEG_COLUMNS = ["originReference", "destinationReference", "originDate", "destinationDate", "originWindow", "destinationWindow", "remarks"];
const ORDER_COLUMNS = ["customerReference", "transportUnit", "product", "temperature"];

const editText = async (rowId: string, colId: string, value: string) => {
  fireEvent.click(pencil(rowId, colId)!);
  const input = within(cell(rowId, colId)).getByRole("textbox", { name: "Edit" });
  fireEvent.change(input, { target: { value } });
  await act(async () => void fireEvent.keyDown(input, { key: "Enter" }));
};
const popup = () => document.querySelector<HTMLElement>("[data-slot=popup-edit-cell]");
const openPopup = async (rowId: string, colId: string) => {
  await act(async () => void fireEvent.click(pencil(rowId, colId)!));
  await waitFor(() => expect(popup()).not.toBeNull());
  return popup()!;
};

const conflict = () => ({ data: undefined, error: { error: { message: "Stale" } }, response: new Response(null, { status: 409 }) });

beforeAll(stubGlobals);
beforeEach(() => {
  resetClient();
  vi.mocked(toast.error).mockReset();
  grants = { "PlanningExecution.Legs.Update": true, "Orders.TransportOrders.Update": true };
  features = { "Apps.Planboard": "true", "Apps.Planboard.Orders": "true" };
  routes.PUT[LEG_PUT] = (init) => ({ ...ROWS().find((r) => r.id === init?.params?.path?.id), ...(init!.body as object), concurrencyStamp: "leg-stamp-new", remarks: (init!.body as { remarks?: string }).remarks });
  routes.PUT[ORDER_FIELD] = (init) => ({ concurrencyStamp: "order-stamp-new", customerReference: (init!.body as { value: string }).value });
  routes.PUT[LINE_FIELD] = () => ({ concurrencyStamp: "order-stamp-new", transportUnitId: "u9", transportUnitName: "Crate", productId: "p1", productName: "Apples", temperatureId: null, temperatureName: null });
  routes.GET["/api/master-data/transport-unit/lookup"] = () => [{ id: "u1", name: "Pallet" }, { id: "u9", name: "Crate" }];
  routes.GET["/api/master-data/product/lookup"] = () => [{ id: "p1", name: "Apples" }, { id: "p2", name: "Pears" }];
  routes.GET["/api/master-data/temperature/lookup"] = () => [{ id: "c1", name: "Cold" }];
});

describe("useLegInlineColumns: who may edit what", () => {
  it("everything with both permissions and the features on", () => {
    show();
    expect(editable("l1")).toEqual([...LEG_COLUMNS, ...ORDER_COLUMNS]);
  });

  it.each([[3, "completed"], [4, "cancelled"]])("a leg on a %s (%s) trip is read-only, its neighbours are not", (status) => {
    show([leg("l1", { tripStatus: status as LegDto["tripStatus"] }), leg("l2", { tripStatus: 2 }), leg("l3")]);
    expect(editable("l1")).toEqual([]);
    expect(editable("l2")).toEqual([...LEG_COLUMNS, ...ORDER_COLUMNS]);
    expect(editable("l3")).toEqual([...LEG_COLUMNS, ...ORDER_COLUMNS]);
  });

  it("a row that is no leg (a group row, without a line) is read-only", () => {
    show([leg("g1", { orderLineId: undefined })]);
    expect(editable("g1")).toEqual([]);
  });

  it("without Legs.Update the leg's columns are read-only, the order's not", () => {
    grants = { "Orders.TransportOrders.Update": true };
    show();
    expect(editable("l1")).toEqual(ORDER_COLUMNS);
  });

  it("without TransportOrders.Update the order's columns are read-only, the leg's not", () => {
    grants = { "PlanningExecution.Legs.Update": true };
    show();
    expect(editable("l1")).toEqual(LEG_COLUMNS);
  });

  it("with the Planboard's Orders feature off the order's columns are read-only, whatever the permission", () => {
    features = { "Apps.Planboard": "true", "Apps.Planboard.Orders": "false" };
    show();
    expect(editable("l1")).toEqual(LEG_COLUMNS);
  });

  it("with the Planboard feature off the same", () => {
    features = { "Apps.Planboard": "false", "Apps.Planboard.Orders": "true" };
    show();
    expect(editable("l1")).toEqual(LEG_COLUMNS);
  });

  it("nothing at all without any right", () => {
    grants = {};
    features = {};
    show();
    expect(editable("l1")).toEqual([]);
  });
});

describe("useLegInlineColumns: the leg's own fields", () => {
  it("a reference goes with the rest of what the server replaces on every call, and the answer patches that row only", async () => {
    show();
    await editText("l1", "originReference", "NEW");
    expect(lastCall(PUT, LEG_PUT)).toMatchObject({ params: { path: { id: "l1" } } });
    expect(lastCall(PUT, LEG_PUT)!.body).toEqual({ concurrencyStamp: "leg-stamp-l1", originReference: "NEW", destinationReference: "DR", remarks: "Remark" });
    await waitFor(() => expect(latest[0].concurrencyStamp).toBe("leg-stamp-new"));
    expect(latest[1].concurrencyStamp).toBe("leg-stamp-l2");
  });

  it("remarks", async () => {
    show();
    await editText("l1", "remarks", "New remark");
    expect(lastCall(PUT, LEG_PUT)!.body).toMatchObject({ remarks: "New remark", originReference: "OR" });
  });

  it("a planned date sends that date", async () => {
    show();
    const panel = await openPopup("l1", "originDate");
    await act(async () => void fireEvent.change(panel.querySelector("input")!, { target: { value: "2026-11-12" } }));
    expect(lastCall(PUT, LEG_PUT)!.body).toMatchObject({ originPlannedDate: "2026-11-12" });
    expect(lastCall(PUT, LEG_PUT)!.body).not.toHaveProperty("destinationPlannedDate");
    await waitFor(() => expect(popup()).toBeNull());
  });

  it("a planned date cannot be emptied", async () => {
    show();
    const panel = await openPopup("l1", "destinationDate");
    await act(async () => void fireEvent.change(panel.querySelector("input")!, { target: { value: "" } }));
    expect(calls(PUT, LEG_PUT)).toHaveLength(0);
  });

  it("a window sends both of its ends as instants", async () => {
    show();
    const panel = await openPopup("l1", "destinationWindow");
    const inputs = panel.querySelectorAll("input");
    fireEvent.change(inputs[0], { target: { value: "2026-11-11" } });
    fireEvent.change(inputs[1], { target: { value: "08:00" } });
    fireEvent.change(inputs[2], { target: { value: "2026-11-11" } });
    fireEvent.change(inputs[3], { target: { value: "10:30" } });
    await act(async () => void fireEvent.click(within(panel).getByRole("button", { name: "Save" })));
    expect(lastCall(PUT, LEG_PUT)!.body).toMatchObject({ destinationWindow: { from: new Date(2026, 10, 11, 8, 0).toISOString(), till: new Date(2026, 10, 11, 10, 30).toISOString() } });
    await waitFor(() => expect(popup()).toBeNull());
  });

  it("an unreadable window time does not save and says so", async () => {
    show();
    const panel = await openPopup("l1", "originWindow");
    const inputs = panel.querySelectorAll("input");
    fireEvent.change(inputs[1], { target: { value: "99:99" } });
    await act(async () => void fireEvent.click(within(panel).getByRole("button", { name: "Save" })));
    expect(calls(PUT, LEG_PUT)).toHaveLength(0);
    expect(within(panel).getByText("Orders:InvalidTime")).toBeInTheDocument();
  });

  it("Cancel in the window's popup saves nothing", async () => {
    show();
    const panel = await openPopup("l1", "originWindow");
    fireEvent.click(within(panel).getByRole("button", { name: "Cancel" }));
    await waitFor(() => expect(popup()).toBeNull());
    expect(calls(PUT, LEG_PUT)).toHaveLength(0);
  });

  it("legWindowText shows what there is", () => {
    const format = (value: string) => `<${value}>`;
    expect(legWindowText(undefined, format)).toBe("");
    expect(legWindowText({ windowFrom: "a" } as never, format)).toBe("<a> – ");
    expect(legWindowText({ windowTill: "b" } as never, format)).toBe(" – <b>");
    expect(legWindowText({ windowFrom: "a", windowTill: "b" } as never, format)).toBe("<a> – <b>");
  });
});

describe("useLegInlineColumns: a 409 on the leg", () => {
  const FRESH = (reference: string) => ({ ...leg("l1"), concurrencyStamp: "leg-stamp-fresh", origin: { reference, plannedDate: "2026-11-10" } });

  it("the job only moved the stamp: retried on the fresh row, no error", async () => {
    routes.GET[LEG_PUT] = () => FRESH("OR");
    routes.PUT[LEG_PUT] = (init) => ((init!.body as { concurrencyStamp: string }).concurrencyStamp === "leg-stamp-fresh" ? { ...leg("l1"), concurrencyStamp: "leg-stamp-new" } : conflict());
    show();
    await editText("l1", "originReference", "NEW");
    await waitFor(() => expect(calls(PUT, LEG_PUT)).toHaveLength(2));
    expect(lastCall(PUT, LEG_PUT)!.body).toMatchObject({ concurrencyStamp: "leg-stamp-fresh", originReference: "NEW" });
    expect(toast.error).not.toHaveBeenCalled();
  });

  it("someone changed that field: the 409 stands, and the cell shows the old value again", async () => {
    routes.GET[LEG_PUT] = () => FRESH("SOMEONE ELSE");
    routes.PUT[LEG_PUT] = () => conflict();
    show();
    await editText("l1", "originReference", "NEW");
    await waitFor(() => expect(toast.error).toHaveBeenCalledTimes(1));
    expect(calls(PUT, LEG_PUT)).toHaveLength(1);
    await waitFor(() => expect(within(cell("l1", "originReference")).getByText("OR")).toBeInTheDocument());
  });

  it("the refetch fails: the 409 stands", async () => {
    routes.GET[LEG_PUT] = () => fail("Gone", 404);
    routes.PUT[LEG_PUT] = () => conflict();
    show();
    await editText("l1", "remarks", "NEW");
    await waitFor(() => expect(toast.error).toHaveBeenCalledWith("Stale"));
    expect(calls(PUT, LEG_PUT)).toHaveLength(1);
  });

  it("another failure goes straight to the toast", async () => {
    routes.PUT[LEG_PUT] = () => fail("Refused");
    show();
    await editText("l1", "remarks", "NEW");
    await waitFor(() => expect(toast.error).toHaveBeenCalledWith("Refused"));
    expect(calls(GET, LEG_PUT)).toHaveLength(0);
  });
});

describe("useLegInlineColumns: the order's fields", () => {
  it("customer reference: through Orders with the order's stamp", async () => {
    show();
    await editText("l1", "customerReference", "CR2");
    expect(lastCall(PUT, ORDER_FIELD)).toMatchObject({ params: { path: { id: "o1" } }, body: { field: 1, value: "CR2", concurrencyStamp: "order-stamp" } });
    expect(calls(PUT, LEG_PUT)).toHaveLength(0);
  });

  it("the stamp and the value are patched on every row of the order, on no other order's", async () => {
    show();
    await editText("l1", "customerReference", "CR2");
    await waitFor(() => expect(latest[0].orderConcurrencyStamp).toBe("order-stamp-new"));
    expect(latest.map((row) => row.orderConcurrencyStamp)).toEqual(["order-stamp-new", "order-stamp-new", "order-stamp-new", "other-stamp"]);
    expect(latest.map((row) => row.customerReference)).toEqual(["CR2", "CR2", "CR2", "Other"]);
  });

  it("a second edit on another row of the order carries the new stamp: it does not run into its own 409", async () => {
    show();
    await editText("l1", "customerReference", "CR2");
    await waitFor(() => expect(latest[1].orderConcurrencyStamp).toBe("order-stamp-new"));
    await editText("l2", "customerReference", "CR3");
    expect(lastCall(PUT, ORDER_FIELD)!.body).toMatchObject({ concurrencyStamp: "order-stamp-new", value: "CR3" });
  });

  it("a 409 with the field unchanged retries with the order's fresh stamp from the leg", async () => {
    routes.GET[LEG_PUT] = () => leg("l1", { orderConcurrencyStamp: "order-stamp-fresh" });
    routes.PUT[ORDER_FIELD] = (init) => ((init!.body as { concurrencyStamp: string }).concurrencyStamp === "order-stamp-fresh" ? { concurrencyStamp: "order-stamp-new", customerReference: "CR2" } : conflict());
    show();
    await editText("l1", "customerReference", "CR2");
    await waitFor(() => expect(latest[0].customerReference).toBe("CR2"));
    expect(calls(PUT, ORDER_FIELD)).toHaveLength(2);
  });

  it("a 409 with the field changed stands", async () => {
    routes.GET[LEG_PUT] = () => leg("l1", { customerReference: "SOMEONE ELSE" });
    routes.PUT[ORDER_FIELD] = () => conflict();
    show();
    await editText("l1", "customerReference", "CR2");
    await waitFor(() => expect(toast.error).toHaveBeenCalledTimes(1));
    expect(calls(PUT, ORDER_FIELD)).toHaveLength(1);
    expect(latest[0].orderConcurrencyStamp).toBe("order-stamp");
  });

  it("a line field: through Orders; the names on that line's rows, the stamp on all of the order's", async () => {
    show();
    const panel = await openPopup("l1", "transportUnit");
    const select = panel.querySelector("select")!;
    await waitFor(() => expect(select.querySelectorAll("option").length).toBeGreaterThan(2));
    await act(async () => void fireEvent.change(select, { target: { value: "u9" } }));
    expect(lastCall(PUT, LINE_FIELD)).toMatchObject({ params: { path: { id: "o1", lineId: "line-1" } }, body: { field: 1, value: "u9", concurrencyStamp: "order-stamp" } });
    await waitFor(() => expect(latest[0].transportUnitName).toBe("Crate"));
    expect(latest.map((row) => row.transportUnitName)).toEqual(["Crate", "Crate", "Box", "Pallet"]);
    expect(latest.map((row) => row.orderConcurrencyStamp)).toEqual(["order-stamp-new", "order-stamp-new", "order-stamp-new", "other-stamp"]);
    await waitFor(() => expect(popup()).toBeNull());
  });

  it("a transport unit is required, a temperature is not", async () => {
    show();
    const panel = await openPopup("l1", "transportUnit");
    await act(async () => void fireEvent.change(panel.querySelector("select")!, { target: { value: "" } }));
    expect(calls(PUT, LINE_FIELD)).toHaveLength(0);
    expect(popup()).not.toBeNull();
  });

  it("clearing an optional line field sends null", async () => {
    show([leg("l1", { temperatureId: "c1", temperatureName: "Cold" })]);
    const panel = await openPopup("l1", "temperature");
    await act(async () => void fireEvent.change(panel.querySelector("select")!, { target: { value: "" } }));
    expect(lastCall(PUT, LINE_FIELD)!.body).toMatchObject({ field: 3, value: null });
  });

  it("a failed line field shows the failure and keeps the popup", async () => {
    routes.PUT[LINE_FIELD] = () => fail("Refused");
    show();
    const panel = await openPopup("l1", "product");
    const select = panel.querySelector("select")!;
    await waitFor(() => expect(select.querySelectorAll("option").length).toBeGreaterThan(2));
    await act(async () => void fireEvent.change(select, { target: { value: "p2" } }));
    await waitFor(() => expect(toast.error).toHaveBeenCalledWith("Refused"));
    expect(popup()).not.toBeNull();
    expect(latest[0].productName).toBe("Apples");
  });
});
