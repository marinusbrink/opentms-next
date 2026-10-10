import { describe, expect, it } from "vitest";

import { getApiErrorMessage } from "@/lib/api/errors";

const t = (key: string) => key;

describe("getApiErrorMessage (UI-FB-004)", () => {
  it("prefers the ABP error message", () => {
    expect(getApiErrorMessage({ error: { message: "Username taken" }, title: "Bad request" }, t)).toBe("Username taken");
  });

  it("falls back to the problem-details title, then detail", () => {
    expect(getApiErrorMessage({ title: "Bad request" }, t)).toBe("Bad request");
    expect(getApiErrorMessage({ detail: "Field X is required" }, t)).toBe("Field X is required");
  });

  it("returns the localized fallback for anything else", () => {
    expect(getApiErrorMessage(undefined, t)).toBe("UnexpectedError");
    expect(getApiErrorMessage(new TypeError("Failed to fetch"), t)).toBe("UnexpectedError");
    expect(getApiErrorMessage({ error: { message: "   " } }, t)).toBe("UnexpectedError");
  });
});
