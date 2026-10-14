import { act, fireEvent, render, screen } from "@testing-library/react";
import { beforeAll, beforeEach, describe, expect, it, vi } from "vitest";

/* The tenant editor in create mode (docs/designs/tenants-and-editions.md): name and admin email are required,
 * the email must look like one, and Save sends exactly { name, adminEmailAddress, editionId } — no password,
 * no connection string (D1–D3). Risk class: Medium (UI flow) on a Critical create. */

vi.mock("@/lib/i18n/LocalizationProvider", () => ({
  useL: () => ({ t: (key: string) => key, culture: "en", isLoading: false }),
}));
vi.mock("sonner", () => ({ toast: { success: vi.fn(), error: vi.fn() } }));

const createMutateAsync = vi.fn();
const updateMutateAsync = vi.fn();
vi.mock("@/domains/platform/tenants", () => ({
  TenantProvisioningStates: { Queued: "Queued", Running: "Running", Succeeded: "Succeeded", Failed: "Failed" },
  useCreateTenant: () => ({ mutateAsync: createMutateAsync, isPending: false }),
  useUpdateTenant: () => ({ mutateAsync: updateMutateAsync, isPending: false }),
  useEditionLookup: () => ({
    data: [
      { id: "e-gold", displayName: "Gold" },
      { id: "e-basic", displayName: "Basic" },
    ],
  }),
  useTenant: () => ({ data: undefined }),
  useTenantConnectionPreview: (name: string) => ({
    data: name
      ? { connectionString: `Host=localhost;Database=t_${name}_xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx;Password=********`, databaseName: "", serverName: "" }
      : undefined,
  }),
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

import { TenantFormDialog } from "@/apps/tenants/TenantFormDialog";

const field = (id: string) => document.getElementById(id) as HTMLInputElement;
const type = (id: string, value: string) => fireEvent.change(field(id), { target: { value } });
const submitForm = () =>
  act(async () => {
    fireEvent.submit(document.body.querySelector("form")!);
  });

beforeEach(() => {
  createMutateAsync.mockReset();
  createMutateAsync.mockResolvedValue({ id: "new" });
  updateMutateAsync.mockReset();
});

function renderCreate() {
  const onSuccess = vi.fn();
  const onClose = vi.fn();
  render(<TenantFormDialog mode="create" open onClose={onClose} onSuccess={onSuccess} />);
  return { onSuccess, onClose };
}

describe("TenantFormDialog (create)", () => {
  it("requires the name and the admin email and sends nothing", async () => {
    renderCreate();

    await submitForm();

    expect(screen.getAllByText("FieldRequired")).toHaveLength(2);
    expect(createMutateAsync).not.toHaveBeenCalled();
  });

  it("treats a name of only spaces as missing", async () => {
    renderCreate();
    type("tf-name", "   ");
    type("tf-adminEmailAddress", "admin@acme.test");

    await submitForm();

    expect(screen.getByText("FieldRequired")).toBeInTheDocument();
    expect(createMutateAsync).not.toHaveBeenCalled();
  });

  it.each(["not-an-email", "admin@", "@acme.test", "ad min@acme.test"])("refuses the invalid email %s", async (email) => {
    renderCreate();
    type("tf-name", "Acme");
    type("tf-adminEmailAddress", email);

    await submitForm();

    expect(screen.getByText("Tenants:InvalidEmail")).toBeInTheDocument();
    expect(createMutateAsync).not.toHaveBeenCalled();
  });

  it("sends exactly name, admin email and a null edition, trimmed", async () => {
    const { onSuccess, onClose } = renderCreate();
    type("tf-name", "  Acme Transport ");
    type("tf-adminEmailAddress", " admin@acme.test ");

    await submitForm();

    expect(createMutateAsync).toHaveBeenCalledOnce();
    expect(createMutateAsync).toHaveBeenCalledWith({
      name: "Acme Transport",
      adminEmailAddress: "admin@acme.test",
      editionId: null,
    });
    expect(Object.keys(createMutateAsync.mock.calls[0][0]).sort()).toEqual(["adminEmailAddress", "editionId", "name"]);
    expect(onSuccess).toHaveBeenCalled();
    expect(onClose).toHaveBeenCalled();
  });

  it("sends the chosen edition's id", async () => {
    renderCreate();
    type("tf-name", "Acme");
    type("tf-adminEmailAddress", "admin@acme.test");

    const picker = field("tf-edition");
    fireEvent.pointerDown(picker);
    fireEvent.mouseDown(picker);
    fireEvent.click(picker);
    fireEvent.input(picker, { target: { value: "Gol" }, inputType: "insertText" });
    fireEvent.click(await screen.findByRole("option", { name: "Gold" }));

    await submitForm();

    expect(createMutateAsync).toHaveBeenCalledWith({ name: "Acme", adminEmailAddress: "admin@acme.test", editionId: "e-gold" });
  });

  it("shows the generated connection string read-only and has no password field", () => {
    renderCreate();

    const connection = field("tf-connection");
    expect(connection).toHaveAttribute("readonly");
    expect(document.querySelector('input[type="password"]')).toBeNull();
  });
});
