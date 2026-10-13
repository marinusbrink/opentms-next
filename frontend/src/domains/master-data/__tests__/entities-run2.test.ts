import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { renderHook, waitFor } from "@testing-library/react";
import { createElement, type ReactNode } from "react";
import { beforeEach, describe, expect, it, vi } from "vitest";

/* The run-2 domain hooks and helpers (design 43): the roles flags round trip, the lookup pages the
 * EntityLookups call (the employer page narrowed by roles), the archive, unarchive and anonymise
 * endpoints. Risk class: Medium — the request shapes decide what the server filters and mutates.
 * Tenant isolation is not decided here: every call goes through the typed client whose middleware
 * sends the __tenant header; the negative tests live on the server (PostgreSQL tests of design 43). */

vi.mock("@/lib/api/client", () => ({
  apiClient: { GET: vi.fn(), POST: vi.fn(), PUT: vi.fn(), DELETE: vi.fn() },
}));

import { apiClient } from "@/lib/api/client";
import {
  EMPLOYER_ROLES,
  RELATION_ROLES,
  asRoles,
  contactPersonRoleKey,
  listToRoles,
  qualificationKindKey,
  relationLookupPage,
  relationRoleKeys,
  rolesToList,
  toRelationOption,
  toUserOption,
  useAnonymiseDriver,
  useArchiveDriver,
  useArchiveRelation,
  useUnarchiveDriver,
  useUnarchiveRelation,
  userLookupPage,
} from "@/domains/master-data/entities";

const GET = vi.mocked(apiClient.GET);
const POST = vi.mocked(apiClient.POST);

function wrapper({ children }: { children: ReactNode }) {
  const client = new QueryClient({ defaultOptions: { mutations: { retry: false } } });
  return createElement(QueryClientProvider, { client }, children);
}

/* The generated client's response type is a discriminated FetchResponse; the mocks hand over the loose shape the hooks read. */
const ok = (data: unknown) => ({ data, error: undefined, response: new Response() }) as never;
const failed = (error: unknown) => ({ data: undefined, error, response: new Response() }) as never;

beforeEach(() => {
  GET.mockReset();
  POST.mockReset();
});

describe("relation roles flags", () => {
  it("lists the five flags in UI order and the employer roles are Carrier | OwnCompany", () => {
    expect(RELATION_ROLES.map((r) => r.value)).toEqual([1, 2, 4, 8, 16]);
    expect(EMPLOYER_ROLES).toBe(18);
  });

  it("round-trips a flags value through the checkbox list", () => {
    expect(rolesToList(undefined)).toEqual([]);
    expect(rolesToList(0)).toEqual([]);
    expect(rolesToList(3)).toEqual(["1", "2"]);
    expect(rolesToList(18)).toEqual(["2", "16"]);
    expect(listToRoles([])).toBe(0);
    expect(listToRoles(["1", "16"])).toBe(17);
    expect(listToRoles(rolesToList(31))).toBe(31);
  });

  it("names the roles a flags value carries", () => {
    expect(relationRoleKeys(18)).toEqual(["MasterData:Role.Carrier", "MasterData:Role.OwnCompany"]);
    expect(relationRoleKeys(undefined)).toEqual([]);
    expect(asRoles(0)).toBeUndefined();
    expect(asRoles(undefined)).toBeUndefined();
    expect(asRoles(18)).toBe(18);
  });

  it("falls back to Other for unknown contact-person roles and qualification kinds", () => {
    expect(contactPersonRoleKey(2)).toBe("MasterData:ContactPersonRole.Invoicing");
    expect(contactPersonRoleKey(undefined)).toBe("MasterData:ContactPersonRole.Other");
    expect(qualificationKindKey(3)).toBe("MasterData:QualificationKind.DrivingLicenceCE");
    expect(qualificationKindKey(undefined)).toBe("MasterData:QualificationKind.Other");
  });
});

describe("relationLookupPage", () => {
  it("asks the lookup page with the term, the window and the roles filter, and maps the options", async () => {
    GET.mockResolvedValueOnce(ok({ items: [{ id: "r1", code: "ACME", name: "Acme", city: "Delft", countryCode: "NL", roles: 2 }], totalCount: 1 }));
    const page = relationLookupPage(EMPLOYER_ROLES);
    const result = await page({ term: "ac", skip: 25, take: 25 });
    expect(GET).toHaveBeenCalledWith("/api/master-data/relation/lookup-page", { params: { query: { term: "ac", skip: 25, take: 25, roles: 18 } } });
    expect(result).toEqual({ items: [{ id: "r1", code: "ACME", name: "Acme", city: "Delft", countryCode: "NL", roles: 2 }], totalCount: 1 });
  });

  it("sends no term and no roles when both are empty", async () => {
    GET.mockResolvedValueOnce(ok({ items: [], totalCount: 0 }));
    await relationLookupPage()({ term: "", skip: 0, take: 25 });
    expect(GET).toHaveBeenCalledWith("/api/master-data/relation/lookup-page", { params: { query: { term: undefined, skip: 0, take: 25, roles: undefined } } });
  });

  it("throws the API error", async () => {
    GET.mockResolvedValueOnce(failed({ error: { message: "nope" } }));
    await expect(relationLookupPage()({ term: "", skip: 0, take: 25 })).rejects.toEqual({ error: { message: "nope" } });
  });

  it("maps null fields of a lookup row to empty strings and nulls", () => {
    expect(toRelationOption({ id: "r1" })).toEqual({ id: "r1", code: "", name: "", city: null, countryCode: null, roles: 0 });
    expect(toUserOption({ id: "u1", userName: "jan" })).toEqual({ id: "u1", name: "", userName: "jan", email: null });
  });
});

describe("userLookupPage", () => {
  it("asks the driver user-lookup page and maps the users", async () => {
    GET.mockResolvedValueOnce(ok({ items: [{ id: "u1", name: "Jan Jansen", userName: "jan", email: "jan@example.com" }], totalCount: 1 }));
    const result = await userLookupPage({ term: "ja", skip: 0, take: 25 });
    expect(GET).toHaveBeenCalledWith("/api/master-data/driver/user-lookup-page", { params: { query: { term: "ja", skip: 0, take: 25 } } });
    expect(result).toEqual({ items: [{ id: "u1", name: "Jan Jansen", userName: "jan", email: "jan@example.com" }], totalCount: 1 });
  });
});

describe("archive, unarchive and anonymise mutations", () => {
  it.each([
    ["useArchiveRelation", useArchiveRelation, "/api/master-data/relation/{id}/archive"],
    ["useUnarchiveRelation", useUnarchiveRelation, "/api/master-data/relation/{id}/unarchive"],
    ["useArchiveDriver", useArchiveDriver, "/api/master-data/driver/{id}/archive"],
    ["useUnarchiveDriver", useUnarchiveDriver, "/api/master-data/driver/{id}/unarchive"],
    ["useAnonymiseDriver", useAnonymiseDriver, "/api/master-data/driver/{id}/anonymise"],
  ])("%s posts to %s with the id in the path", async (_name, useHook, path) => {
    POST.mockResolvedValueOnce(ok(undefined));
    const { result } = renderHook(() => useHook(), { wrapper });
    await result.current.mutateAsync("abc");
    expect(POST).toHaveBeenCalledWith(path, { params: { path: { id: "abc" } } });
  });

  it("rejects with the API error so the caller can show it", async () => {
    POST.mockResolvedValueOnce(failed({ error: { message: "Already anonymised" } }));
    const { result } = renderHook(() => useAnonymiseDriver(), { wrapper });
    await expect(result.current.mutateAsync("abc")).rejects.toEqual({ error: { message: "Already anonymised" } });
    await waitFor(() => expect(result.current.isError).toBe(true));
  });
});
