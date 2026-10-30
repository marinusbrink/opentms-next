import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { act, fireEvent, render, screen } from "@testing-library/react";
import type { ReactNode } from "react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import { EntityLookup } from "@/components/ui/entity-lookup";
import { ReferenceCombobox } from "@/components/ui/reference-combobox";
import { buildTree, TreeSelect } from "@/components/ui/tree-select";

/* The edit pencil of UI-LOOK-005 (design 61 "Order page review" 2) in the three single-value pickers: shown only while a
 * value is selected and `onEdit` is given, labelled with `editLabel` ("Edit", or "Open" for a read-only record), no Tab
 * stop (tabindex -1, Tab goes from the input to the next field, UI-KEY-004), usable in a disabled control (opening the
 * record changes nothing in the form), handing over the selected value; EntityLookup shows `editedLabel` (the name the
 * record got in the editor) instead of its own label. Risk class: Medium (UI flow of the library pickers). */

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
  vi.stubGlobal("ResizeObserver", ObserverStub);
});
afterEach(() => vi.unstubAllGlobals());

const wrapper = ({ children }: { children: ReactNode }) => <QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}>{children}</QueryClientProvider>;

const OPTIONS = [
  { value: "c-eur", label: "EUR Euro" },
  { value: "c-usd", label: "USD US dollar" },
];

const relations = [{ id: "r-1", name: "Rotterdam Freight" }];
const fetchPage = () => Promise.resolve({ items: relations, totalCount: relations.length });

const pencil = (name: string) => screen.queryByRole("button", { name });

describe("ReferenceCombobox — the pencil", () => {
  it("shows only with a value and onEdit, labelled by editLabel, as no Tab stop, and hands over the value", () => {
    const onEdit = vi.fn();
    const { rerender } = render(<ReferenceCombobox id="cur" value={null} onChange={() => {}} options={OPTIONS} onEdit={onEdit} editLabel="Open" />);
    expect(pencil("Open")).toBeNull();

    rerender(<ReferenceCombobox id="cur" value="c-usd" onChange={() => {}} options={OPTIONS} onEdit={onEdit} editLabel="Open" />);
    const button = pencil("Open")!;
    expect(button).toHaveAttribute("tabindex", "-1");
    fireEvent.click(button);
    expect(onEdit).toHaveBeenCalledWith("c-usd");

    rerender(<ReferenceCombobox id="cur" value="c-usd" onChange={() => {}} options={OPTIONS} />);
    expect(screen.queryByRole("button", { name: "Edit" })).toBeNull();
    expect(pencil("Open")).toBeNull();
  });

  it("defaults the label to Edit and works in a disabled control", () => {
    const onEdit = vi.fn();
    render(<ReferenceCombobox id="cur" value="c-eur" onChange={() => {}} options={OPTIONS} onEdit={onEdit} disabled />);
    const button = pencil("Edit")!;
    expect(button).not.toBeDisabled();
    fireEvent.click(button);
    expect(onEdit).toHaveBeenCalledWith("c-eur");
  });

  it("Tab from the input skips the pencil", () => {
    render(
      <>
        <ReferenceCombobox id="cur" value="c-eur" onChange={() => {}} options={OPTIONS} onEdit={() => {}} />
        <input aria-label="next" />
      </>,
    );
    const focusable = Array.from(document.querySelectorAll<HTMLElement>("input, button")).filter((el) => el.tabIndex >= 0 && !el.hasAttribute("disabled"));
    expect(focusable).not.toContain(pencil("Edit"));
    expect(focusable).toContain(screen.getByLabelText("next"));
  });
});

describe("EntityLookup — the pencil", () => {
  it("is labelled by editLabel, no Tab stop and usable while disabled", () => {
    const onEdit = vi.fn();
    render(<EntityLookup id="rel" value={relations[0]} onChange={() => {}} fetchPage={fetchPage} getLabel={(r) => r.name} onEdit={onEdit} editLabel="Open" disabled />, { wrapper });
    const button = pencil("Open")!;
    expect(button).toHaveAttribute("tabindex", "-1");
    expect(button).not.toBeDisabled();
    fireEvent.click(button);
    expect(onEdit).toHaveBeenCalledWith(relations[0]);
  });

  it("shows the name the record got in the editor instead of the value's own label", () => {
    const { rerender } = render(<EntityLookup id="rel" value={relations[0]} onChange={() => {}} fetchPage={fetchPage} getLabel={(r) => r.name} onEdit={() => {}} editedLabel={() => undefined} />, { wrapper });
    expect(screen.getByRole("combobox")).toHaveValue("Rotterdam Freight");
    rerender(<EntityLookup id="rel" value={relations[0]} onChange={() => {}} fetchPage={fetchPage} getLabel={(r) => r.name} onEdit={() => {}} editedLabel={(id) => (id === "r-1" ? "Rotterdam Freight B.V." : undefined)} />);
    expect(screen.getByRole("combobox")).toHaveValue("Rotterdam Freight B.V.");
  });

  it("shows no pencil without a value", () => {
    render(<EntityLookup id="rel" value={null} onChange={() => {}} fetchPage={fetchPage} getLabel={(r: { id: string; name: string }) => r.name} onEdit={() => {}} />, { wrapper });
    expect(pencil("Edit")).toBeNull();
  });
});

describe("TreeSelect — the pencil", () => {
  const nodes = buildTree(
    [
      { id: "root", name: "Head office", parentId: null },
      { id: "plan", name: "Planning", parentId: "root" },
    ],
    (u) => u.id,
    (u) => u.parentId,
    (u) => u.name,
  );

  it("shows only with a selected node and onEdit, is no Tab stop, hands over the id and works while disabled", async () => {
    const onEdit = vi.fn();
    const { rerender } = render(<TreeSelect id="ou" nodes={nodes} value={null} onChange={() => {}} onEdit={onEdit} editLabel="Open" />);
    expect(pencil("Open")).toBeNull();

    rerender(<TreeSelect id="ou" nodes={nodes} value="plan" onChange={() => {}} onEdit={onEdit} editLabel="Open" disabled />);
    const button = pencil("Open")!;
    expect(button).toHaveAttribute("tabindex", "-1");
    await act(async () => {
      fireEvent.click(button);
    });
    expect(onEdit).toHaveBeenCalledWith("plan");
    expect(screen.getByRole("combobox")).toHaveTextContent("Planning");

    rerender(<TreeSelect id="ou" nodes={nodes} value="plan" onChange={() => {}} />);
    expect(screen.queryByRole("button", { name: "Edit" })).toBeNull();
  });
});
