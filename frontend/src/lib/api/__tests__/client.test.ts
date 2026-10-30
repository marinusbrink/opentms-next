import { beforeAll, beforeEach, describe, expect, it, vi } from "vitest";

/* The generated client's middleware: the culture the screen renders in goes out as Accept-Language (design 63), next to
 * the bearer token and ABP's __tenant header. Risk class: Medium (Accept-Language); the token and tenant headers are
 * tenant isolation (Critical) and are checked here as a regression guard. */

const fetchMock = vi.fn();
let client: typeof import("@/lib/api/client");

beforeAll(async () => {
  vi.stubGlobal("fetch", fetchMock);
  client = await import("@/lib/api/client");
});

beforeEach(() => {
  fetchMock.mockReset();
  fetchMock.mockImplementation(async () => new Response("{}", { status: 200, headers: { "Content-Type": "application/json" } }));
  client.setCultureProvider(() => undefined);
  client.setAccessTokenProvider(() => undefined);
  client.setTenantProvider(() => undefined);
});

const lastRequest = (): Request => {
  const [input] = fetchMock.mock.calls.at(-1)!;
  return input as Request;
};

describe("apiClient headers", () => {
  it("sends the current culture as Accept-Language", async () => {
    client.setCultureProvider(() => "nl");
    await client.apiClient.GET("/api/platform/my-profile");
    expect(lastRequest().headers.get("Accept-Language")).toBe("nl");
  });

  it("asks the provider on every request, so a language change applies at once", async () => {
    let culture = "en";
    client.setCultureProvider(() => culture);
    await client.apiClient.GET("/api/platform/my-profile");
    expect(lastRequest().headers.get("Accept-Language")).toBe("en");
    culture = "nl";
    await client.apiClient.GET("/api/platform/my-profile");
    expect(lastRequest().headers.get("Accept-Language")).toBe("nl");
  });

  it("sends no Accept-Language of its own without a culture", async () => {
    await client.apiClient.GET("/api/platform/my-profile");
    expect(lastRequest().headers.get("Accept-Language")).not.toBe("nl");
  });

  it("still sends the bearer token and the __tenant header", async () => {
    client.setAccessTokenProvider(() => "tok");
    client.setTenantProvider(() => "t-acme");
    client.setCultureProvider(() => "en");
    await client.apiClient.GET("/api/platform/my-profile");
    const request = lastRequest();
    expect(request.headers.get("Authorization")).toBe("Bearer tok");
    expect(request.headers.get("__tenant")).toBe("t-acme");
    expect(request.headers.get("Accept-Language")).toBe("en");
  });
});
