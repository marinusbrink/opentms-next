import { describe, expect, it } from "vitest";

import { applyTenantTheme, BRAND_FALLBACK, resolveBrand } from "@/lib/theme/apply-tenant-theme";

describe("resolveBrand (UI-TOK-003, D-01)", () => {
  it("falls back to the product blue without a theme setting", () => {
    expect(resolveBrand(undefined)).toBe(BRAND_FALLBACK);
    expect(resolveBrand({})).toBe(BRAND_FALLBACK);
    expect(resolveBrand({ "App.UiManagement.Theme": "" })).toBe(BRAND_FALLBACK);
  });

  it("maps the named tenant themes", () => {
    expect(resolveBrand({ "App.UiManagement.Theme": "red" })).toBe("#E20613");
    expect(resolveBrand({ "App.UiManagement.Theme": "Green" })).toBe("#006847");
  });

  it("uses a valid custom colour and rejects anything that is not a hex colour", () => {
    expect(resolveBrand({ "App.UiManagement.Theme": "custom", "App.UiManagement.CustomColor": "#123abc" })).toBe("#123abc");
    expect(resolveBrand({ "App.UiManagement.Theme": "custom", "App.UiManagement.CustomColor": "red; }" })).toBe(BRAND_FALLBACK);
  });

  it("sets --brand on the document element", () => {
    applyTenantTheme({ "App.UiManagement.Theme": "blue" });
    expect(document.documentElement.style.getPropertyValue("--brand")).toBe("#38A8C6");
  });
});
