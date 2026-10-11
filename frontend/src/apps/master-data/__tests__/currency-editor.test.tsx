import { act, fireEvent, render, screen } from "@testing-library/react";
import { beforeAll, beforeEach, describe, expect, it, vi } from "vitest";

/* The currency editor over the shared MasterDataEditor (design 41): validation on submit with the
 * first erroneous field focused, the DTO sent on create and update (upper-cased code, parsed numbers,
 * the localized-name map, the concurrency stamp), read-only without Update, the Set as default command.
 * Risk class: Medium. */

vi.mock("@/lib/i18n/LocalizationProvider", () => ({
  useL: () => ({ t: (k: string) => k, culture: "en", isLoading: false }),
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
  currencies: {
    entity: "currencies",
    useDetail: () => detail,
    useCreate: () => ({ mutateAsync: createMutateAsync, isPending: false }),
    useUpdate: () => ({ mutateAsync: updateMutateAsync, isPending: false }),
    useDelete: () => ({ mutateAsync: deleteMutateAsync, isPending: false }),
  },
  useSetDefaultCurrency: () => ({ mutateAsync: setDefaultMutateAsync, isPending: false }),
}));

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
  setDefaultMutateAsync.mockReset();
  invalidateQueries.mockReset();
  grants = { "MasterData.Currencies.Delete": true };
  detail = { data: undefined, isLoading: false, error: null };
});

import { CurrencyEditor } from "@/apps/master-data/editors/CurrencyEditor";

const stored = {
  id: "c1",
  isoCode: "EUR",
  isoNumeric: 978,
  minorUnits: 2,
  symbol: "€",
  name: "Euro",
  names: { en: "Euro", nl: "Euro" },
  description: null,
  isActive: true,
  isDefault: false,
  concurrencyStamp: "stamp-1",
  creationTime: "2026-10-11T10:00:00Z",
};

const inputByLabel = (label: string) => screen.getByLabelText(new RegExp(`^${label.replace(/[.*+?^${}()|[\]\\]/g, "\\$&")}`)) as HTMLInputElement;

const submit = () =>
  act(async () => {
    fireEvent.submit(document.querySelector("form")!);
    await Promise.resolve();
  });

describe("CurrencyEditor", () => {
  it("refuses to save without an ISO code and a name, and sends nothing", async () => {
    render(<CurrencyEditor mode="create" readOnly={false} open onClose={() => {}} onSaved={() => {}} />);
    await submit();
    expect(createMutateAsync).not.toHaveBeenCalled();
    expect(screen.getAllByText("MasterData:Required").length).toBeGreaterThanOrEqual(1);
  });

  it("creates with the upper-cased code, parsed numbers and the localized-name map", async () => {
    const onSaved = vi.fn();
    const onClose = vi.fn();
    render(<CurrencyEditor mode="create" readOnly={false} open onClose={onClose} onSaved={onSaved} />);
    fireEvent.change(inputByLabel("MasterData:IsoCode"), { target: { value: "chf" } });
    fireEvent.change(inputByLabel("MasterData:IsoNumeric"), { target: { value: "756" } });
    fireEvent.change(inputByLabel("MasterData:MinorUnits"), { target: { value: "2" } });
    fireEvent.change(inputByLabel("MasterData:Name"), { target: { value: "Swiss franc" } });
    await submit();

    expect(createMutateAsync).toHaveBeenCalledTimes(1);
    expect(createMutateAsync.mock.calls[0][0]).toEqual({
      isoCode: "CHF",
      isoNumeric: 756,
      minorUnits: 2,
      symbol: null,
      names: { en: "Swiss franc" },
      description: null,
      isActive: true,
    });
    expect(onSaved).toHaveBeenCalled();
    expect(onClose).toHaveBeenCalled();
    expect(invalidateQueries).toHaveBeenCalledWith({ queryKey: ["masterData", "currencies"] });
  });

  it("updates with the loaded record's concurrency stamp and keeps the other languages' names", async () => {
    detail = { data: { ...stored, names: { en: "Euro", nl: "Euro (nl)" } }, isLoading: false, error: null };
    render(<CurrencyEditor mode="edit" id="c1" readOnly={false} open onClose={() => {}} onSaved={() => {}} />);
    expect(inputByLabel("MasterData:Name").value).toBe("Euro");
    fireEvent.change(inputByLabel("MasterData:Symbol"), { target: { value: "EUR" } });
    fireEvent.change(inputByLabel("MasterData:Name"), { target: { value: "Euro coin" } });
    await submit();
    expect(updateMutateAsync).toHaveBeenCalledTimes(1);
    expect(updateMutateAsync.mock.calls[0][0].id).toBe("c1");
    expect(updateMutateAsync.mock.calls[0][0].dto.concurrencyStamp).toBe("stamp-1");
    expect(updateMutateAsync.mock.calls[0][0].dto.symbol).toBe("EUR");
    expect(updateMutateAsync.mock.calls[0][0].dto.names).toEqual({ en: "Euro coin", nl: "Euro (nl)" });
  });

  it("is read-only without the Update permission: no Save, disabled inputs (UI-PERM-004)", () => {
    detail = { data: stored, isLoading: false, error: null };
    render(<CurrencyEditor mode="edit" id="c1" readOnly open onClose={() => {}} onSaved={() => {}} />);
    expect(screen.queryByText("Save")).toBeNull();
    expect(inputByLabel("MasterData:IsoCode").disabled).toBe(true);
  });

  it("offers Set as default for a non-default active currency and Delete with the permission", () => {
    detail = { data: stored, isLoading: false, error: null };
    render(<CurrencyEditor mode="edit" id="c1" readOnly={false} open onClose={() => {}} onSaved={() => {}} />);
    expect(screen.getByText("MasterData:SetAsDefault")).toBeInTheDocument();
    expect(screen.getAllByText("Delete").length).toBeGreaterThanOrEqual(1);
  });

  it("hides Set as default when the currency already is the default and shows the badge", () => {
    detail = { data: { ...stored, isDefault: true }, isLoading: false, error: null };
    render(<CurrencyEditor mode="edit" id="c1" readOnly={false} open onClose={() => {}} onSaved={() => {}} />);
    expect(screen.queryByText("MasterData:SetAsDefault")).toBeNull();
    expect(screen.getByText("MasterData:IsDefault")).toBeInTheDocument();
  });
});
