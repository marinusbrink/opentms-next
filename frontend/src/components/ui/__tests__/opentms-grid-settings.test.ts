/**
 * The failure path of grid settings persistence (UI-FB-005: no silent failures).
 * Risk class: High (design #6 §Test risk analysis — column personalization load/save/reset).
 *
 * Before docs/designs/grid-settings-storage.md the grid fired `void apiClient.PUT(...).catch(...)`;
 * openapi-fetch resolves on an HTTP 500 instead of rejecting, so the catch never ran and the user
 * never learned that no layout was being saved. These tests pin the visible failure path.
 */

import type { ColumnState } from "ag-grid-community";
import { beforeEach, describe, expect, it, vi } from "vitest";

vi.mock("@/lib/api/client", () => ({
  apiClient: { GET: vi.fn(), POST: vi.fn(), PUT: vi.fn(), DELETE: vi.fn() },
}));

const toastError = vi.fn();
vi.mock("sonner", () => ({
  toast: { error: (...args: unknown[]) => toastError(...args) },
}));

import { apiClient } from "@/lib/api/client";
import {
  buildGridSettingsPayload,
  createGridSettingsPersister,
  gridSettingsToastId,
} from "@/components/ui/opentms-grid-settings";

const PUT = vi.mocked(apiClient.PUT);
const DELETE = vi.mocked(apiClient.DELETE);
const t = (key: string) => key;

const colState: ColumnState[] = [
  { colId: "reference", hide: false, width: 180, sort: "asc", sortIndex: 0 },
  { colId: "status", hide: true, width: undefined },
];

function ok(status = 204) {
  return { data: undefined, error: undefined, response: { ok: true, status } as Response };
}

function failed(status: number, error: unknown) {
  return { data: undefined, error, response: { ok: false, status } as Response };
}

beforeEach(() => {
  PUT.mockReset();
  DELETE.mockReset();
  toastError.mockReset();
  vi.spyOn(console, "error").mockImplementation(() => {});
});

describe("buildGridSettingsPayload", () => {
  it("maps visibility, order, width and the sorted columns onto GridSettingsDto", () => {
    expect(buildGridSettingsPayload(colState)).toEqual({
      columnStates: [
        { colId: "reference", visible: true, order: 0, width: 180 },
        { colId: "status", visible: false, order: 1, width: null },
      ],
      sortModels: [{ colId: "reference", sort: "asc" }],
    });
  });
});

describe("createGridSettingsPersister.save", () => {
  it("PUTs the payload for the grid and stays quiet on success", async () => {
    PUT.mockResolvedValue(ok() as never);

    const saved = await createGridSettingsPersister("orders", t).save(colState);

    expect(saved).toBe(true);
    expect(PUT).toHaveBeenCalledWith("/api/platform/grid-settings/{gridId}", {
      params: { path: { gridId: "orders" } },
      body: buildGridSettingsPayload(colState),
    });
    expect(toastError).not.toHaveBeenCalled();
  });

  it("shows the API error in a toast when the server rejects the save (HTTP 500 resolves, not rejects)", async () => {
    PUT.mockResolvedValue(failed(500, { error: { message: "value too long for type character varying(2048)" } }) as never);

    const saved = await createGridSettingsPersister("orders", t).save(colState);

    expect(saved).toBe(false);
    expect(toastError).toHaveBeenCalledTimes(1);
    expect(toastError).toHaveBeenCalledWith("Grid:SettingsSaveFailed", {
      id: gridSettingsToastId("orders"),
      description: "value too long for type character varying(2048)",
    });
  });

  it("shows the localized fallback when the request itself fails", async () => {
    PUT.mockRejectedValue(new TypeError("Failed to fetch"));

    const saved = await createGridSettingsPersister("orders", t).save(colState);

    expect(saved).toBe(false);
    expect(toastError).toHaveBeenCalledWith("Grid:SettingsSaveFailed", {
      id: gridSettingsToastId("orders"),
      description: "UnexpectedError",
    });
  });

  it("reuses one toast id per grid so repeated failures update the toast instead of stacking", async () => {
    PUT.mockResolvedValue(failed(500, { title: "Internal Server Error" }) as never);
    const persister = createGridSettingsPersister("orders", t);

    await persister.save(colState);
    await persister.save(colState);

    expect(toastError).toHaveBeenCalledTimes(2);
    const ids = toastError.mock.calls.map((call) => (call[1] as { id: string }).id);
    expect(new Set(ids).size).toBe(1);
    expect(ids[0]).toBe(gridSettingsToastId("orders"));
  });
});

describe("createGridSettingsPersister.reset", () => {
  it("DELETEs the saved layout and stays quiet on success", async () => {
    DELETE.mockResolvedValue(ok() as never);

    const reset = await createGridSettingsPersister("orders", t).reset();

    expect(reset).toBe(true);
    expect(DELETE).toHaveBeenCalledWith("/api/platform/grid-settings/{gridId}", {
      params: { path: { gridId: "orders" } },
    });
    expect(toastError).not.toHaveBeenCalled();
  });

  it("shows a toast when the reset is rejected by the server", async () => {
    DELETE.mockResolvedValue(failed(500, { error: { message: "boom" } }) as never);

    const reset = await createGridSettingsPersister("orders", t).reset();

    expect(reset).toBe(false);
    expect(toastError).toHaveBeenCalledWith("Grid:SettingsResetFailed", {
      id: gridSettingsToastId("orders"),
      description: "boom",
    });
  });
});
