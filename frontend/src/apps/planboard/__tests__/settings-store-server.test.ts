import { beforeAll, beforeEach, describe, expect, it, vi } from "vitest";

/* The server store of the Planboard settings (design 70, "Persistence") over a fake fetch: each key is the user's
 * GridUserSettings row (`/api/platform/grid-settings/{gridId}`) with the value as its `viewState`; an absent row reads as
 * null, a failed request rejects, and an invalid key never reaches the network. Risk class: Medium (per-user settings;
 * the endpoint's user/tenant scoping is the backend's GridSettings tests). */

vi.mock("@/lib/i18n/LocalizationProvider", () => ({ useL: () => ({ t: (k: string) => k, culture: "en", isLoading: false }) }));

const fetchMock = vi.fn();
let mod: typeof import("@/apps/planboard/settings-store");

beforeAll(async () => {
  vi.stubGlobal("fetch", fetchMock);
  mod = await import("@/apps/planboard/settings-store");
});

const json = (body: unknown, status = 200) => new Response(JSON.stringify(body), { status, headers: { "Content-Type": "application/json" } });
const lastRequest = () => fetchMock.mock.calls.at(-1)![0] as Request;

beforeEach(() => {
  fetchMock.mockReset();
});

describe("createServerSettingsStore", () => {
  it("loads the viewState of the key's row", async () => {
    fetchMock.mockResolvedValueOnce(json({ gridId: "planboard.layout", columnStates: [], sortModels: [], viewState: { version: 1 } }));
    const store = mod.createServerSettingsStore();
    expect(await store.load("planboard.layout")).toEqual({ version: 1 });
    expect(lastRequest().method).toBe("GET");
    expect(new URL(lastRequest().url).pathname).toBe("/api/platform/grid-settings/planboard.layout");
  });

  it("reads a row without viewState (or an empty body) as null", async () => {
    fetchMock.mockResolvedValueOnce(json({ gridId: "planboard.pane.trips", columnStates: [], sortModels: [] }));
    const store = mod.createServerSettingsStore();
    expect(await store.load("planboard.pane.trips")).toBeNull();
    fetchMock.mockResolvedValueOnce(new Response(null, { status: 204 }));
    expect(await store.load("planboard.pane.trips")).toBeNull();
  });

  it("saves the value as viewState with empty grid parts", async () => {
    fetchMock.mockResolvedValueOnce(json({}));
    const store = mod.createServerSettingsStore();
    expect(await store.save("planboard.pane.scheduler-ab12", { mode: "driver" })).toBe(true);
    const request = lastRequest();
    expect(request.method).toBe("PUT");
    expect(new URL(request.url).pathname).toBe("/api/platform/grid-settings/planboard.pane.scheduler-ab12");
    expect(await request.json()).toEqual({ columnStates: [], sortModels: [], viewState: { mode: "driver" } });
  });

  it("removes with DELETE", async () => {
    fetchMock.mockResolvedValueOnce(new Response(null, { status: 204 }));
    const store = mod.createServerSettingsStore();
    expect(await store.remove("planboard.layout")).toBe(true);
    expect(lastRequest().method).toBe("DELETE");
  });

  it("rejects with the API error when the request fails", async () => {
    fetchMock.mockResolvedValueOnce(json({ error: { code: "Platform:GridSettingsTooLarge", message: "Too large" } }, 400));
    const store = mod.createServerSettingsStore();
    await expect(store.save("planboard.layout", {})).rejects.toMatchObject({ error: { code: "Platform:GridSettingsTooLarge" } });
    fetchMock.mockResolvedValueOnce(json({ error: { message: "boom" } }, 500));
    await expect(store.load("planboard.layout")).rejects.toBeTruthy();
  });

  it("refuses an invalid key without a request", async () => {
    const store = mod.createServerSettingsStore();
    await expect(store.load("planboard.pane.a/b")).rejects.toThrow(/Invalid Planboard settings key/);
    await expect(store.save("../x", 1)).rejects.toThrow(/Invalid Planboard settings key/);
    await expect(store.remove("")).rejects.toThrow(/Invalid Planboard settings key/);
    expect(fetchMock).not.toHaveBeenCalled();
  });
});
