import { describe, expect, it } from "vitest";

import { cn } from "@/lib/utils";

/* Risk class: High — every dialog's width tier (UI-DLG-003) depends on the custom
 * container tokens winning over the shadcn base max-width. */
describe("cn", () => {
  it("lets a dialog width tier replace the base max-width of the shadcn dialog", () => {
    expect(cn("w-full sm:max-w-sm", "sm:max-w-dialog-md")).toBe("w-full sm:max-w-dialog-md");
    expect(cn("max-w-[calc(100%-2rem)] sm:max-w-sm", "max-w-[calc(100%-2rem)] sm:max-w-dialog-confirm")).toBe(
      "max-w-[calc(100%-2rem)] sm:max-w-dialog-confirm",
    );
  });

  it("still merges the ordinary utilities", () => {
    expect(cn("p-2", "p-4")).toBe("p-4");
    expect(cn("text-sm", false, undefined, "font-medium")).toBe("text-sm font-medium");
  });
});
