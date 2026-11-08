import { act, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { beforeAll, beforeEach, describe, expect, it, vi } from "vitest";

/* The stop detail window (design 74 §3): a save sends only what changed, and when `record-stop-actuals` fails after the stop's
 * update the window stays open and a retry sends only what is left. Read-only without `canEdit`; the place fields only on a
 * stop without legs. Risk class: High (planning data). The server's refusal of a place on a stop with legs is in the backend's
 * PlanboardReviewFieldsAndStopsTests. */

vi.mock("@/lib/i18n/LocalizationProvider", () => ({ useL: () => ({ t: (k: string) => k, culture: "en", isLoading: false }) }));
vi.mock("@/lib/abp/queries", () => ({ useApplicationConfiguration: () => ({ data: { currentTenant: { id: "t-1" }, auth: { grantedPolicies: {} }, features: { values: {} } } }) }));
vi.mock("@/lib/api/client", () => ({ apiClient: { GET: vi.fn(), POST: vi.fn(), PUT: vi.fn(), DELETE: vi.fn() } }));
vi.mock("@/components/ui/entity-lookup", async (importOriginal) => ({ ...(await importOriginal<typeof import("@/components/ui/entity-lookup")>()), EntityLookup: (await import("@/apps/transport/__tests__/transport-test-mocks")).MockEntityLookup }));
vi.mock("@/components/ui/date-picker", async (importOriginal) => ({ ...(await importOriginal<typeof import("@/components/ui/date-picker")>()), DatePicker: (await import("@/apps/transport/__tests__/transport-test-mocks")).MockDatePicker }));
vi.mock("@/components/ui/reference-combobox", async () => ({ ReferenceCombobox: (await import("@/apps/transport/__tests__/transport-test-mocks")).MockReferenceCombobox }));
vi.mock("@/domains/planning-execution/components/StopDetailMap", () => ({ default: ({ latitude, longitude }: { latitude: number; longitude: number }) => <div data-testid="stop-map">{`${latitude},${longitude}`}</div> }));
vi.mock("sonner", () => ({ toast: { success: vi.fn(), error: vi.fn(), warning: vi.fn() } }));

import { toast } from "sonner";
import { fail, lookupItems, ribbonButton, stubGlobals } from "@/apps/transport/__tests__/transport-test-mocks";
import { calls, lastCall, POST, PUT, resetClient, routes } from "@/apps/transport/__tests__/test-setup-mocks";
import { StopDetailDialog } from "@/domains/planning-execution/components/StopDetailDialog";
import type { TripStopDto } from "@/domains/planning-execution/trips";

const UPDATE = "/api/planning/transport-trip/{id}/stop/{stopId}";
const ACTUALS = "/api/planning/transport-trip/{id}/record-stop-actuals/{stopId}";

const STOP: TripStopDto = {
  id: "s1",
  position: 2,
  relationId: "r1",
  relationName: "Depot Noord",
  relationAddressId: "a1",
  remarks: "Ring first",
  plannedDurationMin: 30,
  actualArrival: new Date(2026, 10, 10, 8, 0).toISOString(),
  street: "Kade",
  houseNumber: "1",
  postalCode: "5000 AA",
  city: "Tilburg",
  countryCode: "NL",
  legs: [{ legId: "l1", role: 1, orderNumber: "O-1", customerName: "Cust", originName: "A", originCity: "Aa", destinationName: "B", destinationCity: "Bb", quantity: 3 }],
} as unknown as TripStopDto;

const NO_LEGS = { ...STOP, legs: [] } as TripStopDto;

const onClose = vi.fn();

function open(stop: TripStopDto = STOP, canEdit = true) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } });
  return render(
    <QueryClientProvider client={client}>
      <StopDetailDialog tripId="t1" stop={stop} canEdit={canEdit} onClose={onClose} />
    </QueryClientProvider>,
  );
}

const field = <T extends HTMLElement = HTMLInputElement>(id: string) => document.getElementById(`stop-detail-${id}`) as T | null;
const type = (id: string, value: string) => fireEvent.change(field(id)!, { target: { value } });
const save = async () => {
  await act(async () => void fireEvent.click(ribbonButton("Save")!));
};

beforeAll(stubGlobals);
beforeEach(() => {
  onClose.mockReset();
  vi.mocked(toast.success).mockReset();
  resetClient();
  lookupItems.current = { "stop-detail-relation": [{ id: "r2", name: "Other relation" }] } as never;
  routes.PUT[UPDATE] = () => ({ id: "t1" });
  routes.POST[ACTUALS] = () => ({ id: "t1" });
});

describe("StopDetailDialog: a save sends only what changed", () => {
  it("nothing changed: neither request, and the window closes", async () => {
    open();
    await save();
    expect(calls(PUT, UPDATE)).toHaveLength(0);
    expect(calls(POST, ACTUALS)).toHaveLength(0);
    expect(onClose).toHaveBeenCalledTimes(1);
  });

  it("remarks alone: the stop's update with only the remarks, no record-stop-actuals call", async () => {
    open();
    type("remarks", "  Call ahead ");
    await save();
    expect(lastCall(PUT, UPDATE)).toMatchObject({ params: { path: { id: "t1", stopId: "s1" } }, body: { remarks: "Call ahead" } });
    expect(Object.keys(lastCall(PUT, UPDATE)!.body as object)).toEqual(["remarks"]);
    expect(calls(POST, ACTUALS)).toHaveLength(0);
    expect(toast.success).toHaveBeenCalledWith("PlanningExecution:StopSaved");
    expect(onClose).toHaveBeenCalledTimes(1);
  });

  it("the planned duration alone, as a number", async () => {
    open();
    type("duration", "45");
    await save();
    expect(lastCall(PUT, UPDATE)!.body).toEqual({ plannedDurationMin: 45 });
    expect(calls(POST, ACTUALS)).toHaveLength(0);
  });

  it("an actual time alone: record-stop-actuals with that time, no update; null keeps the other", async () => {
    open();
    type("departure-date", "2026-11-10");
    type("departure-time", "09:15");
    await save();
    expect(calls(PUT, UPDATE)).toHaveLength(0);
    expect(lastCall(POST, ACTUALS)).toMatchObject({
      params: { path: { id: "t1", stopId: "s1" } },
      body: { actualArrival: null, actualDeparture: new Date(2026, 10, 10, 9, 15).toISOString(), cumulativeDistanceM: null },
    });
    expect(onClose).toHaveBeenCalledTimes(1);
  });

  it("both: the update first, then the actuals", async () => {
    open();
    type("remarks", "x");
    type("arrival-time", "08:30");
    await save();
    expect(calls(PUT, UPDATE)).toHaveLength(1);
    expect(calls(POST, ACTUALS)).toHaveLength(1);
    expect(PUT.mock.invocationCallOrder[0]).toBeLessThan(POST.mock.invocationCallOrder[0]);
    expect(lastCall(POST, ACTUALS)!.body).toMatchObject({ actualArrival: new Date(2026, 10, 10, 8, 30).toISOString(), actualDeparture: null });
  });
});

describe("StopDetailDialog: partial failure", () => {
  it("the actuals fail after the update: the window stays with the error, and a retry sends only the actuals", async () => {
    let failing = true;
    routes.POST[ACTUALS] = () => (failing ? fail("Arrival after departure") : { id: "t1" });
    open();
    type("remarks", "Saved once");
    type("arrival-time", "08:30");
    await save();
    expect(screen.getByText("Arrival after departure")).toBeInTheDocument();
    expect(onClose).not.toHaveBeenCalled();
    expect(calls(PUT, UPDATE)).toHaveLength(1);

    failing = false;
    await save();
    expect(calls(PUT, UPDATE)).toHaveLength(1);
    expect(calls(POST, ACTUALS)).toHaveLength(2);
    expect(onClose).toHaveBeenCalledTimes(1);
  });

  it("the update fails: nothing else is sent, and a retry sends both", async () => {
    let failing = true;
    routes.PUT[UPDATE] = () => (failing ? fail("Refused") : { id: "t1" });
    open();
    type("remarks", "x");
    type("arrival-time", "08:30");
    await save();
    expect(screen.getByText("Refused")).toBeInTheDocument();
    expect(calls(POST, ACTUALS)).toHaveLength(0);
    expect(onClose).not.toHaveBeenCalled();

    failing = false;
    await save();
    expect(calls(PUT, UPDATE)).toHaveLength(2);
    expect(calls(POST, ACTUALS)).toHaveLength(1);
    expect(onClose).toHaveBeenCalledTimes(1);
  });

  it("the error of a failed try goes away on the next", async () => {
    let failing = true;
    routes.PUT[UPDATE] = () => (failing ? fail("Refused") : { id: "t1" });
    open();
    type("remarks", "x");
    await save();
    failing = false;
    routes.PUT[UPDATE] = () => {
      expect(screen.queryByText("Refused")).toBeNull();
      return { id: "t1" };
    };
    await save();
    expect(onClose).toHaveBeenCalledTimes(1);
  });
});

describe("StopDetailDialog: validation", () => {
  it("does not save on an invalid time, and says so only after the try", async () => {
    open();
    type("arrival-time", "25:99");
    expect(screen.queryByText("Orders:InvalidTime")).toBeNull();
    await save();
    expect(screen.getByText("Orders:InvalidTime")).toBeInTheDocument();
    expect(calls(POST, ACTUALS)).toHaveLength(0);
    expect(calls(PUT, UPDATE)).toHaveLength(0);
    expect(onClose).not.toHaveBeenCalled();
  });

  it("does not empty a stored actual", async () => {
    open();
    type("arrival-date", "");
    type("arrival-time", "");
    await save();
    expect(screen.getByText("PlanningExecution:StopValueCannotBeEmptied")).toBeInTheDocument();
    expect(calls(POST, ACTUALS)).toHaveLength(0);
  });

  it("does not take a bad duration", async () => {
    open();
    type("duration", "1.5");
    await save();
    expect(screen.getByText("MasterData:InvalidNumber")).toBeInTheDocument();
    expect(calls(PUT, UPDATE)).toHaveLength(0);
  });
});

describe("StopDetailDialog: read-only without canEdit", () => {
  it("offers no save and disables every field", () => {
    open(NO_LEGS, false);
    expect(ribbonButton("Save")).toBeNull();
    for (const id of ["arrival-date", "arrival-time", "departure-date", "departure-time", "duration", "remarks"]) {
      expect(field(id), id).toBeDisabled();
    }
  });

  it("shows the place as text, also on a stop without legs", () => {
    open(NO_LEGS, false);
    expect(field("relation")).toBeNull();
    expect(field("address")).toBeNull();
    expect(screen.getByText("Depot Noord")).toBeInTheDocument();
    expect(screen.getByText("Kade 1, 5000 AA Tilburg, NL")).toBeInTheDocument();
  });

  it("closes without asking", () => {
    open(STOP, false);
    fireEvent.click(ribbonButton("Close")!);
    expect(onClose).toHaveBeenCalledTimes(1);
  });
});

describe("StopDetailDialog: the place", () => {
  it("is read-only on a stop with legs", () => {
    open(STOP, true);
    expect(field("relation")).toBeNull();
    expect(field("address")).toBeNull();
    expect(screen.getByText("Depot Noord")).toBeInTheDocument();
  });

  it("is editable on a stop without legs; a new relation empties the address", async () => {
    open(NO_LEGS, true);
    expect(field<HTMLSelectElement>("relation")!.value).toBe("r1");
    fireEvent.change(field("relation")!, { target: { value: "r2" } });
    await save();
    expect(lastCall(PUT, UPDATE)!.body).toEqual({ relationId: "r2", relationAddressId: null });
  });

  it("a stop with legs never sends a place, whatever else changes", async () => {
    open(STOP, true);
    type("remarks", "x");
    await save();
    expect(lastCall(PUT, UPDATE)!.body).not.toHaveProperty("relationId");
    expect(lastCall(PUT, UPDATE)!.body).not.toHaveProperty("relationAddressId");
  });
});

describe("StopDetailDialog: what it shows", () => {
  it("the map only with coordinates", async () => {
    open({ ...STOP, latitude: 51.5, longitude: 5.1 });
    expect(await screen.findByTestId("stop-map")).toHaveTextContent("51.5,5.1");
  });

  it("no map without coordinates", async () => {
    open(STOP);
    await act(async () => undefined);
    expect(screen.queryByTestId("stop-map")).toBeNull();
  });

  it("the contact section only with contact details", () => {
    open({ ...STOP, relationPhone: "013-1234567" });
    expect(screen.getByText("013-1234567")).toBeInTheDocument();
    expect(screen.getByText("MasterData:Phone")).toBeInTheDocument();
  });

  it("no contact section without them", () => {
    open(STOP);
    expect(screen.queryByText("MasterData:Phone")).toBeNull();
  });

  it("no record tab: General and Legs only (PO 2026-11-06)", () => {
    open(STOP);
    expect(Array.from(document.querySelectorAll("[data-editor-tab-strip] [role=tab]")).map((tab) => tab.textContent?.trim())).toEqual(["General", "PlanningExecution:Legs"]);
  });

  it("the Legs tab lists the stop's legs read-only", async () => {
    open(STOP);
    fireEvent.click(screen.getByRole("tab", { name: "PlanningExecution:Legs" }));
    await waitFor(() => expect(screen.getByText("O-1")).toBeInTheDocument());
    expect(screen.getByText("Cust")).toBeInTheDocument();
    expect(screen.getByText("A, Aa")).toBeInTheDocument();
    expect(screen.getByText("B, Bb")).toBeInTheDocument();
  });

  it("the Legs tab says so when there are none", async () => {
    open(NO_LEGS);
    fireEvent.click(screen.getByRole("tab", { name: "PlanningExecution:Legs" }));
    await waitFor(() => expect(screen.getByText("PlanningExecution:StopDetail.NoLegs")).toBeInTheDocument());
  });

  it("closing untouched does not ask, closing with a change does", () => {
    open(STOP);
    fireEvent.click(ribbonButton("Close")!);
    expect(onClose).toHaveBeenCalledTimes(1);
    onClose.mockReset();
    type("remarks", "x");
    fireEvent.click(ribbonButton("Close")!);
    expect(onClose).not.toHaveBeenCalled();
  });
});
