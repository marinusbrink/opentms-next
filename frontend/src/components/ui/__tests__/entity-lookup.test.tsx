import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { act, fireEvent, render, screen, waitFor } from "@testing-library/react";
import type { ReactNode } from "react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import { EntityLookup, ENTITY_LOOKUP_PAGE_SIZE } from "@/components/ui/entity-lookup";

/* The entity picker (UI-LOOK-002..007, component-map GAP-01 option (a)).
 * Risk class: Medium — every master-data and order screen picks records through it. */

vi.mock("@/lib/i18n/LocalizationProvider", () => ({
  useL: () => ({ t: (k: string) => k, culture: "en", isLoading: false }),
}));

interface Relation {
  id: string;
  name: string;
  city: string;
}

const relations: Relation[] = [
  { id: "1", name: "Rotterdam Freight", city: "Rotterdam" },
  { id: "2", name: "Rhine Logistics", city: "Duisburg" },
  { id: "3", name: "Antwerp Cargo", city: "Antwerp" },
];

function makeFetchPage(source: Relation[] = relations) {
  return vi.fn(async ({ term, skip, take }: { term: string; skip: number; take: number }) => {
    const matches = source.filter((r) => r.name.toLowerCase().includes(term.toLowerCase()));
    return { items: matches.slice(skip, skip + take), totalCount: matches.length };
  });
}

function wrapper({ children }: { children: ReactNode }) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return <QueryClientProvider client={client}>{children}</QueryClientProvider>;
}

class ObserverStub {
  static instances: ObserverStub[] = [];
  callback: IntersectionObserverCallback;
  constructor(callback: IntersectionObserverCallback) {
    this.callback = callback;
    ObserverStub.instances.push(this);
  }
  observe() {}
  disconnect() {}
  unobserve() {}
}

const input = () => screen.getByRole("combobox");
/* Base UI opens on pointer/mouse down, and reads `inputType` to tell typing from autofill. */
const openPopup = (element: HTMLElement) => {
  fireEvent.pointerDown(element);
  fireEvent.mouseDown(element);
  fireEvent.click(element);
};
const type = (element: HTMLElement, value: string) => fireEvent.input(element, { target: { value }, inputType: "insertText" });

describe("EntityLookup", () => {
  beforeEach(() => {
    ObserverStub.instances = [];
    vi.stubGlobal("IntersectionObserver", ObserverStub);
  });
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it("loads the first page with an empty term when opened, and searches only after the debounce", async () => {
    const fetchPage = makeFetchPage();
    render(<EntityLookup id="rel" value={null} onChange={() => {}} fetchPage={fetchPage} getLabel={(r) => r.name} />, { wrapper });
    expect(fetchPage).not.toHaveBeenCalled();

    openPopup(input());
    await waitFor(() => expect(fetchPage).toHaveBeenCalledWith({ term: "", skip: 0, take: ENTITY_LOOKUP_PAGE_SIZE }));
    expect(await screen.findByText("Rotterdam Freight")).toBeInTheDocument();
    expect(screen.getByText("Antwerp Cargo")).toBeInTheDocument();

    type(input(), "rh");
    expect(fetchPage).toHaveBeenCalledTimes(1);
    await waitFor(() => expect(fetchPage).toHaveBeenCalledWith({ term: "rh", skip: 0, take: ENTITY_LOOKUP_PAGE_SIZE }));
    await waitFor(() => expect(screen.queryByText("Antwerp Cargo")).toBeNull());
    expect(screen.getByText("Rhine Logistics")).toBeInTheDocument();
    expect(fetchPage).toHaveBeenCalledTimes(2);
  });

  it("asks the user to type more below two characters and does not fetch for that term", async () => {
    const fetchPage = makeFetchPage();
    render(<EntityLookup id="rel" value={null} onChange={() => {}} fetchPage={fetchPage} getLabel={(r) => r.name} />, { wrapper });
    type(input(), "r");
    expect(await screen.findByText("TypeToSearch")).toBeInTheDocument();
    await new Promise((resolve) => setTimeout(resolve, 300));
    expect(fetchPage.mock.calls.some(([q]) => q.term === "r")).toBe(false);
  });

  it("shows NoItemsFound for an empty result", async () => {
    render(<EntityLookup id="rel" value={null} onChange={() => {}} fetchPage={makeFetchPage([])} getLabel={(r) => r.name} />, { wrapper });
    openPopup(input());
    expect(await screen.findByText("NoItemsFound")).toBeInTheDocument();
  });

  it("selects an item through onChange and renders the option through renderOption", async () => {
    const onChange = vi.fn();
    render(
      <EntityLookup
        id="rel"
        value={null}
        onChange={onChange}
        fetchPage={makeFetchPage()}
        getLabel={(r) => r.name}
        renderOption={(r) => (
          <>
            <span className="font-medium">{r.name}</span>
            <span className="text-label text-muted-foreground">{r.city}</span>
          </>
        )}
      />,
      { wrapper },
    );
    openPopup(input());
    const option = await screen.findByText("Duisburg");
    fireEvent.click(option.closest("[role=option]")!);
    expect(onChange).toHaveBeenCalledWith(expect.objectContaining({ id: "2" }));
  });

  it("renders the Add new row only with onCreateNew and passes the typed term", async () => {
    const fetchPage = makeFetchPage();
    const { unmount } = render(<EntityLookup id="rel" value={null} onChange={() => {}} fetchPage={fetchPage} getLabel={(r) => r.name} />, { wrapper });
    openPopup(input());
    await screen.findByText("Rotterdam Freight");
    expect(screen.queryByText("Add new relation…")).toBeNull();
    unmount();

    const onCreateNew = vi.fn();
    render(
      <EntityLookup id="rel" value={null} onChange={() => {}} fetchPage={fetchPage} getLabel={(r) => r.name} onCreateNew={onCreateNew} createLabel="Add new relation…" />,
      { wrapper },
    );
    type(input(), "Zeeland ");
    fireEvent.click(await screen.findByText("Add new relation…"));
    expect(onCreateNew).toHaveBeenCalledWith("Zeeland");
  });

  it("shows the selected name when closed, the clear only when optional, and the pencil only with onEdit", () => {
    const onEdit = vi.fn();
    const { rerender } = render(
      <EntityLookup id="rel" value={relations[0]} onChange={() => {}} fetchPage={makeFetchPage()} getLabel={(r) => r.name} onEdit={onEdit} />,
      { wrapper },
    );
    expect(input()).toHaveValue("Rotterdam Freight");
    expect(document.querySelector("[data-slot=combobox-clear]")).not.toBeNull();
    fireEvent.click(screen.getByRole("button", { name: "Edit" }));
    expect(onEdit).toHaveBeenCalledWith(relations[0]);

    rerender(<EntityLookup id="rel" value={relations[0]} onChange={() => {}} fetchPage={makeFetchPage()} getLabel={(r) => r.name} required />);
    expect(document.querySelector("[data-slot=combobox-clear]")).toBeNull();
    expect(screen.queryByRole("button", { name: "Edit" })).toBeNull();
  });

  it("clears an optional value with the X", () => {
    const onChange = vi.fn();
    render(<EntityLookup id="rel" value={relations[0]} onChange={onChange} fetchPage={makeFetchPage()} getLabel={(r) => r.name} />, { wrapper });
    fireEvent.click(document.querySelector("[data-slot=combobox-clear]")!);
    expect(onChange).toHaveBeenCalledWith(null);
  });

  it("loads the next page when the sentinel becomes visible and more rows exist", async () => {
    const many = Array.from({ length: 30 }, (_, i) => ({ id: String(i), name: `Relation ${i}`, city: "X" }));
    const fetchPage = makeFetchPage(many);
    render(<EntityLookup id="rel" value={null} onChange={() => {}} fetchPage={fetchPage} getLabel={(r) => r.name} />, { wrapper });
    openPopup(input());
    await screen.findByText("Relation 24");
    expect(screen.queryByText("Relation 25")).toBeNull();
    await waitFor(() => expect(ObserverStub.instances.length).toBeGreaterThan(0));

    const observer = ObserverStub.instances[ObserverStub.instances.length - 1];
    act(() => {
      observer.callback([{ isIntersecting: true } as IntersectionObserverEntry], observer as unknown as IntersectionObserver);
    });
    await waitFor(() => expect(fetchPage).toHaveBeenCalledWith({ term: "", skip: 25, take: ENTITY_LOOKUP_PAGE_SIZE }));
    expect(await screen.findByText("Relation 29")).toBeInTheDocument();
    expect(document.querySelector("[data-slot=entity-lookup-sentinel]")).toBeNull();
  });

  it("shows a comma list with +N when multiple, a checkbox per option, and toggles through onChange", async () => {
    const onChange = vi.fn();
    const four = [...relations, { id: "4", name: "Delta Shipping", city: "Delft" }];
    render(<EntityLookup id="rel" multiple value={four} onChange={onChange} fetchPage={makeFetchPage(four)} getLabel={(r) => r.name} />, { wrapper });
    const trigger = screen.getByRole("combobox");
    expect(trigger).toHaveTextContent("Rotterdam Freight, Rhine Logistics, Antwerp Cargo, MoreSelected");
    expect(document.querySelector("[data-slot=combobox-chip]")).toBeNull();

    openPopup(trigger);
    const option = await screen.findByText("Delta Shipping");
    fireEvent.click(option.closest("[role=option]")!);
    expect(onChange).toHaveBeenCalledWith(four.slice(0, 3));

    fireEvent.click(screen.getByRole("button", { name: "Clear" }));
    expect(onChange).toHaveBeenCalledWith([]);
  });
});
