import { act, render, screen } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";

/* The Tenants list's "Log in as…" row action (docs/designs/tenants-and-editions.md, D14 step 1): offered only with
 * Platform.Tenants.Impersonation (UI-PERM-002: hidden, never disabled), for a single row, and it opens the picker for
 * that tenant. Risk class: Critical — authentication. */

vi.mock("@/lib/i18n/LocalizationProvider", () => ({
  useL: () => ({ t: (k: string) => k, culture: "en", isLoading: false }),
}));

let grants: Record<string, boolean> = {};
vi.mock("@/lib/abp/queries", () => ({
  useApplicationConfiguration: () => ({ data: { auth: { grantedPolicies: grants }, features: { values: {} } } }),
}));

vi.mock("@/domains/platform/tenants", () => ({
  TenantProvisioningStates: { Queued: "Queued", Running: "Running", Succeeded: "Succeeded", Failed: "Failed" },
  useRetryTenantProvisioning: () => ({ mutateAsync: vi.fn() }),
  useResendTenantInvitation: () => ({ mutateAsync: vi.fn() }),
  useTenantsGridFetcher: () => vi.fn().mockResolvedValue({ rows: [], totalCount: 0, filteredCount: 0 }),
}));

type RowAction = { id: string; labelKey: string; onClick: () => void };
let rowActions: ((rows: unknown[]) => RowAction[]) | undefined;
vi.mock("@/components/ui/opentms-grid", () => ({
  OpenTmsGrid: (props: { rowActions: (rows: unknown[]) => RowAction[] }) => {
    rowActions = props.rowActions;
    return <div data-testid="tenants-grid" />;
  },
}));

vi.mock("@/domains/platform/components/ImpersonateTenantDialog", () => ({
  ImpersonateTenantDialog: ({ tenant }: { tenant: { name: string } }) => <div data-testid="impersonate-dialog">{tenant.name}</div>,
}));
vi.mock("@/apps/tenants/TenantFormDialog", () => ({ TenantFormDialog: () => null }));
vi.mock("@/apps/tenants/DeleteTenantDialog", () => ({ DeleteTenantDialog: () => null }));
vi.mock("@/components/ui/app-command-bar", () => ({ AppCommandBar: () => null }));

import { TenantsView } from "@/apps/tenants/TenantsView";

const ACME = { id: "t-acme", name: "acme", provisioningState: "Succeeded" };
const BETA = { id: "t-beta", name: "beta", provisioningState: "Succeeded" };

async function renderView() {
  render(<TenantsView />);
  await screen.findByTestId("tenants-grid");
}

beforeEach(() => {
  rowActions = undefined;
  grants = { "Platform.Tenants": true };
});

describe("TenantsView – Log in as…", () => {
  it("is not offered without Platform.Tenants.Impersonation", async () => {
    grants = { "Platform.Tenants": true, "Platform.Tenants.Update": true, "Platform.Tenants.Delete": true };
    await renderView();
    expect(rowActions!([ACME]).map((a) => a.id)).not.toContain("impersonate");
  });

  it("is offered for a single row with the permission and opens the picker for that tenant", async () => {
    grants = { "Platform.Tenants": true, "Platform.Tenants.Impersonation": true };
    await renderView();

    const action = rowActions!([ACME]).find((a) => a.id === "impersonate")!;
    expect(action.labelKey).toBe("Impersonation:LogInAsEllipsis");
    expect(screen.queryByTestId("impersonate-dialog")).toBeNull();

    act(() => action.onClick());
    expect(screen.getByTestId("impersonate-dialog")).toHaveTextContent("acme");
  });

  it("is not offered for a multi-row selection", async () => {
    grants = { "Platform.Tenants": true, "Platform.Tenants.Impersonation": true };
    await renderView();
    expect(rowActions!([ACME, BETA])).toEqual([]);
  });
});
