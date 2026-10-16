import { render, screen } from "@testing-library/react";
import { beforeAll, describe, expect, it, vi } from "vitest";

/* The edition editor never scrolls as a whole; only its feature list does (PO 2026-10-14). In edit mode with
 * ManageFeatures it passes panelScroll to EntityDialog and renders the feature list in its own scroll region inside a
 * full-height column; in create mode (no Features tab) the dialog keeps the default layout. Real EntityDialog; the
 * editions domain hooks are mocked. Risk class: Medium (UI). */

vi.mock("@/lib/i18n/LocalizationProvider", () => ({
  useL: () => ({ t: (key: string) => key, culture: "en", isLoading: false }),
}));
vi.mock("@/lib/abp/queries", () => ({
  useApplicationConfiguration: () => ({ data: { features: { values: {} }, localization: { currentCulture: { cultureName: "en" } } } }),
}));
vi.mock("sonner", () => ({ toast: { success: vi.fn(), error: vi.fn() } }));

vi.mock("@/domains/platform/editions", () => {
  const mutation = () => ({ mutateAsync: vi.fn(), isPending: false });
  /* Stable references, as TanStack Query returns them: the dialog derives its form state from data in an effect. */
  const groups = [
    {
      name: "Apps",
      displayName: "Apps and screens",
      features: [
        { name: "Apps.MasterData", displayName: "Master data app", description: null, depth: 0, parentName: null, value: "true", valueType: { name: "ToggleStringValueType" } },
        { name: "Apps.MasterData.General.Reference", displayName: "Reference", description: null, depth: 1, parentName: "Apps.MasterData", value: "true", valueType: { name: "ToggleStringValueType" } },
      ],
    },
  ];
  const loaded = { isLoading: false, isError: false, data: groups };
  const idle = { isLoading: false, isError: false, data: undefined };
  return {
    useCreateEdition: mutation,
    useUpdateEdition: mutation,
    useUpdateEditionFeatures: mutation,
    useEditionFeatures: (editionId: string | undefined) => (editionId ? loaded : idle),
  };
});

class MockResizeObserver {
  observe() {}
  disconnect() {}
  unobserve() {}
}
beforeAll(() => vi.stubGlobal("ResizeObserver", MockResizeObserver));

import { EditionFormDialog } from "@/apps/tenants/EditionFormDialog";

const EDITION = { id: "e-gold", displayName: "Gold", creationTime: "2026-10-14T10:00:00Z" } as never;

const content = () => document.body.querySelector('[data-variant="editor"]') as HTMLElement;
const body = () => document.body.querySelector('[data-slot="entity-dialog-body"]') as HTMLElement;
const list = () => document.body.querySelector('[data-slot="edition-feature-list"]') as HTMLElement | null;
const classes = (el: Element) => el.className.split(/\s+/);

describe("EditionFormDialog – only the feature list scrolls", () => {
  it("in edit mode with ManageFeatures passes panelScroll and renders the feature list inside its scroll region", () => {
    render(<EditionFormDialog mode="edit" edition={EDITION} canManageFeatures open onClose={() => {}} onSuccess={() => {}} />);

    /* panelScroll reached EntityDialog: fixed height, non-scrolling body. */
    expect(classes(content())).toContain("h-[92vh]");
    expect(classes(body())).toContain("overflow-hidden");
    expect(classes(body())).not.toContain("overflow-auto");

    const region = list();
    expect(region).not.toBeNull();
    expect(classes(region!)).toEqual(expect.arrayContaining(["min-h-0", "flex-1", "overflow-y-auto"]));
    expect(region!.textContent).toContain("Master data app");
    expect(region!.textContent).toContain("Reference");

    /* The Features tab content is a full-height column that lets the list shrink. */
    const column = region!.parentElement!;
    expect(classes(column)).toEqual(expect.arrayContaining(["flex", "h-full", "min-h-0", "flex-col"]));
    const panel = region!.closest('[role="tabpanel"]')!;
    expect(panel.getAttribute("aria-label")).toBe("Editions:Features");
    expect(classes(panel)).toEqual(expect.arrayContaining(["min-h-0", "overflow-y-auto"]));
  });

  it("in create mode has no feature list and keeps the default dialog layout", () => {
    render(<EditionFormDialog mode="create" canManageFeatures open onClose={() => {}} onSuccess={() => {}} />);

    expect(list()).toBeNull();
    expect(screen.queryByRole("tab", { name: "Editions:Features" })).toBeNull();
    expect(classes(content())).not.toContain("h-[92vh]");
    expect(classes(body())).toContain("overflow-auto");
  });

  it("in edit mode without ManageFeatures has no feature list and keeps the default layout", () => {
    render(<EditionFormDialog mode="edit" edition={EDITION} canManageFeatures={false} open onClose={() => {}} onSuccess={() => {}} />);

    expect(list()).toBeNull();
    expect(classes(content())).not.toContain("h-[92vh]");
    expect(classes(body())).toContain("overflow-auto");
  });
});
