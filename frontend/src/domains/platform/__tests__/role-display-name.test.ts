import { describe, expect, it } from "vitest";

import { roleDisplayName } from "@/domains/platform/administration-roles";

/* A role's shown name (docs/designs/71-predefined-roles.md): the localized `Role:<stored name>` for a seeded role,
 * the stored name for any role the tenant made itself. */
const texts: Record<string, string> = { "Role:Order entry": "Orderinvoer", "Role:admin": "Beheerder" };
const t = (key: string) => texts[key] ?? key;

describe("roleDisplayName", () => {
  it("shows a seeded role by its localized name", () => {
    expect(roleDisplayName("Order entry", t)).toBe("Orderinvoer");
    expect(roleDisplayName("admin", t)).toBe("Beheerder");
  });

  it("shows a role without a localized name by its stored name", () => {
    expect(roleDisplayName("Night shift", t)).toBe("Night shift");
  });

  it("matches the stored name exactly", () => {
    expect(roleDisplayName("order entry", t)).toBe("order entry");
  });

  it("shows nothing for a missing name", () => {
    expect(roleDisplayName(undefined, t)).toBe("");
    expect(roleDisplayName(null, t)).toBe("");
  });
});
