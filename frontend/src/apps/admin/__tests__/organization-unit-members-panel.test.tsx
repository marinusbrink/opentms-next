import { fireEvent, render, screen } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";

/* The Members tab of the organization-unit editor: a staged list on the editor's state.
 * Risk class: Medium (UI flow) with a permission gate (UI-PERM-002). */

vi.mock("@/lib/i18n/LocalizationProvider", () => ({
  useL: () => ({ t: (k: string) => k, culture: "en", isLoading: false }),
}));

let linesGridProps: { readOnly?: boolean; rows?: unknown[]; onRowsChange?: (rows: unknown[]) => void; toolbar?: unknown } = {};
let pickerProps: { excludeIds?: ReadonlySet<string>; onPicked?: (users: unknown[]) => void } = {};

vi.mock("@/components/ui/lines-grid-lazy", () => ({
  LazyLinesGrid: (props: typeof linesGridProps) => {
    linesGridProps = props;
    return (
      <div data-testid="lines-grid">
        {props.readOnly ? null : <div data-testid="toolbar">{props.toolbar as React.ReactNode}</div>}
      </div>
    );
  },
}));

vi.mock("@/apps/admin/components/AddOrganizationUnitMembersDialog", () => ({
  AddOrganizationUnitMembersDialog: (props: typeof pickerProps) => {
    pickerProps = props;
    return <div data-testid="add-members-dialog" />;
  },
}));

import { OrganizationUnitMembersPanel } from "@/apps/admin/components/OrganizationUnitMembersPanel";

const members = [{ id: "a", userName: "a" }, { id: "b", userName: "b" }];

beforeEach(() => {
  linesGridProps = {};
  pickerProps = {};
});

describe("OrganizationUnitMembersPanel", () => {
  it("is a read-only list without the ManageMembers permission: no Add, no removal", () => {
    render(<OrganizationUnitMembersPanel unitId="u1" unitName="Planning" members={members} onChange={vi.fn()} canManage={false} />);

    expect(linesGridProps.readOnly).toBe(true);
    expect(linesGridProps.rows).toEqual(members);
    expect(screen.queryByText("Administration:AddMembers")).not.toBeInTheDocument();
  });

  it("stages picked users (never twice) and passes removals straight through to the editor", () => {
    const onChange = vi.fn();
    render(<OrganizationUnitMembersPanel unitId="u1" unitName="Planning" members={members} onChange={onChange} canManage />);

    fireEvent.click(screen.getByText("Administration:AddMembers"));
    expect(screen.getByTestId("add-members-dialog")).toBeInTheDocument();
    expect([...pickerProps.excludeIds!]).toEqual(["a", "b"]);

    pickerProps.onPicked!([{ id: "b" }, { id: "c", userName: "c" }]);
    expect(onChange).toHaveBeenCalledWith([...members, { id: "c", userName: "c" }]);

    linesGridProps.onRowsChange!([members[0]]);
    expect(onChange).toHaveBeenLastCalledWith([members[0]]);
  });
});
