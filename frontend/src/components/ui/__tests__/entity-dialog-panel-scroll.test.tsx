import { render, screen } from "@testing-library/react";
import { Save } from "lucide-react";
import { describe, expect, it, vi } from "vitest";

import { EntityDialog, type EntityDialogTab } from "@/components/ui/entity-dialog";

/* EntityDialog's opt-in panelScroll (PO 2026-10-14: the edition dialog itself never scrolls vertically; only a region
 * inside a tab may). With panelScroll and tabs the dialog gets a fixed height, the body stops scrolling and becomes a
 * one-row grid whose row may shrink, and every panel scrolls on its own; UI-DLG-015 still holds (every panel in the
 * same grid cell). Without the prop nothing changes. Risk class: Medium (UI) — the dialog shell of every editor. */

vi.mock("@/lib/i18n/LocalizationProvider", () => ({
  useL: () => ({ t: (key: string) => key, culture: "en", isLoading: false }),
}));
vi.mock("@/lib/abp/queries", () => ({
  useApplicationConfiguration: () => ({ data: { features: { values: {} } } }),
}));

class MockResizeObserver {
  observe() {}
  disconnect() {}
  unobserve() {}
}
vi.stubGlobal("ResizeObserver", MockResizeObserver);

const TABS: EntityDialogTab[] = [
  { value: "general", label: "General", content: <p>General body</p> },
  { value: "features", label: "Features", content: <p>Features body</p> },
];

function renderDialog(props: { panelScroll?: boolean; tabs?: EntityDialogTab[] }) {
  render(
    <EntityDialog
      open
      onOpenChange={() => {}}
      variant="editor"
      size="md"
      title="Edition Gold"
      commands={[{ id: "save", labelKey: "Save", icon: Save, isPrimary: true, onClick: () => {} }]}
      tabs={props.tabs}
      panelScroll={props.panelScroll}
    >
      {props.tabs ? undefined : <p>Plain body</p>}
    </EntityDialog>,
  );
}

const body = () => document.body.querySelector('[data-slot="entity-dialog-body"]') as HTMLElement;
const content = () => document.body.querySelector('[data-variant="editor"]') as HTMLElement;
const panels = () => screen.getAllByRole("tabpanel", { hidden: true });
const classes = (el: HTMLElement) => el.className.split(/\s+/);

describe("EntityDialog – panelScroll", () => {
  it("with tabs: fixed dialog height, a non-scrolling body as a shrinkable one-row grid, every panel scrolling", () => {
    renderDialog({ panelScroll: true, tabs: TABS });

    expect(classes(content())).toContain("h-[92vh]");
    expect(classes(content())).toContain("max-h-[92vh]");

    expect(classes(body())).toContain("overflow-hidden");
    expect(classes(body())).not.toContain("overflow-auto");
    expect(classes(body())).toContain("grid");
    expect(classes(body())).toContain("grid-rows-[minmax(0,1fr)]");
    expect(classes(body())).not.toContain("content-start");

    expect(panels()).toHaveLength(2);
    for (const panel of panels()) {
      expect(classes(panel)).toEqual(expect.arrayContaining(["min-h-0", "overflow-y-auto"]));
      /* UI-DLG-015: one grid cell for every panel, so the height does not change between tabs. */
      expect(classes(panel)).toEqual(expect.arrayContaining(["col-start-1", "row-start-1"]));
    }
  });

  it("without the prop a tabbed dialog keeps the default: scrolling body, content-start grid, plain panels, no fixed height", () => {
    renderDialog({ tabs: TABS });

    expect(classes(content())).not.toContain("h-[92vh]");
    expect(classes(content())).toContain("max-h-[92vh]");
    expect(classes(body())).toContain("overflow-auto");
    expect(classes(body())).not.toContain("overflow-hidden");
    expect(classes(body())).toEqual(expect.arrayContaining(["grid", "content-start"]));
    expect(classes(body())).not.toContain("grid-rows-[minmax(0,1fr)]");
    for (const panel of panels()) {
      expect(classes(panel)).not.toContain("overflow-y-auto");
      expect(classes(panel)).not.toContain("min-h-0");
      expect(classes(panel)).toEqual(expect.arrayContaining(["col-start-1", "row-start-1"]));
    }
  });

  it("panelScroll on a dialog without tabs changes nothing", () => {
    renderDialog({ panelScroll: true });

    expect(screen.getByText("Plain body")).toBeInTheDocument();
    expect(classes(content())).not.toContain("h-[92vh]");
    expect(classes(body())).toContain("overflow-auto");
    expect(classes(body())).not.toContain("overflow-hidden");
    expect(classes(body())).not.toContain("grid");
  });

  it("panelScroll={false} is the default", () => {
    renderDialog({ panelScroll: false, tabs: TABS });
    expect(classes(body())).toContain("overflow-auto");
    expect(classes(content())).not.toContain("h-[92vh]");
  });
});
