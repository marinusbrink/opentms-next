import { act, fireEvent, render, screen } from "@testing-library/react";
import { beforeAll, beforeEach, describe, expect, it, vi } from "vitest";

/* The two-phase delete of an organization unit (UI-ACT-005, UI-FB-008): a plain confirm,
 * then, when the server answers 409, a second confirm with the sub-unit and member counts.
 * Risk class: High — the forced delete cascades over sub-units and memberships. */

/* Two keys get their real placeholders so the names and counts can be asserted. */
const TEXTS: Record<string, string> = {
  "Administration:DeleteOrganizationUnitQuestion": "Delete organization unit {0}?",
  "Administration:OrganizationUnitInUseWarning": "The unit '{0}' has {1} sub-units and {2} members.",
};

vi.mock("@/lib/i18n/LocalizationProvider", () => ({
  useL: () => ({ t: (k: string) => TEXTS[k] ?? k, culture: "en", isLoading: false }),
}));

vi.mock("@/lib/abp/queries", () => ({
  useApplicationConfiguration: () => ({
    data: {
      auth: {
        grantedPolicies: {
          "Platform.Administration.OrganizationUnits": true,
          "Platform.Administration.OrganizationUnits.Create": true,
          "Platform.Administration.OrganizationUnits.Update": true,
          "Platform.Administration.OrganizationUnits.Delete": true,
        },
      },
    },
  }),
}));

const deleteMutateAsync = vi.fn();
let gridProps: { rowActions?: (rows: unknown[]) => { id: string; onClick: () => void }[]; treeData?: unknown } = {};

vi.mock("@/domains/platform/administration-organization-units", () => ({
  useOrganizationUnitsGridFetcher: () => vi.fn().mockResolvedValue({ rows: [], totalCount: 0, filteredCount: 0 }),
  useDeleteOrganizationUnit: () => ({ mutateAsync: deleteMutateAsync, isPending: false }),
}));

vi.mock("@/apps/admin/components/OrganizationUnitFormDialog", () => ({
  OrganizationUnitFormDialog: () => null,
}));

vi.mock("@/components/ui/opentms-grid-lazy", () => ({
  LazyOpenTmsGrid: (props: typeof gridProps) => {
    gridProps = props;
    return <div data-testid="mock-grid" />;
  },
}));

class MockResizeObserver {
  private readonly cb: ResizeObserverCallback;
  constructor(cb: ResizeObserverCallback) {
    this.cb = cb;
  }
  observe(el: Element) {
    this.cb([{ contentRect: { width: 9999 } as DOMRectReadOnly, target: el } as ResizeObserverEntry], this as unknown as ResizeObserver);
  }
  disconnect() {}
  unobserve() {}
}

beforeAll(() => {
  vi.stubGlobal("ResizeObserver", MockResizeObserver);
});

beforeEach(() => {
  deleteMutateAsync.mockReset();
  gridProps = {};
});

import { OrganizationUnitsView } from "@/apps/admin/organization-units/OrganizationUnitsView";

const unit = { id: "u1", displayName: "Head office", hasChildren: true };

describe("OrganizationUnitsView delete", () => {
  it("offers Edit, New sub-unit and Delete for one row and nothing for several", () => {
    render(<OrganizationUnitsView />);
    expect(gridProps.rowActions!([unit]).map((a) => a.id)).toEqual(["edit", "new-sub-unit", "delete"]);
    expect(gridProps.rowActions!([unit, { id: "u2" }])).toEqual([]);
    expect(gridProps.treeData).toBeDefined();
  });

  it("asks once, then again with the counts when the unit is in use, and forces on the second yes", async () => {
    deleteMutateAsync
      .mockResolvedValueOnce({ status: "conflict", displayName: "Head office", childCount: 2, memberCount: 3 })
      .mockResolvedValueOnce({ status: "deleted" });
    render(<OrganizationUnitsView />);

    act(() => gridProps.rowActions!([unit]).find((a) => a.id === "delete")!.onClick());
    expect(screen.getByText(/Delete organization unit Head office\?/)).toBeInTheDocument();

    await act(async () => {
      fireEvent.click(screen.getByRole("button", { name: "Delete" }));
    });
    expect(deleteMutateAsync).toHaveBeenNthCalledWith(1, { id: "u1", force: false });

    expect(screen.getByText("The unit 'Head office' has 2 sub-units and 3 members.")).toBeInTheDocument();

    await act(async () => {
      fireEvent.click(screen.getByRole("button", { name: "Delete" }));
    });
    expect(deleteMutateAsync).toHaveBeenNthCalledWith(2, { id: "u1", force: true });
    expect(screen.queryByText(/has 2 sub-units/)).not.toBeInTheDocument();
  });
});
