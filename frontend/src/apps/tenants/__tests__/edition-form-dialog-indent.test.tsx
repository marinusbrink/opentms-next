import { render } from "@testing-library/react";
import { beforeAll, describe, expect, it, vi } from "vitest";

/* The edition editor's feature tree (review B5): a child feature sits one step in per level of ABP's depth, through
 * spacing-scale classes ("", ps-6, ps-12, ps-18; deeper levels keep the last step), never an inline style
 * (UI-TOK-001). Real EntityDialog; the editions domain hooks are mocked with stable references. Risk class: Low (layout)
 * on a Medium screen. */

vi.mock("@/lib/i18n/LocalizationProvider", () => ({
  useL: () => ({ t: (key: string) => key, culture: "en", isLoading: false }),
}));
vi.mock("@/lib/abp/queries", () => ({
  useApplicationConfiguration: () => ({ data: { features: { values: {} }, localization: { currentCulture: { cultureName: "en" } } } }),
}));
vi.mock("sonner", () => ({ toast: { success: vi.fn(), error: vi.fn() } }));

vi.mock("@/domains/platform/editions", () => {
  const mutation = () => ({ mutateAsync: vi.fn(), isPending: false });
  const toggle = (name: string, depth: number) => ({
    name,
    displayName: name,
    description: null,
    depth,
    parentName: null,
    value: "true",
    valueType: { name: "ToggleStringValueType" },
  });
  const groups = [
    {
      name: "Apps",
      displayName: "Apps and screens",
      features: [
        toggle("Apps.MasterData", 0),
        toggle("Apps.MasterData.Reference", 1),
        toggle("Apps.MasterData.Reference.Countries", 2),
        toggle("Deep.Three", 3),
        toggle("Deep.Four", 4),
        toggle("Deep.Nine", 9),
        { name: "Text.Two", displayName: "Text.Two", description: null, depth: 2, parentName: null, value: "x", valueType: { name: "FreeTextStringValueType" } },
      ],
    },
  ];
  const loaded = { isLoading: false, isError: false, data: groups };
  return {
    useCreateEdition: mutation,
    useUpdateEdition: mutation,
    useUpdateEditionFeatures: mutation,
    useEditionFeatures: () => loaded,
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
const INDENTS = ["ps-6", "ps-12", "ps-18"];

function rowOf(name: string): HTMLElement {
  const id = `ef-feature-${name.replace(/[^A-Za-z0-9_-]/g, "_")}`;
  const control = document.getElementById(id)!;
  return (control.closest("label") ?? control.closest("[class*='ps-'], div")) as HTMLElement;
}

const indentOf = (el: HTMLElement) => el.className.split(/\s+/).filter((c) => INDENTS.includes(c));

describe("EditionFormDialog – feature indent by depth", () => {
  it.each([
    ["Apps.MasterData", []],
    ["Apps.MasterData.Reference", ["ps-6"]],
    ["Apps.MasterData.Reference.Countries", ["ps-12"]],
    ["Deep.Three", ["ps-18"]],
    ["Deep.Four", ["ps-18"]],
    ["Deep.Nine", ["ps-18"]],
  ])("toggle %s gets %j", (name, expected) => {
    render(<EditionFormDialog mode="edit" edition={EDITION} canManageFeatures open onClose={() => {}} onSuccess={() => {}} />);
    expect(indentOf(rowOf(name))).toEqual(expected);
  });

  it("a non-toggle feature is indented the same way", () => {
    render(<EditionFormDialog mode="edit" edition={EDITION} canManageFeatures open onClose={() => {}} onSuccess={() => {}} />);
    const input = document.getElementById("ef-feature-Text_Two")!;
    const wrapper = Array.from(document.querySelectorAll<HTMLElement>("[data-slot='edition-feature-list'] div")).find(
      (el) => el.contains(input) && indentOf(el).length > 0,
    );
    expect(wrapper && indentOf(wrapper)).toEqual(["ps-12"]);
  });

  it("uses no inline padding or margin for the indent", () => {
    render(<EditionFormDialog mode="edit" edition={EDITION} canManageFeatures open onClose={() => {}} onSuccess={() => {}} />);
    /* The feature rows: the children of each section's grid (a toggle's label, or the wrapper of a field). */
    const rows = Array.from(document.querySelectorAll<HTMLElement>("[data-slot='edition-feature-list'] .grid.gap-3 > *"));
    expect(rows).toHaveLength(7);
    for (const el of rows) {
      expect(el.style.paddingLeft + el.style.paddingInlineStart + el.style.marginLeft + el.style.marginInlineStart).toBe("");
    }
  });
});
