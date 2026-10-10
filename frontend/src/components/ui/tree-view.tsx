import { useState } from "react";
import { ChevronDown, ChevronRight } from "lucide-react";

import { Checkbox } from "@/components/ui/checkbox";
import type { TreeSelectNode } from "@/components/ui/tree-select";
import { useL } from "@/lib/i18n/LocalizationProvider";
import { cn } from "@/lib/utils";

/* An inline tree with a checkbox per node (membership of organisation units on a user, for
 * example). Checking a node checks that node only: no cascading to children, because a
 * membership is per unit. Branches expand and collapse; the path to every checked node starts
 * expanded so the current state is visible. */
export interface TreeViewProps {
  nodes: TreeSelectNode[];
  checkedIds: ReadonlySet<string>;
  onCheckedChange: (id: string, checked: boolean) => void;
  disabled?: boolean;
  /** Localized text when there are no nodes. */
  emptyText?: string;
  "aria-label"?: string;
  className?: string;
}

function ancestorsOfChecked(nodes: readonly TreeSelectNode[], checked: ReadonlySet<string>, trail: string[], into: Set<string>) {
  for (const node of nodes) {
    if (checked.has(node.id)) trail.forEach((id) => into.add(id));
    if (node.children) ancestorsOfChecked(node.children, checked, [...trail, node.id], into);
  }
}

export function TreeView({ nodes, checkedIds, onCheckedChange, disabled = false, emptyText, "aria-label": ariaLabel, className }: TreeViewProps) {
  const { t } = useL();
  const [expanded, setExpanded] = useState<Set<string>>(() => {
    const initial = new Set<string>();
    ancestorsOfChecked(nodes, checkedIds, [], initial);
    return initial;
  });

  const toggle = (id: string) =>
    setExpanded((prev) => {
      const next = new Set(prev);
      if (next.has(id)) next.delete(id);
      else next.add(id);
      return next;
    });

  if (nodes.length === 0) {
    return <p className="text-sm text-muted-foreground">{emptyText ?? t("Grid:Empty")}</p>;
  }

  const renderNodes = (list: readonly TreeSelectNode[], depth: number) =>
    list.map((node) => {
      const hasChildren = (node.children?.length ?? 0) > 0;
      const isExpanded = expanded.has(node.id);
      const checked = checkedIds.has(node.id);
      return (
        <li key={node.id} role="treeitem" aria-expanded={hasChildren ? isExpanded : undefined} aria-selected={checked}>
          <div className="flex items-center gap-1" style={{ paddingLeft: `${depth * 1.25}rem` }}>
            {hasChildren ? (
              <button
                type="button"
                tabIndex={-1}
                aria-label={isExpanded ? t("Collapse") : t("Expand")}
                onClick={() => toggle(node.id)}
                className="flex size-6 shrink-0 items-center justify-center rounded text-muted-foreground hover:bg-muted"
              >
                {isExpanded ? <ChevronDown className="size-4" /> : <ChevronRight className="size-4" />}
              </button>
            ) : (
              <span className="size-6 shrink-0" aria-hidden="true" />
            )}
            <label className={cn("flex min-w-0 flex-1 items-center gap-2 rounded px-2 py-1.5 text-sm", !disabled && "cursor-pointer hover:bg-muted")}>
              <Checkbox checked={checked} onCheckedChange={(next) => onCheckedChange(node.id, next === true)} disabled={disabled} />
              <span className="truncate">{node.label}</span>
            </label>
          </div>
          {hasChildren && isExpanded && (
            <ul role="group">{renderNodes(node.children!, depth + 1)}</ul>
          )}
        </li>
      );
    });

  return (
    <ul role="tree" aria-label={ariaLabel} className={cn("flex flex-col", className)}>
      {renderNodes(nodes, 0)}
    </ul>
  );
}
