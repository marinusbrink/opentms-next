import { describe, expect, it } from "vitest";

import {
  addDays,
  changedSections,
  joinDateTime,
  lineInputOf,
  mergeSavedSections,
  newLine,
  orderValuesOf,
  savePlan,
  splitDateTime,
  stopValuesOf,
  templateLineInputOf,
  templateValuesOf,
  termsInputOf,
  tripRequestInputOf,
  validateOrder,
  type OrderValues,
} from "@/apps/transport/orders/order-form";
import type { TransportOrderDto } from "@/domains/orders/transport-orders";

/* The order editor's form model (design 61): the DTO ↔ values mappers, the section diff a save runs on, the
 * client-side validation (the API's rules stay the API's) and the date helpers. Risk class: Medium. */

const stored: TransportOrderDto = {
  id: "o1",
  number: "TO-2026-00001",
  status: 2,
  transportOrderTypeId: "type-1",
  customerRelationId: "rel-1",
  customerName: "Acme",
  orderDate: "2026-10-25",
  customerReference: "REF",
  concurrencyStamp: "s1",
  defaultStops: [
    { waypointTypeId: "wp-load", waypointTypeName: "Load", dayOffset: 0, position: 1 },
    { waypointTypeId: "wp-unload", waypointTypeName: "Unload", dayOffset: 1, position: 2 },
  ],
  lines: [
    {
      id: "l1",
      position: 1,
      transportUnitId: "unit-1",
      quantity: 2,
      weightKg: 100.5,
      paymentInstruction: 0,
      stops: [
        { id: "s1", position: 1, waypointTypeId: "wp-load", relationId: "rel-1", relationAddressId: "adr-1", date: "2026-10-25", windowFrom: "08:00:00", windowTill: "12:00:00" },
        { id: "s2", position: 2, waypointTypeId: "wp-unload", relationId: "rel-2", relationAddressId: "adr-2", date: "2026-10-26" },
      ],
    },
  ],
  financialTerms: { currencyCode: "EUR", vatPercentageId: "vat-1", paymentTermDays: 30, priceListIds: ["pl-1"], multipleMatches: 1, discountsOrSurcharges: [{ discountOrSurchargeId: "ds-1", source: 1 }], fixedPriceAmount: null, sendFreightDocumentsWithInvoice: true },
  tripRequest: undefined,
};

describe("orderValuesOf / lineInputOf", () => {
  it("maps the record to form values and back without losing fields", () => {
    const values = orderValuesOf(stored);
    expect(values.customer).toMatchObject({ id: "rel-1", name: "Acme" });
    expect(values.lines).toHaveLength(1);
    expect(values.lines[0].quantity).toBe("2");
    expect(values.lines[0].stops[0].windowFrom).toBe("08:00");
    expect(values.defaultStops.map((s) => s.dayOffset)).toEqual(["0", "1"]);
    expect(values.terms.discountsOrSurcharges).toEqual([{ discountOrSurchargeId: "ds-1", source: 1 }]);

    const input = lineInputOf(values.lines[0]);
    expect(input).toMatchObject({ id: "l1", transportUnitId: "unit-1", quantity: 2, weightKg: 100.5, paymentInstruction: 0 });
    expect(input.stops?.[0]).toMatchObject({ id: "s1", waypointTypeId: "wp-load", relationId: "rel-1", relationAddressId: "adr-1", date: "2026-10-25", windowFrom: "08:00:00", windowTill: "12:00:00" });
    expect(input.stops?.[1].windowFrom).toBeNull();
    expect(termsInputOf(values.terms)).toEqual({ currencyCode: "EUR", vatPercentageId: "vat-1", paymentTermDays: 30, priceListIds: ["pl-1"], multipleMatches: 1, discountsOrSurcharges: [{ discountOrSurchargeId: "ds-1", source: 1 }], fixedPriceAmount: null, sendFreightDocumentsWithInvoice: true });
  });

  it("a new line starts with the stop pattern; order lines get dates from the order date and offsets, template lines keep offsets", () => {
    const values = orderValuesOf(stored);
    const line = newLine(values.defaultStops, "2026-10-25", "order");
    expect(line.stops.map((s) => [s.waypointTypeId, s.date])).toEqual([
      ["wp-load", "2026-10-25"],
      ["wp-unload", "2026-10-26"],
    ]);
    expect(line.quantity).toBe("1");
    const templateLine = newLine(values.defaultStops, null, "template");
    expect(templateLineInputOf(templateLine).stops?.map((s) => s.dayOffset)).toEqual([0, 1]);
    expect(stopValuesOf({ waypointTypeId: "wp", relationId: "r", relationAddressId: "a", dayOffset: 3 }).dayOffset).toBe("3");
  });
});

describe("changedSections", () => {
  it("names only the sections that differ from the loaded record", () => {
    const initial = orderValuesOf(stored);
    const same = orderValuesOf(stored);
    expect(Object.values(changedSections(same, initial)).every((v) => v === false)).toBe(true);

    const header: OrderValues = { ...initial, customerReference: "NEW" };
    expect(changedSections(header, initial)).toMatchObject({ header: true, lines: false, terms: false });

    const lines: OrderValues = { ...initial, lines: [{ ...initial.lines[0], quantity: "3" }] };
    expect(changedSections(lines, initial)).toMatchObject({ header: false, lines: true });

    const terms: OrderValues = { ...initial, terms: { ...initial.terms, paymentTermDays: "14" } };
    expect(changedSections(terms, initial)).toMatchObject({ terms: true, lines: false });

    const trip: OrderValues = { ...initial, tripRequest: { ...initial.tripRequest, enabled: true } };
    expect(changedSections(trip, initial)).toMatchObject({ tripRequest: true });
  });

  it("whitespace-only edits are no change (UI-FORM-009)", () => {
    const initial = orderValuesOf(stored);
    expect(changedSections({ ...initial, internalRemarks: "   " }, initial).header).toBe(false);
  });
});

describe("validateOrder", () => {
  it("requires the type, the customer and (orders) the date; the type's required flags are marks only", () => {
    const values = orderValuesOf(undefined);
    expect(validateOrder(values, "order")).toMatchObject({ transportOrderTypeId: "MasterData:Required", customer: "MasterData:Required", orderDate: "MasterData:Required" });
    expect(validateOrder(orderValuesOf(stored), "order")).toEqual({});
  });

  it("reports an incomplete stop, a line without unit and a bad time on the Lines tab", () => {
    const values = orderValuesOf(stored);
    const broken: OrderValues = { ...values, lines: [{ ...values.lines[0], transportUnitId: null, stops: [{ ...values.lines[0].stops[0], windowFrom: "25:99" }] }] };
    expect(validateOrder(broken, "order").lines).toBeDefined();
  });

  it("checks the template's name and schedule in template mode", () => {
    const values = templateValuesOf({ id: "t1", name: "", transportOrderTypeId: "type-1", customerRelationId: "rel-1", schedule: { recurrence: 1, interval: 0, daysOfWeek: 0, horizonDays: 5, validFrom: "2026-10-01" } });
    const errors = validateOrder(values, "template");
    expect(errors.name).toBe("MasterData:Required");
    expect(errors.schedule).toBeDefined();
    expect(errors.orderDate).toBeUndefined();
  });

  it("a trip request needs its type, places and departure date", () => {
    const values = orderValuesOf(stored);
    const withTrip: OrderValues = { ...values, tripRequest: { ...values.tripRequest, enabled: true } };
    expect(validateOrder(withTrip, "order").tripRequest).toBe("Orders:TripRequestIncomplete");
    expect(tripRequestInputOf(values.tripRequest)).toBeUndefined();
  });
});

describe("date helpers", () => {
  it("addDays is a calendar operation", () => {
    expect(addDays("2026-10-31", 1)).toBe("2026-11-01");
    expect(addDays("2026-03-28", 3)).toBe("2026-03-31");
    expect(addDays(null, 1)).toBeNull();
  });

  it("splitDateTime and joinDateTime round-trip in the browser's zone", () => {
    const iso = joinDateTime("2026-10-25", "14:30")!;
    expect(splitDateTime(iso)).toEqual({ date: "2026-10-25", time: "14:30" });
    expect(joinDateTime(null, "14:30")).toBeNull();
    expect(splitDateTime(null)).toEqual({ date: null, time: "" });
  });
});

describe("savePlan / mergeSavedSections (one call for header and terms; a save that stops part-way)", () => {
  const none = { header: false, defaultStops: false, lines: false, references: false, terms: false, tripRequest: false, attributes: false };

  it("puts the terms into the header call when both change or the customer changed, and keeps the terms command for a terms-only change", () => {
    expect(savePlan({ ...none, header: true, terms: true }, false)).toEqual([{ command: "header", sections: ["header", "terms"] }]);
    expect(savePlan({ ...none, header: true }, true)).toEqual([{ command: "header", sections: ["header", "terms"] }]);
    expect(savePlan({ ...none, header: true }, false)).toEqual([{ command: "header", sections: ["header"] }]);
    expect(savePlan({ ...none, terms: true }, false)).toEqual([{ command: "terms", sections: ["terms"] }]);
    expect(savePlan({ ...none, lines: true, attributes: true, header: true, tripRequest: true }, false).map((s) => s.command)).toEqual(["header", "lines", "tripRequest", "attributes"]);
    expect(savePlan(none, true)).toEqual([]);
  });

  it("takes the saved sections from the server and keeps the others as typed", () => {
    const server = orderValuesOf(stored);
    const typed: OrderValues = { ...server, customerReference: "TYPED", lines: [{ ...server.lines[0], id: null, quantity: "9" }], attributes: { a: "1" } };
    const fromServer: OrderValues = { ...server, customerReference: "REF-SERVER", lines: [{ ...server.lines[0], id: "l-new", quantity: "9" }] };
    const merged = mergeSavedSections(typed, fromServer, ["header", "lines"]);
    expect(merged.customerReference).toBe("REF-SERVER");
    expect(merged.lines[0].id).toBe("l-new");
    expect(merged.attributes).toEqual({ a: "1" });
    expect(changedSections(merged, fromServer)).toEqual({ ...none, attributes: true });
  });
});
