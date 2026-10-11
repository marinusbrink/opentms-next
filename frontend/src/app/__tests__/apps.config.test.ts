import { describe, expect, it } from "vitest";

import { APPS, findAppByPath } from "@/app/apps.config";

describe("apps.config – AppView extension (low risk)", () => {
  const adminApp = APPS.find((a) => a.id === "admin")!;

  it("admin app declares a views array", () => {
    expect(adminApp.views).toBeDefined();
    expect(Array.isArray(adminApp.views)).toBe(true);
  });

  it("admin app has four views: Users, Roles, Organization units and Audit logs with the two logbooks", () => {
    expect(adminApp.views).toHaveLength(4);
    const [users, roles, organizationUnits, auditLogs] = adminApp.views!;
    expect(users.path).toBe("/admin/users");
    expect(users.nameKey).toBe("Administration:Users");
    expect(roles.path).toBe("/admin/roles");
    expect(roles.nameKey).toBe("Administration:Roles");
    expect(organizationUnits.path).toBe("/admin/organization-units");
    expect(organizationUnits.nameKey).toBe("Administration:OrganizationUnits");
    expect(auditLogs.path).toBe("/admin/logbook");
    expect(auditLogs.nameKey).toBe("Administration:AuditLogs");
    const [functionalLogbook, technicalLogbook] = auditLogs.children!;
    expect(functionalLogbook.path).toBe("/admin/logbook/functional");
    expect(functionalLogbook.nameKey).toBe("Administration:FunctionalLogbook");
    expect(technicalLogbook.path).toBe("/admin/logbook/technical");
    expect(technicalLogbook.nameKey).toBe("Administration:TechnicalLogbook");
  });

  it("each admin view has a non-null icon (React component)", () => {
    adminApp.views!.forEach((view) => {
      // Lucide icons are React.forwardRef objects — typeof is "object", not "function"
      expect(view.icon).toBeTruthy();
    });
  });

  it("only Audit logs declares children, and every child has an icon", () => {
    adminApp.views!.forEach((view) => {
      if (view.nameKey === "Administration:AuditLogs") {
        expect(view.children).toHaveLength(2);
        view.children!.forEach((child) => expect(child.icon).toBeTruthy());
      } else {
        expect(view.children).toBeUndefined();
      }
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
