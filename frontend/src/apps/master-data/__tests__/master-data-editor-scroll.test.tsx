import { render, screen } from "@testing-library/react";
import { beforeAll, describe, expect, it, vi } from "vitest";

/* MasterDataEditor under UI-DLG-006 (PO 2026-10-21): it passes `panelScroll` through to EntityDialog (the fixed
 * height h-[92vh], only when asked), and gives every tab a full-height content column: the first tab's <form> and the
 * body wrapper of every tab are `h-full min-h-0`, so a tab can keep part of itself fixed and scroll a region while the
 * alerts and the notice keep their height. The dialog body stays overflow-hidden and the panels scroll, as the shell
 * guarantees. Real EntityDialog; the query client and the toasts are mocked. Risk class: Medium (UI) — the editor of
 * every Master Data entity. */

vi.mock("@/lib/i18n/LocalizationProvider", () => ({
  useL: () => ({ t: (key: string) => key, culture: "en", isLoading: false }),
}));
vi.mock("@/lib/abp/queries", () => ({
  useApplicationConfiguration: () => ({ data: { features: { values: {} } } }),
}));
vi.mock("@tanstack/react-query", () => ({
  useQueryClient: () => ({ invalidateQueries: vi.fn() }),
}));
vi.mock("sonner", () => ({ toast: { success: vi.fn(), error: vi.fn() } }));

class MockResizeObserver {
  observe() {}
  disconnect() {}
  unobserve() {}
}
beforeAll(() => vi.stubGlobal("ResizeObserver", MockResizeObserver));

import { MasterDataEditor, type EditorTab } from "@/components/record-editor/MasterDataEditor";

interface Values {
  name: string;
}
interface Dto {
  id?: string;
  name?: string;
  concurrencyStamp?: string | null;
  creationTime?: string;
  lastModificationTime?: string | null;
}

const TABS: EditorTab<Values>[] = [
  { value: "general", labelKey: "General", fields: ["name"], render: () => <p data-testid="general-content">General content</p> },
  { value: "lines", labelKey: "Lines", fields: [], render: () => <p data-testid="lines-content">Lines content</p> },
];

function renderEditor(props: { panelScroll?: boolean; mode?: "create" | "edit"; detail?: { data: Dto | undefined; isLoading: boolean; error: unknown }; notice?: React.ReactNode } = {}) {
  return render(
    <MasterDataEditor<Values, Dto>
      entityKey="Product"
      queryEntity="products"
      size="md"
      mode={props.mode ?? "create"}
      readOnly={false}
      open
      onClose={() => {}}
      onSaved={() => {}}
      detail={props.detail ?? { data: undefined, isLoading: false, error: null }}
      titleOf={(dto) => dto.name ?? ""}
      initialValues={() => ({ name: "" })}
      validate={() => ({})}
      save={async () => ({})}
      isSaving={false}
      tabs={TABS}
      notice={props.notice}
      panelScroll={props.panelScroll}
    />,
  );
}

const content = () => document.body.querySelector('[data-variant="editor"]') as HTMLElement;
const body = () => document.body.querySelector('[data-slot="entity-dialog-body"]') as HTMLElement;
const panel = (label: string) => document.body.querySelector(`[role="tabpanel"][aria-label="${label}"]`) as HTMLElement;
const form = () => content().querySelector("form") as HTMLFormElement;
const classes = (el: Element) => (el.getAttribute("class") ?? "").split(/\s+/).filter(Boolean);
const COLUMN = ["flex", "h-full", "min-h-0", "flex-col"];

describe("MasterDataEditor – panelScroll and the full-height tab columns (UI-DLG-006)", () => {
  it("passes panelScroll through to EntityDialog: the fixed height only when asked", () => {
    const on = renderEditor({ panelScroll: true });
    expect(classes(content())).toContain("h-[92vh]");
    expect(classes(body())).toContain("overflow-hidden");
    on.unmount();

    const off = renderEditor({ panelScroll: false });
    expect(classes(content())).not.toContain("h-[92vh]");
    expect(classes(body())).toContain("overflow-hidden");
    off.unmount();

    renderEditor();
    expect(classes(content())).not.toContain("h-[92vh]");
  });

  it("the first tab's form is h-full min-h-0 and its body wrapper a full-height column, so the tab can scroll a region of itself", () => {
    renderEditor({ panelScroll: true });
    const general = panel("General");
    expect(classes(general)).toEqual(expect.arrayContaining(["min-h-0", "overflow-y-auto"]));
    expect(form().parentElement).toBe(general);
    expect(classes(form())).toEqual(expect.arrayContaining(["h-full", "min-h-0"]));
    const column = screen.getByTestId("general-content").parentElement!;
    expect(column.parentElement).toBe(form());
    expect(classes(column)).toEqual(expect.arrayContaining(COLUMN));
  });

  it("every other tab's body wrapper is the same full-height column, directly in its panel", () => {
    renderEditor({ panelScroll: true });
    const lines = panel("Lines");
    const column = screen.getByTestId("lines-content").parentElement!;
    expect(column.parentElement).toBe(lines);
    expect(classes(column)).toEqual(expect.arrayContaining(COLUMN));
    expect(lines.querySelector("form")).toBeNull();
  });

  it("the columns are the same without panelScroll (the shell decides the height, the editor the column)", () => {
    renderEditor();
    expect(classes(form())).toEqual(expect.arrayContaining(["h-full", "min-h-0"]));
    expect(classes(screen.getByTestId("general-content").parentElement!)).toEqual(expect.arrayContaining(COLUMN));
    expect(classes(screen.getByTestId("lines-content").parentElement!)).toEqual(expect.arrayContaining(COLUMN));
  });

  it("the notice sits in the first tab's column above the content, keeping its own height", () => {
    renderEditor({ panelScroll: true, notice: <p data-testid="notice">Defaults failed</p> });
    const column = screen.getByTestId("general-content").parentElement!;
    expect(screen.getByTestId("notice").parentElement).toBe(column);
    expect(Array.from(column.children).indexOf(screen.getByTestId("notice"))).toBeLessThan(Array.from(column.children).indexOf(screen.getByTestId("general-content")));
    expect(panel("Lines").querySelector('[data-testid="notice"]')).toBeNull();
  });

  it("while the record loads the skeleton takes the column's place, the form keeps its height classes and the dialog body stays overflow-hidden", () => {
    renderEditor({ mode: "edit", panelScroll: true, detail: { data: undefined, isLoading: true, error: null } });
    expect(classes(form())).toEqual(expect.arrayContaining(["h-full", "min-h-0"]));
    expect(form().querySelector('[data-slot="skeleton"]')).not.toBeNull();
    expect(screen.queryByTestId("general-content")).toBeNull();
    expect(classes(body())).toContain("overflow-hidden");
    expect(classes(content())).toContain("h-[92vh]");
  });
});
