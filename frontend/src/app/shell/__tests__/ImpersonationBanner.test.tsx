import { act, fireEvent, render, screen } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";

/* The "logged in as" strip (docs/designs/tenants-and-editions.md, D14 step 5): only while the session carries ABP's
 * impersonator claim, naming who is logged in as whom; its button asks the API for the return ticket and switches the
 * session back to the impersonator's tenant (or the host). Risk class: Critical — authentication. */

vi.mock("@/lib/i18n/LocalizationProvider", () => ({
  useL: () => ({ t: (k: string) => (k === "Impersonation:Banner" ? "{0} in {1} for {2}" : k), culture: "en", isLoading: false }),
}));

let config: unknown;
vi.mock("@/lib/abp/queries", () => ({
  useApplicationConfiguration: () => ({ data: config }),
}));

const returnMutate = vi.fn();
let returnPending = false;
vi.mock("@/domains/platform/impersonation", () => ({
  useReturnFromImpersonation: () => ({ mutateAsync: returnMutate, isPending: returnPending }),
}));

const switchSession = vi.fn();
vi.mock("@/auth/session-switch", () => ({
  switchSession: (...args: unknown[]) => switchSession(...args),
}));

vi.mock("@/lib/api/errors", () => ({
  getApiErrorMessage: (err: unknown) => (err instanceof Error ? err.message : "error"),
}));

import { ImpersonationBanner } from "@/app/shell/ImpersonationBanner";

const impersonated = (extra: Record<string, unknown> = {}) => ({
  currentTenant: { id: "t-acme", name: "acme" },
  currentUser: { userName: "acme.planner", impersonatorUserId: "u-admin", impersonatorUserName: "admin", ...extra },
});

beforeEach(() => {
  returnMutate.mockReset();
  switchSession.mockReset();
  returnPending = false;
  config = impersonated();
});

describe("ImpersonationBanner", () => {
  it("is not shown in an ordinary session or before the configuration loads", () => {
    config = { currentTenant: { id: "t-acme", name: "acme" }, currentUser: { userName: "acme.planner" } };
    const { container, unmount } = render(<ImpersonationBanner />);
    expect(container).toBeEmptyDOMElement();
    unmount();

    config = undefined;
    expect(render(<ImpersonationBanner />).container).toBeEmptyDOMElement();
  });

  it("names the user, the tenant and the impersonator, with Back to host for a host impersonator", () => {
    render(<ImpersonationBanner />);
    expect(screen.getByRole("status")).toHaveTextContent("acme.planner in acme for admin");
    expect(screen.getByRole("button", { name: "Impersonation:BackToHost" })).toBeInTheDocument();
  });

  it("offers Back to my account when the impersonator has a tenant", () => {
    config = impersonated({ impersonatorTenantId: "t-home", impersonatorTenantName: "home" });
    render(<ImpersonationBanner />);
    expect(screen.getByRole("button", { name: "Impersonation:BackToTenant" })).toBeInTheDocument();
  });

  it("Back to host asks for the return ticket and switches to the host session", async () => {
    returnMutate.mockResolvedValue("https://auth.example.test/Account/Impersonate?ticket=back");
    render(<ImpersonationBanner />);

    await act(async () => fireEvent.click(screen.getByRole("button", { name: "Impersonation:BackToHost" })));

    expect(returnMutate).toHaveBeenCalledOnce();
    expect(switchSession).toHaveBeenCalledExactlyOnceWith("https://auth.example.test/Account/Impersonate?ticket=back", undefined);
  });

  it("Back to my account switches to the impersonator's tenant", async () => {
    config = impersonated({ impersonatorTenantId: "t-home", impersonatorTenantName: "home" });
    returnMutate.mockResolvedValue("https://auth.example.test/r");
    render(<ImpersonationBanner />);

    await act(async () => fireEvent.click(screen.getByRole("button", { name: "Impersonation:BackToTenant" })));

    expect(switchSession).toHaveBeenCalledExactlyOnceWith("https://auth.example.test/r", { id: "t-home", name: "home" });
  });

  it("shows the error and stays when the API refuses", async () => {
    returnMutate.mockRejectedValue(new Error("You are not logged in as another user."));
    render(<ImpersonationBanner />);

    await act(async () => fireEvent.click(screen.getByRole("button", { name: "Impersonation:BackToHost" })));

    expect(screen.getByRole("status")).toHaveTextContent("You are not logged in as another user.");
    expect(switchSession).not.toHaveBeenCalled();
  });

  it("disables the button while the request runs", () => {
    returnPending = true;
    render(<ImpersonationBanner />);
    expect(screen.getByRole("button", { name: "Impersonation:BackToHost" })).toBeDisabled();
  });
});
