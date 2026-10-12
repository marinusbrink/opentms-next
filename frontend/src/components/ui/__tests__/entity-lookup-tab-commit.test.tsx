import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import type { ReactNode } from "react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import { EntityLookup } from "@/components/ui/entity-lookup";

/* UI-KEY-004 "Tab-to-commit" for the entity picker (design 43): Base UI closes the popup on Tab
 * without selecting, so the lookup hands over the highlighted item, or the only match of a typed
 * term, before the popup closes. Risk class: Medium (UI flow; the rule requires a component test). */

vi.mock("@/lib/i18n/LocalizationProvider", () => ({
  useL: () => ({ t: (k: string) => k, culture: "en", isLoading: false }),
}));

interface Relation {
  id: string;
  name: string;
}

const relations: Relation[] = [
  { id: "1", name: "Rotterdam Freight" },
  { id: "2", name: "Rhine Logistics" },
  { id: "3", name: "Antwerp Cargo" },
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
  observe() {}
  disconnect() {}
  unobserve() {}
}

const input = () => screen.getByRole("combobox");
const openPopup = (element: HTMLElement) => {
  fireEvent.pointerDown(element);
  fireEvent.mouseDown(element);
  fireEvent.click(element);
};
const type = (element: HTMLElement, value: string) => fireEvent.input(element, { target: { value }, inputType: "insertText" });

describe("EntityLookup Tab-to-commit (UI-KEY-004)", () => {
  beforeEach(() => {
    vi.stubGlobal("IntersectionObserver", ObserverStub);
  });
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it("commits the highlighted item when Tab leaves the control (ArrowDown, then Tab)", async () => {
    const onChange = vi.fn();
    render(<EntityLookup id="rel" value={null} onChange={onChange} fetchPage={makeFetchPage()} getLabel={(r) => r.name} />, { wrapper });
    openPopup(input());
    await screen.findByText("Rotterdam Freight");

    fireEvent.keyDown(input(), { key: "ArrowDown" });
    await waitFor(() => expect(document.querySelector("[role=option][data-highlighted]")).not.toBeNull());
    const highlighted = document.querySelector("[role=option][data-highlighted]")!.textContent;
    expect(onChange).not.toHaveBeenCalled();

    fireEvent.keyDown(input(), { key: "Tab" });
    expect(onChange).toHaveBeenCalledTimes(1);
    expect(onChange.mock.calls[0][0].name).toBe(highlighted);
  });

  it("commits the only match of a typed term on Tab, without a highlight", async () => {
    const onChange = vi.fn();
    const fetchPage = makeFetchPage();
    render(<EntityLookup id="rel" value={null} onChange={onChange} fetchPage={fetchPage} getLabel={(r) => r.name} />, { wrapper });
    type(input(), "rhine");
    await waitFor(() => expect(fetchPage).toHaveBeenCalledWith({ term: "rhine", skip: 0, take: 25 }));
    await screen.findByText("Rhine Logistics");
    expect(screen.queryByText("Rotterdam Freight")).toBeNull();

    fireEvent.keyDown(input(), { key: "Tab" });
    expect(onChange).toHaveBeenCalledWith(expect.objectContaining({ id: "2", name: "Rhine Logistics" }));
  });

  it("commits nothing on Tab when several items match and none is highlighted, or when the popup is closed", async () => {
    const onChange = vi.fn();
    const fetchPage = makeFetchPage();
    render(<EntityLookup id="rel" value={null} onChange={onChange} fetchPage={fetchPage} getLabel={(r) => r.name} />, { wrapper });

    fireEvent.keyDown(input(), { key: "Tab" });
    expect(onChange).not.toHaveBeenCalled();

    type(input(), "e");
    type(input(), "er");
    await waitFor(() => expect(fetchPage).toHaveBeenCalledWith({ term: "er", skip: 0, take: 25 }));
    await screen.findByText("Rotterdam Freight");
    expect(screen.getByText("Antwerp Cargo")).toBeInTheDocument();
    expect(screen.queryByText("Rhine Logistics")).toBeNull();

    fireEvent.keyDown(input(), { key: "Tab" });
    expect(onChange).not.toHaveBeenCalled();
  });

  it("does not re-commit the value that is already selected", async () => {
    const onChange = vi.fn();
    render(<EntityLookup id="rel" value={relations[0]} onChange={onChange} fetchPage={makeFetchPage([relations[0]])} getLabel={(r) => r.name} />, { wrapper });
    openPopup(input());
    await screen.findByRole("option");
    fireEvent.keyDown(input(), { key: "ArrowDown" });
    fireEvent.keyDown(input(), { key: "Tab" });
    expect(onChange).not.toHaveBeenCalled();
  });

  it("never commits on Tab in multiple mode", async () => {
    const onChange = vi.fn();
    render(<EntityLookup id="rel" multiple value={[]} onChange={onChange} fetchPage={makeFetchPage([relations[1]])} getLabel={(r) => r.name} />, { wrapper });
    const trigger = screen.getByRole("combobox");
    openPopup(trigger);
    await screen.findByText("Rhine Logistics");
    fireEvent.keyDown(screen.getByRole("combobox", { name: "Search" }), { key: "Tab" });
    expect(onChange).not.toHaveBeenCalled();
  });
});
