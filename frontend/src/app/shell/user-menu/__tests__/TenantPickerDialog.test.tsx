import { act, fireEvent, render, screen } from "@testing-library/react";
import { beforeAll, beforeEach, describe, expect, it, vi } from "vitest";

/* "Log in to a tenant…" (docs/designs/63-user-menu.md): a server-paged grid of tenants through the impersonation
 * endpoint; the primary "Log in as…" (Impersonation:LogInAsEllipsis) needs exactly one selected, loaded row; a double-click picks directly. The grid is a stand-in that
 * exposes the callbacks. Risk class: Critical — authorization (server side); this UI part: Medium. */

vi.mock("@/lib/i18n/LocalizationProvider", () => ({
  useL: () => ({ t: (k: string) => k, culture: "en", isLoading: false, setUserLanguage: vi.fn() }),
}));

const fetchImpersonationTenants = vi.fn();
vi.mock("@/domains/platform/impersonation", () => ({
  fetchImpersonationTenants: (...args: unknown[]) => fetchImpersonationTenants(...args),
}));

type GridProps = {
  gridId: string;
  fetchRows: (req: unknown) => Promise<unknown>;
  onSelectionChange: (s: unknown) => void;
  onOpen: (row: { id: string; name: string }) => void;
};
let grid: GridProps | undefined;
vi.mock("@/components/ui/opentms-grid-lazy", () => ({
  LazyOpenTmsGrid: (props: GridProps) => {
    grid = props;
    return <div data-testid="tenants-grid" data-grid-id={props.gridId} />;
  },
}));

class MockResizeObserver {
  observe() {}
  disconnect() {}
  unobserve() {}
}
beforeAll(() => vi.stubGlobal("ResizeObserver", MockResizeObserver));

import { TenantPickerDialog } from "@/app/shell/user-menu/TenantPickerDialog";

const ROWS = [
  { id: "t-acme", name: "acme" },
  { id: "t-beta", name: "beta" },
];
const select = (ids: string[]) => act(() => grid!.onSelectionChange({ mode: "Explicit", explicitIds: ids, filterRequest: null, excludedIds: [] }));
const next = () => screen.getByRole("button", { name: "Impersonation:LogInAsEllipsis" });
const loadRows = async () => {
  await act(async () => {
    await grid!.fetchRows({ startRow: 0, endRow: 50 });
  });
};

beforeEach(() => {
  grid = undefined;
  fetchImpersonationTenants.mockReset();
  fetchImpersonationTenants.mockResolvedValue({ rows: ROWS, totalCount: 2, filteredCount: 2 });
});

describe("TenantPickerDialog", () => {
  it("pages the tenants through the impersonation endpoint", async () => {
    render(<TenantPickerDialog open onClose={() => {}} onPick={() => {}} />);
    expect(screen.getByTestId("tenants-grid").dataset.gridId).toBe("platform.userMenu.impersonationTenants");
    await loadRows();
    expect(fetchImpersonationTenants).toHaveBeenCalledWith({ startRow: 0, endRow: 50 });
  });

  it("enables Log in as… only with exactly one loaded row selected, and it picks that row", async () => {
    const onPick = vi.fn();
    render(<TenantPickerDialog open onClose={() => {}} onPick={onPick} />);
    await loadRows();
    expect(next()).toBeDisabled();

    select(["t-acme", "t-beta"]);
    expect(next()).toBeDisabled();

    select(["t-unknown"]);
    expect(next()).toBeDisabled();

    select(["t-beta"]);
    expect(next()).toBeEnabled();
    fireEvent.click(next());
    expect(onPick).toHaveBeenCalledExactlyOnceWith({ id: "t-beta", name: "beta" });
  });

  it("a double-click picks the row directly", () => {
    const onPick = vi.fn();
    render(<TenantPickerDialog open onClose={() => {}} onPick={onPick} />);
    act(() => grid!.onOpen({ id: "t-acme", name: "acme" }));
    expect(onPick).toHaveBeenCalledExactlyOnceWith({ id: "t-acme", name: "acme" });
  });

  it("Cancel closes", () => {
    const onClose = vi.fn();
    render(<TenantPickerDialog open onClose={onClose} onPick={() => {}} />);
    fireEvent.click(screen.getByRole("button", { name: "Cancel" }));
    expect(onClose).toHaveBeenCalled();
  });
});
