import { act, fireEvent, render, screen } from "@testing-library/react";
import { beforeAll, beforeEach, describe, expect, it, vi } from "vitest";

/* The "Log in as" picker (docs/designs/tenants-and-editions.md, D14 step 1): an action dialog with the tenant's users;
 * the primary needs exactly one selected user, a double-click (onOpen) chooses directly, an API refusal shows inline,
 * and a granted ticket switches the session into the tenant. The grid is a stand-in that exposes the callbacks.
 * Risk class: Critical — authentication. */

vi.mock("@/lib/i18n/LocalizationProvider", () => ({
  useL: () => ({ t: (k: string) => k, culture: "en", isLoading: false }),
}));
vi.mock("@/lib/abp/queries", () => ({
  useApplicationConfiguration: () => ({ data: { features: { values: {} } } }),
}));
vi.mock("@/lib/api/errors", () => ({
  getApiErrorMessage: (err: unknown) => (err instanceof Error ? err.message : "error"),
}));

const fetchTenantUsers = vi.fn();
const startMutate = vi.fn();
vi.mock("@/domains/platform/impersonation", () => ({
  fetchTenantUsers: (...args: unknown[]) => fetchTenantUsers(...args),
  useStartImpersonation: () => ({ mutateAsync: startMutate, isPending: false }),
}));

const switchSession = vi.fn();
vi.mock("@/auth/session-switch", () => ({
  switchSession: (...args: unknown[]) => switchSession(...args),
}));

type GridProps = {
  gridId: string;
  fetchRows: (req: unknown) => unknown;
  onSelectionChange: (s: unknown) => void;
  onOpen: (row: { id: string }) => void;
};
let grid: GridProps | undefined;
vi.mock("@/components/ui/opentms-grid-lazy", () => ({
  LazyOpenTmsGrid: (props: GridProps) => {
    grid = props;
    return <div data-testid="users-grid" data-grid-id={props.gridId} />;
  },
}));

class MockResizeObserver {
  observe() {}
  disconnect() {}
  unobserve() {}
}
beforeAll(() => vi.stubGlobal("ResizeObserver", MockResizeObserver));

import { ImpersonateTenantDialog } from "@/domains/platform/components/ImpersonateTenantDialog";

const TENANT = { id: "t-acme", name: "acme" } as never;
const select = (ids: string[], mode = "Explicit") =>
  act(() => grid!.onSelectionChange({ mode, explicitIds: ids, filterRequest: null, excludedIds: [] }));
const logInAs = () => screen.getByRole("button", { name: "Impersonation:LogInAs" });

beforeEach(() => {
  grid = undefined;
  fetchTenantUsers.mockReset();
  startMutate.mockReset();
  switchSession.mockReset();
});

describe("ImpersonateTenantDialog", () => {
  it("lists the tenant's users through the impersonation endpoint", () => {
    render(<ImpersonateTenantDialog tenant={TENANT} open onClose={() => {}} />);
    expect(screen.getByTestId("users-grid").dataset.gridId).toBe("platform.tenants.impersonationUsers");
    grid!.fetchRows({ startRow: 0, endRow: 50 });
    expect(fetchTenantUsers).toHaveBeenCalledWith("t-acme", { startRow: 0, endRow: 50 });
  });

  it("enables Log in as only with exactly one selected user", () => {
    render(<ImpersonateTenantDialog tenant={TENANT} open onClose={() => {}} />);
    expect(logInAs()).toBeDisabled();

    select(["u-1"]);
    expect(logInAs()).toBeEnabled();

    select(["u-1", "u-2"]);
    expect(logInAs()).toBeDisabled();

    select([], "Explicit");
    expect(logInAs()).toBeDisabled();

    select(["u-1"], "FilterBased");
    expect(logInAs()).toBeDisabled();
  });

  it("Log in as starts for the selected user and switches into the tenant", async () => {
    startMutate.mockResolvedValue("https://auth.example.test/Account/Impersonate?ticket=t");
    render(<ImpersonateTenantDialog tenant={TENANT} open onClose={() => {}} />);
    select(["u-1"]);

    await act(async () => fireEvent.click(logInAs()));

    expect(startMutate).toHaveBeenCalledExactlyOnceWith({ tenantId: "t-acme", userId: "u-1" });
    expect(switchSession).toHaveBeenCalledExactlyOnceWith("https://auth.example.test/Account/Impersonate?ticket=t", { id: "t-acme", name: "acme" });
  });

  it("a double-click on a row starts for that row directly", async () => {
    startMutate.mockResolvedValue("https://auth.example.test/x");
    render(<ImpersonateTenantDialog tenant={TENANT} open onClose={() => {}} />);

    await act(async () => grid!.onOpen({ id: "u-9" }));

    expect(startMutate).toHaveBeenCalledExactlyOnceWith({ tenantId: "t-acme", userId: "u-9" });
    expect(switchSession).toHaveBeenCalledOnce();
  });

  it("shows the refusal inline and does not leave", async () => {
    startMutate.mockRejectedValue(new Error("This user does not exist or is not active."));
    render(<ImpersonateTenantDialog tenant={TENANT} open onClose={() => {}} />);
    select(["u-1"]);

    await act(async () => fireEvent.click(logInAs()));

    expect(screen.getByRole("alert")).toHaveTextContent("This user does not exist or is not active.");
    expect(switchSession).not.toHaveBeenCalled();
  });

  it("Cancel closes without starting", () => {
    const onClose = vi.fn();
    render(<ImpersonateTenantDialog tenant={TENANT} open onClose={onClose} />);
    fireEvent.click(screen.getByRole("button", { name: "Cancel" }));
    expect(onClose).toHaveBeenCalled();
    expect(startMutate).not.toHaveBeenCalled();
  });
});
