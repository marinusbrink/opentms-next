import { describe, expect, it } from "vitest";

import { countText } from "@/lib/i18n/count-text";

/* Count texts pick the singular key for exactly one (browser-run finding 6). Risk class: Low. */

const texts: Record<string, string> = {
  "X:Notes": "{0} consignment notes will be printed",
  "X:Notes.One": "{0} consignment note will be printed",
  "X:Skipped": "{0} trips are skipped: {1}",
  "X:Skipped.One": "{0} trip is skipped: {1}",
  "X:NoSingular": "{0} items",
};
const t = (key: string) => texts[key] ?? key;

describe("countText", () => {
  it("uses the .One key for exactly one and the plural key otherwise", () => {
    expect(countText(t, "X:Notes", 1)).toBe("1 consignment note will be printed");
    expect(countText(t, "X:Notes", 0)).toBe("0 consignment notes will be printed");
    expect(countText(t, "X:Notes", 3)).toBe("3 consignment notes will be printed");
  });

  it("fills the further arguments", () => {
    expect(countText(t, "X:Skipped", 1, "TR-1")).toBe("1 trip is skipped: TR-1");
    expect(countText(t, "X:Skipped", 2, "TR-1, TR-2")).toBe("2 trips are skipped: TR-1, TR-2");
  });

  it("falls back to the plural text when the singular key is missing", () => {
    expect(countText(t, "X:NoSingular", 1)).toBe("1 items");
  });
});
