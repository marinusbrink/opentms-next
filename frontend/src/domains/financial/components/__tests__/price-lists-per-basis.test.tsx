import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { useState } from "react";
import type { Mock } from "vitest";
import { beforeEach, describe, expect, it, vi } from "vitest";

import type { MultiSelectProps } from "@/components/ui/multi-select";

/* The revenue price lists per basis (design 61 "Order page review" 3, the legacy block "Price calculation", compacted on
 * the PO's request of 2026-10-28): a row only per charged basis (switched on, or one of its lists chosen), in the legacy
 * order (quantity, colli, EPQ, weight, distance, positions, fixed price per freight item, volume, loading metres, load
 * factor, duration, vehicle type, toll distance), each the revenue lists of that basis and a remove button; "Add price
 * basis" offers the others in that order and focuses the new row's picker; a row added without a list stays (an open
 * line per item), removing a row clears that row's lists only, a disabled block (the order's fixed price) disables the
 * rows, removes and the menu, a list the record knows (archived, or created in place) shows in the row of its basis, a
 * chosen list of unknown basis is found through the lookups, the value keeps the order of the chosen lists, only the
 * shown rows load their lookup, and the helpers withCreatedList / orderedRevenueBases. The lookups are the real hooks
 * over the client mock; the MultiSelect is a native stand-in (its own tests cover it), the menu the real one. Risk
 * class: Medium (UI flow; the value feeds pricing). */

vi.mock("@/lib/i18n/LocalizationProvider", () => ({ useL: () => ({ t: (k: string) => k, culture: "en", isLoading: false }) }));
vi.mock("@/lib/api/client", () => ({ apiClient: { GET: vi.fn(), POST: vi.fn(), PUT: vi.fn(), DELETE: vi.fn() } }));
vi.mock("@/components/ui/multi-select", () => ({
  MultiSelect: (props: MultiSelectProps) => (
    <div id={props.id} role="group" tabIndex={-1} data-slot="mock-multi-select" data-disabled={props.disabled ? "true" : "false"}>
      {props.options.map((o) => (
        <label key={o.id}>
          <input type="checkbox" aria-label={o.label} disabled={props.disabled} checked={props.value.includes(o.id)} onChange={(e) => props.onChange(e.target.checked ? [...props.value, o.id] : props.value.filter((id) => id !== o.id))} />
          {o.label}
        </label>
      ))}
      {props.onCreateNew && (
        <button type="button" data-create-for={props.id} onClick={() => props.onCreateNew!("typed")}>
          {props.createLabel}
        </button>
      )}
    </div>
  ),
}));

import { apiClient } from "@/lib/api/client";
import { PriceListsPerBasis } from "@/domains/financial/components/PriceListsPerBasis";
import { REVENUE_BASES, orderedRevenueBases, withCreatedList, withKnownList, type KnownPriceList, type PriceListsPerBasisValue } from "@/domains/financial/price-lists";

const GET = apiClient.GET as unknown as Mock<(path: string, init?: { params?: { query?: Record<string, unknown> } }) => Promise<unknown>>;

/* Revenue lists per basis: two by weight, one by quantity, one by distance. */
const LISTS = [
  { id: "pl-w1", code: "W1", name: "Weight NL", basis: 4 },
  { id: "pl-w2", code: "W2", name: "Weight BE", basis: 4 },
  { id: "pl-q1", code: "Q1", name: "Quantity", basis: 1 },
  { id: "pl-d1", code: "D1", name: "Distance", basis: 9 },
];

beforeEach(() => {
  GET.mockReset().mockImplementation(async (path, init) => {
    if (path !== "/api/financial/price-list/lookup") return { data: [], error: undefined, response: new Response(null, { status: 200 }) };
    const query = init?.params?.query ?? {};
    const data = query.side === 1 ? LISTS.filter((l) => l.basis === query.basis).map(({ basis: _basis, ...l }) => l) : [];
    return { data, error: undefined, response: new Response(null, { status: 200 }) };
  });
});

const LEGACY_ORDER = [1, 3, 8, 4, 9, 2, 13, 5, 6, 7, 11, 12, 10];

function Harness({ initial, known = [], disabled = false, onValue }: { initial: PriceListsPerBasisValue; known?: KnownPriceList[]; disabled?: boolean; onValue?: (v: PriceListsPerBasisValue) => void }) {
  const [value, setValue] = useState(initial);
  const [knownLists, setKnown] = useState(known);
  return (
    <>
      <PriceListsPerBasis
        idPrefix="t"
        value={value}
        onChange={(next) => {
          setValue(next);
          onValue?.(next);
        }}
        known={knownLists}
        disabled={disabled}
        createInPlace={{
          createLabel: "Financial:AddNewPriceList",
          onCreateNew: () => {
            /* As the order's Financial tab does: the new list (by weight) joins the row of its basis. */
            setKnown((k) => withKnownList(k, { id: "pl-new", basis: 4, label: "NEW New weight list" }));
            setValue((v) => withCreatedList(v, "pl-new", 4));
          },
        }}
      />
      <output data-testid="value">{JSON.stringify(value)}</output>
    </>
  );
}

function renderBlock(props: Parameters<typeof Harness>[0]) {
  render(
    <QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}>
      <Harness {...props} />
    </QueryClientProvider>,
  );
}

const rows = () => Array.from(document.querySelectorAll<HTMLElement>("[data-slot=charged-basis-row]"));
const shown = () => rows().map((r) => Number(r.dataset.basis));
const row = (basis: number) => document.querySelector<HTMLElement>(`[data-slot=charged-basis-row][data-basis="${basis}"]`)!;
const keyOf = (basis: number) => REVENUE_BASES.find((b) => b.value === basis)!.key;
const removeButton = (basis: number) => within(row(basis)).getByRole("button", { name: `Financial:RemoveChargedBasis: ${keyOf(basis)}` });
const addButton = () => screen.getByRole("button", { name: /Financial:AddChargedBasis/ });
const openAddMenu = async () => {
  fireEvent.click(addButton());
  return (await screen.findAllByRole("menuitem")).map((item) => item.textContent);
};
const addBasis = async (basis: number) => {
  fireEvent.click(addButton());
  fireEvent.click(await screen.findByRole("menuitem", { name: keyOf(basis) }));
};
const value = () => JSON.parse(screen.getByTestId("value").textContent!) as PriceListsPerBasisValue;
const lookedUpBases = () => GET.mock.calls.filter(([path]) => path === "/api/financial/price-list/lookup").map(([, init]) => init?.params?.query?.basis);

describe("PriceListsPerBasis", () => {
  it("shows a row per charged basis only, in the legacy order, each with the revenue lists of that basis; only those rows load", async () => {
    renderBlock({ initial: { priceListIds: ["pl-w1"], chargedBases: [9, 1] }, known: [{ id: "pl-w1", basis: 4, label: "W1 Weight NL" }] });

    /* Weight is charged through its list, distance and quantity are switched on. */
    expect(shown()).toEqual([1, 4, 9]);
    expect(row(4).textContent).toContain("Financial:ChargedBasis.WeightKg");
    await waitFor(() => expect(within(row(4)).getByLabelText("W2 Weight BE")).toBeInTheDocument());
    expect(within(row(4)).getByLabelText("W1 Weight NL")).toBeChecked();
    expect(within(row(4)).queryByLabelText("Q1 Quantity")).toBeNull();
    await waitFor(() => expect(within(row(1)).getByLabelText("Q1 Quantity")).toBeInTheDocument());
    expect(GET).toHaveBeenCalledWith("/api/financial/price-list/lookup", { params: { query: { side: 1, basis: 4, take: 500 } } });
    expect([...new Set(lookedUpBases())].sort()).toEqual([1, 4, 9]);
  });

  it("without a charged basis shows the hint, loads no lookup, and the menu offers every basis in the legacy order", async () => {
    renderBlock({ initial: { priceListIds: [], chargedBases: [] } });

    expect(rows()).toHaveLength(0);
    expect(document.querySelector("[data-slot=no-charged-bases]")).toHaveTextContent("Financial:NoChargedBases");
    expect(lookedUpBases()).toEqual([]);
    expect(await openAddMenu()).toEqual(LEGACY_ORDER.map(keyOf));
  });

  it("a basis added from the menu shows its row without a list, takes the focus, and leaves the menu", async () => {
    renderBlock({ initial: { priceListIds: [], chargedBases: [4] } });

    await addBasis(1);
    /* Switched on without a list: an open line per freight item. */
    expect(value()).toEqual({ priceListIds: [], chargedBases: [1, 4] });
    expect(shown()).toEqual([1, 4]);
    await waitFor(() => expect(document.activeElement).toBe(document.getElementById("t-1-lists")));
    const offered = await openAddMenu();
    expect(offered).toHaveLength(LEGACY_ORDER.length - 2);
    expect(offered).not.toContain(keyOf(1));
    expect(offered).not.toContain(keyOf(4));
  });

  it("choosing lists keeps the order of the lists chosen and the rows charged", async () => {
    renderBlock({ initial: { priceListIds: [], chargedBases: [1, 4] } });
    await waitFor(() => expect(within(row(4)).getByLabelText("W2 Weight BE")).toBeInTheDocument());
    await waitFor(() => expect(within(row(1)).getByLabelText("Q1 Quantity")).toBeInTheDocument());

    fireEvent.click(within(row(4)).getByLabelText("W2 Weight BE"));
    fireEvent.click(within(row(1)).getByLabelText("Q1 Quantity"));
    fireEvent.click(within(row(4)).getByLabelText("W1 Weight NL"));
    expect(value()).toEqual({ priceListIds: ["pl-w2", "pl-q1", "pl-w1"], chargedBases: [1, 4] });

    /* Unticking the last list of a charged row leaves the row. */
    fireEvent.click(within(row(1)).getByLabelText("Q1 Quantity"));
    expect(value()).toEqual({ priceListIds: ["pl-w2", "pl-w1"], chargedBases: [1, 4] });
    expect(shown()).toEqual([1, 4]);
  });

  it("removing a row clears that row's lists only", async () => {
    const known: KnownPriceList[] = [
      { id: "pl-q1", basis: 1, label: "Q1 Quantity" },
      { id: "pl-w1", basis: 4, label: "W1 Weight NL" },
      { id: "pl-d1", basis: 9, label: "D1 Distance" },
    ];
    renderBlock({ initial: { priceListIds: ["pl-q1", "pl-w1", "pl-d1"], chargedBases: [4, 11] }, known });
    expect(shown()).toEqual([1, 4, 9, 11]);

    fireEvent.click(removeButton(4));
    expect(value()).toEqual({ priceListIds: ["pl-q1", "pl-d1"], chargedBases: [11] });
    expect(shown()).toEqual([1, 9, 11]);

    fireEvent.click(removeButton(11));
    expect(value()).toEqual({ priceListIds: ["pl-q1", "pl-d1"], chargedBases: [] });
    expect(shown()).toEqual([1, 9]);
  });

  it("disabled (the order's fixed price) disables the rows, the removes and the menu, and offers no Add new", async () => {
    renderBlock({ initial: { priceListIds: ["pl-w1"], chargedBases: [4, 9] }, disabled: true });
    await waitFor(() => expect(within(row(4)).getByLabelText("W1 Weight NL")).toBeDisabled());
    for (const r of rows()) expect(r.querySelector("[data-slot=mock-multi-select]")).toHaveAttribute("data-disabled", "true");
    expect(removeButton(4)).toBeDisabled();
    expect(removeButton(9)).toBeDisabled();
    expect(addButton()).toHaveAttribute("data-disabled");
    expect(document.querySelector("[data-create-for]")).toBeNull();
  });

  it("a list created in place lands in the row of its basis, checked, and adds that row", async () => {
    renderBlock({ initial: { priceListIds: ["pl-q1"], chargedBases: [] }, known: [{ id: "pl-q1", basis: 1, label: "Q1 Quantity" }] });
    await waitFor(() => expect(within(row(1)).getByLabelText("Q1 Quantity")).toBeChecked());
    expect(shown()).toEqual([1]);

    /* Created from the quantity row, the new list prices by weight. */
    fireEvent.click(row(1).querySelector<HTMLElement>("[data-create-for=t-1-lists]")!);

    expect(value()).toEqual({ priceListIds: ["pl-q1", "pl-new"], chargedBases: [4] });
    expect(shown()).toEqual([1, 4]);
    expect(within(row(4)).getByLabelText("NEW New weight list")).toBeChecked();
    expect(within(row(1)).queryByLabelText("NEW New weight list")).toBeNull();
  });

  it("a list the record knows but the lookup no longer returns (archived) shows in its row", async () => {
    renderBlock({ initial: { priceListIds: ["pl-old"], chargedBases: [] }, known: [{ id: "pl-old", basis: 9, label: "OLD Archived distance" }] });
    expect(shown()).toEqual([9]);
    await waitFor(() => expect(within(row(9)).getByLabelText("D1 Distance")).toBeInTheDocument());
    expect(within(row(9)).getByLabelText("OLD Archived distance")).toBeChecked();
  });

  it("a chosen list whose basis the record does not know is found through the lookups and shows in its basis row", async () => {
    renderBlock({ initial: { priceListIds: ["pl-d1"], chargedBases: [] } });

    await waitFor(() => expect(shown()).toEqual([9]));
    expect(within(row(9)).getByLabelText("D1 Distance")).toBeChecked();
    expect(lookedUpBases()).toEqual(expect.arrayContaining(LEGACY_ORDER));
  });
});

describe("the per-basis helpers", () => {
  it("orderedRevenueBases keeps the legacy order, each basis once, revenue bases only", () => {
    expect(orderedRevenueBases([10, 4, 1, 4, 0, 14])).toEqual([1, 4, 10]);
    expect(orderedRevenueBases([...LEGACY_ORDER].reverse() as never)).toEqual(LEGACY_ORDER);
  });

  it("withCreatedList adds a list once and switches its basis on", () => {
    expect(withCreatedList({ priceListIds: ["a"], chargedBases: [9] }, "b", 4)).toEqual({ priceListIds: ["a", "b"], chargedBases: [4, 9] });
    expect(withCreatedList({ priceListIds: ["a"], chargedBases: [4] }, "a", 4)).toEqual({ priceListIds: ["a"], chargedBases: [4] });
    expect(withCreatedList({ priceListIds: [], chargedBases: [] }, "c", null)).toEqual({ priceListIds: ["c"], chargedBases: [] });
  });
});
