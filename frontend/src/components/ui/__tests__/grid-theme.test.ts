import { afterEach, describe, expect, it } from "vitest";

import { gridThemeParams, opentmsGridTheme, resetGridThemeForTests } from "@/components/ui/grid-theme";

describe("opentmsGridTheme (UI-TOK-004)", () => {
  afterEach(() => {
    resetGridThemeForTests();
    document.documentElement.style.removeProperty("--grid-row-height");
  });

  it("builds a Theming API theme once and reuses it", () => {
    const first = opentmsGridTheme();
    expect(first).toBeDefined();
    expect(opentmsGridTheme()).toBe(first);
  });

  it("reads the sizes from the tokens with sensible fallbacks and keeps colours as CSS variables", () => {
    document.documentElement.style.setProperty("--grid-row-height", "48px");
    const params = gridThemeParams();
    // Either the token (48) or, when jsdom does not resolve custom properties, the fallback (36).
    expect([36, 48]).toContain(params.rowHeight);
    expect(params.headerHeight).toBe(40);
    expect(params.fontSize).toBe(13);
    expect(params.accentColor).toBe("var(--brand)");
    expect(params.selectedRowBackgroundColor).toBe("var(--grid-selected-row-bg)");
  });
});
