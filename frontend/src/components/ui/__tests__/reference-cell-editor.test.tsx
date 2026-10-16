import { cleanup, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import type { CustomCellEditorProps } from "ag-grid-react";
import { describe, expect, it, vi } from "vitest";

import { ReferenceCellEditor, referenceCellEditorColDef, type ReferenceCellEditorParams } from "@/components/ui/reference-cell-editor";

/* The LinesGrid cell editor that replaced agRichSelectCellEditor for record columns (UI-LOOK-004): it renders the
 * options inline, filters on typing (the highlight starts on the first match), picks by click or arrows + Enter,
 * commits the value to the grid and ends the edit; Tab commits the highlighted option and lets the key through to
 * the grid, which moves on (UI-KEY-004); its "Add new …" row is reached with ArrowDown past the last option and
 * triggered with Enter or a click, cancels the edit and hands the typed text and the row to `onCreateNew`. The grid's
 * side (Escape, focus loss) is AG Grid's; jsdom cannot run AG Grid's popup editing, so the editor runs here against
 * the CustomCellEditorProps contract. Risk class: Medium (UI flow of a library component). */

vi.mock("@/lib/i18n/LocalizationProvider", () => ({
  useL: () => ({ t: (k: string) => k, culture: "en", isLoading: false }),
}));

interface Row {
  key: number;
  vehicleTypeId: string | null;
}

const OPTIONS = [
  { value: "vt-1", label: "Tractor" },
  { value: "vt-2", label: "Trailer" },
  { value: "vt-3", label: "Rigid truck" },
];

function renderEditor(overrides: Partial<CustomCellEditorProps<Row, string | null> & ReferenceCellEditorParams<Row>> = {}) {
  const onValueChange = vi.fn();
  const stopEditing = vi.fn();
  const api = { stopEditing: vi.fn() };
  const row: Row = { key: 7, vehicleTypeId: "vt-2" };
  const props = {
    value: "vt-2",
    onValueChange,
    stopEditing,
    api,
    data: row,
    eventKey: null,
    options: OPTIONS,
    ...overrides,
  } as unknown as CustomCellEditorProps<Row, string | null> & ReferenceCellEditorParams<Row>;
  const onParentKeyDown = vi.fn();
  render(
    <div onKeyDown={onParentKeyDown}>
      <ReferenceCellEditor<Row> {...props} />
    </div>,
  );
  return { onValueChange, stopEditing, api, row, onParentKeyDown };
}

const input = () => screen.getByRole("combobox");
const options = () => screen.getAllByRole("option");
const optionLabels = () => options().map((o) => o.textContent);
const active = () => {
  const id = input().getAttribute("aria-activedescendant");
  return id ? document.getElementById(id)?.textContent : undefined;
};

describe("ReferenceCellEditor", () => {
  it("lists every option, marks the cell's value and starts with it highlighted", () => {
    renderEditor();
    expect(optionLabels()).toEqual(["Tractor", "Trailer", "Rigid truck"]);
    expect(options()[1]).toHaveAttribute("aria-selected", "true");
    expect(options()[0]).toHaveAttribute("aria-selected", "false");
    expect(active()).toBe("Trailer");
    expect(input()).toHaveFocus();
  });

  it("commits a clicked option to the row and ends the edit afterwards", async () => {
    const { onValueChange, stopEditing } = renderEditor();
    fireEvent.click(screen.getByRole("option", { name: "Rigid truck" }));
    expect(onValueChange).toHaveBeenCalledWith("vt-3");
    await waitFor(() => expect(stopEditing).toHaveBeenCalledTimes(1));
    expect(stopEditing).toHaveBeenCalledWith();
  });

  it("filters on typing (case-insensitive, anywhere in the label) and shows NoOptions without a match", () => {
    renderEditor();
    fireEvent.change(input(), { target: { value: "TR" } });
    expect(optionLabels()).toEqual(["Tractor", "Trailer", "Rigid truck"]);
    fireEvent.change(input(), { target: { value: "rigid" } });
    expect(optionLabels()).toEqual(["Rigid truck"]);
    expect(active()).toBe("Rigid truck");
    fireEvent.change(input(), { target: { value: "boat" } });
    expect(screen.queryAllByRole("option")).toHaveLength(0);
    expect(screen.getByText("MasterData:NoOptions")).toBeInTheDocument();
  });

  it("takes a printable key that started the edit as the first typed character", () => {
    renderEditor({ eventKey: "r" });
    expect(input()).toHaveValue("r");
    expect(optionLabels()).toEqual(["Tractor", "Trailer", "Rigid truck"]);
  });

  it("with a printable start key the highlight is the first match of the filtered list, not the cell's value", () => {
    /* "a" matches Tractor and Trailer; the cell's value is Trailer. */
    const { onValueChange } = renderEditor({ eventKey: "a" });
    expect(optionLabels()).toEqual(["Tractor", "Trailer"]);
    expect(active()).toBe("Tractor");
    fireEvent.keyDown(input(), { key: "Enter" });
    expect(onValueChange).toHaveBeenCalledWith("vt-1");
  });

  it("with a printable start key that matches one option, that option is highlighted", () => {
    renderEditor({ eventKey: "g", value: "vt-1" });
    expect(optionLabels()).toEqual(["Rigid truck"]);
    expect(active()).toBe("Rigid truck");
  });

  it("ignores a non-printable start key", () => {
    renderEditor({ eventKey: "Enter" });
    expect(input()).toHaveValue("");
  });

  it("moves the highlight with the arrow keys (clamped) and picks it with Enter, without the grid seeing those keys", async () => {
    const { onValueChange, stopEditing, onParentKeyDown } = renderEditor();
    fireEvent.keyDown(input(), { key: "ArrowDown" });
    expect(active()).toBe("Rigid truck");
    fireEvent.keyDown(input(), { key: "ArrowDown" });
    expect(active()).toBe("Rigid truck");
    fireEvent.keyDown(input(), { key: "ArrowUp" });
    fireEvent.keyDown(input(), { key: "ArrowUp" });
    fireEvent.keyDown(input(), { key: "ArrowUp" });
    expect(active()).toBe("Tractor");
    fireEvent.keyDown(input(), { key: "Enter" });
    expect(onValueChange).toHaveBeenCalledWith("vt-1");
    await waitFor(() => expect(stopEditing).toHaveBeenCalled());
    expect(onParentKeyDown).not.toHaveBeenCalled();
  });

  it("Enter with nothing matching picks nothing", async () => {
    const { onValueChange, stopEditing } = renderEditor();
    fireEvent.change(input(), { target: { value: "boat" } });
    fireEvent.keyDown(input(), { key: "Enter" });
    fireEvent.keyDown(input(), { key: "ArrowDown" });
    await new Promise((resolve) => setTimeout(resolve, 5));
    expect(onValueChange).not.toHaveBeenCalled();
    expect(stopEditing).not.toHaveBeenCalled();
  });

  it("Tab commits the highlighted option and lets the key through to the grid, which moves on (UI-KEY-004)", async () => {
    const { onValueChange, stopEditing, api, onParentKeyDown } = renderEditor();
    fireEvent.keyDown(input(), { key: "ArrowDown" });
    const tab = fireEvent.keyDown(input(), { key: "Tab" });
    expect(onValueChange).toHaveBeenCalledTimes(1);
    expect(onValueChange).toHaveBeenCalledWith("vt-3");
    /* Not prevented and not stopped: AG Grid's own Tab handling ends the edit with this value and moves on. */
    expect(tab).toBe(true);
    expect(onParentKeyDown).toHaveBeenCalledWith(expect.objectContaining({ key: "Tab" }));
    await new Promise((resolve) => setTimeout(resolve, 5));
    expect(stopEditing).not.toHaveBeenCalled();
    expect(api.stopEditing).not.toHaveBeenCalled();
  });

  it("Tab after typing commits the first match", () => {
    const { onValueChange } = renderEditor();
    fireEvent.change(input(), { target: { value: "rig" } });
    fireEvent.keyDown(input(), { key: "Tab" });
    expect(onValueChange).toHaveBeenCalledWith("vt-3");
  });

  it("Tab with the cell's value highlighted commits that value; Tab without a match commits nothing", () => {
    const first = renderEditor();
    fireEvent.keyDown(input(), { key: "Tab" });
    expect(first.onValueChange).toHaveBeenCalledWith("vt-2");
    cleanup();

    const second = renderEditor();
    fireEvent.change(input(), { target: { value: "boat" } });
    fireEvent.keyDown(input(), { key: "Tab" });
    expect(second.onValueChange).not.toHaveBeenCalled();
    expect(second.onParentKeyDown).toHaveBeenCalledWith(expect.objectContaining({ key: "Tab" }));
  });

  it("the commit happens before an ancestor's native keydown listener (AG Grid's popup) sees Tab", () => {
    const { onValueChange } = renderEditor();
    const seen: unknown[] = [];
    const ancestor = screen.getByRole("listbox").parentElement!.parentElement!;
    const listener = (e: KeyboardEvent) => {
      if (e.key === "Tab") seen.push(onValueChange.mock.calls.length);
    };
    ancestor.addEventListener("keydown", listener);
    fireEvent.keyDown(input(), { key: "Tab" });
    ancestor.removeEventListener("keydown", listener);
    expect(seen).toEqual([1]);
  });

  it("leaves Escape to the grid: no value, no stop of its own, the key reaches the grid", async () => {
    const { onValueChange, stopEditing, api, onParentKeyDown } = renderEditor();
    fireEvent.keyDown(input(), { key: "Escape" });
    await new Promise((resolve) => setTimeout(resolve, 5));
    expect(onValueChange).not.toHaveBeenCalled();
    expect(stopEditing).not.toHaveBeenCalled();
    expect(api.stopEditing).not.toHaveBeenCalled();
    expect(onParentKeyDown).toHaveBeenCalledWith(expect.objectContaining({ key: "Escape" }));
  });

  it("has no Add new row without onCreateNew", () => {
    renderEditor({ createLabel: "Add new vehicle type…" });
    expect(screen.queryByRole("button", { name: "Add new vehicle type…" })).toBeNull();
  });

  it("the Add new row cancels the edit and hands the typed text and the row to onCreateNew", () => {
    const onCreateNew = vi.fn();
    const { api, row, onValueChange } = renderEditor({ onCreateNew, createLabel: "Add new vehicle type…" });
    fireEvent.change(input(), { target: { value: "  Low loader " } });
    const button = screen.getByRole("button", { name: "Add new vehicle type…" });
    /* The row sits below the list, not among the options. */
    expect(within(screen.getByRole("listbox")).queryByRole("button")).toBeNull();
    fireEvent.click(button);
    expect(api.stopEditing).toHaveBeenCalledWith(true);
    expect(onCreateNew).toHaveBeenCalledWith("Low loader", row);
    expect(onValueChange).not.toHaveBeenCalled();
    /* Cancel first, then open the editor on top. */
    expect(api.stopEditing.mock.invocationCallOrder[0]).toBeLessThan(onCreateNew.mock.invocationCallOrder[0]);
  });

  it("ArrowDown past the last option highlights Add new; Enter there creates with the typed text; ArrowUp goes back", () => {
    const onCreateNew = vi.fn();
    const { api, row, onValueChange, onParentKeyDown } = renderEditor({ onCreateNew, createLabel: "Add new vehicle type…" });
    const button = screen.getByRole("button", { name: "Add new vehicle type…" });
    fireEvent.change(input(), { target: { value: "tr" } });
    expect(optionLabels()).toEqual(["Tractor", "Trailer", "Rigid truck"]);
    fireEvent.keyDown(input(), { key: "ArrowDown" });
    fireEvent.keyDown(input(), { key: "ArrowDown" });
    expect(active()).toBe("Rigid truck");
    expect(button).not.toHaveAttribute("data-highlighted");

    fireEvent.keyDown(input(), { key: "ArrowDown" });
    expect(input()).toHaveAttribute("aria-activedescendant", button.id);
    expect(button).toHaveAttribute("data-highlighted");
    /* Clamped on the create row. */
    fireEvent.keyDown(input(), { key: "ArrowDown" });
    expect(input()).toHaveAttribute("aria-activedescendant", button.id);

    fireEvent.keyDown(input(), { key: "ArrowUp" });
    expect(active()).toBe("Rigid truck");
    expect(button).not.toHaveAttribute("data-highlighted");

    fireEvent.keyDown(input(), { key: "ArrowDown" });
    fireEvent.keyDown(input(), { key: "Enter" });
    expect(api.stopEditing).toHaveBeenCalledWith(true);
    expect(onCreateNew).toHaveBeenCalledWith("tr", row);
    expect(onValueChange).not.toHaveBeenCalled();
    expect(onParentKeyDown).not.toHaveBeenCalled();
  });

  it("without a match ArrowDown goes straight to Add new, and Enter creates", () => {
    const onCreateNew = vi.fn();
    const { row } = renderEditor({ onCreateNew });
    fireEvent.change(input(), { target: { value: "Low loader" } });
    fireEvent.keyDown(input(), { key: "Enter" });
    expect(onCreateNew).not.toHaveBeenCalled();
    fireEvent.keyDown(input(), { key: "ArrowDown" });
    fireEvent.keyDown(input(), { key: "Enter" });
    expect(onCreateNew).toHaveBeenCalledWith("Low loader", row);
  });

  it("typing after reaching Add new returns the highlight to the first match", () => {
    const onCreateNew = vi.fn();
    const { onValueChange } = renderEditor({ onCreateNew, value: "vt-3" });
    fireEvent.keyDown(input(), { key: "ArrowDown" });
    fireEvent.change(input(), { target: { value: "trai" } });
    expect(active()).toBe("Trailer");
    fireEvent.keyDown(input(), { key: "Enter" });
    expect(onValueChange).toHaveBeenCalledWith("vt-2");
    expect(onCreateNew).not.toHaveBeenCalled();
  });

  it("Tab on Add new commits nothing and creates nothing", () => {
    const onCreateNew = vi.fn();
    const { onValueChange, api } = renderEditor({ onCreateNew, value: "vt-3" });
    fireEvent.keyDown(input(), { key: "ArrowDown" });
    fireEvent.keyDown(input(), { key: "Tab" });
    expect(onValueChange).not.toHaveBeenCalled();
    expect(onCreateNew).not.toHaveBeenCalled();
    expect(api.stopEditing).not.toHaveBeenCalled();
  });

  it("uses the generic label when none is given", () => {
    renderEditor({ onCreateNew: vi.fn() });
    expect(screen.getByRole("button", { name: "AddNew" })).toBeInTheDocument();
  });
});

describe("referenceCellEditorColDef", () => {
  it("makes a popup editor under the cell with the params passed through", () => {
    const onCreateNew = vi.fn();
    const def = referenceCellEditorColDef<Row>({ options: OPTIONS, onCreateNew, createLabel: "x" });
    expect(def.cellEditor).toBe(ReferenceCellEditor);
    expect(def.cellEditorPopup).toBe(true);
    expect(def.cellEditorPopupPosition).toBe("under");
    expect(def.cellEditorParams).toEqual({ options: OPTIONS, onCreateNew, createLabel: "x" });
  });
});
