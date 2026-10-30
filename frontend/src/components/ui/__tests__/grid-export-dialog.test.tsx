import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";

import { GridExportDialog } from "@/components/ui/grid-export-dialog";

/* The export dialog of a list (UI-GRID-013): an action dialog with "visible or all columns" and "filtered or all rows",
 * visible + filtered by default; the chosen columns go out with their shown headers in display order; a server
 * refusal shows inside the dialog, which stays open (UI-FB-004); a success closes it and confirms. Risk class: Medium. */

vi.mock("@/lib/i18n/LocalizationProvider", () => ({ useL: () => ({ t: (key: string) => key, culture: "en", isLoading: false }) }));
vi.mock("sonner", () => ({ toast: { success: vi.fn(), error: vi.fn() } }));
import { toast } from "sonner";

const columns = [
  { id: "number", header: "Number", visible: true },
  { id: "source", header: "Source", visible: false },
  { id: "status", header: "Status", visible: true },
];

function renderDialog(onExport: (choice: unknown) => Promise<void> = vi.fn(async () => undefined)) {
  const onOpenChange = vi.fn();
  render(<GridExportDialog open onOpenChange={onOpenChange} columns={columns} onExport={onExport} />);
  return { onExport, onOpenChange };
}

beforeEach(() => vi.mocked(toast.success).mockClear());

describe("GridExportDialog", () => {
  it("is titled and offers both choices, visible columns and filtered rows by default", () => {
    renderDialog();
    expect(screen.getByRole("dialog")).toHaveTextContent("Grid:ExportTitle");
    expect(screen.getByRole("radio", { name: /^Grid:ExportVisibleColumns/ })).toHaveAttribute("aria-checked", "true");
    expect(screen.getByRole("radio", { name: /^Grid:ExportAllColumns/ })).toHaveAttribute("aria-checked", "false");
    expect(screen.getByRole("radio", { name: /^Grid:ExportFilteredRows/ })).toHaveAttribute("aria-checked", "true");
    expect(screen.getByRole("radio", { name: /^Grid:ExportAllRows/ })).toHaveAttribute("aria-checked", "false");
    expect(screen.getByText(/Grid:ExportVisibleColumns \(2\)/)).toBeInTheDocument();
    expect(screen.getByText(/Grid:ExportAllColumns \(3\)/)).toBeInTheDocument();
  });

  it("exports the visible columns in display order with their headers and the filtered rows; closes and confirms", async () => {
    const { onExport, onOpenChange } = renderDialog();
    fireEvent.click(screen.getByRole("button", { name: "Grid:Export" }));
    await waitFor(() => expect(onExport).toHaveBeenCalledWith({ columns: [{ field: "number", header: "Number" }, { field: "status", header: "Status" }], allRows: false }));
    await waitFor(() => expect(onOpenChange).toHaveBeenCalledWith(false));
    expect(toast.success).toHaveBeenCalledWith("Grid:ExportDownloaded");
  });

  it("exports all columns and all rows when chosen", async () => {
    const { onExport } = renderDialog();
    fireEvent.click(screen.getByRole("radio", { name: /^Grid:ExportAllColumns/ }));
    fireEvent.click(screen.getByRole("radio", { name: /^Grid:ExportAllRows/ }));
    fireEvent.click(screen.getByRole("button", { name: "Grid:Export" }));
    await waitFor(() =>
      expect(onExport).toHaveBeenCalledWith({ columns: [{ field: "number", header: "Number" }, { field: "source", header: "Source" }, { field: "status", header: "Status" }], allRows: true }),
    );
  });

  it("shows the server's refusal inside the dialog and stays open", async () => {
    const onExport = vi.fn(async () => {
      throw { error: { code: "OpenTms.Orders:ExportTooManyRows", message: "The export would hold 60000 rows; at most 50000 are allowed." } };
    });
    const { onOpenChange } = renderDialog(onExport);
    fireEvent.click(screen.getByRole("button", { name: "Grid:Export" }));
    expect(await screen.findByText("The export would hold 60000 rows; at most 50000 are allowed.")).toBeInTheDocument();
    expect(onOpenChange).not.toHaveBeenCalledWith(false);
    expect(toast.success).not.toHaveBeenCalled();
    expect(screen.getByRole("button", { name: "Grid:Export" })).not.toBeDisabled();
  });

  it("shows the busy verb and disables the buttons while the export runs", async () => {
    let finish: () => void = () => undefined;
    renderDialog(vi.fn(() => new Promise<void>((resolve) => (finish = resolve))));
    fireEvent.click(screen.getByRole("button", { name: "Grid:Export" }));
    expect(await screen.findByRole("button", { name: "Grid:Exporting" })).toBeDisabled();
    expect(screen.getByRole("button", { name: "Cancel" })).toBeDisabled();
    finish();
    await waitFor(() => expect(toast.success).toHaveBeenCalled());
  });
});
