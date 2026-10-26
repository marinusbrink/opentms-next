import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { act, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { beforeAll, beforeEach, describe, expect, it, vi } from "vitest";

/* "Add new edition…" in the tenant editor's edition picker (UI-LOOK-004, PO decision 2026-10-16): the row exists only
 * when the view grants it (Platform.Editions.Create → canCreateEdition) and the editor is not read-only; it opens the
 * real edition editor on top in create mode with the typed text as its name; after that saves, the new edition is the
 * tenant's edition and is sent on Save. The real ReferenceCombobox (Base UI) and EditionFormDialog run; the tenants
 * and editions domain hooks are mocked (the edition lookup is a real query over a mocked server list).
 * Risk class: Medium (UI flow) on a Critical create (tenant provisioning). */

vi.mock("@/lib/i18n/LocalizationProvider", () => ({
  useL: () => ({ t: (key: string) => key, culture: "en", isLoading: false }),
}));
vi.mock("@/lib/abp/queries", () => ({
  useApplicationConfiguration: () => ({ data: { features: { values: {} }, localization: { currentCulture: { cultureName: "en" } } } }),
}));
vi.mock("sonner", () => ({ toast: { success: vi.fn(), error: vi.fn() } }));

const server = vi.hoisted(() => ({ editions: [] as { id: string; displayName: string }[] }));
const createTenant = vi.fn();
const createEdition = vi.fn();

vi.mock("@/domains/platform/tenants", async () => {
  const { useQuery } = await import("@tanstack/react-query");
  return {
    TenantProvisioningStates: { Queued: "Queued", Running: "Running", Succeeded: "Succeeded", Failed: "Failed" },
    useCreateTenant: () => ({ mutateAsync: createTenant, isPending: false }),
    useUpdateTenant: () => ({ mutateAsync: vi.fn(), isPending: false }),
    useEditionLookup: () => useQuery({ queryKey: ["platform", "tenants", "edition-lookup"], queryFn: async () => [...server.editions] }),
    useTenant: () => ({ data: undefined }),
    useTenantConnectionPreview: () => ({ data: undefined }),
  };
});

vi.mock("@/domains/platform/editions", () => {
  const idle = { isLoading: false, isError: false, data: undefined };
  return {
    useCreateEdition: () => ({ mutateAsync: createEdition, isPending: false }),
    useUpdateEdition: () => ({ mutateAsync: vi.fn(), isPending: false }),
    useUpdateEditionFeatures: () => ({ mutateAsync: vi.fn(), isPending: false }),
    useEditionFeatures: () => idle,
  };
});

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

import { TenantFormDialog } from "@/apps/tenants/TenantFormDialog";

beforeEach(() => {
  server.editions = [{ id: "e-gold", displayName: "Gold" }];
  createTenant.mockReset().mockResolvedValue({ id: "t-new" });
  createEdition.mockReset().mockImplementation(async ({ displayName }: { displayName: string }) => {
    const created = { id: "e-new", displayName };
    server.editions.push(created);
    return created;
  });
});

function renderCreate(props: { canCreateEdition?: boolean; readOnly?: boolean } = {}) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  render(
    <QueryClientProvider client={client}>
      <TenantFormDialog mode="create" open onClose={vi.fn()} onSuccess={vi.fn()} {...props} />
    </QueryClientProvider>,
  );
  return client;
}

const picker = () => document.getElementById("tf-edition") as HTMLInputElement;
const openPicker = () => {
  fireEvent.pointerDown(picker());
  fireEvent.mouseDown(picker());
  fireEvent.click(picker());
};
const typeIn = (element: HTMLElement, value: string) => fireEvent.input(element, { target: { value }, inputType: "insertText" });
const createRow = () => screen.queryByRole("option", { name: "Editions:AddNewEdition" });
const editionDialog = () => document.getElementById("ef-name")?.closest<HTMLElement>("[role=dialog]") ?? null;

describe("TenantFormDialog — Add new edition", () => {
  it("has no row without canCreateEdition", async () => {
    renderCreate();
    openPicker();
    expect(await screen.findByRole("option", { name: "Gold" })).toBeInTheDocument();
    expect(createRow()).toBeNull();
  });

  it("has no row in a read-only editor even with canCreateEdition", () => {
    renderCreate({ canCreateEdition: true, readOnly: true });
    openPicker();
    expect(createRow()).toBeNull();
  });

  it("opens the edition editor with the typed name; after its save the new edition is selected and sent", async () => {
    renderCreate({ canCreateEdition: true });
    fireEvent.change(document.getElementById("tf-name")!, { target: { value: "Acme" } });
    fireEvent.change(document.getElementById("tf-adminEmailAddress")!, { target: { value: "admin@acme.test" } });

    openPicker();
    typeIn(picker(), "Platinum");
    fireEvent.click(await screen.findByRole("option", { name: "Editions:AddNewEdition" }));

    await waitFor(() => expect(editionDialog()).not.toBeNull());
    expect(document.getElementById("ef-name")).toHaveValue("Platinum");

    await act(async () => {
      fireEvent.submit(editionDialog()!.querySelector("form")!);
    });
    expect(createEdition).toHaveBeenCalledWith({ displayName: "Platinum" });
    await waitFor(() => expect(editionDialog()).toBeNull());
    await waitFor(() => expect(picker()).toHaveValue("Platinum"));

    await act(async () => {
      fireEvent.submit(document.body.querySelector("form")!);
    });
    expect(createTenant).toHaveBeenCalledWith({ name: "Acme", adminEmailAddress: "admin@acme.test", editionId: "e-new" });
  });

  it("puts the new edition into the picker's list at once", async () => {
    const client = renderCreate({ canCreateEdition: true });
    await waitFor(() => expect(client.getQueryData(["platform", "tenants", "edition-lookup"])).toBeDefined());
    const setQueryData = vi.spyOn(client, "setQueryData");
    const invalidate = vi.spyOn(client, "invalidateQueries");

    openPicker();
    typeIn(picker(), "Silver");
    fireEvent.click(await screen.findByRole("option", { name: "Editions:AddNewEdition" }));
    await waitFor(() => expect(editionDialog()).not.toBeNull());
    await act(async () => {
      fireEvent.submit(editionDialog()!.querySelector("form")!);
    });
    await waitFor(() => expect(editionDialog()).toBeNull());
    /* Added to the cached list before the refetch (which brings the server's list) replaces it. */
    const call = setQueryData.mock.calls.find(([key]) => JSON.stringify(key) === JSON.stringify(["platform", "tenants", "edition-lookup"]));
    expect(call).toBeDefined();
    const updater = call![1] as (rows: unknown[] | undefined) => unknown[] | undefined;
    expect(updater([{ id: "e-gold", displayName: "Gold" }])).toEqual([{ id: "e-gold", displayName: "Gold" }, { id: "e-new", displayName: "Silver" }]);
    expect(updater([{ id: "e-new", displayName: "Silver" }])).toEqual([{ id: "e-new", displayName: "Silver" }]);
    expect(invalidate).toHaveBeenCalledWith({ queryKey: ["platform", "tenants", "edition-lookup"] });
  });

  it("a cancelled edition editor changes nothing", async () => {
    renderCreate({ canCreateEdition: true });
    openPicker();
    fireEvent.click(await screen.findByRole("option", { name: "Editions:AddNewEdition" }));
    await waitFor(() => expect(editionDialog()).not.toBeNull());
    /* Close without a change: the dialog's own Close command. */
    const dialog = editionDialog()!;
    fireEvent.click(within(dialog).getAllByRole("button", { name: /Close|Cancel/i, hidden: true })[0]);
    await waitFor(() => expect(editionDialog()).toBeNull());
    expect(createEdition).not.toHaveBeenCalled();
    expect(picker()).toHaveValue("");
  });
});
