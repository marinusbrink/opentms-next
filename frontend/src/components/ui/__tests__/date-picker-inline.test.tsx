import type { CustomCellEditorProps } from "ag-grid-react";
import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";

import { DATE_CELL_EDITOR_COL_DEF, DatePicker, DatePickerCellEditor } from "@/components/ui/date-picker";

/* The DatePicker's grid-editor modes (design 43: the qualification dates of a driver): the inline
 * calendar, the popup cell editor and commit-while-typing. Risk class: Medium — a wrong hand-over
 * here loses a typed date when the grid commits on Enter, a timezone shift moves it by a day. */

vi.mock("@/lib/i18n/LocalizationProvider", () => ({
  useL: () => ({ t: (k: string) => k, culture: "nl", isLoading: false }),
}));

const cellEditorProps = (value: string | null, onValueChange: (next: string | null) => void, stopEditing: () => void = () => {}) =>
  ({ value, onValueChange, stopEditing }) as unknown as CustomCellEditorProps<unknown, string | null>;

describe("DatePicker calendar=\"inline\"", () => {
  it("renders the calendar under the input without the pick button, and a day click commits YYYY-MM-DD", () => {
    const onChange = vi.fn();
    render(<DatePicker id="d" value="2026-03-28" onChange={onChange} calendar="inline" />);
    expect(screen.queryByRole("button", { name: "PickDate" })).toBeNull();
    const inline = document.querySelector("[data-slot=date-picker-calendar]");
    expect(inline).not.toBeNull();
    expect(inline!.querySelector("[role=grid]")).not.toBeNull();

    fireEvent.click(screen.getByRole("button", { name: /15 maart 2026/i }));
    expect(onChange).toHaveBeenCalledWith("2026-03-15");
    expect(screen.getByRole("textbox")).toHaveValue("15-03-2026");
  });

  it("keeps the popover closed: there is no popover content for the inline mode", () => {
    render(<DatePicker id="d" value={null} onChange={() => {}} calendar="inline" />);
    expect(document.querySelector("[data-slot=popover-content]")).toBeNull();
    expect(document.querySelectorAll("[role=grid]")).toHaveLength(1);
  });
});

describe("DatePicker commitWhileTyping", () => {
  it("hands over a complete typed date at once and stays silent while the date is incomplete", () => {
    const onChange = vi.fn();
    render(<DatePicker id="d" value={null} onChange={onChange} commitWhileTyping />);
    const input = screen.getByRole("textbox");
    fireEvent.change(input, { target: { value: "15" } });
    fireEvent.change(input, { target: { value: "1504" } });
    fireEvent.change(input, { target: { value: "150420" } });
    expect(onChange).not.toHaveBeenCalled();
    expect(input).toHaveValue("15-04-20");

    fireEvent.change(input, { target: { value: "15042026" } });
    expect(onChange).toHaveBeenCalledTimes(1);
    expect(onChange).toHaveBeenCalledWith("2026-04-15");
  });

  it("hands over null when the text is emptied, and an invalid complete date is not committed", () => {
    const onChange = vi.fn();
    const { rerender } = render(<DatePicker id="d" value="2026-04-15" onChange={onChange} commitWhileTyping />);
    const input = screen.getByRole("textbox");
    fireEvent.change(input, { target: { value: "" } });
    expect(onChange).toHaveBeenCalledWith(null);

    rerender(<DatePicker id="d" value={null} onChange={onChange} commitWhileTyping />);
    onChange.mockClear();
    fireEvent.change(input, { target: { value: "31022026" } });
    expect(onChange).not.toHaveBeenCalled();
  });

  it("does not commit while typing by default", () => {
    const onChange = vi.fn();
    render(<DatePicker id="d" value={null} onChange={onChange} />);
    fireEvent.change(screen.getByRole("textbox"), { target: { value: "15042026" } });
    expect(onChange).not.toHaveBeenCalled();
  });
});

describe("DatePickerCellEditor", () => {
  it("is a popup editor positioned under the cell", () => {
    expect(DATE_CELL_EDITOR_COL_DEF.cellEditor).toBe(DatePickerCellEditor);
    expect(DATE_CELL_EDITOR_COL_DEF.cellEditorPopup).toBe(true);
    expect(DATE_CELL_EDITOR_COL_DEF.cellEditorPopupPosition).toBe("under");
  });

  it("renders the masked input with the inline calendar; a day click hands YYYY-MM-DD to the grid and ends the edit", async () => {
    const onValueChange = vi.fn();
    const stopEditing = vi.fn();
    render(<DatePickerCellEditor {...cellEditorProps("2026-03-28", onValueChange, stopEditing)} />);
    const editor = document.querySelector("[data-slot=date-picker-cell-editor]");
    expect(editor).not.toBeNull();
    expect(editor!.querySelector("[data-slot=date-picker-calendar] [role=grid]")).not.toBeNull();
    expect(screen.queryByRole("button", { name: "PickDate" })).toBeNull();
    expect(screen.getByRole("textbox")).toHaveValue("28-03-2026");

    fireEvent.click(screen.getByRole("button", { name: /29 maart 2026/i }));
    expect(onValueChange).toHaveBeenCalledWith("2026-03-29");
    expect(stopEditing).not.toHaveBeenCalled();
    await waitFor(() => expect(stopEditing).toHaveBeenCalledTimes(1));
  });

  it("does not end the edit while the user is still typing", () => {
    const stopEditing = vi.fn();
    render(<DatePickerCellEditor {...cellEditorProps(null, vi.fn(), stopEditing)} />);
    fireEvent.change(screen.getByRole("textbox"), { target: { value: "01012030" } });
    expect(stopEditing).not.toHaveBeenCalled();
  });

  it("hands a complete typed date to the grid without waiting for blur or Enter", () => {
    const onValueChange = vi.fn();
    render(<DatePickerCellEditor {...cellEditorProps(null, onValueChange)} />);
    fireEvent.change(screen.getByRole("textbox"), { target: { value: "01012030" } });
    expect(onValueChange).toHaveBeenCalledWith("2030-01-01");
  });
});
