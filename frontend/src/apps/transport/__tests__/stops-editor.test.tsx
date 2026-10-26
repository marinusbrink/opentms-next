import { act, fireEvent, render, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { useState } from "react";
import { beforeAll, beforeEach, describe, expect, it, vi } from "vitest";

/* The stops editor's inline detail (design 61, browser-run findings 2 and 3): opening a stop brings its detail into
 * view and puts the cursor in its first field; picking a relation preselects its default address (the one without an
 * address type), or its only address, while the address is empty; a relation with several typed addresses and no
 * default leaves the choice to the user. Risk class: Low. */

vi.mock("@/lib/i18n/LocalizationProvider", () => ({ useL: () => ({ t: (k: string) => k, culture: "en", isLoading: false }) }));
vi.mock("@/lib/abp/queries", () => ({
  useApplicationConfiguration: () => ({ data: { currentTenant: { id: "t-1" }, auth: { grantedPolicies: {} }, features: { values: {} } } }),
}));
vi.mock("@/lib/api/client", () => ({ apiClient: { GET: vi.fn(), POST: vi.fn(), PUT: vi.fn(), DELETE: vi.fn() } }));
vi.mock("@/components/ui/lines-grid-lazy", async () => ({ LazyLinesGrid: (await import("./transport-test-mocks")).MockLinesGrid }));
vi.mock("@/components/ui/entity-lookup", async (importOriginal) => ({ ...(await importOriginal<typeof import("@/components/ui/entity-lookup")>()), EntityLookup: (await import("./transport-test-mocks")).MockEntityLookup }));
vi.mock("@/components/ui/reference-combobox", async () => ({ ReferenceCombobox: (await import("./transport-test-mocks")).MockReferenceCombobox }));

import { StopsEditor } from "@/apps/transport/components/StopsEditor";
import { defaultAddressIdOf } from "@/apps/transport/components/RelationFields";
import { newStop, type StopValues } from "@/apps/transport/orders/order-form";
import { lookupItems, stubGlobals } from "./transport-test-mocks";
import { GET, resetClient, routes } from "./test-setup-mocks";

const scrollIntoView = vi.fn();
beforeAll(() => {
  stubGlobals();
  Element.prototype.scrollIntoView = scrollIntoView;
});
beforeEach(() => {
  scrollIntoView.mockReset();
  lookupItems.current = {};
  resetClient();
});

function Harness({ initial }: { initial: StopValues[] }) {
  const [stops, setStops] = useState(initial);
  return (
    <>
      <StopsEditor idPrefix="t" stops={stops} onChange={setStops} mode="order" kind="line" readOnly={false} orderDate="2026-10-25" waypointTypeOptions={[{ value: "wp-load", label: "Load" }]} timeSlotOptions={[]} />
      <output data-testid="addresses">{stops.map((s) => s.relationAddressId ?? "-").join(",")}</output>
    </>
  );
}

function renderEditor(initial: StopValues[]) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={client}>
      <Harness initial={initial} />
    </QueryClientProvider>,
  );
}

const stop = (key: string, change: Partial<StopValues> = {}): StopValues => ({ ...newStop("2026-10-25"), key, ...change });
const editButtons = () => Array.from(document.querySelectorAll<HTMLButtonElement>("[data-slot=mock-line] button")).filter((b) => b.textContent === "Edit");
const addresses = () => document.querySelector("[data-testid=addresses]")!.textContent;

describe("StopsEditor – the stop detail (finding 2)", () => {
  it("scrolls the opened detail into view (nearest) and focuses its first field", async () => {
    renderEditor([stop("k1"), stop("k2")]);
    await act(async () => {
      fireEvent.click(editButtons()[1]);
    });
    const detail = document.querySelector<HTMLElement>("[data-slot=stop-detail]")!;
    expect(detail.dataset.key).toBe("k2");
    expect(scrollIntoView).toHaveBeenCalledWith({ block: "nearest" });
    expect(scrollIntoView.mock.contexts.at(-1)).toBe(detail);
    expect(document.activeElement).toBe(document.getElementById("t-k2-relation"));

    /* Opening another stop moves the view and the focus again. */
    await act(async () => {
      fireEvent.click(editButtons()[0]);
    });
    expect(scrollIntoView).toHaveBeenCalledTimes(2);
    expect(document.activeElement).toBe(document.getElementById("t-k1-relation"));
  });
});

describe("StopsEditor – the default address (finding 3)", () => {
  const pick = async (relationId: string) => {
    const select = document.getElementById("t-k1-relation") as HTMLSelectElement;
    await act(async () => {
      fireEvent.change(select, { target: { value: relationId } });
    });
  };

  beforeEach(() => {
    lookupItems.current = { "t-k1-relation": [{ id: "rel-a", name: "A" }, { id: "rel-b", name: "B" }, { id: "rel-c", name: "C" }] };
    routes.GET["/api/master-data/relation/{id}/addresses"] = (init) => {
      const id = init?.params?.path?.id;
      if (id === "rel-a") return [{ id: "a-typed", addressTypeId: "type-1", street: "Dock", city: "X" }, { id: "a-default", addressTypeId: null, street: "Main", city: "X" }];
      if (id === "rel-b") return [{ id: "b-only", addressTypeId: "type-1", street: "Only", city: "Y" }];
      return [{ id: "c-1", addressTypeId: "type-1", street: "One", city: "Z" }, { id: "c-2", addressTypeId: "type-2", street: "Two", city: "Z" }];
    };
  });

  it("preselects the relation's default address when a relation is picked", async () => {
    renderEditor([stop("k1")]);
    await act(async () => {
      fireEvent.click(editButtons()[0]);
    });
    await pick("rel-a");
    await waitFor(() => expect(addresses()).toBe("a-default"));
    expect((document.getElementById("t-k1-address") as HTMLSelectElement).value).toBe("a-default");

    /* Another relation replaces it: its only address. */
    await pick("rel-b");
    await waitFor(() => expect(addresses()).toBe("b-only"));
  });

  it("leaves the address empty for a relation with several typed addresses and no default", async () => {
    renderEditor([stop("k1")]);
    await act(async () => {
      fireEvent.click(editButtons()[0]);
    });
    await pick("rel-c");
    await waitFor(() => expect(GET.mock.calls.some(([p, init]) => p === "/api/master-data/relation/{id}/addresses" && init?.params?.path?.id === "rel-c")).toBe(true));
    await act(async () => {
      await Promise.resolve();
    });
    expect(addresses()).toBe("-");
  });

  it("does not touch a stored stop's address on open, even when it is empty", async () => {
    renderEditor([stop("k1", { relationId: "rel-a", relationName: "A", relationAddressId: null })]);
    await act(async () => {
      fireEvent.click(editButtons()[0]);
    });
    await waitFor(() => expect(GET.mock.calls.some(([p]) => p === "/api/master-data/relation/{id}/addresses")).toBe(true));
    await act(async () => {
      await Promise.resolve();
    });
    expect(addresses()).toBe("-");
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
