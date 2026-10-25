import { render } from "@testing-library/react";
import { Save } from "lucide-react";
import { describe, expect, it, vi } from "vitest";

import { EntityDialog, type EntityDialogProps, type EntityDialogSize, type EntityDialogTab } from "@/components/ui/entity-dialog";

/* Guard for UI-DLG-006 (PO 2026-10-21, docs/conventions/ui/ui-rules/dialog.md): the dialog never scrolls; a tab panel
 * or a content section scrolls. Over every variant EntityDialog has (editor / action, tabs / no tabs, panelScroll on /
 * off, the size tiers): no element of the popup carries overflow-auto; the popup and the body are overflow-hidden; the
 * header, ribbon and footer are fixed (shrink-0); every tab panel and the one content section are the only scrolling
 * elements (min-h-0 overflow-y-auto) and carry the body's padding; and UI-DLG-015 still holds (every panel mounted in
 * the same grid cell of a shrinkable one-row grid). Risk class: Medium (UI) — the dialog shell of every editor and
 * action dialog. */

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
  { value: "lines", label: "Lines", content: <p>Lines body</p> },
  { value: "remarks", label: "Remarks", content: <p>Remarks body</p> },
];

interface Variant {
  name: string;
  props: Partial<EntityDialogProps> & Pick<EntityDialogProps, "variant">;
  /** The tab panels expected; 0 means the one content section is expected. */
  panels: number;
  fixedHeight: boolean;
}

const VARIANTS: Variant[] = [
  { name: "editor with tabs", props: { variant: "editor", tabs: TABS }, panels: 3, fixedHeight: false },
  { name: "editor with tabs, panelScroll", props: { variant: "editor", tabs: TABS, panelScroll: true }, panels: 3, fixedHeight: true },
  { name: "editor without tabs", props: { variant: "editor" }, panels: 0, fixedHeight: false },
  { name: "editor without tabs, panelScroll", props: { variant: "editor", panelScroll: true }, panels: 0, fixedHeight: false },
  { name: "editor with an empty tab list", props: { variant: "editor", tabs: [] }, panels: 0, fixedHeight: false },
  { name: "action", props: { variant: "action" }, panels: 0, fixedHeight: false },
  { name: "action, panelScroll", props: { variant: "action", panelScroll: true }, panels: 0, fixedHeight: false },
  /* Tabs are editor-only: an action dialog handed tabs renders its children in the section. */
  { name: "action handed tabs", props: { variant: "action", tabs: TABS, panelScroll: true }, panels: 0, fixedHeight: false },
];

const SIZES: EntityDialogSize[] = ["sm", "md", "lg", "full"];

function renderVariant(variant: Variant, size: EntityDialogSize = "md") {
  return render(
    <EntityDialog
      open
      onOpenChange={() => {}}
      size={size}
      title="Order 2026-000123"
      commands={[{ id: "save", labelKey: "Save", icon: Save, isPrimary: true, onClick: () => {} }]}
      footer={variant.props.variant === "action" ? <button type="button">Run</button> : undefined}
      {...variant.props}
    >
      <p>Plain body</p>
    </EntityDialog>,
  );
}

const SCROLL_TOKEN = /^(?:[a-z0-9-]+:)*overflow-(?:x-|y-)?(?:auto|scroll)$/;
const PADDING_TOKEN = /^p[xytrbl]?-/;

const popup = () => document.body.querySelector('[role="dialog"]') as HTMLElement;
const slot = (name: string) => popup().querySelector(`[data-slot="entity-dialog-${name}"]`) as HTMLElement | null;
const body = () => slot("body")!;
const classes = (el: Element) => (el.getAttribute("class") ?? "").split(/\s+/).filter(Boolean);
const describeEl = (el: Element) => `<${el.tagName.toLowerCase()} data-slot="${el.getAttribute("data-slot") ?? ""}" class="${el.className}">`;
const all = () => [popup(), ...Array.from(popup().querySelectorAll<HTMLElement>("*"))];
const panelsIn = (root: Element) => Array.from(root.querySelectorAll<HTMLElement>('[data-slot="entity-dialog-panel"]'));

describe("UI-DLG-006 guard: the dialog never scrolls, a panel or a section does", () => {
  for (const variant of VARIANTS) {
    describe(variant.name, () => {
      it("no element of the popup carries overflow-auto; the popup and the body are overflow-hidden", () => {
        renderVariant(variant);
        expect(popup().getAttribute("data-variant")).toBe(variant.props.variant);
        expect(classes(popup())).toEqual(expect.arrayContaining(["flex", "flex-col", "overflow-hidden", "max-h-[92vh]"]));
        expect(classes(popup()).includes("h-[92vh]"), "fixed height h-[92vh]").toBe(variant.fixedHeight);

        expect(classes(body())).toEqual(expect.arrayContaining(["min-h-0", "flex-1", "overflow-hidden"]));
        for (const el of all()) {
          expect(classes(el).includes("overflow-auto"), `overflow-auto on ${describeEl(el)}`).toBe(false);
          expect(classes(el).some((c) => /overflow-x-(auto|scroll)/.test(c)), `horizontal scrolling on ${describeEl(el)}`).toBe(false);
        }
        /* The body's padding sits on the scrolling panel / section, not on the body. */
        expect(classes(body()).filter((c) => PADDING_TOKEN.test(c))).toEqual([]);
      });

      it("the fixed parts are shrink-0: header, ribbon (editor) and footer (action)", () => {
        renderVariant(variant);
        expect(classes(slot("header")!)).toContain("shrink-0");
        if (variant.props.variant === "editor") {
          expect(classes(slot("ribbon")!)).toContain("shrink-0");
          expect(slot("footer")).toBeNull();
        } else {
          expect(slot("ribbon")).toBeNull();
          expect(classes(slot("footer")!)).toContain("shrink-0");
        }
      });

      it("the only scrolling elements are the tab panels or the one content section, each min-h-0 overflow-y-auto with the body's padding", () => {
        renderVariant(variant);
        const scrolling = all().filter((el) => classes(el).some((c) => SCROLL_TOKEN.test(c)));
        const panels = panelsIn(body());
        const section = slot("section");

        if (variant.panels > 0) {
          expect(section).toBeNull();
          expect(panels).toHaveLength(variant.panels);
          expect(scrolling).toEqual(panels);
          expect(classes(body())).toEqual(expect.arrayContaining(["grid", "grid-rows-[minmax(0,1fr)]"]));
          expect(classes(body())).not.toContain("content-start");
          expect(Array.from(body().children)).toEqual(panels);
          for (const panel of panels) {
            expect(classes(panel)).toEqual(expect.arrayContaining(["min-h-0", "overflow-y-auto", "px-6", "py-5"]));
            expect(panel.getAttribute("role")).toBe("tabpanel");
          }
        } else {
          expect(panels).toEqual([]);
          expect(section).not.toBeNull();
          expect(scrolling).toEqual([section]);
          expect(section!.parentElement).toBe(body());
          expect(classes(body())).toEqual(expect.arrayContaining(["flex", "flex-col"]));
          expect(classes(body())).not.toContain("grid");
          expect(classes(section!)).toEqual(expect.arrayContaining(["min-h-0", "flex-1", "overflow-y-auto"]));
          expect(classes(section!)).toEqual(expect.arrayContaining(variant.props.variant === "editor" ? ["px-6", "py-5"] : ["p-4"]));
          expect(section!.textContent).toBe("Plain body");
        }
      });

      if (variant.panels > 0) {
        it("UI-DLG-015 still holds: every panel mounted in the same grid cell, one active, the others inert and hidden", () => {
          renderVariant(variant);
          const panels = panelsIn(body());
          expect(panels.map((p) => p.getAttribute("aria-label"))).toEqual(TABS.map((t) => t.label));
          for (const panel of panels) expect(classes(panel)).toEqual(expect.arrayContaining(["col-start-1", "row-start-1"]));
          const active = panels.filter((p) => p.getAttribute("data-state") === "active");
          expect(active).toHaveLength(1);
          expect(active[0].getAttribute("aria-label")).toBe("General");
          expect(active[0].hasAttribute("inert")).toBe(false);
          for (const panel of panels.filter((p) => p !== active[0])) {
            expect(panel.hasAttribute("inert")).toBe(true);
            expect(panel.getAttribute("aria-hidden")).toBe("true");
            expect(panel.style.visibility).toBe("hidden");
            expect(classes(panel)).toContain("pointer-events-none");
          }
        });
      }
    });
  }

  it("every size tier keeps the popup and the body overflow-hidden and the panels scrolling; full adds its own height, never a scrollbar", () => {
    for (const size of SIZES) {
      const { unmount } = renderVariant(VARIANTS[0], size);
      expect(popup().getAttribute("data-size")).toBe(size);
      expect(classes(popup())).toContain("overflow-hidden");
      expect(classes(body())).toContain("overflow-hidden");
      expect(classes(popup()).includes("h-[96vh]"), `h-[96vh] for size ${size}`).toBe(size === "full");
      for (const el of all()) expect(classes(el).includes("overflow-auto"), `overflow-auto on ${describeEl(el)} (size ${size})`).toBe(false);
      for (const panel of panelsIn(body())) expect(classes(panel)).toEqual(expect.arrayContaining(["min-h-0", "overflow-y-auto"]));
      unmount();
    }
  });

  it("a scrolling region a feature puts inside a panel sits under the panel, never beside it", () => {
    const tabs: EntityDialogTab[] = [
      {
        value: "features",
        label: "Features",
        content: (
          <div className="flex h-full min-h-0 flex-col">
            <p>Fixed caption</p>
            <div data-testid="region" className="min-h-0 flex-1 overflow-y-auto">
              Long list
            </div>
          </div>
        ),
      },
    ];
    renderVariant({ name: "region", props: { variant: "editor", tabs, panelScroll: true }, panels: 1, fixedHeight: true });
    const region = popup().querySelector('[data-testid="region"]')!;
    const panel = region.closest('[data-slot="entity-dialog-panel"]');
    expect(panel).not.toBeNull();
    expect(panel!.parentElement).toBe(body());
    const scrolling = all().filter((el) => classes(el).some((c) => SCROLL_TOKEN.test(c)));
    expect(scrolling).toEqual([panel, region]);
  });
});
