import { render, waitFor, within } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import type { ReactNode } from "react";
import { beforeAll, beforeEach, describe, expect, it, vi } from "vitest";

/* Design 72 build note 20 (review M1) on the review page: Orders refused to create the order from the received values, so
 * the intake carries the pseudo-field `order` (Unresolved, the business error code). The page shows it as one red row at
 * the top of the order section: the error code's own text (any module's resource), a generic text when the code has none,
 * the hint to correct or reject, and no picker; it is never listed again under "other open fields". A resolved or absent
 * `order` entry shows nothing. Risk class: Medium (UI flow on top of the High order chain). */

const navigateMock = vi.fn();
vi.mock("@tanstack/react-router", () => ({
  useNavigate: () => navigateMock,
  useLocation: () => ({ pathname: "/transport/intake", search: {} }),
  Link: ({ to, children, className }: { to: string; children?: ReactNode; className?: string }) => (
    <a href={to} className={className}>
      {children}
    </a>
  ),
}));
/* A localizer that knows one module error text, like ABP's endpoint; any other key comes back as itself. */
const TEXTS: Record<string, string> = { "Orders:StopWindowAmbiguous": "A stop has a start time without an end time." };
vi.mock("@/lib/i18n/LocalizationProvider", () => ({ useL: () => ({ t: (k: string) => TEXTS[k] ?? k, culture: "en", isLoading: false }) }));

let grants: Record<string, boolean> = {};
let features: Record<string, string> = {};
vi.mock("@/lib/abp/queries", () => ({
  useApplicationConfiguration: () => ({ data: { currentTenant: { id: "t-1" }, auth: { grantedPolicies: grants }, features: { values: features }, localization: { languages: [] } } }),
}));
vi.mock("@/lib/api/client", () => ({ apiClient: { GET: vi.fn(), POST: vi.fn(), PUT: vi.fn(), DELETE: vi.fn() } }));
vi.mock("@/lib/realtime/record-updates", () => ({
  DerivedValueStatus: { Pending: 1, UpToDate: 2, Failed: 3 },
  recordUpdatesClient: () => ({ watch: () => () => undefined, subscribe: () => () => undefined, onReconnected: () => () => undefined }),
}));
vi.mock("@/components/ui/opentms-grid-lazy", async () => ({ LazyOpenTmsGrid: (await import("./transport-test-mocks")).MockOpenTmsGrid }));
vi.mock("@/components/ui/lines-grid-lazy", async () => ({ LazyLinesGrid: (await import("./transport-test-mocks")).MockLinesGrid }));
vi.mock("@/components/ui/entity-lookup", async (importOriginal) => ({ ...(await importOriginal<typeof import("@/components/ui/entity-lookup")>()), EntityLookup: (await import("./transport-test-mocks")).MockEntityLookup }));
vi.mock("@/components/ui/multi-select", async () => ({ MultiSelect: (await import("./transport-test-mocks")).MockMultiSelect }));
vi.mock("@/components/ui/date-picker", async (importOriginal) => ({ ...(await importOriginal<typeof import("@/components/ui/date-picker")>()), DatePicker: (await import("./transport-test-mocks")).MockDatePicker }));
vi.mock("@/components/ui/reference-combobox", async () => ({ ReferenceCombobox: (await import("./transport-test-mocks")).MockReferenceCombobox }));
vi.mock("@/components/ui/select", async () => {
  const mocks = await import("./transport-test-mocks");
  return { Select: mocks.MockSelect, SelectTrigger: mocks.MockSelectTrigger, SelectValue: mocks.Nothing, SelectContent: mocks.Nothing, SelectItem: mocks.Nothing };
});
vi.mock("sonner", () => ({ toast: { success: vi.fn(), error: vi.fn(), warning: vi.fn() } }));

import { IntakePage } from "@/apps/transport/pages/IntakePage";
import { ORDER_REFUSAL_PATH, orderRefusalText } from "@/apps/transport/intake/review-model";
import { resetClient, routes } from "./test-setup-mocks";
import { ribbonButton, stubGlobals } from "./transport-test-mocks";

const FEATURES = {
  "Integrations.OrderIntake": "true",
  "Apps.Transport": "true",
  "Apps.Transport.Intake": "true",
  "Apps.Transport.Orders": "true",
  "Apps.Integrations": "true",
  "Apps.Integrations.Received": "true",
};
const RESOLVE = { "Orders.OrderIntakes": true, "Orders.OrderIntakes.Resolve": true };

const INTAKE = {
  id: "i-open",
  status: 1,
  mappingConfidence: 0.8,
  creationTime: "2026-11-03T08:00:00Z",
  sourceSummary: { channel: "Gmail", senderAddress: "planning@acme.test", subject: "Order PO-4711", fileName: "order-4711.pdf", sourceDocumentId: "doc-1", intakeIndex: 0, source: 5 },
  payload: {
    customerReference: "PO-4711",
    orderTypeName: "Standaard",
    customer: { name: "ACME B.V.", emailAddress: "planning@acme.test" },
    lines: [
      {
        transportUnitName: "Europallet",
        quantity: 2,
        weightKg: 800,
        stops: [
          { role: 1, date: "2026-11-04", address: { name: "Acme DC", street: "Dam", houseNumber: "1", postalCode: "1012 AB", city: "Amsterdam", countryCode: "NL" } },
          { role: 2, date: "2026-11-05", address: { name: "Beta", postalCode: "3011 AA", city: "Rotterdam", countryCode: "NL" } },
        ],
      },
    ],
  },
  resolution: [
    { fieldPath: "order.customer", status: 3, method: null, candidates: [{ id: "r-acme", name: "Acme", detail: "Amsterdam" }, { id: "r-acme2", name: "Acme Logistics", detail: "Utrecht" }] },
    { fieldPath: "order.orderType", status: 1, method: 2, targetId: "tt-1", targetName: "Standard" },
    { fieldPath: "lines[0].transportUnit", status: 2, method: null, candidates: [{ id: "tu-eur", name: "Europallet" }, { id: "tu-eur2", name: "Euro pallet (block)" }] },
    { fieldPath: "lines[0].stops[0].address", status: 1, method: 3, targetId: "a-1", targetName: "Acme DC, Dam 1, Amsterdam" },
    { fieldPath: "lines[0].stops[0].waypointType", status: 1, method: 5, targetId: "wp-load", targetName: "Load" },
    { fieldPath: "lines[0].stops[1].address", status: 1, method: 2, targetId: "a-2", targetName: "Beta, Rotterdam" },
    { fieldPath: "lines[0].stops[1].waypointType", status: 1, method: 5, targetId: "wp-unload", targetName: "Unload" },
  ],
};

const RESOLVED = INTAKE.resolution.map((entry) => ({ ...entry, status: 1, method: 2, targetId: entry.targetId ?? "x", targetName: entry.targetName ?? "x", candidates: [] }));

function withRefusal(errorCode: string | null, status = 3) {
  return { ...INTAKE, resolution: [...RESOLVED, { fieldPath: ORDER_REFUSAL_PATH, status, method: null, candidates: [], errorCode }] };
}

beforeAll(stubGlobals);
beforeEach(() => {
  navigateMock.mockReset();
  grants = { ...RESOLVE };
  features = { ...FEATURES };
  resetClient();
});

function renderWith(element: React.ReactElement) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } });
  return render(<QueryClientProvider client={client}>{element}</QueryClientProvider>);
}

const review = () => document.querySelector<HTMLElement>("[data-slot=intake-review]");
const refusalRows = () => Array.from(document.querySelectorAll<HTMLElement>(`[data-slot=intake-review] [data-path="${ORDER_REFUSAL_PATH}"]`));
const waitForReview = () => waitFor(() => expect(review()).not.toBeNull());

describe("orderRefusalText (design 72 build note 20)", () => {
  const t = (k: string) => TEXTS[k] ?? k;

  it("is the error code's own text when the code has one", () => {
    expect(orderRefusalText(t, "Orders:StopWindowAmbiguous")).toBe("A stop has a start time without an end time.");
  });

  it.each([["Orders:NoTextForThis"], [null], [undefined], [""]])("falls back to the generic text for %s", (code) => {
    expect(orderRefusalText(t, code)).toBe("Orders:IntakeOrderRefusedUnknown");
  });
});

describe("IntakePage – a refused order creation (design 72 build note 20)", () => {
  it("shows one red row with the error code's text and the hint, without a picker", async () => {
    routes.GET["/api/orders/order-intake/{id}"] = () => withRefusal("Orders:StopWindowAmbiguous");
    renderWith(<IntakePage id="i-open" />);
    await waitForReview();
    await waitFor(() => expect(refusalRows()).toHaveLength(1));

    const row = refusalRows()[0];
    expect(row).toHaveAttribute("data-status", "Unresolved");
    expect(row).toHaveAttribute("role", "alert");
    expect(row.textContent).toContain("Orders:IntakeOrderRefused");
    expect(row.textContent).toContain("A stop has a start time without an end time.");
    expect(row.textContent).toContain("Orders:IntakeOrderRefusedHint");
    expect(row.textContent).not.toContain("Orders:StopWindowAmbiguous");
    expect(within(row).getByText("Orders:IntakeResolutionStatus.Unresolved")).toHaveAttribute("data-tone", "danger");
    expect(within(row).queryAllByRole("button")).toHaveLength(0);
    expect(within(row).queryAllByRole("combobox")).toHaveLength(0);
    expect(within(row).queryAllByRole("textbox")).toHaveLength(0);
    expect(within(row).queryByLabelText("Orders:IntakeCandidates")).toBeNull();
  });

  it("shows the generic text for a code without its own text", async () => {
    routes.GET["/api/orders/order-intake/{id}"] = () => withRefusal("Volo.Abp:SomethingUnknown");
    renderWith(<IntakePage id="i-open" />);
    await waitForReview();
    await waitFor(() => expect(refusalRows()).toHaveLength(1));
    expect(refusalRows()[0].textContent).toContain("Orders:IntakeOrderRefusedUnknown");
    expect(refusalRows()[0].textContent).not.toContain("Volo.Abp:SomethingUnknown");
  });

  it("shows the generic text when the refusal carries no code", async () => {
    routes.GET["/api/orders/order-intake/{id}"] = () => withRefusal(null);
    renderWith(<IntakePage id="i-open" />);
    await waitForReview();
    await waitFor(() => expect(refusalRows()).toHaveLength(1));
    expect(refusalRows()[0].textContent).toContain("Orders:IntakeOrderRefusedUnknown");
  });

  it("is not listed again under the other open fields", async () => {
    routes.GET["/api/orders/order-intake/{id}"] = () => withRefusal("Orders:StopWindowAmbiguous");
    renderWith(<IntakePage id="i-open" />);
    await waitForReview();
    await waitFor(() => expect(refusalRows()).toHaveLength(1));
    expect(review()!.textContent).not.toContain("Orders:IntakeOtherFields");
  });

  it("an unknown open path is still listed under the other open fields, next to the refusal", async () => {
    routes.GET["/api/orders/order-intake/{id}"] = () => {
      const intake = withRefusal("Orders:StopWindowAmbiguous");
      return { ...intake, resolution: [...intake.resolution, { fieldPath: "order.somethingNew", status: 3, method: null, candidates: [] }] };
    };
    renderWith(<IntakePage id="i-open" />);
    await waitForReview();
    await waitFor(() => expect(review()!.textContent).toContain("Orders:IntakeOtherFields"));
    expect(refusalRows()).toHaveLength(1);
    expect(document.querySelectorAll('[data-slot=intake-review] [data-path="order.somethingNew"]')).toHaveLength(1);
  });

  it("shows nothing for a resolved or absent order entry", async () => {
    routes.GET["/api/orders/order-intake/{id}"] = () => withRefusal(null, 1);
    const first = renderWith(<IntakePage id="i-open" />);
    await waitForReview();
    expect(refusalRows()).toHaveLength(0);
    first.unmount();

    routes.GET["/api/orders/order-intake/{id}"] = () => ({ ...INTAKE, resolution: RESOLVED });
    renderWith(<IntakePage id="i-open" />);
    await waitForReview();
    expect(refusalRows()).toHaveLength(0);
  });

  it("leaves Create order available: the clerk corrects the values and tries again", async () => {
    routes.GET["/api/orders/order-intake/{id}"] = () => withRefusal("Orders:StopWindowAmbiguous");
    renderWith(<IntakePage id="i-open" />);
    await waitForReview();
    await waitFor(() => expect(refusalRows()).toHaveLength(1));
    expect(ribbonButton("Orders:IntakeCreateOrder")).not.toBeNull();
  });
});
