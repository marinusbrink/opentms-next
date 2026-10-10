import { fireEvent, render, screen } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";

import { buildTree } from "@/components/ui/tree-select";
import { TreeView } from "@/components/ui/tree-view";

/* The checkbox tree. Risk class: Medium — a cascading check would silently add memberships. */

vi.mock("@/lib/i18n/LocalizationProvider", () => ({
  useL: () => ({ t: (k: string) => k, culture: "en", isLoading: false }),
}));

const nodes = buildTree(
  [
    { id: "root", name: "Head office", parentId: null },
    { id: "plan", name: "Planning", parentId: "root" },
    { id: "rtd", name: "Rotterdam", parentId: "plan" },
    { id: "wh", name: "Warehouse", parentId: null },
  ],
  (u) => u.id,
  (u) => u.parentId,
  (u) => u.name,
);

describe("TreeView", () => {
  it("expands the path to checked nodes, checks one node at a time and reports the change", () => {
    const onCheckedChange = vi.fn();
    render(<TreeView nodes={nodes} checkedIds={new Set(["rtd"])} onCheckedChange={onCheckedChange} />);

    const items = screen.getAllByRole("treeitem");
    expect(items.map((i) => i.textContent)).toEqual(["Head officePlanningRotterdam", "PlanningRotterdam", "Rotterdam", "Warehouse"]);
    expect(screen.getByRole("checkbox", { name: "Rotterdam" })).toBeChecked();
    expect(screen.getByRole("checkbox", { name: "Planning" })).not.toBeChecked();

    fireEvent.click(screen.getByRole("checkbox", { name: "Head office" }));
    expect(onCheckedChange).toHaveBeenCalledWith("root", true);
    fireEvent.click(screen.getByRole("checkbox", { name: "Rotterdam" }));
    expect(onCheckedChange).toHaveBeenCalledWith("rtd", false);
  });

  it("collapses a branch, disables every checkbox when disabled, and says when the tree is empty", () => {
    const { rerender } = render(<TreeView nodes={nodes} checkedIds={new Set(["plan"])} onCheckedChange={vi.fn()} disabled />);
    expect(screen.getByRole("checkbox", { name: "Planning" })).toHaveAttribute("data-disabled");

    fireEvent.click(screen.getAllByRole("button", { name: "Collapse" })[0]);
    expect(screen.queryByRole("checkbox", { name: "Planning" })).not.toBeInTheDocument();

    rerender(<TreeView nodes={[]} checkedIds={new Set()} onCheckedChange={vi.fn()} emptyText="Nothing here" />);
    expect(screen.getByText("Nothing here")).toBeInTheDocument();
  });
});
