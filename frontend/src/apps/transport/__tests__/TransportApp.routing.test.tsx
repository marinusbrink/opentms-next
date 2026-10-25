import { render, screen } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";

/* The Transport app's index (design 61): the bare app path goes to the first list that is on, a list path without a
 * filter to the list's first saved filter (UI-PAGE-004), record paths render the record pages, and every view shows
 * the standard page states (UI-PAGE-005: PermissionDenied, FeatureUnavailable) until its screen is built. Risk class:
 * High (feature and permission gating of every Transport screen). */

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

let grants: Record<string, boolean> = {};
let features: Record<string, string> = {};
let loaded = true;
vi.mock("@/lib/abp/queries", () => ({
  useApplicationConfiguration: () => ({
    data: loaded ? { currentTenant: { id: "t-1" }, auth: { grantedPolicies: grants }, features: { values: features } } : undefined,
  }),
}));

import { TransportApp } from "@/apps/transport";
import { ORDER_FILTERS, TRIP_FILTERS, filterOf } from "@/apps/transport/filters";

const ALL_ON = {
  "Apps.Transport": "true",
  "Apps.Transport.Orders": "true",
  "Apps.Transport.Orders.Templates": "true",
  "Apps.Transport.Trips": "true",
  "Apps.Transport.Documents": "true",
};
const ALL_GRANTED = { "Orders.TransportOrders": true, "Orders.TransportOrderTemplates": true, "PlanningExecution.TransportTrips": true };
const ID = "3fa85f64-5717-4562-b3fc-2c963f66afa6";

beforeEach(() => {
  navigateMock.mockReset();
  features = { ...ALL_ON };
  grants = { ...ALL_GRANTED };
  loaded = true;
  search = {};
});

const go = (path: string, s: Record<string, unknown> = {}) => {
  pathname = path;
  search = s;
  return render(<TransportApp />);
};
const placeholder = () => document.querySelector<HTMLElement>('[data-slot="view-placeholder"]');

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
    expect(placeholder()).toHaveAttribute("data-filter", "drafts");
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
  ])("%s renders the record page with the id", (path, slot) => {
    go(path);
    const page = document.querySelector(`[data-slot="${slot}"]`);
    expect(page).not.toBeNull();
    expect(page).toHaveAttribute("data-record-id", ID);
  });

  it("the record pages carry their list's heading; the lists carry none (UI-PAGE-002)", () => {
    const { unmount } = go(`/transport/orders/${ID}`);
    expect(screen.getByRole("heading", { level: 1 })).toHaveTextContent("App:Transport.Orders");
    unmount();
    go("/transport/orders", { filter: "today" });
    expect(screen.queryByRole("heading")).toBeNull();
  });

  it("a trailing slash still finds the view", () => {
    go("/transport/documents/");
    expect(placeholder()).not.toBeNull();
    expect(navigateMock).not.toHaveBeenCalled();
  });
});

describe("TransportApp – page states (UI-PAGE-005, UI-PERM-004, UI-PERM-006)", () => {
  it.each([
    ["/transport/orders", "Orders.TransportOrders", { filter: "today" }],
    ["/transport/templates", "Orders.TransportOrderTemplates", {}],
    ["/transport/trips", "PlanningExecution.TransportTrips", { filter: "open" }],
    ["/transport/documents", "Orders.TransportOrders", {}],
    [`/transport/orders/${ID}`, "Orders.TransportOrders", {}],
    [`/transport/templates/${ID}`, "Orders.TransportOrderTemplates", {}],
    [`/transport/trips/${ID}`, "PlanningExecution.TransportTrips", {}],
  ])("%s shows PermissionDenied without %s and the page with it", (path, permission, s) => {
    grants = {};
    const { unmount } = go(path, s);
    expect(screen.getByRole("status")).toHaveTextContent("PermissionDenied");
    expect(placeholder()).toBeNull();
    unmount();

    grants = { [permission]: true };
    go(path, s);
    expect(screen.queryByRole("status")).toBeNull();
    expect(placeholder()).not.toBeNull();
  });

  it.each([
    ["/transport/orders", "Apps.Transport.Orders", { filter: "today" }],
    ["/transport/templates", "Apps.Transport.Orders.Templates", {}],
    ["/transport/trips", "Apps.Transport.Trips", { filter: "today" }],
    ["/transport/documents", "Apps.Transport.Documents", {}],
    [`/transport/orders/${ID}`, "Apps.Transport.Orders", {}],
    [`/transport/trips/${ID}`, "Apps.Transport.Trips", {}],
  ])("%s shows FeatureUnavailable when %s is off, even with the permission", (path, feature, s) => {
    features = { ...ALL_ON, [feature]: "false" };
    go(path, s);
    expect(screen.getByRole("status")).toHaveTextContent("FeatureUnavailable");
  });

  it("Templates follow Orders through the chain", () => {
    features = { ...ALL_ON, "Apps.Transport.Orders": "false" };
    go("/transport/templates");
    expect(screen.getByRole("status")).toHaveTextContent("FeatureUnavailable");
  });

  it("the feature gate waits for the configuration; the permission gate holds meanwhile", () => {
    loaded = false;
    go("/transport/documents");
    expect(screen.getByRole("status")).toHaveTextContent("PermissionDenied");
  });

  it("the stub text shows in the content slot until the screen is built", () => {
    go("/transport/documents");
    expect(screen.getByText("Shell:EmptyAppTitle")).toBeInTheDocument();
    expect(screen.getByText("Shell:EmptyAppText")).toBeInTheDocument();
  });
});
