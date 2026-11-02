import { act, render } from "@testing-library/react";
import { beforeAll, beforeEach, describe, expect, it, vi } from "vitest";

/* The Unlock row action of the users list: shown only for a locked-out account and only with
 * the Unlock permission (UI-PERM-002). Risk class: High — it overrides the lockout duration.
 * Resend invitation: only for an invited user who has not chosen a password yet, and only with the
 * Create permission. Risk class: High — account access. */

vi.mock("@/lib/i18n/LocalizationProvider", () => ({
  useL: () => ({ t: (k: string) => k, culture: "en", isLoading: false }),
}));

let grants: Record<string, boolean> = {};
vi.mock("@/lib/abp/queries", () => ({
  useApplicationConfiguration: () => ({ data: { auth: { grantedPolicies: grants } } }),
}));

const unlockMutateAsync = vi.fn();
const resendMutateAsync = vi.fn();
const toastSuccess = vi.fn();
const toastError = vi.fn();
vi.mock("sonner", () => ({ toast: { success: (...a: unknown[]) => toastSuccess(...a), error: (...a: unknown[]) => toastError(...a) } }));

let gridProps: { rowActions?: (rows: unknown[]) => { id: string; onClick: () => void }[] } = {};
vi.mock("@/domains/platform/administration-users", () => ({
  useUsersGridFetcher: () => vi.fn().mockResolvedValue({ rows: [], totalCount: 0, filteredCount: 0 }),
  useDeleteUser: () => ({ mutateAsync: vi.fn(), isPending: false }),
  useUnlockUser: () => ({ mutateAsync: unlockMutateAsync, isPending: false }),
  useResendInvitation: () => ({ mutateAsync: resendMutateAsync, isPending: false }),
}));
vi.mock("@/domains/platform/components/UserFormDialog", () => ({ UserFormDialog: () => null }));
vi.mock("@/apps/admin/components/ResetPasswordDialog", () => ({ ResetPasswordDialog: () => null }));
vi.mock("@/apps/admin/components/BulkDeleteUsersDialog", () => ({ BulkDeleteUsersDialog: () => null }));
vi.mock("@/components/ui/opentms-grid", () => ({
  OpenTmsGrid: (props: typeof gridProps) => {
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
  unlockMutateAsync.mockReset();
  resendMutateAsync.mockReset();
  toastSuccess.mockReset();
  toastError.mockReset();
  gridProps = {};
  grants = {
    "Platform.Administration.Users": true,
    "Platform.Administration.Users.ResetPassword": true,
    "Platform.Administration.Users.Unlock": true,
  };
});

import { UsersView } from "@/apps/admin/users/UsersView";

const renderView = () =>
  act(async () => {
    render(<UsersView />);
  });

describe("UsersView unlock", () => {
  it("shows Unlock next to Reset password for a locked-out user only", async () => {
    await renderView();
    expect(gridProps.rowActions!([{ id: "u1", isLockedOut: true }]).map((a) => a.id)).toEqual(["reset-password", "unlock"]);
    expect(gridProps.rowActions!([{ id: "u2", isLockedOut: false }]).map((a) => a.id)).toEqual(["reset-password"]);
    expect(gridProps.rowActions!([{ id: "u1", isLockedOut: true }, { id: "u2", isLockedOut: true }]).map((a) => a.id)).toEqual([]);
  });

  it("is hidden without the Unlock permission", async () => {
    grants = { "Platform.Administration.Users": true, "Platform.Administration.Users.ResetPassword": true };
    await renderView();
    expect(gridProps.rowActions!([{ id: "u1", isLockedOut: true }]).map((a) => a.id)).toEqual(["reset-password"]);
  });

  it("unlocks, confirms with a toast and reports a failure with a toast", async () => {
    unlockMutateAsync.mockResolvedValueOnce(undefined).mockRejectedValueOnce({ error: { message: "nope" } });
    await renderView();
    const unlock = () => gridProps.rowActions!([{ id: "u1", isLockedOut: true }]).find((a) => a.id === "unlock")!;

    await act(async () => unlock().onClick());
    expect(unlockMutateAsync).toHaveBeenCalledWith("u1");
    expect(toastSuccess).toHaveBeenCalledWith("Administration:UserUnlocked");

    await act(async () => unlock().onClick());
    expect(toastError).toHaveBeenCalledWith("nope");
  });
});

describe("UsersView resend invitation", () => {
  it("is offered only for a pending invitation, a single row and with the Create permission", async () => {
    grants = { ...grants, "Platform.Administration.Users.Create": true };
    await renderView();
    expect(gridProps.rowActions!([{ id: "u1", invitationPending: true }]).map((a) => a.id)).toEqual([
      "reset-password",
      "resend-invitation",
    ]);
    expect(gridProps.rowActions!([{ id: "u2", invitationPending: false }]).map((a) => a.id)).toEqual(["reset-password"]);
    expect(
      gridProps.rowActions!([
        { id: "u1", invitationPending: true },
        { id: "u2", invitationPending: true },
      ]).map((a) => a.id),
    ).toEqual([]);
  });

  it("is hidden without the Create permission", async () => {
    await renderView();
    expect(gridProps.rowActions!([{ id: "u1", invitationPending: true }]).map((a) => a.id)).toEqual(["reset-password"]);
  });

  it("resends, confirms with a toast and reports a failure with a toast", async () => {
    grants = { ...grants, "Platform.Administration.Users.Create": true };
    resendMutateAsync.mockResolvedValueOnce(undefined).mockRejectedValueOnce({ error: { message: "no mail" } });
    await renderView();
    const resend = () =>
      gridProps.rowActions!([{ id: "u1", invitationPending: true }]).find((a) => a.id === "resend-invitation")!;

    await act(async () => resend().onClick());
    expect(resendMutateAsync).toHaveBeenCalledWith("u1");
    expect(toastSuccess).toHaveBeenCalledWith("Administration:InvitationSent");

    await act(async () => resend().onClick());
    expect(toastError).toHaveBeenCalledWith("no mail");
  });
});
