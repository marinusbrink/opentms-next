import type { ReactNode } from "react";
import { fireEvent, render, screen } from "@testing-library/react";
import type { ColDef, GetContextMenuItemsParams, ICellRendererParams } from "ag-grid-community";
import { beforeEach, describe, expect, it, vi } from "vitest";

/* LinesGrid.canRemoveRow (design 48: a vehicle's characteristics inherited from its type cannot be removed). AG Grid is
 * replaced by a stand-in that records its props and renders each row's cells through the column renderers, so the
 * test exercises LinesGrid's own logic: the trash icon per row, the context-menu Remove and removeRows' filtering.
 * Risk class: Medium (UI behaviour of a library component). */

vi.mock("@/lib/i18n/LocalizationProvider", () => ({
  useL: () => ({ t: (k: string) => k, culture: "en", isLoading: false }),
}));

vi.mock("ag-grid-enterprise", () => ({ AllEnterpriseModule: {} }));
vi.mock("ag-grid-community", async (importOriginal) => ({
  ...(await importOriginal<typeof import("ag-grid-community")>()),
  ModuleRegistry: { registerModules: () => undefined },
}));

interface GridProps {
  rowData: Row[];
  columnDefs: ColDef<Row>[];
  getContextMenuItems: (params: GetContextMenuItemsParams<Row>) => { name: string; action: () => void }[];
}

const grid: { props?: GridProps; selected: Row[] } = { selected: [] };

vi.mock("ag-grid-react", () => ({
  AgGridReact: (props: GridProps) => {
    grid.props = props;
    return (
      <div data-testid="grid">
        {props.rowData.map((row) => (
          <div key={row.id} data-testid={`row-${row.id}`}>
            {props.columnDefs.map((col, index) => {
              const renderer = col.cellRenderer as ((p: ICellRendererParams<Row>) => ReactNode) | undefined;
              return <span key={index}>{typeof renderer === "function" ? renderer({ data: row } as ICellRendererParams<Row>) : String(row[col.field as keyof Row] ?? "")}</span>;
            })}
          </div>
        ))}
      </div>
    );
  },
}));

import { LinesGrid } from "@/components/ui/lines-grid";

interface Row {
  id: string;
  name: string;
  inherited: boolean;
}

const ROWS: Row[] = [
  { id: "adr", name: "Dangerous goods", inherited: true },
  { id: "cool", name: "Cooling", inherited: false },
  { id: "tl", name: "Tail lift", inherited: false },
];

const columns: ColDef<Row>[] = [{ field: "name" }];

function renderGrid(props: { canRemoveRow?: (row: Row) => boolean; readOnly?: boolean } = {}) {
  const onRowsChange = vi.fn();
  render(<LinesGrid<Row> rows={ROWS} onRowsChange={onRowsChange} getRowId={(r) => r.id} columnDefs={columns} {...props} />);
  return onRowsChange;
}

/** The context menu as AG Grid would ask for it: clicked on `clicked` with `selected` selected. */
function menu(clicked: Row | undefined, selected: Row[] = []) {
  grid.selected = selected;
  return grid.props!.getContextMenuItems({ node: clicked ? { data: clicked } : null, api: { getSelectedRows: () => grid.selected } } as unknown as GetContextMenuItemsParams<Row>);
}

const trash = (id: string) => screen.getByTestId(`row-${id}`).querySelector("button[aria-label=Remove]");

beforeEach(() => {
  grid.props = undefined;
  grid.selected = [];
});

describe("LinesGrid.canRemoveRow", () => {
  it("without the prop every row has a trash icon and the context menu offers Remove", () => {
    const onRowsChange = renderGrid();
    for (const row of ROWS) expect(trash(row.id)).not.toBeNull();
    expect(menu(ROWS[0]).map((i) => i.name)).toEqual(["Remove"]);

    fireEvent.click(trash("adr")!);
    expect(onRowsChange).toHaveBeenCalledWith([ROWS[1], ROWS[2]]);
  });

  it("a row the predicate refuses gets no trash icon; the others keep theirs and remove only themselves", () => {
    const onRowsChange = renderGrid({ canRemoveRow: (row) => !row.inherited });
    expect(trash("adr")).toBeNull();
    expect(trash("cool")).not.toBeNull();
    expect(trash("tl")).not.toBeNull();

    fireEvent.click(trash("cool")!);
    expect(onRowsChange).toHaveBeenCalledWith([ROWS[0], ROWS[2]]);
  });

  it("the context menu on a refused row offers no Remove", () => {
    renderGrid({ canRemoveRow: (row) => !row.inherited });
    expect(menu(ROWS[0])).toEqual([]);
  });

  it("the context menu over a mixed selection removes only the removable rows", () => {
    const onRowsChange = renderGrid({ canRemoveRow: (row) => !row.inherited });
    const items = menu(ROWS[0], ROWS);
    expect(items.map((i) => i.name)).toEqual(["Remove"]);

    items[0].action();
    expect(onRowsChange).toHaveBeenCalledTimes(1);
    expect(onRowsChange).toHaveBeenCalledWith([ROWS[0]]);
  });

  it("removes nothing (and does not call onRowsChange) when every target is refused", () => {
    const onRowsChange = renderGrid({ canRemoveRow: () => false });
    for (const row of ROWS) expect(trash(row.id)).toBeNull();
    expect(menu(ROWS[1], ROWS)).toEqual([]);
    expect(onRowsChange).not.toHaveBeenCalled();
  });

  it("a read-only grid has no remove column and no menu, whatever the predicate", () => {
    renderGrid({ canRemoveRow: () => true, readOnly: true });
    for (const row of ROWS) expect(trash(row.id)).toBeNull();
    expect(grid.props!.columnDefs).toHaveLength(1);
    expect(menu(ROWS[1])).toEqual([]);
  });
});
