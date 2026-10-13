import { beforeEach, describe, expect, it, vi } from "vitest";

/* The address-lookup client of design 44: one GET on the Integrations module with the country code,
 * the postal code and the optional house number as query parameters; the DTO when the provider found
 * something, null when it did not, the client's error thrown. Risk class: Medium — the request shape
 * decides what the server (and through it the provider) answers; the UI treats a throw as silent.
 * Tenant isolation is not decided here: the lookup reads public data, not tenant data (design 44,
 * "Caching and privacy"); the call goes through the typed client whose middleware sends the __tenant
 * header, and the feature / permission gate is enforced on the server (its own tests). */

vi.mock("@/lib/api/client", () => ({
  apiClient: { GET: vi.fn(), POST: vi.fn(), PUT: vi.fn(), DELETE: vi.fn() },
}));

import { apiClient } from "@/lib/api/client";
import {
  ADDRESS_LOOKUP_FEATURE,
  ADDRESS_LOOKUP_PERMISSION,
  fetchAddressSuggestions,
  lookupAddress,
} from "@/domains/integrations/address-lookup";

const GET = vi.mocked(apiClient.GET);

const ok = (data: unknown) => ({ data, error: undefined, response: new Response() }) as never;
const failed = (error: unknown) => ({ data: undefined, error, response: new Response() }) as never;

const PATH = "/api/integrations/address-lookup";
const FOUND = { found: true, street: "Dam", houseNumber: "1", city: "Amsterdam", latitude: 52.3731, longitude: 4.8932, provider: "PDOK" };

beforeEach(() => {
  GET.mockReset();
});

describe("address lookup constants", () => {
  it("names the feature and the permission of design 44 (the same key on both sides)", () => {
    expect(ADDRESS_LOOKUP_FEATURE).toBe("Integrations.AddressLookup");
    expect(ADDRESS_LOOKUP_PERMISSION).toBe("Integrations.AddressLookup");
  });
});

describe("lookupAddress", () => {
  it("asks GET /api/integrations/address-lookup with the country, postal code and house number as query parameters and hands the DTO back when found", async () => {
    GET.mockResolvedValue(ok(FOUND));
    const result = await lookupAddress("NL", "1012AB", "1");
    expect(GET).toHaveBeenCalledTimes(1);
    expect(GET).toHaveBeenCalledWith(PATH, { params: { query: { countryCode: "NL", postalCode: "1012AB", houseNumber: "1" } } });
    expect(result).toEqual(FOUND);
  });

  it("leaves the house number out of the query when there is none (undefined, never the text 'null')", async () => {
    GET.mockResolvedValue(ok(FOUND));
    await lookupAddress("DE", "10115", null);
    const [path, init] = GET.mock.calls[0] as unknown as [string, { params: { query: Record<string, unknown> } }];
    expect(path).toBe(PATH);
    expect(init.params.query.countryCode).toBe("DE");
    expect(init.params.query.postalCode).toBe("10115");
    expect("houseNumber" in init.params.query ? init.params.query.houseNumber : undefined).toBeUndefined();
    expect(Object.values(init.params.query)).not.toContain("null");
  });

  it("answers null when the provider found nothing (found false), also when the body is missing", async () => {
    GET.mockResolvedValueOnce(ok({ found: false, provider: "PDOK" }));
    expect(await lookupAddress("NL", "9999ZZ", "1")).toBeNull();
    GET.mockResolvedValueOnce(ok(undefined));
    expect(await lookupAddress("NL", "9999ZZ", "1")).toBeNull();
  });

  it("throws the client's error (the caller decides that a failure is silent)", async () => {
    const error = { error: { code: "Forbidden", message: "no" } };
    GET.mockResolvedValue(failed(error));
    await expect(lookupAddress("NL", "1012AB", null)).rejects.toBe(error);
  });
});

/* The postal-code dropdown (PO 2026-10-13, extension of design 44): one GET on the suggestions route with the
 * country code and the typed term; the list as the server sends it, an empty list for no body, the client's
 * error thrown (the component drops it silently). Risk class: Medium — the request shape is the contract. */
describe("fetchAddressSuggestions", () => {
  const SUGGESTIONS_PATH = "/api/integrations/address-lookup/suggestions";
  const LINES = [
    { label: "Stationsplein, 1012AB Amsterdam", postalCode: "1012AB", street: "Stationsplein", houseNumber: null, houseNumberSuffix: null, city: "Amsterdam", latitude: 52.3789, longitude: 4.9001 },
    { label: "Damrak, 1012AC Amsterdam", postalCode: "1012AC", street: "Damrak", houseNumber: null, houseNumberSuffix: null, city: "Amsterdam", latitude: 52.3752, longitude: 4.8945 },
  ];

  it("asks GET /api/integrations/address-lookup/suggestions with the country code and the term as query parameters and hands the list back as sent", async () => {
    GET.mockResolvedValue(ok(LINES));
    const result = await fetchAddressSuggestions("NL", "1012");
    expect(GET).toHaveBeenCalledTimes(1);
    expect(GET).toHaveBeenCalledWith(SUGGESTIONS_PATH, { params: { query: { countryCode: "NL", term: "1012" } } });
    expect(result).toEqual(LINES);
  });

  it("passes the term untouched: spaces and case are the server's to normalise", async () => {
    GET.mockResolvedValue(ok([]));
    await fetchAddressSuggestions("NL", "1012 ab 7");
    expect(GET).toHaveBeenCalledWith(SUGGESTIONS_PATH, { params: { query: { countryCode: "NL", term: "1012 ab 7" } } });
  });

  it("answers an empty list for an empty answer and for a missing body", async () => {
    GET.mockResolvedValueOnce(ok([]));
    expect(await fetchAddressSuggestions("NL", "9999")).toEqual([]);
    GET.mockResolvedValueOnce(ok(undefined));
    expect(await fetchAddressSuggestions("NL", "9999")).toEqual([]);
  });

  it("throws the client's error (the dropdown decides that a failure is silent)", async () => {
    const error = { error: { code: "Forbidden", message: "no" } };
    GET.mockResolvedValue(failed(error));
    await expect(fetchAddressSuggestions("NL", "1012")).rejects.toBe(error);
  });

  it("does not touch the lookup route", async () => {
    GET.mockResolvedValue(ok([]));
    await fetchAddressSuggestions("NL", "1012");
    expect((GET.mock.calls as unknown as [string][]).map(([path]) => path)).toEqual([SUGGESTIONS_PATH]);
  });
});
