import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import type { ReactNode } from "react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import { EntityLookup } from "@/components/ui/entity-lookup";
import { MultiSelect } from "@/components/ui/multi-select";
import { ReferenceCombobox } from "@/components/ui/reference-combobox";
import { TreeSelect } from "@/components/ui/tree-select";

/* UI-LOOK-004 (PO decision 2026-10-16) in the four library pickers: the "Add new …" row exists only with `onCreateNew`,
 * carries `createLabel` (or the default), hands over what the user typed (the tree has no search: ""), and closes the
 * list so the entity's editor opens on top; MultiSelect and EntityLookup also clear their search. The real Base UI
 * components run here. Risk class: Medium (UI flow of a library component every record picker uses). */

vi.mock("@/lib/i18n/LocalizationProvider", () => ({
  useL: () => ({ t: (k: string) => k, culture: "en", isLoading: false }),
}));

class ObserverStub {
  observe() {}
  disconnect() {}
  unobserve() {}
}

beforeEach(() => {
  vi.stubGlobal("IntersectionObserver", ObserverStub);
});
afterEach(() => {
  vi.unstubAllGlobals();
});

/* Base UI opens on pointer/mouse down and reads `inputType` to tell typing from autofill. */
const openPopup = (element: HTMLElement) => {
  fireEvent.pointerDown(element);
  fireEvent.mouseDown(element);
  fireEvent.click(element);
};
const type = (element: HTMLElement, value: string) => fireEvent.input(element, { target: { value }, inputType: "insertText" });
const createRow = (label: string) => screen.queryByRole("button", { name: label });

const LABEL = "Add new thing…";

describe("ReferenceCombobox — Add new", () => {
  const options = [
    { value: "a", label: "Alpha" },
    { value: "b", label: "Beta" },
  ];

  it("has no Add new row without onCreateNew", async () => {
    render(<ReferenceCombobox id="rc" value={null} onChange={() => {}} options={options} createLabel={LABEL} />);
    openPopup(screen.getByRole("combobox"));
    expect(await screen.findByText("Alpha")).toBeInTheDocument();
    expect(createRow(LABEL)).toBeNull();
    expect(createRow("AddNew")).toBeNull();
  });

  it("shows the row with the label, passes the typed text and closes the list", async () => {
    const onCreateNew = vi.fn();
    render(<ReferenceCombobox id="rc" value={null} onChange={() => {}} options={options} onCreateNew={onCreateNew} createLabel={LABEL} />);
    const input = screen.getByRole("combobox");
    openPopup(input);
    type(input, "  Zeeland ");
    const row = await screen.findByRole("button", { name: LABEL });
    fireEvent.click(row);
    expect(onCreateNew).toHaveBeenCalledTimes(1);
    expect(onCreateNew).toHaveBeenCalledWith("Zeeland");
    await waitFor(() => expect(createRow(LABEL)).toBeNull());
    expect(input).toHaveAttribute("aria-expanded", "false");
  });

  it("falls back to the generic label", async () => {
    render(<ReferenceCombobox id="rc" value={null} onChange={() => {}} options={options} onCreateNew={() => {}} />);
    openPopup(screen.getByRole("combobox"));
    expect(await screen.findByRole("button", { name: "AddNew" })).toBeInTheDocument();
  });

  it("does not hand over the selected value's label as the typed text", async () => {
    const onCreateNew = vi.fn();
    render(<ReferenceCombobox id="rc" value="a" onChange={() => {}} options={options} onCreateNew={onCreateNew} createLabel={LABEL} />);
    openPopup(screen.getByRole("combobox"));
    fireEvent.click(await screen.findByRole("button", { name: LABEL }));
    expect(onCreateNew).toHaveBeenCalledWith("");
  });

  it("starts with an empty term on the next opening", async () => {
    const onCreateNew = vi.fn();
    render(<ReferenceCombobox id="rc" value={null} onChange={() => {}} options={options} onCreateNew={onCreateNew} createLabel={LABEL} />);
    const input = screen.getByRole("combobox");
    openPopup(input);
    type(input, "Zee");
    fireEvent.click(await screen.findByRole("button", { name: LABEL }));
    await waitFor(() => expect(createRow(LABEL)).toBeNull());
    openPopup(input);
    fireEvent.click(await screen.findByRole("button", { name: LABEL }));
    expect(onCreateNew).toHaveBeenLastCalledWith("");
  });
});

describe("MultiSelect — Add new", () => {
  const options = [
    { id: "a", label: "Alpha" },
    { id: "b", label: "Beta" },
  ];
  const trigger = () => document.getElementById("ms")!;
  const search = () => screen.getByPlaceholderText("MasterData:Search") as HTMLInputElement;
  /* Runs first: a MultiSelect unmounted while its list was open (the other tests end that way) leaves Base UI
   * state in jsdom that reopens the next one's list after it closes; seen only in jsdom, not a product finding. */

  it("passes the typed search, closes the list and clears the search", async () => {
    const onCreateNew = vi.fn();
    render(<MultiSelect id="ms" value={["a"]} onChange={() => {}} options={options} onCreateNew={onCreateNew} createLabel={LABEL} />);
    openPopup(trigger());
    type(search(), " Gamma ");
    fireEvent.click(await screen.findByRole("button", { name: LABEL }));
    expect(onCreateNew).toHaveBeenCalledTimes(1);
    expect(onCreateNew).toHaveBeenCalledWith("Gamma");
    await waitFor(() => expect(createRow(LABEL)).toBeNull());
    await waitFor(() => expect(trigger()).toHaveAttribute("aria-expanded", "false"));

    openPopup(trigger());
    expect(await screen.findByRole("button", { name: LABEL })).toBeInTheDocument();
    expect(search()).toHaveValue("");
    expect(screen.getByText("Beta")).toBeInTheDocument();
  });

  it("has no Add new row without onCreateNew", async () => {
    render(<MultiSelect id="ms" value={[]} onChange={() => {}} options={options} createLabel={LABEL} />);
    openPopup(trigger());
    expect(await screen.findByText("Alpha")).toBeInTheDocument();
    expect(createRow(LABEL)).toBeNull();
  });

  it("falls back to the generic label", async () => {
    render(<MultiSelect id="ms" value={[]} onChange={() => {}} options={options} onCreateNew={() => {}} />);
    openPopup(trigger());
    expect(await screen.findByRole("button", { name: "MasterData:AddNew" })).toBeInTheDocument();
  });
});

describe("TreeSelect — Add new", () => {
  const nodes = [{ id: "root", label: "Head office", children: [{ id: "plan", label: "Planning" }] }];

  it("has no Add new row without onCreateNew", async () => {
    render(<TreeSelect id="ts" nodes={nodes} value={null} onChange={() => {}} createLabel={LABEL} />);
    fireEvent.click(screen.getByRole("combobox"));
    expect(await screen.findByText("Head office")).toBeInTheDocument();
    expect(createRow(LABEL)).toBeNull();
  });

  it("shows the row below the tree, passes an empty term and closes the list", async () => {
    const onCreateNew = vi.fn();
    render(<TreeSelect id="ts" nodes={nodes} value={null} onChange={() => {}} onCreateNew={onCreateNew} createLabel={LABEL} />);
    fireEvent.click(screen.getByRole("combobox"));
    const row = await screen.findByRole("button", { name: LABEL });
    /* The row is not one of the tree's options. */
    expect(screen.getAllByRole("option").map((o) => o.textContent)).toEqual(["Head office"]);
    fireEvent.click(row);
    expect(onCreateNew).toHaveBeenCalledWith("");
    await waitFor(() => expect(screen.queryByRole("option")).toBeNull());
    expect(screen.getByRole("combobox")).toHaveAttribute("aria-expanded", "false");
  });

  it("falls back to the generic label", async () => {
    render(<TreeSelect id="ts" nodes={nodes} value={null} onChange={() => {}} onCreateNew={() => {}} />);
    fireEvent.click(screen.getByRole("combobox"));
    expect(await screen.findByRole("button", { name: "AddNew" })).toBeInTheDocument();
  });
});

describe("EntityLookup — Add new", () => {
  interface Item {
    id: string;
    name: string;
  }
  const items: Item[] = [{ id: "1", name: "Rotterdam Freight" }];
  const fetchPage = async () => ({ items, totalCount: items.length });
  function wrapper({ children }: { children: ReactNode }) {
    const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    return <QueryClientProvider client={client}>{children}</QueryClientProvider>;
  }

  it("closes the list and clears the typed text when the row is used", async () => {
    const onCreateNew = vi.fn();
    render(<EntityLookup<Item> id="el" value={null} onChange={() => {}} fetchPage={fetchPage} getLabel={(r) => r.name} onCreateNew={onCreateNew} createLabel={LABEL} />, {
      wrapper,
    });
    const input = screen.getByRole("combobox") as HTMLInputElement;
    openPopup(input);
    type(input, "Zeeland");
    fireEvent.click(await screen.findByRole("button", { name: LABEL }));
    expect(onCreateNew).toHaveBeenCalledWith("Zeeland");
    await waitFor(() => expect(createRow(LABEL)).toBeNull());
    expect(input).toHaveAttribute("aria-expanded", "false");
    expect(input).toHaveValue("");
  });

  it("has no row without onCreateNew", async () => {
    render(<EntityLookup<Item> id="el" value={null} onChange={() => {}} fetchPage={fetchPage} getLabel={(r) => r.name} createLabel={LABEL} />, { wrapper });
    openPopup(screen.getByRole("combobox"));
    expect(await screen.findByText("Rotterdam Freight")).toBeInTheDocument();
    expect(createRow(LABEL)).toBeNull();
  });
});
