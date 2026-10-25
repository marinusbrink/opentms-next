import type { Mock } from "vitest";
import { vi } from "vitest";

import { apiClient } from "@/lib/api/client";
import { ok } from "./transport-test-mocks";

/* The generated client as a path dispatcher: a test registers the responses per path; anything else answers an empty
 * grid page. Not a test file: vitest's include pattern matches *.test.* only. */

export type ClientCall = (path: string, init?: { params?: { query?: Record<string, unknown>; path?: Record<string, unknown> }; body?: unknown; parseAs?: string }) => Promise<unknown>;
export const GET = apiClient.GET as unknown as Mock<ClientCall>;
export const POST = apiClient.POST as unknown as Mock<ClientCall>;
export const PUT = apiClient.PUT as unknown as Mock<ClientCall>;
export const DELETE = apiClient.DELETE as unknown as Mock<ClientCall>;

export type Handler = (init?: Parameters<ClientCall>[1]) => unknown;
export const routes: { GET: Record<string, Handler>; POST: Record<string, Handler>; PUT: Record<string, Handler>; DELETE: Record<string, Handler> } = { GET: {}, POST: {}, PUT: {}, DELETE: {} };

const EMPTY_PAGE = { rows: [], totalCount: 0, filteredCount: 0 };

export function resetClient() {
  routes.GET = {};
  routes.POST = {};
  routes.PUT = {};
  routes.DELETE = {};
  for (const [method, mock] of [["GET", GET], ["POST", POST], ["PUT", PUT], ["DELETE", DELETE]] as const) {
    mock.mockReset().mockImplementation(async (path, init) => {
      const handler = routes[method][path];
      if (handler) {
        const result = handler(init);
        return result && typeof result === "object" && "response" in (result as object) ? result : ok(result);
      }
      return ok(method === "GET" ? (path.endsWith("/lookup") || path.endsWith("/all") || path.endsWith("/states") ? [] : EMPTY_PAGE) : undefined);
    });
  }
}

export const calls = (mock: Mock<ClientCall>, path: string) => mock.mock.calls.filter(([p]) => p === path);
export const lastCall = (mock: Mock<ClientCall>, path: string) => calls(mock, path).at(-1)?.[1];

/** The common module mocks every Transport screen test needs; call at module scope before importing the screen. */
export function mockTransportModules() {
  vi.mock("@/lib/api/client", () => ({ apiClient: { GET: vi.fn(), POST: vi.fn(), PUT: vi.fn(), DELETE: vi.fn() } }));
}
