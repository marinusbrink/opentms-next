import { act, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { beforeAll, beforeEach, describe, expect, it, vi } from "vitest";

/* Split line, as legacy (design 74 §7): two columns, each with an editable amount and measures that recalculate live; OK needs
 * both amounts above zero (legacy allowed a remaining of 0: not copied); the server decides the stored quantities. Risk class:
 * High (order chain). The server side is in the backend's PlanboardReviewSplitLineTests. */

vi.mock("@/lib/i18n/LocalizationProvider", () => ({ useL: () => ({ t: (k: string) => k, culture: "en", isLoading: false }) }));
vi.mock("@/lib/abp/queries", () => ({ useApplicationConfiguration: () => ({ data: { currentTenant: { id: "t-1" }, auth: { grantedPolicies: {} }, features: { values: {} } } }) }));
vi.mock("@/lib/api/client", () => ({ apiClient: { GET: vi.fn(), POST: vi.fn(), PUT: vi.fn(), DELETE: vi.fn() } }));
vi.mock("sonner", () => ({ toast: { success: vi.fn(), error: vi.fn(), warning: vi.fn() } }));

import { toast } from "sonner";
import { fail, stubGlobals } from "@/apps/transport/__tests__/transport-test-mocks";
import { calls, lastCall, POST, resetClient, routes } from "@/apps/transport/__tests__/test-setup-mocks";
import { SplitLineDialog } from "@/domains/planning-execution/components/SplitLineDialog";
import type { LegDto } from "@/domains/planning-execution/legs";

const SPLIT = "/api/planning/leg/{id}/split-order-line";
const ORDER = "/api/orders/transport-order/{id}";

const LEG = {
  id: "l1",
  orderId: "o1",
  orderLineId: "line-1",
  orderNumber: "O-1",
  quantity: 8,
  weightKg: 80,
  volumeM3: null,
  loadingMeters: null,
  loadFactor: null,
  epq: null,
  transportUnitName: "Pallet",
  productName: "Apples",
  origin: { name: "Origin Co", city: "Tilburg" },
  destination: { name: "Dest Co", city: "Breda" },
} as unknown as LegDto;

const LINE = { id: "LINE-1", quantity: 10, colli: 20, weightKg: 1000, volumeM3: 5, loadingMeters: null, loadFactor: null, epq: null };

const onClose = vi.fn();
const onDone = vi.fn();

async function open(line: Record<string, unknown> | null = LINE, leg: LegDto = LEG) {
  routes.GET[ORDER] = () => (line ? { id: "o1", lines: [line] } : fail("Forbidden", 403));
  const client = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } });
  render(
    <QueryClientProvider client={client}>
      <SplitLineDialog leg={leg} onClose={onClose} onDone={onDone} />
    </QueryClientProvider>,
  );
  await act(async () => undefined);
  if (line) await waitFor(() => expect(hint()).toHaveTextContent("PlanningExecution:SplitLineHint"));
}

const hint = () => document.querySelector("[data-slot=split-line-hint]");
const amount = (column: "new" | "remaining") => document.getElementById(`split-line-${column}`) as HTMLInputElement;
const column = (name: "new" | "remaining") => document.querySelector<HTMLElement>(`[data-slot=split-line-${name}]`)!;
const measure = (name: "new" | "remaining", label: string) => within(column(name)).getByText(label).nextElementSibling?.textContent;
const type = (name: "new" | "remaining", value: string) => fireEvent.change(amount(name), { target: { value } });
const ok = () => screen.getByRole("button", { name: "PlanningExecution:Split" });
const submit = async () => {
  await act(async () => void fireEvent.submit(document.getElementById("split-line-form")!));
};

beforeAll(stubGlobals);
beforeEach(() => {
  onClose.mockReset();
  onDone.mockReset();
  vi.mocked(toast.success).mockReset();
  resetClient();
  routes.POST[SPLIT] = () => ({ id: "new-line" });
});

describe("SplitLineDialog: what it shows", () => {
  it("the read-only line: unit, product, origin, destination", async () => {
    await open();
    expect(screen.getByText("Pallet")).toBeInTheDocument();
    expect(screen.getByText("Apples")).toBeInTheDocument();
    expect(screen.getByText(/Origin Co/)).toBeInTheDocument();
    expect(screen.getByText(/Dest Co/)).toBeInTheDocument();
  });

  it("recalculates both columns live when the new amount is typed", async () => {
    await open();
    expect(measure("new", "Orders:WeightKg")).toBe("–");
    type("new", "4");
    expect(amount("remaining").value).toBe("6");
    expect(measure("new", "Orders:WeightKg")).toBe("400");
    expect(measure("remaining", "Orders:WeightKg")).toBe("600");
    expect(measure("new", "Orders:Colli")).toBe("8");
    expect(measure("remaining", "Orders:Colli")).toBe("12");
    expect(measure("new", "Orders:VolumeM3")).toBe("2");
  });

  it("recalculates when the remaining amount is typed", async () => {
    await open();
    type("remaining", "7.5");
    expect(amount("new").value).toBe("2.5");
    expect(measure("remaining", "Orders:WeightKg")).toBe("750");
    expect(measure("new", "Orders:WeightKg")).toBe("250");
  });

  it("takes a decimal comma", async () => {
    await open();
    type("new", "2,5");
    expect(amount("remaining").value).toBe("7.5");
  });

  it("shows two decimals on screen", async () => {
    await open({ ...LINE, quantity: 3, weightKg: 10 });
    type("new", "1");
    expect(measure("new", "Orders:WeightKg")).toBe("3.33");
    expect(measure("remaining", "Orders:WeightKg")).toBe("6.67");
  });

  it("a measure the line does not have shows no number on either side", async () => {
    await open();
    type("new", "4");
    expect(measure("new", "Orders:LoadingMeters")).not.toMatch(/\d/);
    expect(measure("remaining", "Orders:Epq")).not.toMatch(/\d/);
  });

  it("the other column clears when the typed text is no number", async () => {
    await open();
    type("new", "abc");
    expect(amount("remaining").value).toBe("");
    expect(measure("new", "Orders:WeightKg")).toBe("–");
  });

  it("without the right to read the order the leg's own measures are the base", async () => {
    await open(null);
    type("new", "2");
    expect(amount("remaining").value).toBe("6");
    expect(measure("new", "Orders:WeightKg")).toBe("20");
    expect(measure("remaining", "Orders:WeightKg")).toBe("60");
  });

  it("finds the order's line whatever the case of its id: the line's total, not the leg's", async () => {
    await open();
    type("new", "9");
    expect(amount("remaining").value).toBe("1");
  });
});

describe("SplitLineDialog: OK needs both amounts above zero", () => {
  it.each([
    ["nothing", "new", ""],
    ["no number", "new", "abc"],
    ["a new amount of 0", "new", "0"],
    ["a negative amount", "new", "-1"],
    ["the whole total", "new", "10"],
    ["more than the total", "new", "11"],
    ["a remaining amount of 0", "remaining", "0"],
    ["a remaining amount of the whole total", "remaining", "10"],
  ] as const)("refuses %s: the reason shows and nothing is sent", async (_name, name, value) => {
    await open();
    type(name, value);
    await submit();
    expect(calls(POST, SPLIT)).toHaveLength(0);
    expect(screen.getAllByText("PlanningExecution:SplitLineAmounts").length).toBeGreaterThan(0);
    expect(onClose).not.toHaveBeenCalled();
  });

  it("does not show the reason before the first try", async () => {
    await open();
    type("new", "0");
    expect(screen.queryByText("PlanningExecution:SplitLineAmounts")).toBeNull();
  });

  it("is disabled while the split runs, the cancel and the amounts too", async () => {
    let release: (value: unknown) => void = () => undefined;
    POST.mockImplementationOnce(() => new Promise((resolve) => (release = resolve)));
    await open();
    type("new", "4");
    await submit();
    expect(screen.getByRole("button", { name: "Saving" })).toBeDisabled();
    expect(screen.getByRole("button", { name: "Cancel" })).toBeDisabled();
    expect(amount("new")).toBeDisabled();
    await act(async () => release({ data: { id: "x" }, response: new Response() }));
    expect(onClose).toHaveBeenCalledTimes(1);
  });
});

describe("SplitLineDialog: splitting", () => {
  it("sends the new line's amount; the toast, the dialog closes, the pane reloads", async () => {
    await open();
    type("new", "4");
    await act(async () => void fireEvent.click(ok()));
    expect(calls(POST, SPLIT)).toHaveLength(1);
    expect(lastCall(POST, SPLIT)).toMatchObject({ params: { path: { id: "l1" } }, body: { quantity: 4 } });
    expect(toast.success).toHaveBeenCalledWith("PlanningExecution:LineSplit");
    expect(onClose).toHaveBeenCalledTimes(1);
    expect(onDone).toHaveBeenCalledTimes(1);
  });

  it("sends the complement when the remaining amount was typed", async () => {
    await open();
    type("remaining", "7.5");
    await submit();
    expect(lastCall(POST, SPLIT)!.body).toEqual({ quantity: 2.5 });
  });

  it("the amount typed on the new line goes as typed, beyond the two decimals on screen", async () => {
    await open();
    type("new", "3.333");
    expect(amount("remaining").value).toBe("6.67");
    await submit();
    expect(lastCall(POST, SPLIT)!.body).toEqual({ quantity: 3.333 });
  });

  /* The total stays exact only when new = total − remaining: the planner typed a remaining of exactly 3. */
  it("a remaining amount the planner typed is what remains, on a total with more than two decimals", async () => {
    await open({ ...LINE, quantity: 10.555 });
    type("remaining", "3");
    await submit();
    expect(lastCall(POST, SPLIT)!.body).toEqual({ quantity: 7.555 });
  });

  it("the server's refusal stays in the dialog, and a retry works", async () => {
    let failing = true;
    routes.POST[SPLIT] = () => (failing ? fail("Line is locked") : { id: "x" });
    await open();
    type("new", "4");
    await submit();
    expect(screen.getByText("Line is locked")).toBeInTheDocument();
    expect(onClose).not.toHaveBeenCalled();
    failing = false;
    await submit();
    expect(screen.queryByText("Line is locked")).toBeNull();
    expect(onClose).toHaveBeenCalledTimes(1);
  });

  it("Cancel sends nothing", async () => {
    await open();
    type("new", "4");
    fireEvent.click(screen.getByRole("button", { name: "Cancel" }));
    expect(onClose).toHaveBeenCalled();
    expect(calls(POST, SPLIT)).toHaveLength(0);
  });
});
