import { describe, expect, it } from "vitest";

import { APPS, findAppByPath } from "@/app/apps.config";

describe("apps.config – AppView extension (low risk)", () => {
  const adminApp = APPS.find((a) => a.id === "admin")!;

  it("admin app declares a views array", () => {
    expect(adminApp.views).toBeDefined();
    expect(Array.isArray(adminApp.views)).toBe(true);
  });

  it("admin app has exactly two views: Users and Roles", () => {
    expect(adminApp.views).toHaveLength(2);
    const [users, roles] = adminApp.views!;
    expect(users.path).toBe("/admin/users");
    expect(users.nameKey).toBe("Administration:Users");
    expect(roles.path).toBe("/admin/roles");
    expect(roles.nameKey).toBe("Administration:Roles");
  });

  it("each admin view has a non-null icon (React component)", () => {
    adminApp.views!.forEach((view) => {
      // Lucide icons are React.forwardRef objects — typeof is "object", not "function"
      expect(view.icon).toBeTruthy();
    });
  });

  it("admin views do not declare children in this PBI", () => {
    adminApp.views!.forEach((view) => {
      expect(view.children).toBeUndefined();
    });
  });

  it("every other app has no views declared", () => {
    APPS.filter((a) => a.id !== "admin").forEach((app) => {
      expect(app.views).toBeUndefined();
    });
  });

  it("findAppByPath returns admin app for /admin/users", () => {
    expect(findAppByPath("/admin/users")?.id).toBe("admin");
  });

  it("findAppByPath returns admin app for /admin/roles", () => {
    expect(findAppByPath("/admin/roles")?.id).toBe("admin");
  });

  it("findAppByPath returns admin app for /admin itself", () => {
    expect(findAppByPath("/admin")?.id).toBe("admin");
  });

  it("findAppByPath returns undefined for unknown paths", () => {
    expect(findAppByPath("/unknown")).toBeUndefined();
  });

  it("findAppByPath does not match /administrators (prefix boundary)", () => {
    expect(findAppByPath("/administrators")).toBeUndefined();
  });
});
