import { describe, expect, it, vi } from "vitest";

import { buildContextMenuItems, type GridRowAction } from "@/components/ui/opentms-grid-actions";

/* Sub-menus in the grid's context menu (docs/designs/65-tenant-test-datasets.md: "Reset to dataset" › its datasets).
 * Risk class: Medium (UI flow) — but the parent of a destructive action must never fire on its own click. */

const t = (key: string) => `t(${key})`;

type MenuItem = {
  name: string;
  action?: () => void;
  subMenu?: (MenuItem | "separator")[];
  disabled: boolean;
  tooltip?: string;
};

describe("buildContextMenuItems – children", () => {
  it("renders children as a translated subMenu and gives the parent no action", () => {
    const parentClick = vi.fn();
    const average = vi.fn();
    const large = vi.fn();
    const items = buildContextMenuItems(
      [
        {
          id: "reset",
          labelKey: "Tenants:ResetToDataset",
          onClick: parentClick,
          children: [
            { id: "reset-Average", labelKey: "Tenants:Dataset:Average", onClick: average },
            { id: "reset-Large", labelKey: "Tenants:Dataset:Large", onClick: large },
          ],
        },
      ],
      1,
      t,
    ) as MenuItem[];

    expect(items).toHaveLength(1);
    const parent = items[0];
    expect(parent.name).toBe("t(Tenants:ResetToDataset)");
    expect(parent.action).toBeUndefined();
    expect(parent.subMenu).toBeDefined();
    const sub = parent.subMenu as MenuItem[];
    expect(sub.map((i) => i.name)).toEqual(["t(Tenants:Dataset:Average)", "t(Tenants:Dataset:Large)"]);

    sub[1].action!();
    expect(large).toHaveBeenCalledOnce();
    expect(average).not.toHaveBeenCalled();
    expect(parentClick).not.toHaveBeenCalled();
  });

  it("treats an empty children list as a plain item with its own action", () => {
    const click = vi.fn();
    const [item] = buildContextMenuItems([{ id: "x", labelKey: "X", onClick: click, children: [] }], 1, t) as MenuItem[];
    expect(item.subMenu).toBeUndefined();
    expect(item.action).toBe(click);
  });

  it("gives a plain item no subMenu", () => {
    const [item] = buildContextMenuItems([{ id: "x", labelKey: "X", onClick: vi.fn() }], 1, t) as MenuItem[];
    expect(item.subMenu).toBeUndefined();
  });

  it("keeps a disabled parent disabled with its reason as the tooltip", () => {
    const actions: GridRowAction[] = [
      {
        id: "reset",
        labelKey: "Reset",
        disabled: true,
        disabledReasonKey: "Tenants:ResetAlreadyRunning",
        children: [{ id: "a", labelKey: "A", onClick: vi.fn() }],
      },
    ];
    const [item] = buildContextMenuItems(actions, 1, t) as MenuItem[];
    expect(item.disabled).toBe(true);
    expect(item.tooltip).toBe("t(Tenants:ResetAlreadyRunning)");
  });

  it("drops a single-row parent with children for a multi-row selection", () => {
    const items = buildContextMenuItems(
      [{ id: "reset", labelKey: "Reset", children: [{ id: "a", labelKey: "A", onClick: vi.fn() }] }],
      2,
      t,
    );
    expect(items).toEqual([]);
  });
});
