import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import type { ReactNode } from "react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import { EntityLookup } from "@/components/ui/entity-lookup";
import { ReferenceCombobox } from "@/components/ui/reference-combobox";

/* UI-LOOK-004 from the keyboard (design 61 "Order page, revised" 3: in the fast entry row an unknown relation or unit
 * offers "Add new …" from the keyboard): Enter on a typed term with no match opens the create path with that term,
 * as a click on the row does; with a match (or without onCreateNew) Enter keeps its normal meaning and creates
 * nothing. The real Base UI components run here. Risk class: Medium. */

vi.mock("@/lib/i18n/LocalizationProvider", () => ({
  useL: () => ({ t: (k: string) => k, culture: "en", isLoading: false }),
}));

class ObserverStub {
  observe() {}
  disconnect() {}
  unobserve() {}
}
beforeEach(() => vi.stubGlobal("IntersectionObserver", ObserverStub));
afterEach(() => vi.unstubAllGlobals());

const openPopup = (element: HTMLElement) => {
  fireEvent.pointerDown(element);
  fireEvent.mouseDown(element);
  fireEvent.click(element);
};
const type = (element: HTMLElement, value: string) => fireEvent.input(element, { target: { value }, inputType: "insertText" });

function wrapper({ children }: { children: ReactNode }) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return <QueryClientProvider client={client}>{children}</QueryClientProvider>;
}

describe("EntityLookup — Enter on an unknown term creates", () => {
  const fetchPage = vi.fn(async ({ term }: { term: string }) => {
    const all = [{ id: "1", name: "Rotterdam Freight" }];
    const items = all.filter((r) => r.name.toLowerCase().includes(term.toLowerCase()));
    return { items, totalCount: items.length };
  });

  it("hands the typed term to onCreateNew once the search found nothing, and closes the list", async () => {
    const onCreateNew = vi.fn();
    const onOuterKey = vi.fn();
    render(
      <div onKeyDown={onOuterKey}>
        <EntityLookup id="rel" value={null} onChange={() => {}} fetchPage={fetchPage} getLabel={(r) => r.name} onCreateNew={onCreateNew} />
      </div>,
      { wrapper },
    );
    const input = screen.getByRole("combobox");
    type(input, "Nowhere BV");
    await waitFor(() => expect(fetchPage).toHaveBeenCalledWith(expect.objectContaining({ term: "Nowhere BV" })));
    await screen.findByText("NoItemsFound");
    fireEvent.keyDown(input, { key: "Enter" });
    expect(onCreateNew).toHaveBeenCalledWith("Nowhere BV");
    /* The page's own Enter (the entry row adds an item) does not see it. */
    expect(onOuterKey).not.toHaveBeenCalled();
    await waitFor(() => expect(input).toHaveAttribute("aria-expanded", "false"));
  });

  it("creates nothing on Enter when the term has a match, or without onCreateNew", async () => {
    const onCreateNew = vi.fn();
    const { unmount } = render(<EntityLookup id="rel" value={null} onChange={() => {}} fetchPage={fetchPage} getLabel={(r) => r.name} onCreateNew={onCreateNew} />, { wrapper });
    type(screen.getByRole("combobox"), "rotter");
    await screen.findByText("Rotterdam Freight");
    fireEvent.keyDown(screen.getByRole("combobox"), { key: "Enter" });
    expect(onCreateNew).not.toHaveBeenCalled();
    unmount();

    render(<EntityLookup id="rel" value={null} onChange={() => {}} fetchPage={fetchPage} getLabel={(r) => r.name} />, { wrapper });
    type(screen.getByRole("combobox"), "Nowhere BV");
    await screen.findByText("NoItemsFound");
    fireEvent.keyDown(screen.getByRole("combobox"), { key: "Enter" });
    expect(onCreateNew).not.toHaveBeenCalled();
  });
});

describe("ReferenceCombobox — Enter on an unknown term creates", () => {
  const options = [{ value: "p", label: "Pallet" }];

  it("hands the typed term to onCreateNew when no option contains it; with a match Enter creates nothing", () => {
    const onCreateNew = vi.fn();
    render(<ReferenceCombobox id="unit" value={null} onChange={() => {}} options={options} onCreateNew={onCreateNew} />);
    const input = screen.getByRole("combobox");
    openPopup(input);
    type(input, "pal");
    fireEvent.keyDown(input, { key: "Enter" });
    expect(onCreateNew).not.toHaveBeenCalled();
    type(input, "Crate");
    fireEvent.keyDown(input, { key: "Enter" });
    expect(onCreateNew).toHaveBeenCalledWith("Crate");
  });
});
