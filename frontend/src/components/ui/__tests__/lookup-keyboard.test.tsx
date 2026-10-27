import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { act, fireEvent, render, screen, waitFor } from "@testing-library/react";
import type { ReactNode } from "react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import { EntityLookup } from "@/components/ui/entity-lookup";
import { ReferenceCombobox } from "@/components/ui/reference-combobox";

/* Keyboard-first picking (design 61 "Order page, legacy tab structure" 3, freight item entry; UI-KEY-004, UI-LOOK-004) against the real
 * Base UI pickers, with the server's answer arriving after the keystrokes as it does in the browser: the first match
 * is highlighted once the results arrive, Enter selects it, Enter waits for results still on their way (and then
 * selects the exact code/name match), Enter opens "Add new …" only for a settled empty result, Tab commits the
 * highlighted match and lets focus move on, Escape never clears the value, and "Add new …" is reachable with
 * ArrowDown. Risk class: Medium (UI flow of the library pickers every form uses). */

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

interface Relation {
  id: string;
  code: string;
  name: string;
}

/* "Testing Co" comes first: the exact match "test" is not simply the first row. */
const relations: Relation[] = [
  { id: "r-testing", code: "TCO", name: "Testing Co" },
  { id: "r-test", code: "TEST", name: "test" },
  { id: "r-acme", code: "ACME", name: "Acme" },
];

/** A server whose answers wait until `release()`; `released` answers at once. */
function gatedServer(source: Relation[] = relations) {
  let open = false;
  let waiting: (() => void)[] = [];
  const fetchPage = vi.fn(({ term, skip, take }: { term: string; skip: number; take: number }) => {
    const answer = () => {
      const t = term.toLowerCase();
      const matches = source.filter((r) => r.name.toLowerCase().includes(t) || r.code.toLowerCase().includes(t));
      return { items: matches.slice(skip, skip + take), totalCount: matches.length };
    };
    if (open) return Promise.resolve(answer());
    return new Promise<ReturnType<typeof answer>>((resolve) => waiting.push(() => resolve(answer())));
  });
  const release = async () => {
    open = true;
    await act(async () => {
      const pending = waiting;
      waiting = [];
      pending.forEach((resolve) => resolve());
      await new Promise((resolve) => setTimeout(resolve, 0));
    });
  };
  return { fetchPage, release };
}

function wrapper({ children }: { children: ReactNode }) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return <QueryClientProvider client={client}>{children}</QueryClientProvider>;
}

const input = () => screen.getByRole("combobox") as HTMLInputElement;
const type = (element: HTMLElement, value: string) => fireEvent.input(element, { target: { value }, inputType: "insertText" });
const highlighted = () => document.querySelector("[role=option][data-highlighted]")?.textContent ?? null;
/** A keydown as the browser sends it; returns whether a handler prevented the default (Tab: focus would not move). */
const press = (element: HTMLElement, key: string) => {
  let prevented = false;
  act(() => {
    prevented = !fireEvent.keyDown(element, { key });
  });
  return prevented;
};
const debounce = () => act(async () => new Promise((resolve) => setTimeout(resolve, 300)));

function Lookup(props: { value?: Relation | null; onChange?: (v: Relation | null) => void; onCreateNew?: (term: string) => void; fetchPage: (q: { term: string; skip: number; take: number }) => Promise<{ items: Relation[]; totalCount: number }> }) {
  return (
    <EntityLookup<Relation>
      id="rel"
      value={props.value ?? null}
      onChange={props.onChange ?? (() => {})}
      fetchPage={props.fetchPage}
      getLabel={(r) => r.name}
      onCreateNew={props.onCreateNew}
      createLabel="Add new relation…"
      required
    />
  );
}

describe("EntityLookup from the keyboard, with results arriving after the keystrokes", () => {
  it("highlights the first match once the results arrive, and Enter selects it", async () => {
    const server = gatedServer();
    const onChange = vi.fn();
    render(<Lookup fetchPage={server.fetchPage} onChange={onChange} onCreateNew={vi.fn()} />, { wrapper });
    type(input(), "acm");
    await debounce();
    await server.release();
    await waitFor(() => expect(highlighted()).toBe("Acme"));

    press(input(), "Enter");
    expect(onChange).toHaveBeenCalledTimes(1);
    expect(onChange.mock.calls[0][0]).toMatchObject({ id: "r-acme" });
  });

  it("Enter before the results arrive waits for them and selects the exact name match, never Add new", async () => {
    const server = gatedServer();
    const onChange = vi.fn();
    const onCreateNew = vi.fn();
    render(<Lookup fetchPage={server.fetchPage} onChange={onChange} onCreateNew={onCreateNew} />, { wrapper });
    type(input(), "test");
    press(input(), "Enter");
    expect(onChange).not.toHaveBeenCalled();
    expect(onCreateNew).not.toHaveBeenCalled();

    await debounce();
    await server.release();
    await waitFor(() => expect(onChange).toHaveBeenCalledTimes(1));
    expect(onChange.mock.calls[0][0]).toMatchObject({ id: "r-test" });
    expect(onCreateNew).not.toHaveBeenCalled();
  });

  it("Enter on a settled empty result opens Add new with the typed term", async () => {
    const server = gatedServer();
    const onChange = vi.fn();
    const onCreateNew = vi.fn();
    render(<Lookup fetchPage={server.fetchPage} onChange={onChange} onCreateNew={onCreateNew} />, { wrapper });
    await server.release();
    type(input(), "zzz");
    await debounce();
    await screen.findByText("NoItemsFound");
    press(input(), "Enter");
    expect(onCreateNew).toHaveBeenCalledWith("zzz");
    expect(onChange).not.toHaveBeenCalled();
  });

  it("Enter before results that come back empty opens Add new once they are in", async () => {
    const server = gatedServer();
    const onCreateNew = vi.fn();
    render(<Lookup fetchPage={server.fetchPage} onCreateNew={onCreateNew} />, { wrapper });
    type(input(), "zzz");
    press(input(), "Enter");
    expect(onCreateNew).not.toHaveBeenCalled();
    await debounce();
    await server.release();
    await waitFor(() => expect(onCreateNew).toHaveBeenCalledWith("zzz"));
  });

  it("Tab commits the highlighted first match among several and lets focus move on", async () => {
    const server = gatedServer();
    const onChange = vi.fn();
    render(<Lookup fetchPage={server.fetchPage} onChange={onChange} onCreateNew={vi.fn()} />, { wrapper });
    type(input(), "te");
    await debounce();
    await server.release();
    await waitFor(() => expect(highlighted()).toBe("Testing Co"));

    expect(press(input(), "Tab")).toBe(false);
    expect(onChange).toHaveBeenCalledTimes(1);
    expect(onChange.mock.calls[0][0]).toMatchObject({ id: "r-testing" });
  });

  it("Tab before the results arrive moves on and commits the exact match when they arrive", async () => {
    const server = gatedServer();
    const onChange = vi.fn();
    const onCreateNew = vi.fn();
    render(<Lookup fetchPage={server.fetchPage} onChange={onChange} onCreateNew={onCreateNew} />, { wrapper });
    type(input(), "test");
    expect(press(input(), "Tab")).toBe(false);
    await debounce();
    await server.release();
    await waitFor(() => expect(onChange).toHaveBeenCalledTimes(1));
    expect(onChange.mock.calls[0][0]).toMatchObject({ id: "r-test" });
    expect(onCreateNew).not.toHaveBeenCalled();
  });

  it("Tab without a typed term commits nothing", async () => {
    const server = gatedServer();
    const onChange = vi.fn();
    render(<Lookup fetchPage={server.fetchPage} onChange={onChange} />, { wrapper });
    expect(press(input(), "Tab")).toBe(false);
    await server.release();
    expect(onChange).not.toHaveBeenCalled();
  });

  it("ArrowDown past the matches reaches Add new; Enter there opens it with the term", async () => {
    const server = gatedServer();
    const onChange = vi.fn();
    const onCreateNew = vi.fn();
    render(<Lookup fetchPage={server.fetchPage} onChange={onChange} onCreateNew={onCreateNew} />, { wrapper });
    await server.release();
    type(input(), "acme");
    await debounce();
    await waitFor(() => expect(highlighted()).toBe("Acme"));
    press(input(), "ArrowDown");
    await waitFor(() => expect(highlighted()).toBe("Add new relation…"));
    press(input(), "Enter");
    expect(onCreateNew).toHaveBeenCalledWith("acme");
    expect(onChange).not.toHaveBeenCalled();
  });

  it("Escape keeps the selected value (open: closes the list; closed: does nothing)", async () => {
    const server = gatedServer();
    const onChange = vi.fn();
    render(<Lookup fetchPage={server.fetchPage} value={relations[1]} onChange={onChange} />, { wrapper });
    press(input(), "Escape");
    expect(onChange).not.toHaveBeenCalled();
    expect(input().value).toBe("test");
  });

  it("the list button is no tab stop: Tab goes from the input to the next field", () => {
    const server = gatedServer();
    render(<Lookup fetchPage={server.fetchPage} />, { wrapper });
    const buttons = input().closest("[data-slot=input-group]")!.querySelectorAll("button");
    expect(buttons.length).toBeGreaterThan(0);
    buttons.forEach((b) => expect(b.tabIndex).toBe(-1));
  });
});

describe("ReferenceCombobox from the keyboard", () => {
  const units = [
    { value: "u-box", label: "Box pallet" },
    { value: "u-pal", label: "Pallet" },
    { value: "u-crate", label: "Crate" },
  ];

  function Units(props: { value?: string | null; onChange?: (v: string | null) => void; onCreateNew?: (term: string) => void }) {
    return (
      <ReferenceCombobox
        id="unit"
        value={props.value ?? null}
        onChange={props.onChange ?? (() => {})}
        options={units}
        onCreateNew={props.onCreateNew}
        createLabel="Add new transport unit…"
        required
      />
    );
  }

  it("highlights the first match while typing, and Enter selects it", async () => {
    const onChange = vi.fn();
    render(<Units onChange={onChange} onCreateNew={vi.fn()} />);
    type(input(), "pal");
    await waitFor(() => expect(highlighted()).toBe("Box pallet"));
    press(input(), "Enter");
    expect(onChange).toHaveBeenCalledWith("u-box");
  });

  it("Enter on a term no option contains opens Add new with it", async () => {
    const onChange = vi.fn();
    const onCreateNew = vi.fn();
    render(<Units onChange={onChange} onCreateNew={onCreateNew} />);
    type(input(), "Euro pallet");
    await waitFor(() => expect(screen.queryByText("Box pallet")).toBeNull());
    press(input(), "Enter");
    expect(onCreateNew).toHaveBeenCalledWith("Euro pallet");
    expect(onChange).not.toHaveBeenCalled();
  });

  it("Tab commits the highlighted match and lets focus move on", async () => {
    const onChange = vi.fn();
    render(<Units onChange={onChange} onCreateNew={vi.fn()} />);
    type(input(), "cra");
    await waitFor(() => expect(highlighted()).toBe("Crate"));
    expect(press(input(), "Tab")).toBe(false);
    expect(onChange).toHaveBeenCalledWith("u-crate");
  });

  it("ArrowDown past the matches reaches Add new; Enter there opens it", async () => {
    const onChange = vi.fn();
    const onCreateNew = vi.fn();
    render(<Units onChange={onChange} onCreateNew={onCreateNew} />);
    type(input(), "crate");
    await waitFor(() => expect(highlighted()).toBe("Crate"));
    press(input(), "ArrowDown");
    await waitFor(() => expect(highlighted()).toBe("Add new transport unit…"));
    press(input(), "Enter");
    expect(onCreateNew).toHaveBeenCalledWith("crate");
    expect(onChange).not.toHaveBeenCalled();
  });

  it("Escape keeps the selected value", () => {
    const onChange = vi.fn();
    render(<Units value="u-pal" onChange={onChange} />);
    press(input(), "Escape");
    expect(onChange).not.toHaveBeenCalled();
    expect(input().value).toBe("Pallet");
  });
});
