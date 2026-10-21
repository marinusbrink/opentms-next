import { render, screen } from "@testing-library/react";
import { Save } from "lucide-react";
import { describe, expect, it, vi } from "vitest";

import { EntityDialog, type EntityDialogTab } from "@/components/ui/entity-dialog";

/* EntityDialog's panelScroll under UI-DLG-006 (PO 2026-10-21: the dialog never scrolls; a tab panel or a content
 * section scrolls). Scrolling is no longer opt-in: with or without the prop the body is overflow-hidden, every tab
 * panel scrolls its own content inside a shrinkable one-row grid (UI-DLG-015 kept: every panel in the same grid cell),
 * and a dialog without tabs scrolls its one section. panelScroll only fixes the dialog's height (h-[92vh]) so a panel
 * can keep part of itself fixed and scroll a region; it is ignored without tabs and defaults to false.
 * Risk class: Medium (UI) — the dialog shell of every editor. */

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
  return render(
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
const section = () => document.body.querySelector('[data-slot="entity-dialog-section"]') as HTMLElement | null;
const content = () => document.body.querySelector('[data-variant="editor"]') as HTMLElement;
const panels = () => screen.getAllByRole("tabpanel", { hidden: true });
const classes = (el: HTMLElement) => (el.getAttribute("class") ?? "").split(/\s+/).filter(Boolean);

describe("EntityDialog – panelScroll under UI-DLG-006", () => {
  it("the body never scrolls: overflow-hidden and no overflow-auto, with tabs or without, with panelScroll or without", () => {
    for (const props of [{ tabs: TABS }, { tabs: TABS, panelScroll: true }, {}, { panelScroll: true }]) {
      const { unmount } = renderDialog(props);
      expect(classes(body()), JSON.stringify(props)).toContain("overflow-hidden");
      expect(classes(body()), JSON.stringify(props)).not.toContain("overflow-auto");
      expect(classes(body()), JSON.stringify(props)).not.toContain("overflow-y-auto");
      unmount();
    }
  });

  it("with tabs every panel always scrolls its own content in a shrinkable one-row grid; panelScroll adds only the fixed height", () => {
    for (const panelScroll of [false, true]) {
      const { unmount } = renderDialog({ panelScroll, tabs: TABS });

      expect(classes(content())).toContain("max-h-[92vh]");
      expect(classes(content()).includes("h-[92vh]"), `h-[92vh] with panelScroll=${panelScroll}`).toBe(panelScroll);

      expect(classes(body())).toEqual(expect.arrayContaining(["grid", "grid-rows-[minmax(0,1fr)]", "min-h-0", "flex-1"]));
      expect(classes(body())).not.toContain("content-start");

      expect(panels()).toHaveLength(2);
      for (const panel of panels()) {
        expect(classes(panel)).toEqual(expect.arrayContaining(["min-h-0", "overflow-y-auto"]));
        /* UI-DLG-015: one grid cell for every panel, so the height does not change between tabs. */
        expect(classes(panel)).toEqual(expect.arrayContaining(["col-start-1", "row-start-1"]));
      }
      expect(section()).toBeNull();
      unmount();
    }
  });

  it("without tabs the one content section scrolls; panelScroll changes nothing there", () => {
    for (const panelScroll of [false, true]) {
      const { unmount } = renderDialog({ panelScroll });

      expect(screen.getByText("Plain body")).toBeInTheDocument();
      expect(classes(content())).not.toContain("h-[92vh]");
      expect(classes(content())).toContain("max-h-[92vh]");
      expect(classes(body())).toEqual(expect.arrayContaining(["flex", "flex-col", "overflow-hidden"]));
      expect(classes(body())).not.toContain("grid");

      const region = section();
      expect(region).not.toBeNull();
      expect(region!.parentElement).toBe(body());
      expect(classes(region!)).toEqual(expect.arrayContaining(["min-h-0", "flex-1", "overflow-y-auto"]));
      expect(region!.textContent).toBe("Plain body");
      expect(screen.queryAllByRole("tabpanel", { hidden: true })).toHaveLength(0);
      unmount();
    }
  });

  it("panelScroll only adds the fixed height: body and panels are class-for-class identical with and without it", () => {
    const without = renderDialog({ tabs: TABS });
    const bodyWithout = classes(body());
    const panelsWithout = panels().map(classes);
    const contentWithout = classes(content());
    without.unmount();

    renderDialog({ tabs: TABS, panelScroll: true });
    expect(classes(body())).toEqual(bodyWithout);
    expect(panels().map(classes)).toEqual(panelsWithout);
    expect(classes(content()).filter((c) => c !== "h-[92vh]")).toEqual(contentWithout);
    expect(classes(content())).toContain("h-[92vh]");
  });

  it("panelScroll={false} is the default: the same output as omitting the prop", () => {
    const omitted = renderDialog({ tabs: TABS });
    const contentOmitted = classes(content());
    const bodyOmitted = classes(body());
    omitted.unmount();

    renderDialog({ panelScroll: false, tabs: TABS });
    expect(classes(content())).toEqual(contentOmitted);
    expect(classes(content())).not.toContain("h-[92vh]");
    expect(classes(body())).toEqual(bodyOmitted);
    expect(classes(body())).toContain("overflow-hidden");
  });
});
