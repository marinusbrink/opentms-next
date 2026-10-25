import { render } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";

/* The Transport app's index (design 61): the bare app path goes to the first list that is on, a list path without a
 * filter to the list's first saved filter (UI-PAGE-004), record paths render the record pages with the id, and the
 * filter travels to the list view. The views and pages are stubs here; each has its own tests (orders-view, trips-view,
 * order-editor, trip-editor, documents-view, templates-view). Risk class: High (routing of every Transport screen). */

const navigateMock = vi.fn();
let pathname = "/transport";
let search: Record<string, unknown> = {};
vi.mock("@tanstack/react-router", () => ({
  useLocation: () => ({ pathname, search }),
  useNavigate: () => navigateMock,
}));

vi.mock("@/lib/i18n/LocalizationProvider", () => ({
  useL: () => ({ t: (key: string) => key, culture: "en", isLoading: false }),
}));

let features: Record<string, string> = {};
vi.mock("@/lib/abp/queries", () => ({
  useApplicationConfiguration: () => ({ data: { currentTenant: { id: "t-1" }, auth: { grantedPolicies: {} }, features: { values: features } } }),
}));

vi.mock("@/apps/transport/views/OrdersView", () => ({ OrdersView: ({ filter }: { filter?: string }) => <div data-slot="orders-view" data-filter={filter} /> }));
vi.mock("@/apps/transport/views/TripsView", () => ({ TripsView: ({ filter }: { filter?: string }) => <div data-slot="trips-view" data-filter={filter} /> }));
vi.mock("@/apps/transport/views/TemplatesView", () => ({ TemplatesView: () => <div data-slot="templates-view" /> }));
vi.mock("@/apps/transport/views/DocumentsView", () => ({ DocumentsView: () => <div data-slot="documents-view" /> }));
vi.mock("@/apps/transport/pages/OrderPage", () => ({ OrderPage: ({ id }: { id: string }) => <div data-slot="order-page" data-record-id={id} /> }));
vi.mock("@/apps/transport/pages/TemplatePage", () => ({ TemplatePage: ({ id }: { id: string }) => <div data-slot="template-page" data-record-id={id} /> }));
vi.mock("@/apps/transport/pages/TripPage", () => ({ TripPage: ({ id }: { id: string }) => <div data-slot="trip-page" data-record-id={id} /> }));

import { TransportApp } from "@/apps/transport";
import { ORDER_FILTERS, TRIP_FILTERS, filterOf } from "@/apps/transport/filters";

const ALL_ON = {
  "Apps.Transport": "true",
  "Apps.Transport.Orders": "true",
  "Apps.Transport.Orders.Templates": "true",
  "Apps.Transport.Trips": "true",
  "Apps.Transport.Documents": "true",
};
const ID = "3fa85f64-5717-4562-b3fc-2c963f66afa6";

beforeEach(() => {
  navigateMock.mockReset();
  features = { ...ALL_ON };
  search = {};
});

const go = (path: string, s: Record<string, unknown> = {}) => {
  pathname = path;
  search = s;
  return render(<TransportApp />);
};
const slot = (name: string) => document.querySelector<HTMLElement>(`[data-slot="${name}"]`);

describe("filters", () => {
  it("come from apps.config in nav order, the first being the default", () => {
    expect(ORDER_FILTERS).toEqual(["today", "tomorrow", "this-week", "open", "drafts", "all"]);
    expect(TRIP_FILTERS).toEqual(["today", "this-week", "open", "all"]);
    expect(filterOf({ filter: "drafts" }, "/transport/orders")).toBe("drafts");
    expect(filterOf({ filter: "drafts" }, "/transport/trips")).toBeUndefined();
    expect(filterOf({ filter: 3 }, "/transport/orders")).toBeUndefined();
    expect(filterOf(undefined, "/transport/orders")).toBeUndefined();
  });
});

describe("TransportApp – redirects", () => {
  it("the bare app path goes to the first list that is on", () => {
    go("/transport");
    expect(navigateMock).toHaveBeenCalledWith({ to: "/transport/orders", replace: true });
  });

  it("the bare app path skips a list that is off and does nothing when the app is off", () => {
    features = { ...ALL_ON, "Apps.Transport.Orders": "false" };
    go("/transport/");
    expect(navigateMock).toHaveBeenCalledWith({ to: "/transport/trips", replace: true });

    navigateMock.mockReset();
    features = { ...ALL_ON, "Apps.Transport": "false" };
    go("/transport");
    expect(navigateMock).not.toHaveBeenCalled();
  });

  it("a list path without a filter goes to the list's first saved filter (UI-PAGE-004)", () => {
    go("/transport/orders");
    expect(navigateMock).toHaveBeenCalledWith({ to: "/transport/orders", search: { filter: "today" }, replace: true });
    navigateMock.mockReset();
    go("/transport/trips", {});
    expect(navigateMock).toHaveBeenCalledWith({ to: "/transport/trips", search: { filter: "today" }, replace: true });
  });

  it("an unknown filter is treated as absent", () => {
    go("/transport/orders", { filter: "yesterday" });
    expect(navigateMock).toHaveBeenCalledWith({ to: "/transport/orders", search: { filter: "today" }, replace: true });
  });

  it("a list path with a known filter does not navigate and hands the filter to the view", () => {
    go("/transport/orders", { filter: "drafts" });
    expect(navigateMock).not.toHaveBeenCalled();
    expect(slot("orders-view")).toHaveAttribute("data-filter", "drafts");
    go("/transport/trips", { filter: "open" });
    expect(slot("trips-view")).toHaveAttribute("data-filter", "open");
  });

  it("templates, documents and record pages never redirect", () => {
    for (const path of ["/transport/templates", "/transport/documents", `/transport/orders/${ID}`, `/transport/trips/${ID}`]) {
      const { unmount } = go(path);
      expect(navigateMock, path).not.toHaveBeenCalled();
      unmount();
    }
  });
});

describe("TransportApp – routing by pathname", () => {
  it.each([
    [`/transport/orders/${ID}`, "order-page"],
    [`/transport/templates/${ID}`, "template-page"],
    [`/transport/trips/${ID}`, "trip-page"],
  ])("%s renders the record page with the id", (path, name) => {
    go(path);
    expect(slot(name)).toHaveAttribute("data-record-id", ID);
  });

  it.each([
    ["/transport/templates", "templates-view"],
    ["/transport/documents/", "documents-view"],
    ["/transport/orders", "orders-view"],
    ["/transport/trips", "trips-view"],
  ])("%s renders %s (a trailing slash still finds the view)", (path, name) => {
    go(path, { filter: "today" });
    expect(slot(name)).not.toBeNull();
  });
});
