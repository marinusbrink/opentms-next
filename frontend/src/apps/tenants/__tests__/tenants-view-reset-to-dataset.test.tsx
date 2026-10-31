import { act, render, screen } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";

/* The Tenants list's "Reset to dataset" row action (docs/designs/65-tenant-test-datasets.md, Behaviour 1): offered only
 * with Platform.Tenants.ResetToDataset (UI-PERM-002: hidden, never disabled, without it), only for a usable tenant
 * (provisioned, or from before the Tenants app), disabled while a reset is Queued/Running, and its two dataset children
 * open the confirm dialog for that tenant and dataset. Risk class: Critical (deletes a tenant's data) — component level;
 * the server re-checks every guard (TenantResetToDatasetTests). */

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
  TestDatasets: { Average: "Average", Large: "Large" },
  useResetTenantToDataset: () => ({ mutateAsync: vi.fn() }),
  useTenantsGridFetcher: () => vi.fn().mockResolvedValue({ rows: [], totalCount: 0, filteredCount: 0 }),
}));

type RowAction = {
  id: string;
  labelKey: string;
  onClick?: () => void;
  disabled?: boolean;
  disabledReasonKey?: string;
  children?: RowAction[];
};
let rowActions: ((rows: unknown[]) => RowAction[]) | undefined;
vi.mock("@/components/ui/opentms-grid", () => ({
  OpenTmsGrid: (props: { rowActions: (rows: unknown[]) => RowAction[] }) => {
    rowActions = props.rowActions;
    return <div data-testid="tenants-grid" />;
  },
}));

vi.mock("@/apps/tenants/ResetTenantDatasetDialog", () => ({
  ResetTenantDatasetDialog: ({ request }: { request?: { tenant: { id: string; name: string }; dataset: string } }) =>
    request ? (
      <div data-testid="reset-dialog">
        {request.tenant.id}|{request.tenant.name}|{request.dataset}
      </div>
    ) : null,
}));
vi.mock("@/domains/platform/components/ImpersonateTenantDialog", () => ({ ImpersonateTenantDialog: () => null }));
vi.mock("@/apps/tenants/TenantFormDialog", () => ({ TenantFormDialog: () => null }));
vi.mock("@/apps/tenants/DeleteTenantDialog", () => ({ DeleteTenantDialog: () => null }));
vi.mock("@/components/ui/app-command-bar", () => ({ AppCommandBar: () => null }));

import { TenantsView } from "@/apps/tenants/TenantsView";

const ACME = { id: "t-acme", name: "acme", provisioningState: "Succeeded" };
const BETA = { id: "t-beta", name: "beta", provisioningState: "Succeeded" };
const RESET = "Platform.Tenants.ResetToDataset";

async function renderView() {
  render(<TenantsView />);
  await screen.findByTestId("tenants-grid");
}

const resetAction = (row: object) => rowActions!([row]).find((a) => a.id === "reset-to-dataset");

beforeEach(() => {
  rowActions = undefined;
  grants = { "Platform.Tenants": true, [RESET]: true };
});

describe("TenantsView – Reset to dataset", () => {
  it("is not offered without Platform.Tenants.ResetToDataset", async () => {
    grants = {
      "Platform.Tenants": true,
      "Platform.Tenants.Update": true,
      "Platform.Tenants.Delete": true,
      "Platform.Tenants.Impersonation": true,
    };
    await renderView();
    expect(resetAction(ACME)).toBeUndefined();
  });

  it("is offered with the permission for a provisioned tenant, enabled, with the two datasets as children", async () => {
    await renderView();
    const action = resetAction(ACME)!;
    expect(action).toBeDefined();
    expect(action.labelKey).toBe("Tenants:ResetToDataset");
    expect(action.disabled).toBe(false);
    expect(action.children!.map((c) => c.labelKey)).toEqual(["Tenants:Dataset:Average", "Tenants:Dataset:Large"]);
    expect(action.children!.map((c) => c.id)).toEqual(["reset-to-dataset-Average", "reset-to-dataset-Large"]);
  });

  it("is offered for a tenant from before the Tenants app (no provisioning state)", async () => {
    await renderView();
    expect(resetAction({ id: "t-old", name: "old", provisioningState: null })).toBeDefined();
    expect(resetAction({ id: "t-old", name: "old" })).toBeDefined();
  });

  it.each(["Queued", "Running", "Failed"])("is not offered for a tenant whose provisioning is %s", async (state) => {
    await renderView();
    expect(resetAction({ ...ACME, provisioningState: state })).toBeUndefined();
  });

  it.each(["Queued", "Running"])("is disabled with a reason while a reset is %s", async (state) => {
    await renderView();
    const action = resetAction({ ...ACME, datasetName: "Average", datasetResetState: state })!;
    expect(action.disabled).toBe(true);
    expect(action.disabledReasonKey).toBe("Tenants:ResetAlreadyRunning");
  });

  it.each(["Succeeded", "Failed"])("is enabled again after a reset that %s", async (state) => {
    await renderView();
    expect(resetAction({ ...ACME, datasetName: "Large", datasetResetState: state })!.disabled).toBe(false);
  });

  it("is not offered for a multi-row selection", async () => {
    await renderView();
    expect(rowActions!([ACME, BETA]).map((a) => a.id)).not.toContain("reset-to-dataset");
  });

  it.each([
    [0, "Average"],
    [1, "Large"],
  ])("child %i opens the confirm dialog for this tenant and the %s dataset", async (index, dataset) => {
    await renderView();
    expect(screen.queryByTestId("reset-dialog")).toBeNull();

    const child = resetAction(BETA)!.children![index];
    act(() => child.onClick!());

    expect(screen.getByTestId("reset-dialog")).toHaveTextContent(`t-beta|beta|${dataset}`);
  });
});
