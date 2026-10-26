import { render } from "@testing-library/react";
import { Fingerprint, Printer, Save, Trash2 } from "lucide-react";
import { describe, expect, it, vi } from "vitest";

import { EntityDialog, type EntityDialogTab } from "@/components/ui/entity-dialog";
import { RecordPage } from "@/components/ui/record-page";

/* The tab strip shares the ribbon row (UI-DLG-005, UI-FORM-008). On a record page with many commands and tabs (the
 * order page at 1440 px, browser-run finding 5) the row did not wrap and clipped the last tabs. The row now wraps, so
 * the strip moves under the ribbon, still end-aligned; neither the row nor the strip clips (no overflow-hidden) and
 * nothing scrolls horizontally (UI-DLG-006). jsdom has no layout: the guard is on the classes. Risk class: Low (UI). */

vi.mock("@/lib/i18n/LocalizationProvider", () => ({ useL: () => ({ t: (key: string) => key, culture: "en", isLoading: false }) }));
vi.mock("@/lib/abp/queries", () => ({ useApplicationConfiguration: () => ({ data: { features: { values: {} } } }) }));
vi.stubGlobal(
  "ResizeObserver",
  class {
    observe() {}
    disconnect() {}
    unobserve() {}
  },
);

const TABS: EntityDialogTab[] = [
  { value: "general", label: "General", content: <p>General</p> },
  { value: "lines", label: "Lines", content: <p>Lines</p> },
  { value: "pricing", label: "Pricing", content: <p>Pricing</p> },
  { value: "planning", label: "Planning", content: <p>Planning</p> },
  { value: "documents", label: "Documents", content: <p>Documents</p> },
  { value: "identification", label: "RecordIdentification", icon: Fingerprint, content: <p>Id</p> },
];
const COMMANDS = [
  { id: "save", labelKey: "Save", icon: Save, isPrimary: true, onClick: () => {} },
  { id: "print", labelKey: "Print", icon: Printer, groupKey: "Print", onClick: () => {} },
  { id: "delete", labelKey: "Delete", icon: Trash2, groupKey: "Delete", variant: "destructive" as const, onClick: () => {} },
];

const classes = (el: Element | null) => (el?.getAttribute("class") ?? "").split(/\s+/);

function expectWrappingRow(row: Element | null) {
  expect(row).not.toBeNull();
  const rowClasses = classes(row);
  expect(rowClasses).toContain("flex");
  expect(rowClasses).toContain("flex-wrap");
  expect(rowClasses.some((c) => /^overflow-/.test(c))).toBe(false);
  const strip = row!.querySelector("[data-editor-tab-strip]");
  expect(strip).not.toBeNull();
  const stripClasses = classes(strip);
  /* End-aligned on whichever line it lands (UI-FORM-008), never wider than the row, and it may wrap itself. */
  expect(stripClasses).toEqual(expect.arrayContaining(["ml-auto", "justify-end", "max-w-full", "flex-wrap"]));
  expect(stripClasses.some((c) => /^overflow-(x-)?(hidden|auto|scroll)$/.test(c))).toBe(false);
  expect(strip!.querySelectorAll("[role=tab]")).toHaveLength(TABS.length);
}

describe("the editor tab strip wraps under the ribbon instead of being clipped", () => {
  it("on a record page", () => {
    render(<RecordPage title="Order" commands={COMMANDS} tabs={TABS} onClose={() => {}} />);
    expectWrappingRow(document.querySelector("[data-slot=record-page-ribbon]"));
  });

  it("in an editor dialog", () => {
    render(
      <EntityDialog open onOpenChange={() => {}} variant="editor" size="md" title="Order" commands={COMMANDS} tabs={TABS}>
        <p>Body</p>
      </EntityDialog>,
    );
    expectWrappingRow(document.body.querySelector("[data-slot=entity-dialog-ribbon]"));
  });
});
