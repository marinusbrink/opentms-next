import { describe, expect, it } from "vitest";

import { APPS, findAppByPath, type AppView } from "@/app/apps.config";

describe("apps.config – AppView extension (low risk)", () => {
  const adminApp = APPS.find((a) => a.id === "admin")!;

  it("admin app declares a views array", () => {
    expect(adminApp.views).toBeDefined();
    expect(Array.isArray(adminApp.views)).toBe(true);
  });

  it("admin app has six views: Users, Roles, Organization units, Audit logs with the two logbooks, Maintenance and E-mail", () => {
    expect(adminApp.views).toHaveLength(6);
    const [users, roles, organizationUnits, auditLogs, maintenance, email] = adminApp.views!;
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
    /* Design 64: host only through its host-side permission. */
    expect(email.path).toBe("/admin/email");
    expect(email.nameKey).toBe("Administration:EmailSettings");
    expect(email.feature).toBe("Apps.Administration.Email");
    expect(email.requiredPermission).toBe("Platform.Administration.EmailSettings");
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

  it("every other app except Master data, Transport and Tenants has no views declared", () => {
    APPS.filter((a) => !["admin", "master-data", "tenants", "transport"].includes(a.id)).forEach((app) => {
      expect(app.views).toBeUndefined();
    });
  });

  it("the Transport app has the views Orders, Templates, Trips and Documents and reads from four domains (design 61)", () => {
    const transport = APPS.find((a) => a.id === "transport")!;
    expect(transport.domains).toEqual(["Orders", "PlanningExecution", "Financial", "MasterData"]);
    expect(transport.views!.map((v) => [v.nameKey, v.path, v.feature])).toEqual([
      ["App:Transport.Orders", "/transport/orders", "Apps.Transport.Orders"],
      ["App:Transport.Templates", "/transport/templates", "Apps.Transport.Orders.Templates"],
      ["App:Transport.Trips", "/transport/trips", "Apps.Transport.Trips"],
      ["App:Transport.Documents", "/transport/documents", "Apps.Transport.Documents"],
    ]);
    transport.views!.forEach((view) => expect(view.icon).toBeTruthy());
    expect(findAppByPath("/transport/orders/3fa85f64-5717-4562-b3fc-2c963f66afa6")?.id).toBe("transport");
  });

  it("the Orders and Trips views carry their saved filters as children routing to the list with ?filter= (UI-PAGE-004)", () => {
    const transport = APPS.find((a) => a.id === "transport")!;
    const [orders, templates, trips, documents] = transport.views!;
    const filters = (view: AppView) => view.children!.map((c) => [c.nameKey, c.path, c.search?.filter]);
    expect(filters(orders)).toEqual([
      ["App:Transport.View.Today", "/transport/orders", "today"],
      ["App:Transport.View.Tomorrow", "/transport/orders", "tomorrow"],
      ["App:Transport.View.ThisWeek", "/transport/orders", "this-week"],
      ["App:Transport.View.Open", "/transport/orders", "open"],
      ["App:Transport.View.Drafts", "/transport/orders", "drafts"],
      ["App:Transport.View.All", "/transport/orders", "all"],
    ]);
    expect(filters(trips)).toEqual([
      ["App:Transport.View.Today", "/transport/trips", "today"],
      ["App:Transport.View.ThisWeek", "/transport/trips", "this-week"],
      ["App:Transport.View.Open", "/transport/trips", "open"],
      ["App:Transport.View.All", "/transport/trips", "all"],
    ]);
    [...orders.children!, ...trips.children!].forEach((child) => {
      expect(child.feature).toBeUndefined();
      expect(child.icon).toBeTruthy();
    });
    expect(templates.children).toBeUndefined();
    expect(documents.children).toBeUndefined();
  });

  it("the Master data app has two domain groups, General then Platform, and reads from both domains (design 49; PO 2026-10-16)", () => {
    const masterData = APPS.find((a) => a.id === "master-data")!;
    expect(masterData.views!.map((v) => v.nameKey)).toEqual(["MasterData:Nav.General", "MasterData:Nav.Orders", "MasterData:Nav.PlanningExecution", "MasterData:Nav.Financial", "Platform:Nav.Platform"]);
    expect(masterData.domains).toEqual(["MasterData", "Orders", "PlanningExecution", "Financial", "Platform"]);
  });

  it("the Platform domain group holds its three lists directly under it, no sub-groups (design 54, Assumption 4)", () => {
    const masterData = APPS.find((a) => a.id === "master-data")!;
    const platform = masterData.views![4];
    expect(platform.path).toBe("/master-data/platform");
    expect(platform.feature).toBe("Apps.MasterData.Platform");
    expect(platform.icon).toBeTruthy();
    expect(platform.children!.map((c) => [c.nameKey, c.path, c.feature])).toEqual([
      ["Platform:EmailTemplates", "/master-data/email-templates", "Apps.MasterData.Platform.EmailTemplates"],
      ["Platform:ReportDefinitions", "/master-data/report-definitions", "Apps.MasterData.Platform.ReportDefinitions"],
      ["Platform:DocumentTypes", "/master-data/document-types", "Apps.MasterData.Platform.DocumentTypes"],
    ]);
    platform.children!.forEach((child) => {
      expect(child.children).toBeUndefined();
      expect(child.icon).toBeTruthy();
    });
    expect(findAppByPath("/master-data/document-types")?.id).toBe("master-data");
  });

  it("the Master data app lists its fifteen General lists in four groups under the General domain group (designs 41, 43 and 48; PO 2026-10-16)", () => {
    const masterData = APPS.find((a) => a.id === "master-data")!;
    expect(masterData.views).toHaveLength(5);
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
    expect(masterData.domains).toEqual(["MasterData", "Orders", "PlanningExecution", "Financial", "Platform"]);
    const [, orders, planning, financial] = masterData.views!;
    const lists = (group: AppView) => group.children!.flatMap((g) => g.children!.map((c) => [c.path, c.nameKey, c.feature]));
    /* Design 61 adds the Transport run's lists after each group's earlier ones. */
    expect(lists(orders)).toEqual([
      ["/master-data/transport-order-types", "Orders:TransportOrderTypes", "Apps.MasterData.Orders.TransportOrders.TransportOrderTypes"],
      ["/master-data/waypoint-types", "Orders:WaypointTypes", "Apps.MasterData.Orders.TransportOrders.WaypointTypes"],
      ["/master-data/time-slots", "Orders:TimeSlots", "Apps.MasterData.Orders.TransportOrders.TimeSlots"],
      ["/master-data/consignment-note-profiles", "Orders:ConsignmentNoteProfiles", "Apps.MasterData.Orders.TransportOrders.ConsignmentNoteProfiles"],
      ["/master-data/consignment-note-fixed-descriptions", "Orders:ConsignmentNoteFixedDescriptions", "Apps.MasterData.Orders.TransportOrders.ConsignmentNoteFixedDescriptions"],
      ["/master-data/order-attribute-definitions", "Orders:OrderAttributeDefinitions", "Apps.MasterData.Orders.TransportOrders.OrderAttributeDefinitions"],
    ]);
    expect(lists(planning)).toEqual([
      ["/master-data/transport-trip-types", "PlanningExecution:TransportTripTypes", "Apps.MasterData.PlanningExecution.Trips.TransportTripTypes"],
      ["/master-data/via-depot-rules", "PlanningExecution:ViaDepotRules", "Apps.MasterData.PlanningExecution.Trips.ViaDepotRules"],
      ["/master-data/trip-attribute-definitions", "PlanningExecution:TripAttributeDefinitions", "Apps.MasterData.PlanningExecution.Trips.TripAttributeDefinitions"],
    ]);
    expect(lists(financial)).toEqual([
      ["/master-data/vat-percentages", "Financial:VatPercentages", "Apps.MasterData.Financial.Invoicing.VatPercentages"],
      ["/master-data/outbound-invoice-types", "Financial:OutboundInvoiceTypes", "Apps.MasterData.Financial.Invoicing.OutboundInvoiceTypes"],
      ["/master-data/inbound-invoice-types", "Financial:InboundInvoiceTypes", "Apps.MasterData.Financial.Invoicing.InboundInvoiceTypes"],
      ["/master-data/additional-services", "Financial:AdditionalServices", "Apps.MasterData.Financial.Pricing.AdditionalServices"],
      ["/master-data/discounts-or-surcharges", "Financial:DiscountsOrSurcharges", "Apps.MasterData.Financial.Pricing.DiscountsOrSurcharges"],
      ["/master-data/price-lists", "Financial:PriceLists", "Apps.MasterData.Financial.Pricing.PriceLists"],
      ["/master-data/relation-financial-settings", "Financial:RelationFinancialSettings", "Apps.MasterData.Financial.Pricing.RelationFinancialSettings"],
    ]);
    [...lists(orders), ...lists(planning), ...lists(financial)].forEach(([path]) => expect(findAppByPath(path as string)?.id).toBe("master-data"));
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
