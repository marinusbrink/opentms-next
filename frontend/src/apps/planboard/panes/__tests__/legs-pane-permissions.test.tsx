import { render } from "@testing-library/react";
import { type ReactNode } from "react";
import { describe, expect, it, vi, beforeEach } from "vitest";
import type { ColDef } from "ag-grid-community";
import type { LegDto } from "@/domains/planning-execution/legs";

/* Design 75 risk analysis — "Drag handle permission" (Low risk): the drag handle column appears in the Legs pane's
 * grid only when the user holds PlanningExecution.TransportTrips.AssignLegs; without it the column is absent. */

// Capture what columnDefs the grid receives.
let lastColumnDefs: ColDef<LegDto>[] = [];
vi.mock("@/components/ui/opentms-grid-lazy", () => ({
  LazyOpenTmsGrid: (props: { columnDefs: ColDef<LegDto>[] }) => {
    lastColumnDefs = props.columnDefs ?? [];
    return null;
  },
}));

vi.mock("@/components/ui/skeleton", () => ({ Skeleton: () => null }));
vi.mock("@/components/ui/permission-denied", () => ({ PermissionDenied: () => null }));
vi.mock("@/components/ui/grid-cells", () => ({ AmountCell: () => null, BoolCell: () => null, NumberCell: () => null }));

vi.mock("@/lib/i18n/LocalizationProvider", () => ({ useL: () => ({ t: (k: string) => k, culture: "en", isLoading: false }) }));
vi.mock("@/lib/format", () => ({ useFormat: () => ({ number: (v: unknown) => String(v) }) }));
vi.mock("@/lib/use-open-record", () => ({ useOpenRecord: () => vi.fn() }));
vi.mock("@/lib/api/errors", () => ({ getApiErrorMessage: (_e: unknown, _t: unknown) => "err" }));
vi.mock("sonner", () => ({ toast: { success: vi.fn(), error: vi.fn() } }));
vi.mock("@/lib/api/client", () => ({ apiClient: { POST: vi.fn(), PUT: vi.fn() } }));

let grantedAssignLegs = true;
vi.mock("@/lib/auth/permissions", () => ({
  useIsGranted: (permission: string) => {
    if (permission === "PlanningExecution.Legs") return true;
    if (permission === "PlanningExecution.Legs.Update") return true;
    if (permission === "PlanningExecution.TransportTrips.AssignLegs") return grantedAssignLegs;
    if (permission === "PlanningExecution.TransportTrips.ManageStops") return false;
    return false;
  },
}));

vi.mock("@/apps/planboard/selection", () => ({
  usePlanboardSelection: () => ({
    focusedTripId: "trip-1",
    selectedLegIds: [],
    setSelectedLegIds: vi.fn(),
  }),
}));

vi.mock("@/apps/planboard/live-updates", () => ({
  PLANBOARD_RECORD_TYPES: { trip: "TransportTrip", leg: "Leg" },
  usePlanboardLiveUpdates: vi.fn(),
}));

vi.mock("@/apps/planboard/optimistic-state", () => ({
  usePlanboardOptimisticState: () => ({
    state: { assignedLegs: new Map(), unassignedLegs: new Map(), pendingValues: new Set(), pendingLegData: new Map() },
    enqueueUnassign: vi.fn(),
    isInFlight: () => false,
    processingTripIds: new Set(),
    clearPendingValue: vi.fn(),
  }),
}));

const mockLeg: LegDto = { id: "leg-1", orderNumber: "O1" } as LegDto;
vi.mock("@/domains/planning-execution/legs", () => ({
  legEndLabel: () => "",
  legKeys: { ofTrip: (id: string) => ["legs", id], all: ["legs"] },
  useLegsOfTrip: () => ({ data: [mockLeg], isLoading: false, isError: false }),
}));

vi.mock("@/domains/planning-execution/trips", () => ({
  transportTripKeys: { detail: (id: string) => ["trips", id], list: () => ["trips"] },
  useCarryOutSeparately: () => ({ mutateAsync: vi.fn() }),
}));

vi.mock("@/apps/planboard/panes/lists/grid-helpers", () => ({
  DRAG_COLUMN: { colId: "drag-handle", maxWidth: 32 },
  lower: (s: string) => s.toLowerCase(),
  useGridApiCapture: () => ({ current: () => null, capture: vi.fn() }),
}));

vi.mock("@/apps/planboard/panes/lists/grid-support", () => ({ DragHandleCell: () => null }));
vi.mock("@/apps/planboard/panes/lists/PaneMessage", () => ({ PaneMessage: ({ children }: { children: ReactNode }) => <div>{children}</div> }));
vi.mock("@/apps/planboard/panes/lists/stop-leg-dialogs", () => ({ LegReferencesDialog: () => null }));

vi.mock("@/apps/planboard/panes/lists/leg-columns", () => ({
  useLegInlineColumns: () => ({
    customerReference: () => ({ colId: "customerRef" }),
    transportUnit: () => ({ colId: "transportUnit" }),
    product: () => ({ colId: "product" }),
    temperature: (opts?: { hide?: boolean }) => ({ colId: "temperature", ...opts }),
    originDate: () => ({ colId: "originDate" }),
    destinationDate: () => ({ colId: "destinationDate" }),
    originWindow: () => ({ colId: "originWindow" }),
    destinationWindow: () => ({ colId: "destinationWindow" }),
    originReference: () => ({ colId: "originReference" }),
    destinationReference: () => ({ colId: "destinationReference" }),
    remarks: () => ({ colId: "remarks" }),
  }),
}));

vi.mock("@tanstack/react-query", async (importOriginal) => {
  const actual = await importOriginal<typeof import("@tanstack/react-query")>();
  return {
    ...actual,
    useQueryClient: () => ({ invalidateQueries: vi.fn(), setQueryData: vi.fn(), getQueryData: vi.fn(() => undefined) }),
  };
});

import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import LegsPane from "@/apps/planboard/panes/LegsPane";

function Wrapper({ children }: { children: ReactNode }) {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return <QueryClientProvider client={qc}>{children}</QueryClientProvider>;
}

function renderPane() {
  lastColumnDefs = [];
  render(
    <Wrapper>
      <LegsPane instanceId="test" type="list" />
    </Wrapper>,
  );
}

beforeEach(() => {
  lastColumnDefs = [];
});

describe("LegsPane — drag handle column (design 75 §D)", () => {
  it("includes drag handle column when PlanningExecution.TransportTrips.AssignLegs is granted", () => {
    grantedAssignLegs = true;
    renderPane();
    const dragCol = lastColumnDefs.find((c) => c.colId === "drag-handle");
    expect(dragCol).toBeDefined();
  });

  it("excludes drag handle column when PlanningExecution.TransportTrips.AssignLegs is not granted", () => {
    grantedAssignLegs = false;
    renderPane();
    const dragCol = lastColumnDefs.find((c) => c.colId === "drag-handle");
    expect(dragCol).toBeUndefined();
  });
});
