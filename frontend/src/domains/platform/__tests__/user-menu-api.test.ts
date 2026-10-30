import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { act, renderHook, waitFor } from "@testing-library/react";
import { createElement, type ReactNode } from "react";
import { beforeEach, describe, expect, it, vi } from "vitest";

import type { GridRequest } from "@/components/ui/opentms-grid";

/* The API calls behind the user menu (design 63) and "Log in as" (moved to /api/platform/impersonation/tenants/...):
 * routes, bodies and query mapping. Risk class: Critical — authentication/authorization (server side enforces);
 * this client part: Medium (contract tests on the request shape). */

vi.mock("@/lib/api/client", () => ({
  apiClient: { GET: vi.fn(), POST: vi.fn(), PUT: vi.fn(), DELETE: vi.fn() },
}));

import { apiClient } from "@/lib/api/client";
import {
  fetchImpersonationTenants,
  fetchTenantUsers,
  useReturnFromImpersonation,
  useStartImpersonation,
} from "@/domains/platform/impersonation";
import { useForgetAccount, useMyAccounts, useSwitchAccount } from "@/domains/platform/my-accounts";
import { useChangeMyPassword, useUpdateMyProfile } from "@/domains/platform/my-profile";

const GET = vi.mocked(apiClient.GET);
const POST = vi.mocked(apiClient.POST);
const PUT = vi.mocked(apiClient.PUT);
const DELETE = vi.mocked(apiClient.DELETE);

function request(overrides: Partial<GridRequest> = {}): GridRequest {
  return {
    startRow: 0,
    endRow: 50,
    sortModels: [],
    columnFilters: {},
    wildcardSearch: null,
    rowGroupCols: [],
    groupKeys: [],
    ...overrides,
  };
}

function wrapper({ children }: { children: ReactNode }) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } });
  return createElement(QueryClientProvider, { client }, children);
}

const ok = (data: unknown) => ({ data, error: undefined, response: new Response(null, { status: 200 }) }) as never;

beforeEach(() => {
  GET.mockReset();
  POST.mockReset();
  PUT.mockReset();
  DELETE.mockReset();
});

describe("impersonation routes", () => {
  it("lists tenants through /api/platform/impersonation/tenants with page, search and sort", async () => {
    GET.mockResolvedValue(ok({ rows: [{ id: "t-1", name: "acme" }], totalCount: "3", filteredCount: 1 }));
    const result = await fetchImpersonationTenants(
      request({ startRow: 50, endRow: 100, wildcardSearch: "ac", sortModels: [{ colId: "name", sort: "desc" }] }),
    );
    expect(GET).toHaveBeenCalledWith("/api/platform/impersonation/tenants", {
      params: { query: { startRow: 50, endRow: 100, wildcardSearch: "ac", sortBy: "name", sortDir: "desc" } },
    });
    expect(result).toEqual({ rows: [{ id: "t-1", name: "acme" }], totalCount: 3, filteredCount: 1 });
  });

  it("lists a tenant's users through /api/platform/impersonation/tenants/{id}/users", async () => {
    GET.mockResolvedValue(ok({ rows: [], totalCount: 0, filteredCount: 0 }));
    await fetchTenantUsers("t-acme", request());
    expect(GET).toHaveBeenCalledWith("/api/platform/impersonation/tenants/{id}/users", {
      params: { path: { id: "t-acme" }, query: { startRow: 0, endRow: 50, wildcardSearch: undefined, sortBy: undefined, sortDir: undefined } },
    });
  });

  it("throws the API error of a tenant list", async () => {
    GET.mockResolvedValue({ data: undefined, error: { error: { message: "Forbidden" } } } as never);
    await expect(fetchImpersonationTenants(request())).rejects.toEqual({ error: { message: "Forbidden" } });
  });

  it("starts and ends a session switch through the new routes", async () => {
    POST.mockResolvedValue(ok({ url: "https://auth.example.test/i" }));
    const start = renderHook(() => useStartImpersonation(), { wrapper });
    let url: string | undefined;
    await act(async () => {
      url = await start.result.current.mutateAsync({ tenantId: "t-acme", userId: "u-1" });
    });
    expect(POST).toHaveBeenCalledWith("/api/platform/impersonation/tenants/{id}/users/{userId}", {
      params: { path: { id: "t-acme", userId: "u-1" } },
    });
    expect(url).toBe("https://auth.example.test/i");

    const back = renderHook(() => useReturnFromImpersonation(), { wrapper });
    await act(async () => {
      await back.result.current.mutateAsync();
    });
    expect(POST).toHaveBeenLastCalledWith("/api/platform/impersonation/return");
  });
});

describe("my accounts", () => {
  it("reads the other accounts", async () => {
    GET.mockResolvedValue(ok([{ tenantId: "t-1" }]));
    const { result } = renderHook(() => useMyAccounts(), { wrapper });
    await waitFor(() => expect(result.current.data).toEqual([{ tenantId: "t-1" }]));
    expect(GET).toHaveBeenCalledWith("/api/platform/my-accounts");
  });

  it("switches without a password by default and returns the auth-server URL", async () => {
    POST.mockResolvedValue(ok({ url: "https://auth.example.test/s" }));
    const { result } = renderHook(() => useSwitchAccount(), { wrapper });
    let url: string | undefined;
    await act(async () => {
      url = await result.current.mutateAsync({ tenantId: null });
    });
    expect(POST).toHaveBeenCalledWith("/api/platform/my-accounts/switch", {
      body: { tenantId: null, password: null, rememberPassword: false },
    });
    expect(url).toBe("https://auth.example.test/s");
  });

  it("switches with password and remember", async () => {
    POST.mockResolvedValue(ok({ url: "u" }));
    const { result } = renderHook(() => useSwitchAccount(), { wrapper });
    await act(async () => {
      await result.current.mutateAsync({ tenantId: "t-1", password: "pw", rememberPassword: true });
    });
    expect(POST).toHaveBeenCalledWith("/api/platform/my-accounts/switch", {
      body: { tenantId: "t-1", password: "pw", rememberPassword: true },
    });
  });

  it("throws the switch error so the menu can read its code", async () => {
    const error = { error: { code: "OpenTms.Platform:AccountPasswordRequired" } };
    POST.mockResolvedValue({ data: undefined, error } as never);
    const { result } = renderHook(() => useSwitchAccount(), { wrapper });
    await act(async () => {
      await expect(result.current.mutateAsync({ tenantId: "t-1" })).rejects.toBe(error);
    });
  });

  it("forgets a remembered tenant account by tenantId, the host account without one", async () => {
    DELETE.mockResolvedValue(ok(undefined));
    const { result } = renderHook(() => useForgetAccount(), { wrapper });
    await act(async () => {
      await result.current.mutateAsync("t-1");
    });
    expect(DELETE).toHaveBeenLastCalledWith("/api/platform/my-accounts/remembered", { params: { query: { tenantId: "t-1" } } });
    await act(async () => {
      await result.current.mutateAsync(null);
    });
    expect(DELETE).toHaveBeenLastCalledWith("/api/platform/my-accounts/remembered", { params: { query: { tenantId: undefined } } });
  });
});

describe("my profile", () => {
  it("saves the profile with PUT /api/platform/my-profile", async () => {
    const dto = { name: "Jan", surname: null, phoneNumber: null, language: null };
    PUT.mockResolvedValue(ok({ ...dto, email: "jan@example.test" }));
    const { result } = renderHook(() => useUpdateMyProfile(), { wrapper });
    let saved: unknown;
    await act(async () => {
      saved = await result.current.mutateAsync(dto);
    });
    expect(PUT).toHaveBeenCalledWith("/api/platform/my-profile", { body: dto });
    expect(saved).toEqual({ ...dto, email: "jan@example.test" });
  });

  it("changes the password through ABP's account endpoint", async () => {
    POST.mockResolvedValue(ok(undefined));
    const { result } = renderHook(() => useChangeMyPassword(), { wrapper });
    await act(async () => {
      await result.current.mutateAsync({ currentPassword: "old", newPassword: "new" });
    });
    expect(POST).toHaveBeenCalledWith("/api/account/my-profile/change-password", {
      body: { currentPassword: "old", newPassword: "new" },
    });
  });
});
