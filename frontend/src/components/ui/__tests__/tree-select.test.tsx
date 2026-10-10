import { fireEvent, render, screen } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";

import { buildTree, collectSubtreeIds, TreeSelect } from "@/components/ui/tree-select";

/* The tree picker (UI-LOOK-001 tree data, component-map GAP-02).
 * Risk class: Medium — a wrong subtree exclusion lets a unit be moved under itself. */

vi.mock("@/lib/i18n/LocalizationProvider", () => ({
  useL: () => ({ t: (k: string) => k, culture: "en", isLoading: false }),
}));

const units = [
  { id: "root", name: "Head office", parentId: null },
  { id: "plan", name: "Planning", parentId: "root" },
  { id: "rtd", name: "Rotterdam", parentId: "plan" },
  { id: "wh", name: "Warehouse", parentId: null },
];
const nodes = buildTree(units, (u) => u.id, (u) => u.parentId, (u) => u.name);

describe("buildTree / collectSubtreeIds", () => {
  it("nests children under their parents and keeps orphans as roots", () => {
    expect(nodes.map((n) => n.id)).toEqual(["root", "wh"]);
    expect(nodes[0].children!.map((n) => n.id)).toEqual(["plan"]);
    expect(nodes[0].children![0].children!.map((n) => n.id)).toEqual(["rtd"]);
    expect(buildTree([{ id: "x", name: "x", parentId: "gone" }], (u) => u.id, (u) => u.parentId, (u) => u.name).map((n) => n.id)).toEqual(["x"]);
  });

  it("collects a node with everything below it", () => {
    expect([...collectSubtreeIds(nodes, "plan")].sort()).toEqual(["plan", "rtd"]);
    expect([...collectSubtreeIds(nodes, "wh")]).toEqual(["wh"]);
  });
});

describe("TreeSelect", () => {
  it("shows the selected label, expands the path to it, and lets the user pick a node or none", async () => {
    const onChange = vi.fn();
    render(<TreeSelect id="p" nodes={nodes} value="rtd" onChange={onChange} noneLabel="None" disabledIds={new Set(["wh"])} />);

    const trigger = screen.getByRole("combobox");
    expect(trigger).toHaveTextContent("Rotterdam");
    fireEvent.click(trigger);

    const options = await screen.findAllByRole("option");
    expect(options.map((o) => o.textContent)).toEqual(["None", "Head office", "Planning", "Rotterdam", "Warehouse"]);
    expect(options[4]).toBeDisabled();
    expect(options[3]).toHaveAttribute("aria-selected", "true");

    fireEvent.click(options[1]);
    expect(onChange).toHaveBeenCalledWith("root");

    fireEvent.click(trigger);
    fireEvent.click((await screen.findAllByRole("option"))[0]);
    expect(onChange).toHaveBeenCalledWith(null);
  });

  it("clears an optional value with the X and never shows the X when required", () => {
    const onChange = vi.fn();
    const { rerender } = render(<TreeSelect id="p" nodes={nodes} value="wh" onChange={onChange} />);
    fireEvent.click(screen.getByRole("button", { name: "Clear" }));
    expect(onChange).toHaveBeenCalledWith(null);

    rerender(<TreeSelect id="p" nodes={nodes} value="wh" onChange={onChange} required />);
    expect(screen.queryByRole("button", { name: "Clear" })).not.toBeInTheDocument();
  });

  it("collapses and expands branches", async () => {
    render(<TreeSelect id="p" nodes={nodes} value={null} onChange={vi.fn()} />);
    fireEvent.click(screen.getByRole("combobox"));
    expect((await screen.findAllByRole("option")).map((o) => o.textContent)).toEqual(["Head office", "Warehouse"]);
    fireEvent.click(screen.getByRole("button", { name: "Expand" }));
    expect(screen.getAllByRole("option").map((o) => o.textContent)).toEqual(["Head office", "Planning", "Warehouse"]);
  });
});
