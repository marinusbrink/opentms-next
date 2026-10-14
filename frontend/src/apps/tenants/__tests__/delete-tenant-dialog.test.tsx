import { act, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";

/* Deleting a tenant drops its database (PO decision D5, docs/designs/tenants-and-editions.md): Delete stays
 * disabled until the tenant's exact name is typed. Risk class: Critical (irreversible, Platform) — component level. */

vi.mock("@/lib/i18n/LocalizationProvider", () => ({
  useL: () => ({ t: (key: string) => key, culture: "en", isLoading: false }),
}));
vi.mock("sonner", () => ({ toast: { success: vi.fn(), error: vi.fn() } }));

const deleteMutateAsync = vi.fn();
vi.mock("@/domains/platform/tenants", () => ({
  useDeleteTenant: () => ({ mutateAsync: deleteMutateAsync, isPending: false }),
}));

import { DeleteTenantDialog } from "@/apps/tenants/DeleteTenantDialog";

const tenant = {
  id: "t-1",
  name: "Acme Transport",
  databaseName: "t_acme_transport_0a1b2c3d",
  serverName: "localhost:5432",
  provisioningState: "Succeeded",
  creationTime: "2026-10-14T10:00:00Z",
};

const confirmButton = () => screen.getByRole("button", { name: "Delete" });
const nameInput = () => document.getElementById("dt-confirm-name") as HTMLInputElement;
const type = (value: string) => fireEvent.change(nameInput(), { target: { value } });

beforeEach(() => {
  deleteMutateAsync.mockReset();
  deleteMutateAsync.mockResolvedValue(undefined);
});

describe("DeleteTenantDialog", () => {
  it("keeps Delete disabled until the exact name is typed", () => {
    render(<DeleteTenantDialog tenant={tenant} onClose={vi.fn()} onDeleted={vi.fn()} />);

    expect(confirmButton()).toBeDisabled();
    for (const attempt of ["Acme", "acme transport", "Acme Transport ", " Acme Transport", "Acme  Transport"]) {
      type(attempt);
      expect(confirmButton()).toBeDisabled();
    }

    type("Acme Transport");
    expect(confirmButton()).toBeEnabled();

    type("Acme Transpor");
    expect(confirmButton()).toBeDisabled();
  });

  it("a click on the disabled button deletes nothing", () => {
    render(<DeleteTenantDialog tenant={tenant} onClose={vi.fn()} onDeleted={vi.fn()} />);

    type("wrong");
    fireEvent.click(confirmButton());
    expect(deleteMutateAsync).not.toHaveBeenCalled();
  });

  it("deletes this tenant once confirmed and closes", async () => {
    const onClose = vi.fn();
    const onDeleted = vi.fn();
    render(<DeleteTenantDialog tenant={tenant} onClose={onClose} onDeleted={onDeleted} />);

    type("Acme Transport");
    await act(async () => {
      fireEvent.click(confirmButton());
    });

    await waitFor(() => expect(deleteMutateAsync).toHaveBeenCalledWith("t-1"));
    expect(deleteMutateAsync).toHaveBeenCalledOnce();
    expect(onClose).toHaveBeenCalled();
    expect(onDeleted).toHaveBeenCalled();
  });

  it("warns that the database is dropped when the tenant has one of its own", () => {
    render(<DeleteTenantDialog tenant={tenant} onClose={vi.fn()} onDeleted={vi.fn()} />);

    expect(screen.getByText("Tenants:DeleteDropsDatabase")).toBeInTheDocument();
  });

  it("does not warn about a database for a shared-tier tenant", () => {
    render(<DeleteTenantDialog tenant={{ ...tenant, databaseName: null }} onClose={vi.fn()} onDeleted={vi.fn()} />);

    expect(screen.queryByText(/Tenants:DeleteDropsDatabase/)).not.toBeInTheDocument();
  });

  it("clears the typed name when another tenant is opened", () => {
    const { rerender } = render(<DeleteTenantDialog tenant={tenant} onClose={vi.fn()} onDeleted={vi.fn()} />);
    type("Acme Transport");
    expect(confirmButton()).toBeEnabled();

    rerender(
      <DeleteTenantDialog tenant={{ ...tenant, id: "t-2", name: "Acme Transport" }} onClose={vi.fn()} onDeleted={vi.fn()} />,
    );

    expect(nameInput().value).toBe("");
    expect(confirmButton()).toBeDisabled();
  });
});
