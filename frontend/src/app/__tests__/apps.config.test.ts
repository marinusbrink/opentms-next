import { describe, expect, it } from "vitest";

import { APPS, findAppByPath } from "@/app/apps.config";

describe("apps.config – AppView extension (low risk)", () => {
  const adminApp = APPS.find((a) => a.id === "admin")!;

  it("admin app declares a views array", () => {
    expect(adminApp.views).toBeDefined();
    expect(Array.isArray(adminApp.views)).toBe(true);
  });

  it("admin app has five views: Users, Roles, Organization units, Audit logs with the two logbooks, and Maintenance", () => {
    expect(adminApp.views).toHaveLength(5);
    const [users, roles, organizationUnits, auditLogs, maintenance] = adminApp.views!;
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
    expect(maintenance.path).toBe("/admin/maintenance");
    expect(maintenance.nameKey).toBe("Administration:Maintenance");
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

  it("the Tenants app holds the Tenants and Editions views, each behind its own permission (PO 2026-10-14)", () => {
    const tenants = APPS.find((a) => a.id === "tenants")!;
    expect(tenants.path).toBe("/tenants");
    expect(tenants.nameKey).toBe("App:Tenants");
    expect(tenants.requiredPermission).toEqual(["Platform.Tenants", "Platform.Editions"]);
    expect(tenants.views!.map((v) => [v.path, v.nameKey, v.requiredPermission])).toEqual([
      ["/tenants/list", "Tenants:Nav.Tenants", "Platform.Tenants"],
      ["/tenants/editions", "Tenants:Nav.Editions", "Platform.Editions"],
    ]);
    expect(APPS.find((a) => a.id === "editions")).toBeUndefined();
  });

  it("every other app except Master data and Tenants has no views declared", () => {
    APPS.filter((a) => a.id !== "admin" && a.id !== "master-data" && a.id !== "tenants").forEach((app) => {
      expect(app.views).toBeUndefined();
    });
  });

  it("the Master data app lists its fifteen lists in four groups under the General domain group (designs 41, 43 and 48; PO 2026-10-16)", () => {
    const masterData = APPS.find((a) => a.id === "master-data")!;
    expect(masterData.views).toHaveLength(4);
    const general = masterData.views![0];
    expect(general.nameKey).toBe("MasterData:Nav.General");
    expect(general.icon).toBeTruthy();
    expect(general.children).toHaveLength(4);
    const paths = general.children!.flatMap((group) => group.children!.map((child) => child.path));
    expect(paths).toEqual([
      /* Design 43: Relations first in its group (it owns the group's path). Design 48: Product relation codes after
       * Transport units; Vehicles first in Fleet, then Vehicle combinations, then Drivers. */
      "/master-data/relations",
      "/master-data/address-types",
      "/master-data/relation-categories",
      "/master-data/products",
      "/master-data/transport-units",
      "/master-data/product-relation-codes",
      "/master-data/temperatures",
      "/master-data/vehicles",
      "/master-data/vehicle-combinations",
      "/master-data/drivers",
      "/master-data/vehicle-types",
      "/master-data/transport-characteristics",
      "/master-data/countries",
      "/master-data/currencies",
      "/master-data/geographical-regions",
    ]);
    general.children!.forEach((group) => {
      expect(group.icon).toBeTruthy();
      group.children!.forEach((child) => expect(child.icon).toBeTruthy());
    });
  });

  it("the Master data app holds the Orders, Planning & Execution and Financial domain groups after General (designs 51–53)", () => {
    const masterData = APPS.find((a) => a.id === "master-data")!;
    expect(masterData.domains).toEqual(["MasterData", "Orders", "PlanningExecution", "Financial"]);
    const [, orders, planning, financial] = masterData.views!;
    const lists = (group: (typeof masterData.views)[number]) => group.children!.flatMap((g) => g.children!.map((c) => [c.path, c.nameKey, c.feature]));
    expect(lists(orders)).toEqual([
      ["/master-data/transport-order-types", "Orders:TransportOrderTypes", "Apps.MasterData.Orders.TransportOrders.TransportOrderTypes"],
      ["/master-data/waypoint-types", "Orders:WaypointTypes", "Apps.MasterData.Orders.TransportOrders.WaypointTypes"],
      ["/master-data/time-slots", "Orders:TimeSlots", "Apps.MasterData.Orders.TransportOrders.TimeSlots"],
    ]);
    expect(lists(planning)).toEqual([
      ["/master-data/transport-trip-types", "PlanningExecution:TransportTripTypes", "Apps.MasterData.PlanningExecution.Trips.TransportTripTypes"],
    ]);
    expect(lists(financial)).toEqual([
      ["/master-data/vat-percentages", "Financial:VatPercentages", "Apps.MasterData.Financial.Invoicing.VatPercentages"],
      ["/master-data/outbound-invoice-types", "Financial:OutboundInvoiceTypes", "Apps.MasterData.Financial.Invoicing.OutboundInvoiceTypes"],
      ["/master-data/inbound-invoice-types", "Financial:InboundInvoiceTypes", "Apps.MasterData.Financial.Invoicing.InboundInvoiceTypes"],
      ["/master-data/additional-services", "Financial:AdditionalServices", "Apps.MasterData.Financial.Pricing.AdditionalServices"],
      ["/master-data/discounts-or-surcharges", "Financial:DiscountsOrSurcharges", "Apps.MasterData.Financial.Pricing.DiscountsOrSurcharges"],
    ]);
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
