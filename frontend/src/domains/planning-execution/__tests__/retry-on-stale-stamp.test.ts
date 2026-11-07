import { QueryClient } from "@tanstack/react-query";
import { beforeEach, describe, expect, it, vi } from "vitest";

/* A command that carries a trip's stamp (design 74 §10): the calculator saves the trip after every change, so a stale stamp on
 * an assign is retried once with the fresh one. Risk class: High (planning). */

vi.mock("@/lib/api/client", () => ({ apiClient: { GET: vi.fn(), POST: vi.fn(), PUT: vi.fn(), DELETE: vi.fn() } }));

import { retryOnStaleStamp } from "@/domains/planning-execution/planboard-lists";
import { apiClient } from "@/lib/api/client";

const GET = vi.mocked(apiClient.GET) as unknown as ReturnType<typeof vi.fn>;
const stale = { status: 409, error: { message: "stale" } };
const answer = (concurrencyStamp: string) => ({ data: { id: "t1", concurrencyStamp }, response: new Response() });

let client: QueryClient;
beforeEach(() => {
  GET.mockReset();
  client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
});

describe("retryOnStaleStamp", () => {
  it("does not read the trip when the first attempt succeeds", async () => {
    const attempt = vi.fn(async (stamp: string) => `done ${stamp}`);
    await expect(retryOnStaleStamp(client, "t1", "s1", attempt)).resolves.toBe("done s1");
    expect(attempt).toHaveBeenCalledTimes(1);
    expect(GET).not.toHaveBeenCalled();
  });

  it("sends an empty stamp when the caller has none", async () => {
    const attempt = vi.fn(async () => "ok");
    await retryOnStaleStamp(client, "t1", null, attempt);
    expect(attempt).toHaveBeenCalledWith("");
  });

  it("reads the trip's stamp afresh and retries once on a stale stamp", async () => {
    GET.mockResolvedValue(answer("fresh"));
    const attempt = vi.fn(async (stamp: string) => {
      if (stamp === "s1") throw stale;
      return `done ${stamp}`;
    });
    await expect(retryOnStaleStamp(client, "t1", "s1", attempt)).resolves.toBe("done fresh");
    expect(attempt.mock.calls.map(([stamp]) => stamp)).toEqual(["s1", "fresh"]);
    expect(GET).toHaveBeenCalledWith("/api/planning/transport-trip/{id}", { params: { path: { id: "t1" } } });
  });

  it("reads afresh even with the trip in the cache", async () => {
    GET.mockResolvedValue(answer("fresh"));
    client.setQueryData(["planning", "transport-trips", "detail", "t1"], { id: "t1", concurrencyStamp: "cached" });
    const stamps: string[] = [];
    await retryOnStaleStamp(client, "t1", "s1", async (stamp) => {
      stamps.push(stamp);
      if (stamps.length === 1) throw stale;
      return "ok";
    });
    expect(stamps[1]).toBe("fresh");
  });

  it("a stale stamp on the retry goes to the caller", async () => {
    GET.mockResolvedValue(answer("fresh"));
    const again = { status: 409, error: { message: "again" } };
    const attempt = vi.fn(async (stamp: string): Promise<string> => {
      throw stamp === "s1" ? stale : again;
    });
    await expect(retryOnStaleStamp(client, "t1", "s1", attempt)).rejects.toBe(again);
    expect(attempt).toHaveBeenCalledTimes(2);
  });

  it("any other failure goes to the caller without a read", async () => {
    for (const error of [{ status: 400, error: { message: "bad" } }, { status: 409, error: { code: "OpenTms.PlanningExecution:TripNotPlanned" } }, new Error("x")]) {
      const attempt = vi.fn(async (): Promise<string> => {
        throw error;
      });
      await expect(retryOnStaleStamp(client, "t1", "s1", attempt)).rejects.toBe(error);
      expect(attempt).toHaveBeenCalledTimes(1);
    }
    expect(GET).not.toHaveBeenCalled();
  });

  it("a failing read goes to the caller, without a second attempt", async () => {
    GET.mockResolvedValue({ error: { error: { message: "gone" } }, response: new Response(null, { status: 404 }) });
    const attempt = vi.fn(async (): Promise<string> => {
      throw stale;
    });
    await expect(retryOnStaleStamp(client, "t1", "s1", attempt)).rejects.toBeTruthy();
    expect(attempt).toHaveBeenCalledTimes(1);
  });
});
