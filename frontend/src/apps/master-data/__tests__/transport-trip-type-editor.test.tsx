import { act, fireEvent, render, screen } from "@testing-library/react";
import { beforeAll, beforeEach, describe, expect, it, vi } from "vitest";

/* The transport trip type editor (design 53) on the Platform reference-data hooks (design 50): validation, the DTO
 * sent on create and update (upper-cased code, the localized-name map, the numbering, the next number, the stamp),
 * the numbering preview, the default badge and no Delete for the default. Risk class: Medium. */

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
vi.mock("@/domains/planning-execution/reference-data", () => ({
  TRIP_EXECUTIONS: [
    { value: 1, key: "PlanningExecution:TripExecution.OwnFleet" },
    { value: 2, key: "PlanningExecution:TripExecution.Subcontracted" },
  ],
  transportTripTypes: {
    entity: "transportTripTypes",
    useDetail: () => detail,
    useCreate: () => ({ mutateAsync: createMutateAsync, isPending: false }),
    useUpdate: () => ({ mutateAsync: updateMutateAsync, isPending: false }),
    useDelete: () => ({ mutateAsync: deleteMutateAsync, isPending: false }),
  },
  useSetDefaultTransportTripType: () => ({ mutateAsync: setDefaultMutateAsync, isPending: false }),
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
  grants = { "PlanningExecution.TransportTripTypes.Delete": true };
  detail = { data: undefined, isLoading: false, error: null };
});

import { TransportTripTypeEditor } from "@/apps/master-data/editors/TransportTripTypeEditor";

const stored = {
  id: "t1",
  code: "STD",
  name: "Standard",
  names: { en: "Standard", nl: "Standaard" },
  defaultExecution: 1,
  sendToFleetManagementSystem: true,
  numbering: { prefix: "TR", includeYear: true, includeMonth: false, separator: "-", digits: 5 },
  nextNumber: 42,
  nextNumberPreview: "TR-2026-00042",
  isDefault: false,
  isArchived: false,
  description: null,
  concurrencyStamp: "stamp-1",
  creationTime: "2026-10-22T10:00:00Z",
};

const inputByLabel = (label: string) => screen.getByLabelText(new RegExp(`^${label.replace(/[.*+?^${}()|[\]\\]/g, "\\$&")}`)) as HTMLInputElement;

const submit = () =>
  act(async () => {
    fireEvent.submit(document.querySelector("form")!);
    await Promise.resolve();
  });

describe("TransportTripTypeEditor", () => {
  it("refuses to save without a code and a name, and sends nothing", async () => {
    render(<TransportTripTypeEditor mode="create" readOnly={false} open onClose={() => {}} onSaved={() => {}} />);
    await submit();
    expect(createMutateAsync).not.toHaveBeenCalled();
    expect(screen.getAllByText("MasterData:Required").length).toBeGreaterThanOrEqual(2);
  });

  it("creates with the upper-cased code, the name map and the default numbering of a new type", async () => {
    const onSaved = vi.fn();
    render(<TransportTripTypeEditor mode="create" readOnly={false} open onClose={() => {}} onSaved={onSaved} />);
    fireEvent.change(inputByLabel("MasterData:Code"), { target: { value: "cht" } });
    fireEvent.change(inputByLabel("MasterData:Name"), { target: { value: "Charter" } });
    await submit();

    expect(createMutateAsync).toHaveBeenCalledTimes(1);
    expect(createMutateAsync.mock.calls[0][0]).toEqual({
      code: "CHT",
      names: { en: "Charter" },
      defaultExecution: 1,
      sendToFleetManagementSystem: true,
      numbering: { prefix: "TR", includeYear: true, includeMonth: false, separator: "-", digits: 5 },
      nextNumber: 1,
      isArchived: false,
      description: null,
    });
    expect(onSaved).toHaveBeenCalled();
    expect(invalidateQueries).toHaveBeenCalledWith({ queryKey: ["masterData", "transportTripTypes"] });
  });

  it("updates with the stamp, keeps the other languages' names and refuses a next number below the current one", async () => {
    detail = { data: stored, isLoading: false, error: null };
    render(<TransportTripTypeEditor mode="edit" id="t1" readOnly={false} open onClose={() => {}} onSaved={() => {}} />);
    fireEvent.change(inputByLabel("MasterData:Name"), { target: { value: "Standard trip" } });
    fireEvent.click(screen.getByRole("tab", { name: "MasterData:Numbering" }));
    fireEvent.change(inputByLabel("MasterData:Numbering.NextNumber"), { target: { value: "41" } });
    await submit();
    expect(updateMutateAsync).not.toHaveBeenCalled();

    fireEvent.change(inputByLabel("MasterData:Numbering.NextNumber"), { target: { value: "500" } });
    await submit();
    expect(updateMutateAsync).toHaveBeenCalledTimes(1);
    const { id, dto } = updateMutateAsync.mock.calls[0][0];
    expect(id).toBe("t1");
    expect(dto.concurrencyStamp).toBe("stamp-1");
    expect(dto.names).toEqual({ en: "Standard trip", nl: "Standaard" });
    expect(dto.nextNumber).toBe(500);
  });

  it("shows the default badge and offers no Delete for the default type", () => {
    detail = { data: { ...stored, isDefault: true }, isLoading: false, error: null };
    render(<TransportTripTypeEditor mode="edit" id="t1" readOnly={false} open onClose={() => {}} onSaved={() => {}} />);
    expect(screen.getByText("MasterData:IsDefault")).toBeInTheDocument();
    expect(screen.queryByText("Delete")).toBeNull();
  });
});
