import { act, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";

/* "Reset to dataset" permanently replaces a tenant's business data (docs/designs/65-tenant-test-datasets.md,
 * Behaviour 2): the confirm stays disabled until the tenant's exact name is typed, and the mutation receives the
 * tenant id, the dataset and the typed name (the server checks the name again). Risk class: Critical — component level. */

vi.mock("@/lib/i18n/LocalizationProvider", () => ({
  useL: () => ({ t: (key: string) => key, culture: "en", isLoading: false }),
}));
vi.mock("sonner", () => ({ toast: { success: vi.fn(), error: vi.fn() } }));

const mutateAsync = vi.fn();
vi.mock("@/domains/platform/tenants", () => ({
  useResetTenantToDataset: () => ({ mutateAsync, isPending: false }),
}));

import { ResetTenantDatasetDialog } from "@/apps/tenants/ResetTenantDatasetDialog";

const tenant = {
  id: "t-1",
  name: "Acme Transport",
  provisioningState: "Succeeded",
  creationTime: "2026-10-14T10:00:00Z",
};

const confirmButton = () => screen.getByRole("button", { name: "Tenants:ResetConfirm" });
const nameInput = () => document.getElementById("rt-confirm-name") as HTMLInputElement;
const type = (value: string) => fireEvent.change(nameInput(), { target: { value } });

beforeEach(() => {
  mutateAsync.mockReset();
  mutateAsync.mockResolvedValue(undefined);
});

describe("ResetTenantDatasetDialog", () => {
  it("renders nothing without a request", () => {
    render(<ResetTenantDatasetDialog request={undefined} onClose={vi.fn()} onQueued={vi.fn()} />);
    expect(screen.queryByRole("button", { name: "Tenants:ResetConfirm" })).toBeNull();
  });

  it("keeps the confirm disabled until the exact name is typed", () => {
    render(<ResetTenantDatasetDialog request={{ tenant, dataset: "Average" }} onClose={vi.fn()} onQueued={vi.fn()} />);

    expect(confirmButton()).toBeDisabled();
    for (const attempt of ["Acme", "acme transport", "ACME TRANSPORT", "Acme Transport ", " Acme Transport", "Acme  Transport"]) {
      type(attempt);
      expect(confirmButton()).toBeDisabled();
    }

    type("Acme Transport");
    expect(confirmButton()).toBeEnabled();

    type("Acme Transpor");
    expect(confirmButton()).toBeDisabled();
  });

  it("a click on the disabled confirm resets nothing", () => {
    render(<ResetTenantDatasetDialog request={{ tenant, dataset: "Large" }} onClose={vi.fn()} onQueued={vi.fn()} />);
    type("wrong");
    fireEvent.click(confirmButton());
    expect(mutateAsync).not.toHaveBeenCalled();
  });

  it.each(["Average", "Large"] as const)(
    "calls the mutation with the tenant id, the %s dataset and the typed name, then closes and refreshes",
    async (dataset) => {
      const onClose = vi.fn();
      const onQueued = vi.fn();
      render(<ResetTenantDatasetDialog request={{ tenant, dataset }} onClose={onClose} onQueued={onQueued} />);

      type("Acme Transport");
      await act(async () => {
        fireEvent.click(confirmButton());
      });

      await waitFor(() =>
        expect(mutateAsync).toHaveBeenCalledWith({ id: "t-1", dataset, confirmationName: "Acme Transport" }),
      );
      expect(mutateAsync).toHaveBeenCalledOnce();
      expect(onClose).toHaveBeenCalled();
      expect(onQueued).toHaveBeenCalled();
    },
  );

  it("names the dataset and warns that the data is deleted", () => {
    render(<ResetTenantDatasetDialog request={{ tenant, dataset: "Large" }} onClose={vi.fn()} onQueued={vi.fn()} />);
    expect(screen.getByText("Tenants:Dataset:LargeDescription")).toBeInTheDocument();
    expect(screen.getByText("Tenants:ResetDialogWarning")).toBeInTheDocument();
  });

  it("clears the typed name when another tenant or dataset is chosen", () => {
    const { rerender } = render(
      <ResetTenantDatasetDialog request={{ tenant, dataset: "Average" }} onClose={vi.fn()} onQueued={vi.fn()} />,
    );
    type("Acme Transport");
    expect(confirmButton()).toBeEnabled();

    rerender(<ResetTenantDatasetDialog request={{ tenant, dataset: "Large" }} onClose={vi.fn()} onQueued={vi.fn()} />);
    expect(nameInput().value).toBe("");
    expect(confirmButton()).toBeDisabled();

    type("Acme Transport");
    rerender(
      <ResetTenantDatasetDialog
        request={{ tenant: { ...tenant, id: "t-2" }, dataset: "Large" }}
        onClose={vi.fn()}
        onQueued={vi.fn()}
      />,
    );
    expect(nameInput().value).toBe("");
    expect(confirmButton()).toBeDisabled();
  });
});
