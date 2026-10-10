import type { MenuItemDef } from "ag-grid-community";
import type { LucideIcon } from "lucide-react";

/* Row actions for the context menu (UI-GRID-008). Kept free of AG Grid rendering so the
 * ordering, multi-row filtering and separator rules are unit-testable. */

export interface GridRowAction {
  id: string;
  labelKey: string;
  icon?: LucideIcon;
  onClick: () => void;
  /** Destructive items render in red and get a separator before them. */
  destructive?: boolean;
  /** Stays in the menu when several rows are selected. Default: false (single row only). */
  multi?: boolean;
  /** UI-PERM-005: disabled only for a business-state reason, with a tooltip. */
  disabled?: boolean;
  disabledReasonKey?: string;
}

export type ContextMenuItem = MenuItemDef | "separator";

export function buildContextMenuItems(
  actions: GridRowAction[],
  rowCount: number,
  t: (key: string) => string,
): ContextMenuItem[] {
  const applicable = rowCount > 1 ? actions.filter((action) => action.multi) : actions;
  const items: ContextMenuItem[] = [];

  for (const action of applicable) {
    if (action.destructive && items.length > 0 && items[items.length - 1] !== "separator") {
      items.push("separator");
    }
    items.push({
      name: t(action.labelKey),
      action: action.onClick,
      disabled: action.disabled === true,
      tooltip: action.disabled && action.disabledReasonKey ? t(action.disabledReasonKey) : undefined,
      cssClasses: action.destructive ? ["text-destructive"] : undefined,
    });
  }

  return items;
}

/** The rows a context-menu action applies to: the selection when the clicked row is part of it, otherwise the clicked row. */
export function resolveTargetRows<TRow>(
  clicked: TRow | undefined,
  selected: TRow[],
  getId: (row: TRow) => string,
): TRow[] {
  if (!clicked) return selected;
  if (selected.length > 1 && selected.some((row) => getId(row) === getId(clicked))) {
    return selected;
  }
  return [clicked];
}

/** UI-GRID-007: group rows and pinned rows never open. */
export function canOpenRow(node: { group?: boolean; rowPinned?: string | null; data?: unknown }): boolean {
  return !node.group && !node.rowPinned && node.data != null;
}
