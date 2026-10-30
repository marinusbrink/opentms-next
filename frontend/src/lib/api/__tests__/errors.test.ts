import { describe, expect, it } from "vitest";

import { getApiErrorCode, getApiErrorMessage } from "@/lib/api/errors";

/* The one place that reads ABP's error shape. getApiErrorCode drives the user menu's PasswordRequired fallback
 * (design 63). Risk class: Medium. */

describe("getApiErrorCode", () => {
  it("returns ABP's business error code", () => {
    expect(getApiErrorCode({ error: { code: "OpenTms.Platform:AccountPasswordRequired", message: "x" } })).toBe(
      "OpenTms.Platform:AccountPasswordRequired",
    );
  });

  it.each([
    ["no code", { error: { message: "x" } }],
    ["an empty code", { error: { code: "" } }],
    ["a non-string code", { error: { code: 42 } }],
    ["a non-object error", { error: "boom" }],
    ["a problem-details body", { title: "Bad request", status: 400 }],
    ["null", null],
    ["undefined", undefined],
    ["a string", "OpenTms.Platform:AccountPasswordRequired"],
    ["an Error", new Error("x")],
  ])("returns undefined for %s", (_label, value) => {
    expect(getApiErrorCode(value)).toBeUndefined();
  });
});

describe("getApiErrorMessage", () => {
  const t = (k: string) => `t:${k}`;

  it("prefers ABP's message, then title, then detail, else the localized fallback", () => {
    expect(getApiErrorMessage({ error: { message: "abp" }, title: "title" }, t)).toBe("abp");
    expect(getApiErrorMessage({ title: "title", detail: "detail" }, t)).toBe("title");
    expect(getApiErrorMessage({ detail: "detail" }, t)).toBe("detail");
    expect(getApiErrorMessage({ error: { message: "  " } }, t)).toBe("t:UnexpectedError");
    expect(getApiErrorMessage(undefined, t)).toBe("t:UnexpectedError");
  });
});
