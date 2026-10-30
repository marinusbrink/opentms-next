import { act, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { useState } from "react";
import { beforeAll, beforeEach, describe, expect, it, vi } from "vitest";

/* The stop strip (design 61 "Order page, legacy tab structure" 3 and 4): one row per stop with activity, relation and
 * address, contact, date, time slot, window, reference and remarks; at least two stops, + adds a stop after its row,
 * − removes one (never below two), and in the Edit freight item dialog a stop moves up and down. Picking a relation
 * preselects its default address (the one without an address type) or its only address while the address is empty
 * (browser-run finding 3); a relation with several typed addresses and no default leaves the choice to the user, and a
 * stored stop is never touched on open. Messages show under their field. Risk class: Medium (UI flow). */

vi.mock("@/lib/i18n/LocalizationProvider", () => ({ useL: () => ({ t: (k: string) => k, culture: "en", isLoading: false }) }));
vi.mock("@/lib/abp/queries", () => ({
  useApplicationConfiguration: () => ({ data: { currentTenant: { id: "t-1" }, auth: { grantedPolicies: {} }, features: { values: {} } } }),
}));
vi.mock("@/lib/api/client", () => ({ apiClient: { GET: vi.fn(), POST: vi.fn(), PUT: vi.fn(), DELETE: vi.fn() } }));
vi.mock("@/components/ui/entity-lookup", async (importOriginal) => ({ ...(await importOriginal<typeof import("@/components/ui/entity-lookup")>()), EntityLookup: (await import("./transport-test-mocks")).MockEntityLookup }));
vi.mock("@/components/ui/reference-combobox", async () => ({ ReferenceCombobox: (await import("./transport-test-mocks")).MockReferenceCombobox }));
vi.mock("@/components/ui/date-picker", async (importOriginal) => ({ ...(await importOriginal<typeof import("@/components/ui/date-picker")>()), DatePicker: (await import("./transport-test-mocks")).MockDatePicker }));

import { StopStrip } from "@/apps/transport/components/StopStrip";
import { defaultAddressIdOf } from "@/domains/master-data/components/RelationFields";
import { newStop, type EditorMode, type StopValues } from "@/apps/transport/orders/order-form";
import { lookupItems, stubGlobals } from "./transport-test-mocks";
import { GET, resetClient, routes } from "./test-setup-mocks";

beforeAll(stubGlobals);
beforeEach(() => {
  lookupItems.current = {};
  resetClient();
});

const latest: { stops: StopValues[] } = { stops: [] };

function Harness({ initial, mode = "order", movable = false, errors }: { initial: StopValues[]; mode?: EditorMode; movable?: boolean; errors?: Record<string, string> }) {
  const [stops, setStops] = useState(initial);
  latest.stops = stops;
  return (
    <StopStrip
      idPrefix="t"
      stops={stops}
      onChange={setStops}
      mode={mode}
      readOnly={false}
      orderDate="2026-10-25"
      waypointTypeOptions={[{ value: "wp-load", label: "Load" }, { value: "wp-unload", label: "Unload" }]}
      timeSlotOptions={[]}
      movable={movable}
      errors={errors}
    />
  );
}

function renderStrip(initial: StopValues[], props: { mode?: EditorMode; movable?: boolean; errors?: Record<string, string> } = {}) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={client}>
      <Harness initial={initial} {...props} />
    </QueryClientProvider>,
  );
}

const stop = (key: string, change: Partial<StopValues> = {}): StopValues => ({ ...newStop("2026-10-25"), key, ...change });
const rows = () => Array.from(document.querySelectorAll<HTMLElement>("[data-slot=stop-row]"));
const field = (index: number, name: string) => document.getElementById(`t-stop-${index}-${name}`) as HTMLInputElement | HTMLSelectElement;
const addresses = () => latest.stops.map((s) => s.relationAddressId ?? "-").join(",");

describe("StopStrip – the rows", () => {
  it("one row per stop with activity, relation, address, contact, date, time slot, window, reference and remarks", () => {
    renderStrip([stop("a", { waypointTypeId: "wp-load", waypointTypeName: "Load" }), stop("b")]);
    expect(rows()).toHaveLength(2);
    for (const name of ["activity", "relation", "address", "contact", "date", "slot", "from", "till", "reference", "remarks"]) expect(field(0, name), name).not.toBeNull();
    expect((field(0, "activity") as HTMLSelectElement).value).toBe("wp-load");
    expect((field(0, "date") as HTMLInputElement).value).toBe("2026-10-25");
  });

  it("a template's stops carry a day offset instead of a date", () => {
    renderStrip([stop("a"), stop("b")], { mode: "template" });
    expect(field(0, "date")).toBeNull();
    expect(field(0, "dayOffset")).not.toBeNull();
  });

  it("never fewer than two: − is disabled at two; + adds a stop after its row, − removes that row", () => {
    renderStrip([stop("a", { reference: "A" }), stop("b", { reference: "B" })]);
    const minus = () => rows().map((row) => within(row).getByRole("button", { name: "Orders:RemoveStop" }));
    for (const button of minus()) expect(button).toBeDisabled();

    fireEvent.click(within(rows()[0]).getByRole("button", { name: "Orders:AddStop" }));
    expect(latest.stops.map((s) => s.reference)).toEqual(["A", "", "B"]);
    /* The new stop takes the date of the one before it. */
    expect(latest.stops[1].date).toBe("2026-10-25");
    for (const button of minus()) expect(button).toBeEnabled();

    fireEvent.click(minus()[2]);
    expect(latest.stops.map((s) => s.reference)).toEqual(["A", ""]);
    for (const button of minus()) expect(button).toBeDisabled();
  });

  it("movable (the Edit freight item dialog): a stop moves up and down; the strip of a new item has no move buttons", () => {
    const { unmount } = renderStrip([stop("a", { reference: "A" }), stop("b", { reference: "B" })]);
    expect(screen.queryByRole("button", { name: "Orders:MoveUp" })).toBeNull();
    unmount();

    renderStrip([stop("a", { reference: "A" }), stop("b", { reference: "B" }), stop("c", { reference: "C" })], { movable: true });
    expect(within(rows()[0]).getByRole("button", { name: "Orders:MoveUp" })).toBeDisabled();
    fireEvent.click(within(rows()[0]).getByRole("button", { name: "Orders:MoveDown" }));
    expect(latest.stops.map((s) => s.reference)).toEqual(["B", "A", "C"]);
    fireEvent.click(within(rows()[2]).getByRole("button", { name: "Orders:MoveUp" }));
    expect(latest.stops.map((s) => s.reference)).toEqual(["B", "C", "A"]);
  });

  it("shows a message under the field it is about", () => {
    renderStrip([stop("a"), stop("b")], { errors: { "1:relationAddressId": "Stop 2 (Unload): choose an address" } });
    expect(within(rows()[1]).getByText("Stop 2 (Unload): choose an address")).toBeInTheDocument();
    expect(within(rows()[0]).queryByText("Stop 2 (Unload): choose an address")).toBeNull();
  });
});

describe("StopStrip – the default address (finding 3)", () => {
  const pick = async (relationId: string) => {
    await act(async () => {
      fireEvent.change(field(0, "relation"), { target: { value: relationId } });
    });
  };

  beforeEach(() => {
    lookupItems.current = { "t-stop-0-relation": [{ id: "rel-a", name: "A" }, { id: "rel-b", name: "B" }, { id: "rel-c", name: "C" }] };
    routes.GET["/api/master-data/relation/{id}/addresses"] = (init) => {
      const id = init?.params?.path?.id;
      if (id === "rel-a") return [{ id: "a-typed", addressTypeId: "type-1", street: "Dock", city: "X" }, { id: "a-default", addressTypeId: null, street: "Main", city: "Xtown" }];
      if (id === "rel-b") return [{ id: "b-only", addressTypeId: "type-1", street: "Only", city: "Y" }];
      return [{ id: "c-1", addressTypeId: "type-1", street: "One", city: "Z" }, { id: "c-2", addressTypeId: "type-2", street: "Two", city: "Z" }];
    };
  });

  it("preselects the relation's default address when a relation is picked, with its city", async () => {
    renderStrip([stop("k1"), stop("k2")]);
    await pick("rel-a");
    await waitFor(() => expect(addresses()).toBe("a-default,-"));
    expect((field(0, "address") as HTMLSelectElement).value).toBe("a-default");
    expect(latest.stops[0].city).toBe("Xtown");

    /* Another relation replaces it: its only address. */
    await pick("rel-b");
    await waitFor(() => expect(addresses()).toBe("b-only,-"));
  });

  it("leaves the address empty for a relation with several typed addresses and no default", async () => {
    renderStrip([stop("k1"), stop("k2")]);
    await pick("rel-c");
    await waitFor(() => expect(GET.mock.calls.some(([p, init]) => p === "/api/master-data/relation/{id}/addresses" && init?.params?.path?.id === "rel-c")).toBe(true));
    await act(async () => {
      await Promise.resolve();
    });
    expect(addresses()).toBe("-,-");
  });

  it("does not touch a stored stop's address on open, even when it is empty", async () => {
    renderStrip([stop("k1", { relationId: "rel-a", relationName: "A", relationAddressId: null }), stop("k2")]);
    await waitFor(() => expect(GET.mock.calls.some(([p]) => p === "/api/master-data/relation/{id}/addresses")).toBe(true));
    await act(async () => {
      await Promise.resolve();
    });
    expect(addresses()).toBe("-,-");
  });
});

describe("defaultAddressIdOf", () => {
  it("is the address without a type, else the only one, else none", () => {
    expect(defaultAddressIdOf([{ id: "x", addressTypeId: "t" }, { id: "d", addressTypeId: null }])).toBe("d");
    expect(defaultAddressIdOf([{ id: "only", addressTypeId: "t" }])).toBe("only");
    expect(defaultAddressIdOf([{ id: "1", addressTypeId: "t" }, { id: "2", addressTypeId: "u" }])).toBeNull();
    expect(defaultAddressIdOf([])).toBeNull();
    expect(defaultAddressIdOf(undefined)).toBeNull();
  });
});
