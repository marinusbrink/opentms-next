import { describe, expect, it, vi } from "vitest";

import {
  buildContextMenuItems,
  canOpenRow,
  resolveTargetRows,
  type GridRowAction,
} from "@/components/ui/opentms-grid-actions";

const t = (key: string) => key;

function actions(): GridRowAction[] {
  return [
    { id: "edit", labelKey: "Edit", onClick: vi.fn() },
    { id: "flag", labelKey: "Flag", onClick: vi.fn(), multi: true },
    { id: "delete", labelKey: "Delete", onClick: vi.fn(), destructive: true, multi: true },
  ];
}

describe("buildContextMenuItems (UI-GRID-008)", () => {
  it("keeps the given order and separates the destructive item", () => {
    const items = buildContextMenuItems(actions(), 1, t);
    expect(items.map((i) => (i === "separator" ? "|" : i.name))).toEqual(["Edit", "Flag", "|", "Delete"]);
  });

  it("drops single-row items when several rows are selected", () => {
    const items = buildContextMenuItems(actions(), 3, t);
    expect(items.map((i) => (i === "separator" ? "|" : i.name))).toEqual(["Flag", "|", "Delete"]);
  });

  it("does not start the menu with a separator", () => {
    const items = buildContextMenuItems([{ id: "delete", labelKey: "Delete", onClick: vi.fn(), destructive: true }], 1, t);
    expect(items).toHaveLength(1);
  });

  it("marks destructive items red and carries a disabled reason as the tooltip", () => {
    const [edit, , del] = buildContextMenuItems(
      [
        { id: "edit", labelKey: "Edit", onClick: vi.fn(), disabled: true, disabledReasonKey: "SystemRoleReadOnly" },
        { id: "delete", labelKey: "Delete", onClick: vi.fn(), destructive: true },
      ],
      1,
      t,
    ) as [{ disabled: boolean; tooltip?: string }, "separator", { cssClasses?: string[] }];
    expect(edit.disabled).toBe(true);
    expect(edit.tooltip).toBe("SystemRoleReadOnly");
    expect(del.cssClasses).toEqual(["text-destructive"]);
  });
});

describe("resolveTargetRows", () => {
  const id = (r: { id: string }) => r.id;
  it("uses the selection when the clicked row is part of a multi-selection", () => {
    const selected = [{ id: "a" }, { id: "b" }];
    expect(resolveTargetRows({ id: "a" }, selected, id)).toBe(selected);
  });
  it("uses only the clicked row when it is outside the selection", () => {
    expect(resolveTargetRows({ id: "c" }, [{ id: "a" }, { id: "b" }], id)).toEqual([{ id: "c" }]);
  });
  it("uses the selection when nothing was clicked", () => {
    expect(resolveTargetRows(undefined, [{ id: "a" }], id)).toEqual([{ id: "a" }]);
  });
});

describe("canOpenRow (UI-GRID-007)", () => {
  it("opens data rows only", () => {
    expect(canOpenRow({ data: { id: 1 } })).toBe(true);
    expect(canOpenRow({ group: true, data: {} })).toBe(false);
    expect(canOpenRow({ rowPinned: "bottom", data: {} })).toBe(false);
    expect(canOpenRow({ data: undefined })).toBe(false);
  });

  it("opens tree-data parents: there the group row is the record", () => {
    expect(canOpenRow({ group: true, data: { id: 1 } }, true)).toBe(true);
    expect(canOpenRow({ group: true, data: undefined }, true)).toBe(false);
    expect(canOpenRow({ group: true, rowPinned: "top", data: { id: 1 } }, true)).toBe(false);
  });
});
