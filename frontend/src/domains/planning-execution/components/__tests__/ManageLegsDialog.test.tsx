import { act, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { beforeAll, beforeEach, describe, expect, it, vi } from "vitest";

/* Manage legs (design 74 §4): one block per leg of the line, its rules on screen, the whole line sent on save. A line whose legs
 * run side by side (`isChain` false) shows read-only with the reason; a leg on a trip is read-only with a note naming the trip.
 * The server validates the chain: its refusal shows in the dialog. Risk class: High (order chain, planning). */

vi.mock("@/lib/i18n/LocalizationProvider", () => ({ useL: () => ({ t: (k: string) => k, culture: "en", isLoading: false }) }));
vi.mock("@/lib/abp/queries", () => ({ useApplicationConfiguration: () => ({ data: { currentTenant: { id: "t-1" }, auth: { grantedPolicies: {} }, features: { values: {} } } }) }));
vi.mock("@/lib/api/client", () => ({ apiClient: { GET: vi.fn(), POST: vi.fn(), PUT: vi.fn(), DELETE: vi.fn() } }));
vi.mock("@/components/ui/entity-lookup", async (importOriginal) => ({ ...(await importOriginal<typeof import("@/components/ui/entity-lookup")>()), EntityLookup: (await import("@/apps/transport/__tests__/transport-test-mocks")).MockEntityLookup }));
vi.mock("@/components/ui/date-picker", async (importOriginal) => ({ ...(await importOriginal<typeof import("@/components/ui/date-picker")>()), DatePicker: (await import("@/apps/transport/__tests__/transport-test-mocks")).MockDatePicker }));
vi.mock("@/components/ui/reference-combobox", async () => ({ ReferenceCombobox: (await import("@/apps/transport/__tests__/transport-test-mocks")).MockReferenceCombobox }));
vi.mock("sonner", () => ({ toast: { success: vi.fn(), error: vi.fn(), warning: vi.fn() } }));

import { toast } from "sonner";
import { fail, lookupItems, ribbonButton, stubGlobals } from "@/apps/transport/__tests__/transport-test-mocks";
import { calls, lastCall, PUT, resetClient, routes } from "@/apps/transport/__tests__/test-setup-mocks";
import { ManageLegsDialog } from "@/domains/planning-execution/components/ManageLegsDialog";
import type { LegChainUpdateDto, LegDto } from "@/domains/planning-execution/legs";

const CHAIN = "/api/planning/leg/line/{lineId}";

const endOf = (relationId: string, type: string) => ({ waypointTypeId: type, relationId, name: `Rel ${relationId}`, relationAddressId: null, plannedDate: "2026-11-10", reference: null });
const legRow = (id: string, from: string, to: string, patch: Record<string, unknown> = {}, rowPatch: Record<string, unknown> = {}) => ({
  isReadOnly: false,
  hasActuals: false,
  leg: { id, concurrencyStamp: `stamp-${id}`, origin: endOf(from, "load"), destination: endOf(to, "unload"), ...patch },
  ...rowPatch,
});

const TWO = { orderLineId: "line-1", isChain: true, rows: [legRow("l1", "A", "B"), legRow("l2", "B", "C")] };
const LEG = { id: "l1", orderLineId: "line-1", orderNumber: "O-1" } as LegDto;

const onClose = vi.fn();
const onDone = vi.fn();

async function open(chain: unknown = TWO) {
  routes.GET[CHAIN] = () => chain;
  const client = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } });
  render(
    <QueryClientProvider client={client}>
      <ManageLegsDialog leg={LEG} onClose={onClose} onDone={onDone} />
    </QueryClientProvider>,
  );
  await waitFor(() => expect(document.querySelector("[data-slot=manage-legs]")).not.toBeNull());
}

const control = <T extends HTMLElement = HTMLInputElement>(key: string, side: "origin" | "destination", name: string) => document.getElementById(`manage-legs-${key}-${side}-${name}`) as T;
const blocks = () => Array.from(document.querySelectorAll("[data-slot=manage-legs] [data-slot=form-section]"));
const removeButtons = () => screen.queryAllByRole("button", { name: "PlanningExecution:RemoveLeg" });
const save = async () => {
  await act(async () => void fireEvent.click(ribbonButton("Save")!));
};
const sent = () => lastCall(PUT, CHAIN)!.body as LegChainUpdateDto;

beforeAll(stubGlobals);
beforeEach(() => {
  onClose.mockReset();
  onDone.mockReset();
  vi.mocked(toast.success).mockReset();
  resetClient();
  Element.prototype.scrollIntoView = vi.fn();
  lookupItems.current = { "*-relation": [{ id: "X", name: "Rel X" }, { id: "Y", name: "Rel Y" }] } as never;
  routes.GET["/api/orders/waypoint-type/lookup"] = () => [{ id: "load", name: "Load" }, { id: "unload", name: "Unload" }];
  routes.GET["/api/orders/waypoint-type/{id}"] = (init) => ({ id: init?.params?.path?.id, successiveWaypointTypeId: init?.params?.path?.id === "unload" ? "load" : "unload" });
  routes.PUT[CHAIN] = () => TWO;
});

describe("ManageLegsDialog: what it shows", () => {
  it("one block per leg, under the route from first to last", async () => {
    await open();
    expect(blocks()).toHaveLength(2);
    expect(screen.getByText("PlanningExecution:ManageLegsRoute")).toBeInTheDocument();
    expect(control<HTMLSelectElement>("l1", "origin", "relation").value).toBe("A");
    expect(control<HTMLSelectElement>("l2", "destination", "relation").value).toBe("C");
  });

  it("locks the first origin and the last destination, leaves the joint open", async () => {
    await open();
    expect(control("l1", "origin", "relation")).toBeDisabled();
    expect(control("l1", "origin", "activity")).toBeDisabled();
    expect(control("l2", "destination", "relation")).toBeDisabled();
    expect(control("l1", "destination", "relation")).toBeEnabled();
    expect(control("l2", "origin", "relation")).toBeEnabled();
  });

  it("shows the load's error and a way out when the line cannot be read", async () => {
    routes.GET[CHAIN] = () => fail("Line gone");
    const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    render(
      <QueryClientProvider client={client}>
        <ManageLegsDialog leg={LEG} onClose={onClose} onDone={onDone} />
      </QueryClientProvider>,
    );
    expect(await screen.findByText("Line gone")).toBeInTheDocument();
    expect(ribbonButton("Save")).toBeNull();
  });
});

describe("ManageLegsDialog: a line that is no chain", () => {
  const SIDE_BY_SIDE = { orderLineId: "line-1", isChain: false, rows: [legRow("l1", "A", "B"), legRow("l2", "A", "C")] };

  it("says why, offers no save, add or remove, and locks every field", async () => {
    await open(SIDE_BY_SIDE);
    expect(screen.getByText("OpenTms.PlanningExecution:LegLineNotAChain")).toBeInTheDocument();
    expect(ribbonButton("Save")).toBeNull();
    expect(ribbonButton("PlanningExecution:AddLeg")).toBeNull();
    expect(removeButtons()).toHaveLength(0);
    for (const name of ["date", "activity", "relation", "from-date", "from-time", "till-date", "till-time", "reference"]) {
      expect(control("l2", "origin", name), name).toBeDisabled();
      expect(control("l1", "destination", name), name).toBeDisabled();
    }
    expect(document.getElementById("manage-legs-l1-remarks")).toBeDisabled();
  });

  it("closes without asking", async () => {
    await open(SIDE_BY_SIDE);
    fireEvent.click(ribbonButton("Close")!);
    expect(onClose).toHaveBeenCalledTimes(1);
  });
});

describe("ManageLegsDialog: read-only legs", () => {
  const ON_TRIP = { orderLineId: "line-1", isChain: true, rows: [legRow("l1", "A", "B"), legRow("l2", "B", "C", { tripNumber: "TR-7", tripId: "t7" }, { isReadOnly: true })] };

  it("a leg on a trip is read-only, with a note naming the trip", async () => {
    await open(ON_TRIP);
    expect(screen.getByText("PlanningExecution:LegReadOnlyOnTrip")).toBeInTheDocument();
    for (const name of ["date", "activity", "relation", "from-date", "from-time", "till-date", "till-time", "reference"]) {
      expect(control("l2", "origin", name), name).toBeDisabled();
      expect(control("l2", "destination", name), name).toBeDisabled();
    }
    expect(document.getElementById("manage-legs-l2-remarks")).toBeDisabled();
  });

  it("a leg with actuals says so instead", async () => {
    await open({ orderLineId: "line-1", isChain: true, rows: [legRow("l1", "A", "B", {}, { isReadOnly: true, hasActuals: true }), legRow("l2", "B", "C")] });
    expect(screen.getByText("PlanningExecution:LegReadOnlyActuals")).toBeInTheDocument();
  });

  it("the neighbour's end that must equal it is locked; the neighbour's other fields stay open", async () => {
    await open(ON_TRIP);
    expect(control("l1", "destination", "relation")).toBeDisabled();
    expect(control("l1", "destination", "activity")).toBeDisabled();
    expect(control("l1", "destination", "date")).toBeEnabled();
    expect(control("l1", "destination", "reference")).toBeEnabled();
  });

  it("add and remove are refused when they would change its ends", async () => {
    await open(ON_TRIP);
    expect(ribbonButton("PlanningExecution:AddLeg")).toHaveAttribute("aria-disabled", "true");
    expect(removeButtons().every((button) => (button as HTMLButtonElement).disabled)).toBe(true);
  });
});

describe("ManageLegsDialog: editing", () => {
  it("a destination relation follows into the next origin", async () => {
    await open();
    fireEvent.change(control("l1", "destination", "relation"), { target: { value: "X" } });
    expect(control<HTMLSelectElement>("l2", "origin", "relation").value).toBe("X");
  });

  it("an origin relation follows into the previous destination", async () => {
    await open();
    fireEvent.change(control("l2", "origin", "relation"), { target: { value: "Y" } });
    expect(control<HTMLSelectElement>("l1", "destination", "relation").value).toBe("Y");
  });

  it("Add appends a block; it cannot be saved until its relation is chosen", async () => {
    await open();
    fireEvent.click(ribbonButton("PlanningExecution:AddLeg")!);
    expect(blocks()).toHaveLength(3);
    expect(control<HTMLSelectElement>("l2", "destination", "relation").value).toBe("");
    expect(control<HTMLSelectElement>("new-1", "destination", "relation").value).toBe("C");
    await save();
    expect(calls(PUT, CHAIN)).toHaveLength(0);
    expect(screen.getAllByText("MasterData:Required").length).toBeGreaterThan(0);
    expect(onClose).not.toHaveBeenCalled();
  });

  it("Add, a relation, Save: the new row goes without an id, every read leg with its stamp", async () => {
    await open();
    fireEvent.click(ribbonButton("PlanningExecution:AddLeg")!);
    fireEvent.change(control("new-1", "origin", "relation"), { target: { value: "X" } });
    await save();
    const body = sent();
    expect(body.rows!.map((row) => row.id)).toEqual(["l1", "l2", null]);
    expect(body.rows![1].destination!.relationId).toBe("X");
    expect(body.rows![2].origin!.relationId).toBe("X");
    expect(body.rows![2].destination!.relationId).toBe("C");
    expect(body.stamps).toEqual([{ id: "l1", concurrencyStamp: "stamp-l1" }, { id: "l2", concurrencyStamp: "stamp-l2" }]);
    expect(lastCall(PUT, CHAIN)!.params!.path).toEqual({ lineId: "line-1" });
  });

  it("two adds get their own keys", async () => {
    await open();
    fireEvent.click(ribbonButton("PlanningExecution:AddLeg")!);
    fireEvent.click(ribbonButton("PlanningExecution:AddLeg")!);
    expect(blocks()).toHaveLength(4);
    expect(control("new-2", "origin", "relation")).not.toBeNull();
  });

  it("Remove takes the block out and keeps the chain; a removed leg's stamp still goes along", async () => {
    await open();
    fireEvent.click(removeButtons()[0]);
    expect(blocks()).toHaveLength(1);
    expect(control<HTMLSelectElement>("l2", "origin", "relation").value).toBe("A");
    await save();
    expect(sent().rows!.map((row) => row.id)).toEqual(["l2"]);
    expect(sent().stamps!.map((stamp) => stamp.id)).toEqual(["l1", "l2"]);
  });

  it("no Remove with one leg left", async () => {
    await open({ orderLineId: "line-1", isChain: true, rows: [legRow("l1", "A", "B")] });
    expect(removeButtons()).toHaveLength(0);
  });

  it("an unreadable window time blocks the save and says so", async () => {
    await open();
    fireEvent.change(control("l1", "origin", "from-time"), { target: { value: "99:99" } });
    await save();
    expect(calls(PUT, CHAIN)).toHaveLength(0);
    expect(screen.getByText("Orders:InvalidTime")).toBeInTheDocument();
  });
});

describe("ManageLegsDialog: saving", () => {
  it("success: the toast, the dialog closes and the pane reloads", async () => {
    await open();
    await save();
    expect(toast.success).toHaveBeenCalledWith("PlanningExecution:LegsSaved");
    expect(onClose).toHaveBeenCalledTimes(1);
    expect(onDone).toHaveBeenCalledTimes(1);
  });

  it("the server's refusal shows in the dialog, which stays with what the planner typed", async () => {
    routes.PUT[CHAIN] = () => fail("The chain is not contiguous");
    await open();
    fireEvent.change(control("l1", "destination", "reference"), { target: { value: "REF" } });
    await save();
    expect(screen.getByText("The chain is not contiguous")).toBeInTheDocument();
    expect(onClose).not.toHaveBeenCalled();
    expect(onDone).not.toHaveBeenCalled();
    expect(control<HTMLInputElement>("l1", "destination", "reference").value).toBe("REF");
  });

  it("closing with a change asks first, closing untouched does not", async () => {
    await open();
    fireEvent.change(control("l1", "destination", "reference"), { target: { value: "REF" } });
    fireEvent.click(ribbonButton("Close")!);
    expect(onClose).not.toHaveBeenCalled();
  });
});
