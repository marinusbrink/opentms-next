import { act, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { beforeAll, beforeEach, describe, expect, it, vi } from "vitest";

/* The country editor over the shared MasterDataEditor: the one default country of the tenant
 * (PO 2026-10-13) as in the currency editor: an edit-mode "Set as default" ribbon command, hidden
 * while the record already is the default or the editor is read-only, confirmed in a ConfirmDialog
 * that names the alpha-2 code, the POST to set-default, the toast, onSaved and onClose; a failure
 * stays as a toast with the editor open; the IsDefault badge after the title.
 * Risk class: Medium (UI flow) — component tests per the matrix. */

vi.mock("@/lib/i18n/LocalizationProvider", () => ({
  useL: () => ({ t: (k: string) => (k.endsWith("Question") ? `${k} {0}` : k), culture: "en", isLoading: false }),
}));

let grants: Record<string, boolean> = {};
vi.mock("@/lib/abp/queries", () => ({
  useApplicationConfiguration: () => ({ data: { auth: { grantedPolicies: grants } } }),
}));

const invalidateQueries = vi.fn();
vi.mock("@tanstack/react-query", () => ({
  useQueryClient: () => ({ invalidateQueries }),
}));

const createMutateAsync = vi.fn();
const updateMutateAsync = vi.fn();
const deleteMutateAsync = vi.fn();
const setDefaultMutateAsync = vi.fn();
let detail: { data: unknown; isLoading: boolean; error: unknown } = { data: undefined, isLoading: false, error: null };
vi.mock("@/domains/master-data/entities", () => ({
  countries: {
    entity: "countries",
    useDetail: () => detail,
    useCreate: () => ({ mutateAsync: createMutateAsync, isPending: false }),
    useUpdate: () => ({ mutateAsync: updateMutateAsync, isPending: false }),
    useDelete: () => ({ mutateAsync: deleteMutateAsync, isPending: false }),
  },
  currencies: {
    useLookup: () => ({ data: [{ id: "cur-1", code: "EUR", name: "Euro" }], isLoading: false, error: null }),
  },
  useSetDefaultCountry: () => ({ mutateAsync: setDefaultMutateAsync, isPending: false }),
}));

vi.mock("@/components/ui/reference-combobox", async () => {
  const mocks = await import("./run2-test-mocks");
  return { ReferenceCombobox: mocks.MockReferenceCombobox };
});

vi.mock("sonner", () => ({ toast: { success: vi.fn(), error: vi.fn(), warning: vi.fn() } }));

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
  vi.stubGlobal("requestAnimationFrame", (cb: FrameRequestCallback) => {
    cb(0);
    return 0;
  });
});

beforeEach(() => {
  createMutateAsync.mockReset().mockResolvedValue({});
  updateMutateAsync.mockReset().mockResolvedValue({});
  deleteMutateAsync.mockReset();
  setDefaultMutateAsync.mockReset().mockResolvedValue(undefined);
  invalidateQueries.mockReset();
  vi.mocked(toast.success).mockReset();
  vi.mocked(toast.error).mockReset();
  grants = { "MasterData.Countries.Delete": true };
  detail = { data: undefined, isLoading: false, error: null };
});

import { toast } from "sonner";

import { CountryEditor } from "@/apps/master-data/editors/CountryEditor";

const stored = {
  id: "c1",
  isoAlpha2: "NL",
  isoAlpha3: "NLD",
  isoNumeric: 528,
  name: "Netherlands",
  names: { en: "Netherlands", nl: "Nederland" },
  defaultCurrencyId: "cur-1",
  defaultCurrencyCode: "EUR",
  primaryTimeZoneId: "Europe/Amsterdam",
  description: null,
  isDefault: false,
  concurrencyStamp: "stamp-1",
  creationTime: "2026-10-11T10:00:00Z",
};

const escape = (text: string) => text.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
const inputByLabel = (label: string) => screen.getByLabelText(new RegExp(`^${escape(label)}`), { selector: "input, select, textarea" }) as HTMLInputElement;
const editorDialog = () => document.querySelector<HTMLElement>("[role=dialog]")!;
const header = () => editorDialog().querySelector<HTMLElement>("[data-slot=entity-dialog-header]")!;
const ribbon = (name: string) => within(editorDialog()).getByRole("button", { name });
const queryRibbon = (name: string) => within(editorDialog()).queryByRole("button", { name });
const confirm = () => screen.getByRole("alertdialog");

const submit = () =>
  act(async () => {
    fireEvent.submit(document.querySelector("form")!);
    await Promise.resolve();
  });

describe("CountryEditor — Set as default (PO 2026-10-13)", () => {
  it("a new country offers no Set as default and no badge", () => {
    render(<CountryEditor mode="create" readOnly={false} open onClose={() => {}} onSaved={() => {}} />);
    expect(queryRibbon("MasterData:SetAsDefault")).toBeNull();
    expect(screen.queryByText("MasterData:IsDefault")).toBeNull();
    expect(screen.queryByRole("alertdialog")).toBeNull();
  });

  it("offers Set as default for a loaded non-default country, next to Delete with the permission, without a badge", () => {
    detail = { data: stored, isLoading: false, error: null };
    render(<CountryEditor mode="edit" id="c1" readOnly={false} open onClose={() => {}} onSaved={() => {}} />);
    expect(ribbon("MasterData:SetAsDefault")).not.toHaveAttribute("aria-disabled");
    expect(screen.getAllByText("Delete").length).toBeGreaterThanOrEqual(1);
    expect(within(header()).queryByText("MasterData:IsDefault")).toBeNull();
    expect(setDefaultMutateAsync).not.toHaveBeenCalled();
  });

  it("the command waits for the record: disabled while the detail is still loading", () => {
    detail = { data: undefined, isLoading: true, error: null };
    render(<CountryEditor mode="edit" id="c1" readOnly={false} open onClose={() => {}} onSaved={() => {}} />);
    /* Ribbon commands disable through aria-disabled (UI-DLG-010); a click then does nothing. */
    expect(ribbon("MasterData:SetAsDefault")).toHaveAttribute("aria-disabled", "true");
    fireEvent.click(ribbon("MasterData:SetAsDefault"));
    expect(screen.queryByRole("alertdialog")).toBeNull();
    expect(setDefaultMutateAsync).not.toHaveBeenCalled();
  });

  it("the command opens a confirmation naming the alpha-2 code; nothing is posted until confirmed; Cancel posts nothing", async () => {
    detail = { data: stored, isLoading: false, error: null };
    render(<CountryEditor mode="edit" id="c1" readOnly={false} open onClose={() => {}} onSaved={() => {}} />);
    fireEvent.click(ribbon("MasterData:SetAsDefault"));
    const dialog = confirm();
    expect(within(dialog).getByText("MasterData:SetAsDefault", { selector: "h2, [data-slot=alert-dialog-title]" })).toBeInTheDocument();
    expect(within(dialog).getByText("MasterData:SetAsDefaultCountryQuestion NL")).toBeInTheDocument();
    expect(setDefaultMutateAsync).not.toHaveBeenCalled();

    fireEvent.click(within(dialog).getByRole("button", { name: "Cancel" }));
    await waitFor(() => expect(screen.queryByRole("alertdialog")).toBeNull());
    expect(setDefaultMutateAsync).not.toHaveBeenCalled();
    expect(toast.success).not.toHaveBeenCalled();
  });

  it("confirming posts set-default for the record, toasts DefaultCountrySet, then onSaved and onClose", async () => {
    detail = { data: stored, isLoading: false, error: null };
    const onSaved = vi.fn();
    const onClose = vi.fn();
    render(<CountryEditor mode="edit" id="c1" readOnly={false} open onClose={onClose} onSaved={onSaved} />);
    fireEvent.click(ribbon("MasterData:SetAsDefault"));
    fireEvent.click(within(confirm()).getByRole("button", { name: "MasterData:SetAsDefault" }));

    await waitFor(() => expect(setDefaultMutateAsync).toHaveBeenCalledTimes(1));
    expect(setDefaultMutateAsync).toHaveBeenCalledWith("c1");
    await waitFor(() => expect(onClose).toHaveBeenCalled());
    expect(onSaved).toHaveBeenCalled();
    expect(toast.success).toHaveBeenCalledWith("MasterData:DefaultCountrySet");
    expect(toast.error).not.toHaveBeenCalled();
    await waitFor(() => expect(screen.queryByRole("alertdialog")).toBeNull());
    /* Set as default is its own command: the record itself is not saved by it. */
    expect(updateMutateAsync).not.toHaveBeenCalled();
    expect(createMutateAsync).not.toHaveBeenCalled();
  });

  it("a failing set-default shows the server message as an error toast, closes the confirmation and keeps the editor open", async () => {
    detail = { data: stored, isLoading: false, error: null };
    setDefaultMutateAsync.mockRejectedValue({ error: { message: "Country NL is inactive" } });
    const onSaved = vi.fn();
    const onClose = vi.fn();
    render(<CountryEditor mode="edit" id="c1" readOnly={false} open onClose={onClose} onSaved={onSaved} />);
    fireEvent.click(ribbon("MasterData:SetAsDefault"));
    fireEvent.click(within(confirm()).getByRole("button", { name: "MasterData:SetAsDefault" }));

    await waitFor(() => expect(toast.error).toHaveBeenCalledWith("Country NL is inactive"));
    await waitFor(() => expect(screen.queryByRole("alertdialog")).toBeNull());
    expect(toast.success).not.toHaveBeenCalled();
    expect(onSaved).not.toHaveBeenCalled();
    expect(onClose).not.toHaveBeenCalled();
    expect(editorDialog()).toBeInTheDocument();
    expect(ribbon("MasterData:SetAsDefault")).toBeInTheDocument();
  });

  it("hides Set as default when the country already is the default and shows the IsDefault badge after the title", () => {
    detail = { data: { ...stored, isDefault: true }, isLoading: false, error: null };
    render(<CountryEditor mode="edit" id="c1" readOnly={false} open onClose={() => {}} onSaved={() => {}} />);
    expect(queryRibbon("MasterData:SetAsDefault")).toBeNull();
    expect(within(header()).getByText("MasterData:IsDefault")).toBeInTheDocument();
    expect(header()).toHaveTextContent("MasterData:Country NL");
  });

  it("read-only (no Update permission) hides Set as default, keeps the badge and disables the inputs (UI-PERM-004)", () => {
    detail = { data: { ...stored, isDefault: true }, isLoading: false, error: null };
    const first = render(<CountryEditor mode="edit" id="c1" readOnly open onClose={() => {}} onSaved={() => {}} />);
    expect(queryRibbon("MasterData:SetAsDefault")).toBeNull();
    expect(screen.queryByText("Save")).toBeNull();
    expect(within(header()).getByText("MasterData:IsDefault")).toBeInTheDocument();
    expect(inputByLabel("MasterData:IsoAlpha2").disabled).toBe(true);
    first.unmount();

    detail = { data: stored, isLoading: false, error: null };
    render(<CountryEditor mode="edit" id="c1" readOnly open onClose={() => {}} onSaved={() => {}} />);
    expect(queryRibbon("MasterData:SetAsDefault")).toBeNull();
    expect(screen.queryByText("MasterData:IsDefault")).toBeNull();
  });

  it("a plain update of the default country keeps the DTO untouched by the default flag: the stamp, the codes and the time zone travel, no set-default call", async () => {
    detail = { data: { ...stored, isDefault: true }, isLoading: false, error: null };
    render(<CountryEditor mode="edit" id="c1" readOnly={false} open onClose={() => {}} onSaved={() => {}} />);
    expect(inputByLabel("MasterData:IsoAlpha2").value).toBe("NL");
    expect(inputByLabel("MasterData:PrimaryTimeZone").value).toBe("Europe/Amsterdam");
    fireEvent.change(inputByLabel("MasterData:Name"), { target: { value: "The Netherlands" } });
    await submit();
    expect(updateMutateAsync).toHaveBeenCalledTimes(1);
    expect(updateMutateAsync.mock.calls[0][0]).toEqual({
      id: "c1",
      dto: {
        isoAlpha2: "NL",
        isoAlpha3: "NLD",
        isoNumeric: 528,
        names: { en: "The Netherlands", nl: "Nederland" },
        defaultCurrencyId: "cur-1",
        primaryTimeZoneId: "Europe/Amsterdam",
        description: null,
        concurrencyStamp: "stamp-1",
      },
    });
    expect(setDefaultMutateAsync).not.toHaveBeenCalled();
  });
});
