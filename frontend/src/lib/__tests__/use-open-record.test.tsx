import { renderHook } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";

/* useOpenRecord (UI-DLG-007 as design 61 applies it: a record is a page under its list): the record routes, the
 * navigation that keeps the list's filters, the permission and feature gates with their toasts (UI-PERM-004,
 * UI-PERM-006), closing back to the list, and the load state of a record page. Risk class: High (every record of the
 * Transport run opens through it; a gate that leaks shows a record the user may not see). */

const navigateMock = vi.fn();
let pathname = "/transport/orders";
vi.mock("@tanstack/react-router", () => ({
  useNavigate: () => navigateMock,
  useLocation: () => ({ pathname, search: { filter: "today" } }),
}));

const toastError = vi.fn();
vi.mock("sonner", () => ({ toast: { error: (...args: unknown[]) => toastError(...args), success: vi.fn() } }));

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

import {
  isNotFoundError,
  parseRecordPath,
  RECORD_ROUTES,
  recordLoadState,
  recordPath,
  useCloseRecord,
  useCurrentRecord,
  useOpenRecord,
} from "@/lib/use-open-record";

const TRANSPORT_ON = {
  "Apps.Transport": "true",
  "Apps.Transport.Orders": "true",
  "Apps.Transport.Orders.Templates": "true",
  "Apps.Transport.Trips": "true",
  "Apps.Transport.Documents": "true",
};
const ID = "3fa85f64-5717-4562-b3fc-2c963f66afa6";

beforeEach(() => {
  navigateMock.mockReset();
  toastError.mockReset();
  grants = { "Orders.TransportOrders": true, "Orders.TransportOrderTemplates": true, "PlanningExecution.TransportTrips": true };
  features = { ...TRANSPORT_ON };
  loaded = true;
  pathname = "/transport/orders";
});

describe("record routes", () => {
  it("map every record type to its list, permission and node (design 61)", () => {
    expect(RECORD_ROUTES.TransportOrder).toEqual({ listPath: "/transport/orders", permission: "Orders.TransportOrders", feature: "Apps.Transport.Orders" });
    expect(RECORD_ROUTES.TransportOrderTemplate).toEqual({ listPath: "/transport/templates", permission: "Orders.TransportOrderTemplates", feature: "Apps.Transport.Orders.Templates" });
    expect(RECORD_ROUTES.TransportTrip).toEqual({ listPath: "/transport/trips", permission: "PlanningExecution.TransportTrips", feature: "Apps.Transport.Trips" });
  });

  it("recordPath builds the page route and parseRecordPath reads it back", () => {
    expect(recordPath("TransportOrder", ID)).toBe(`/transport/orders/${ID}`);
    expect(recordPath("TransportTrip", "a/b")).toBe("/transport/trips/a%2Fb");
    expect(parseRecordPath(`/transport/orders/${ID}`)).toEqual({ type: "TransportOrder", id: ID });
    expect(parseRecordPath(`/transport/templates/${ID}/`)).toEqual({ type: "TransportOrderTemplate", id: ID });
    expect(parseRecordPath("/transport/trips/a%2Fb")).toEqual({ type: "TransportTrip", id: "a/b" });
  });

  it("parseRecordPath is undefined for lists, deeper paths and other apps", () => {
    expect(parseRecordPath("/transport/orders")).toBeUndefined();
    expect(parseRecordPath("/transport/orders/")).toBeUndefined();
    expect(parseRecordPath(`/transport/orders/${ID}/lines`)).toBeUndefined();
    expect(parseRecordPath("/transport/documents/x")).toBeUndefined();
    expect(parseRecordPath(`/master-data/relations/${ID}`)).toBeUndefined();
  });
});

describe("useOpenRecord", () => {
  it("navigates to the record page and keeps the list's search (the filters travel with the record)", () => {
    const { result } = renderHook(() => useOpenRecord());
    expect(result.current("TransportOrder", ID)).toBe(true);
    expect(navigateMock).toHaveBeenCalledTimes(1);
    const call = navigateMock.mock.calls[0][0] as { to: string; search: (prev: Record<string, unknown>) => Record<string, unknown>; replace?: boolean };
    expect(call.to).toBe(`/transport/orders/${ID}`);
    expect(call.search({ filter: "today", page: 2 })).toEqual({ filter: "today", page: 2 });
    expect(call.replace).toBeUndefined();
    expect(toastError).not.toHaveBeenCalled();
  });

  it("replaces the history entry when asked (a New dialog handing over to the page)", () => {
    const { result } = renderHook(() => useOpenRecord());
    result.current("TransportTrip", ID, { replace: true });
    expect(navigateMock.mock.calls[0][0]).toMatchObject({ to: `/transport/trips/${ID}`, replace: true });
  });

  it("does not open a record the user may not view: a PermissionDenied toast, no navigation (UI-PERM-004)", () => {
    grants = {};
    const { result } = renderHook(() => useOpenRecord());
    expect(result.current("TransportOrder", ID)).toBe(false);
    expect(navigateMock).not.toHaveBeenCalled();
    expect(toastError).toHaveBeenCalledWith("PermissionDenied");
  });

  it("does not open a record whose node is off: a FeatureUnavailable toast, no navigation (UI-PERM-006)", () => {
    features = { ...TRANSPORT_ON, "Apps.Transport.Trips": "false" };
    const { result } = renderHook(() => useOpenRecord());
    expect(result.current("TransportTrip", ID)).toBe(false);
    expect(result.current("TransportOrder", ID)).toBe(true);
    expect(toastError).toHaveBeenCalledTimes(1);
    expect(toastError).toHaveBeenCalledWith("FeatureUnavailable");
  });

  it("Templates follow the Orders node through the feature chain", () => {
    features = { ...TRANSPORT_ON, "Apps.Transport.Orders": "false" };
    const { result } = renderHook(() => useOpenRecord());
    expect(result.current("TransportOrderTemplate", ID)).toBe(false);
    expect(toastError).toHaveBeenCalledWith("FeatureUnavailable");
  });

  it("checks the permission before the feature", () => {
    grants = {};
    features = {};
    const { result } = renderHook(() => useOpenRecord());
    result.current("TransportOrder", ID);
    expect(toastError).toHaveBeenCalledWith("PermissionDenied");
  });

  it("does not gate on features while the configuration is still loading (the shell gates the route)", () => {
    loaded = false;
    const { result } = renderHook(() => useOpenRecord());
    /* Without configuration nothing is granted either, so the permission gate speaks first. */
    expect(result.current("TransportOrder", ID)).toBe(false);
    expect(toastError).toHaveBeenCalledWith("PermissionDenied");
  });
});

describe("useCloseRecord and useCurrentRecord", () => {
  it("closing goes back to the list, keeping the search", () => {
    const { result } = renderHook(() => useCloseRecord());
    result.current("TransportOrder");
    const call = navigateMock.mock.calls[0][0] as { to: string; search: (prev: Record<string, unknown>) => Record<string, unknown> };
    expect(call.to).toBe("/transport/orders");
    expect(call.search({ filter: "open" })).toEqual({ filter: "open" });
  });

  it("the current record follows the location", () => {
    pathname = `/transport/trips/${ID}`;
    expect(renderHook(() => useCurrentRecord()).result.current).toEqual({ type: "TransportTrip", id: ID });
    pathname = "/transport/trips";
    expect(renderHook(() => useCurrentRecord()).result.current).toBeUndefined();
  });
});

describe("recordLoadState", () => {
  it("is loading, then ready with data", () => {
    expect(recordLoadState({ isLoading: true, error: undefined, data: undefined })).toBe("loading");
    expect(recordLoadState({ isLoading: false, error: undefined, data: { id: ID } })).toBe("ready");
  });

  it("is not-found for ABP's EntityNotFound, a 404, or a finished query without data", () => {
    expect(recordLoadState({ isLoading: false, error: { error: { code: "Volo.Abp.Domain.Entities.EntityNotFoundException", message: "x" } }, data: undefined })).toBe("not-found");
    expect(recordLoadState({ isLoading: false, error: { status: 404 }, data: undefined })).toBe("not-found");
    expect(recordLoadState({ isLoading: false, error: { response: { status: 404 } }, data: undefined })).toBe("not-found");
    expect(recordLoadState({ isLoading: false, error: undefined, data: undefined })).toBe("not-found");
  });

  it("is error for any other failure", () => {
    expect(recordLoadState({ isLoading: false, error: { error: { code: "Orders:StopLockedByTrip", message: "x" } }, data: undefined })).toBe("error");
    expect(recordLoadState({ isLoading: false, error: new Error("network"), data: undefined })).toBe("error");
    expect(isNotFoundError("nope")).toBe(false);
    expect(isNotFoundError(null)).toBe(false);
  });
});
