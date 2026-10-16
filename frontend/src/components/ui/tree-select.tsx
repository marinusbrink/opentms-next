import { useMemo, useState } from "react";
import { ChevronDown, ChevronRight, Plus, X } from "lucide-react";

import { Button } from "@/components/ui/button";
import { Popover, PopoverContent, PopoverTrigger } from "@/components/ui/popover";
import { useL } from "@/lib/i18n/LocalizationProvider";
import { cn } from "@/lib/utils";

/* The tree picker of UI-LOOK-001 for tree data such as organisation units (component-map
 * GAP-02, built as option (a): a Popover with a recursive list, no extra dependency). Single
 * select: the control shows the chosen node's label (UI-LOOK-003); optional ones clear with an
 * X (UI-LOOK-006). Nodes in `disabledIds` (a unit itself and its subtree when picking a new
 * parent) show but cannot be chosen. With `onCreateNew` the list ends with the "Add new …" row of
 * UI-LOOK-004, the same row as EntityLookup's; the tree has no search, so the term is empty. */

export interface TreeSelectNode {
  id: string;
  label: string;
  children?: TreeSelectNode[];
}

export interface TreeSelectProps {
  id: string;
  nodes: TreeSelectNode[];
  value: string | null;
  onChange: (value: string | null) => void;
  /** Shown as the first row and selectable as `null`, e.g. t("Administration:NoParentUnit"). */
  noneLabel?: string;
  disabledIds?: ReadonlySet<string>;
  required?: boolean;
  disabled?: boolean;
  "aria-invalid"?: boolean;
  className?: string;
  /** UI-LOOK-004: shows "Add new …" at the end of the list; omit when the user may not create. The tree has no search: the term is "". */
  onCreateNew?: (term: string) => void;
  /** The label of that row, already translated, e.g. t("Administration:AddNewOrganizationUnit"). */
  createLabel?: string;
}

/** Builds the tree from a flat list of parent references; roots are the items without a known parent. */
export function buildTree<T>(
  items: readonly T[],
  getId: (item: T) => string,
  getParentId: (item: T) => string | null | undefined,
  getLabel: (item: T) => string,
): TreeSelectNode[] {
  const byId = new Map<string, TreeSelectNode>();
  for (const item of items) byId.set(getId(item), { id: getId(item), label: getLabel(item), children: [] });
  const roots: TreeSelectNode[] = [];
  for (const item of items) {
    const node = byId.get(getId(item))!;
    const parentId = getParentId(item);
    const parent = parentId ? byId.get(parentId) : undefined;
    if (parent) parent.children!.push(node);
    else roots.push(node);
  }
  return roots;
}

/** The ids of a node and everything below it. */
export function collectSubtreeIds(nodes: readonly TreeSelectNode[], rootId: string): Set<string> {
  const result = new Set<string>();
  const visit = (node: TreeSelectNode, inside: boolean) => {
    const here = inside || node.id === rootId;
    if (here) result.add(node.id);
    node.children?.forEach((child) => visit(child, here));
  };
  nodes.forEach((node) => visit(node, false));
  return result;
}

function findLabel(nodes: readonly TreeSelectNode[], id: string): string | undefined {
  for (const node of nodes) {
    if (node.id === id) return node.label;
    const inChildren = node.children ? findLabel(node.children, id) : undefined;
    if (inChildren !== undefined) return inChildren;
  }
  return undefined;
}

function ancestorsOf(nodes: readonly TreeSelectNode[], id: string, trail: string[] = []): string[] | undefined {
  for (const node of nodes) {
    if (node.id === id) return trail;
    const found = node.children ? ancestorsOf(node.children, id, [...trail, node.id]) : undefined;
    if (found) return found;
  }
  return undefined;
}

export function TreeSelect({
  id,
  nodes,
  value,
  onChange,
  noneLabel,
  disabledIds,
  required = false,
  disabled = false,
  "aria-invalid": ariaInvalid,
  className,
  onCreateNew,
  createLabel,
}: TreeSelectProps) {
  const { t } = useL();
  const [open, setOpen] = useState(false);
  /* The path to the current value starts expanded so the selection is visible on open. */
  const [expanded, setExpanded] = useState<Set<string>>(() => new Set(value ? (ancestorsOf(nodes, value) ?? []) : []));

  const selectedLabel = useMemo(() => (value ? findLabel(nodes, value) : undefined), [nodes, value]);
  const canClear = !required && !disabled && value != null;

  const toggle = (nodeId: string) =>
    setExpanded((prev) => {
      const next = new Set(prev);
      if (next.has(nodeId)) next.delete(nodeId);
      else next.add(nodeId);
      return next;
    });

  const choose = (next: string | null) => {
    onChange(next);
    setOpen(false);
  };

  const renderNodes = (list: readonly TreeSelectNode[], depth: number) =>
    list.map((node) => {
      const hasChildren = (node.children?.length ?? 0) > 0;
      const isExpanded = expanded.has(node.id);
      const isDisabled = disabledIds?.has(node.id) === true;
      const isSelected = node.id === value;
      return (
        <li key={node.id} role="none">
          <div className="flex items-center" style={{ paddingLeft: `${depth * 1.25}rem` }}>
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
            <button
              type="button"
              role="option"
              aria-selected={isSelected}
              aria-disabled={isDisabled || undefined}
              disabled={isDisabled}
              onClick={() => choose(node.id)}
              className={cn(
                "flex min-w-0 flex-1 items-center rounded px-2 py-1.5 text-left text-sm hover:bg-muted",
                isSelected && "bg-brand-subtle font-medium text-brand",
                isDisabled && "cursor-default opacity-50 hover:bg-transparent",
              )}
            >
              <span className="truncate">{node.label}</span>
            </button>
          </div>
          {hasChildren && isExpanded && (
            <ul role="group" aria-label={node.label}>
              {renderNodes(node.children!, depth + 1)}
            </ul>
          )}
        </li>
      );
    });

  return (
    <Popover open={open} onOpenChange={setOpen}>
      <div className={cn("relative", className)}>
        <PopoverTrigger
          id={id}
          disabled={disabled}
          role="combobox"
          aria-invalid={ariaInvalid}
          aria-haspopup="listbox"
          aria-expanded={open}
          data-slot="tree-select"
          className={cn(
            "flex h-10 w-full min-w-0 items-center rounded-lg border border-input bg-background px-2.5 pr-8 text-left text-sm transition-colors outline-none focus-visible:border-ring focus-visible:ring-3 focus-visible:ring-ring/50 disabled:pointer-events-none disabled:opacity-50",
            ariaInvalid && "border-destructive",
          )}
        >
          <span className="truncate">{selectedLabel ?? (value ? value : "")}</span>
          <ChevronDown className="pointer-events-none absolute right-2 size-4 text-muted-foreground" aria-hidden="true" />
        </PopoverTrigger>
        {canClear && (
          <Button
            type="button"
            variant="ghost"
            size="icon-sm"
            aria-label={t("Clear")}
            onClick={() => onChange(null)}
            className="absolute top-1/2 right-7 -translate-y-1/2"
          >
            <X />
          </Button>
        )}
      </div>
      <PopoverContent align="start" className="max-h-80 w-80 overflow-auto p-1">
        <ul role="listbox" aria-labelledby={id} className="flex flex-col">
          {noneLabel !== undefined && (
            <li role="none">
              <button
                type="button"
                role="option"
                aria-selected={value == null}
                onClick={() => choose(null)}
                className={cn(
                  "flex w-full items-center rounded px-2 py-1.5 text-left text-sm text-muted-foreground hover:bg-muted",
                  value == null && "bg-brand-subtle font-medium text-brand",
                )}
              >
                {noneLabel}
              </button>
            </li>
          )}
          {renderNodes(nodes, 0)}
        </ul>
        {onCreateNew && (
          <div className="border-t border-border p-1">
            <Button
              type="button"
              variant="ghost"
              size="sm"
              className="w-full justify-start"
              onClick={() => {
                setOpen(false);
                onCreateNew("");
              }}
            >
              <Plus aria-hidden="true" /> {createLabel ?? t("AddNew")}
            </Button>
          </div>
        )}
      </PopoverContent>
    </Popover>
  );
}
