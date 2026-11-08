import { renderHook } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";

/* Design 74 §5: `openRecord(type, id, { returnTo })` and `useCloseRecord`. The URL can be shared, so only internal paths count:
 * no open redirect. Risk class: Critical for the rejections (a shared link must never send the user to another host; the
 * sign-in session lives on this origin), High for the rest (every record opens through it). */

const navigateMock = vi.fn();
let search: Record<string, unknown> = {};
vi.mock("@tanstack/react-router", () => ({
  useNavigate: () => navigateMock,
  useLocation: () => ({ pathname: "/transport/orders/o1", search }),
}));
vi.mock("sonner", () => ({ toast: { error: vi.fn(), success: vi.fn() } }));
vi.mock("@/lib/i18n/LocalizationProvider", () => ({ useL: () => ({ t: (key: string) => key, culture: "en", isLoading: false }) }));
vi.mock("@/lib/abp/queries", () => ({
  useApplicationConfiguration: () => ({
    data: { currentTenant: { id: "t-1" }, auth: { grantedPolicies: { "Orders.TransportOrders": true } }, features: { values: { "Apps.Transport": "true", "Apps.Transport.Orders": "true" } } },
  }),
}));

import { isInternalPath, RETURN_TO_PARAM, useCloseRecord, useOpenRecord } from "@/lib/use-open-record";

type NavigateCall = { to: string; search: Record<string, unknown> | ((previous: Record<string, unknown>) => Record<string, unknown>); replace?: boolean };
const lastNavigate = () => navigateMock.mock.calls.at(-1)![0] as NavigateCall;
const searchOf = (call: NavigateCall, previous: Record<string, unknown>) => (typeof call.search === "function" ? call.search(previous) : call.search);

/* A real browser's URL parser decides these; jsdom's follows the same standard. */
const OUTSIDE: [string, unknown][] = [
  ["two slashes", "//evil.example/x"],
  ["slash backslash", "/\\evil.example/x"],
  ["slash tab slash", "/\t/evil.example/x"],
  ["slash newline slash", "/\n/evil.example/x"],
  ["slash carriage return slash", "/\r/evil.example"],
  ["backslash first", "\\\\evil.example"],
  ["an external URL", "https://evil.example/x"],
  ["a javascript URL", "javascript:alert(1)"],
  ["a relative path", "planboard"],
  ["an empty text", ""],
  ["null", null],
  ["undefined", undefined],
  ["a number", 5],
  ["an object", { pathname: "/planboard" }],
  ["an array", ["/planboard"]],
];

beforeEach(() => {
  navigateMock.mockReset();
  search = {};
});

describe("isInternalPath", () => {
  it.each(["/planboard", "/", "/transport/orders?filter=today", "/a//b", "/planboard#x"])("accepts %s", (path) => {
    expect(isInternalPath(path)).toBe(true);
  });

  it.each(OUTSIDE)("refuses %s", (_name, path) => {
    expect(isInternalPath(path)).toBe(false);
  });
});

describe("useOpenRecord with returnTo", () => {
  it("puts an internal path in the search, next to what the list had", () => {
    const { result } = renderHook(() => useOpenRecord());
    expect(result.current("TransportOrder", "o1", { returnTo: "/planboard" })).toBe(true);
    const call = lastNavigate();
    expect(call.to).toBe("/transport/orders/o1");
    expect(searchOf(call, { filter: "today" })).toEqual({ filter: "today", [RETURN_TO_PARAM]: "/planboard" });
  });

  it("omitSearch still works next to it", () => {
    const { result } = renderHook(() => useOpenRecord());
    result.current("TransportOrder", "o1", { returnTo: "/planboard", omitSearch: ["type"] });
    expect(searchOf(lastNavigate(), { type: "x", filter: "today" })).toEqual({ filter: "today", [RETURN_TO_PARAM]: "/planboard" });
  });

  it.each(OUTSIDE)("does not put %s in the URL", (_name, returnTo) => {
    const { result } = renderHook(() => useOpenRecord());
    result.current("TransportOrder", "o1", { returnTo: returnTo as string });
    expect(searchOf(lastNavigate(), { filter: "today" })).toEqual({ filter: "today" });
  });

  it("without returnTo a record opened from a record keeps the one the URL has", () => {
    const { result } = renderHook(() => useOpenRecord());
    result.current("TransportOrder", "o2");
    expect(searchOf(lastNavigate(), { [RETURN_TO_PARAM]: "/planboard" })).toEqual({ [RETURN_TO_PARAM]: "/planboard" });
  });
});

describe("useCloseRecord", () => {
  it("follows returnTo instead of going to the list", () => {
    search = { [RETURN_TO_PARAM]: "/planboard", filter: "today" };
    const { result } = renderHook(() => useCloseRecord());
    result.current("TransportOrder");
    expect(navigateMock).toHaveBeenCalledTimes(1);
    const call = lastNavigate();
    expect(call.to).toBe("/planboard");
    expect(searchOf(call, search)).toEqual({});
  });

  it("takes the query of the path along, not the record's search", () => {
    search = { [RETURN_TO_PARAM]: "/planboard?pane=stops", filter: "today" };
    const { result } = renderHook(() => useCloseRecord());
    result.current("TransportOrder");
    const call = lastNavigate();
    expect(call.to).toBe("/planboard");
    expect(searchOf(call, search)).toEqual({ pane: "stops" });
  });

  it("without returnTo closes to the list with its filters", () => {
    search = { filter: "today" };
    const { result } = renderHook(() => useCloseRecord());
    result.current("TransportOrder");
    const call = lastNavigate();
    expect(call.to).toBe("/transport/orders");
    expect(searchOf(call, search)).toEqual({ filter: "today" });
  });

  it.each(OUTSIDE)("a URL with %s closes to the list and strips the param", (_name, returnTo) => {
    search = { [RETURN_TO_PARAM]: returnTo, filter: "today" };
    const { result } = renderHook(() => useCloseRecord());
    result.current("TransportOrder");
    const call = lastNavigate();
    expect(call.to).toBe("/transport/orders");
    expect(searchOf(call, search)).toEqual({ filter: "today" });
  });

  it("uses the returnTo of the location at the moment of closing", () => {
    search = {};
    const { result, rerender } = renderHook(() => useCloseRecord());
    search = { [RETURN_TO_PARAM]: "/planboard" };
    rerender();
    result.current("TransportOrder");
    expect(lastNavigate().to).toBe("/planboard");
  });
});
